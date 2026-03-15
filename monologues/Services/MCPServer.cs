using mystickymonologues.Models;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace mystickymonologues.Services;

/// <summary>
/// Hosts a local MCP server supporting two transports:
///
///   1. Streamable HTTP (2025-11-25) — Claude Desktop native
///      POST /mcp  →  JSON-RPC request/response
///
///   2. Legacy SSE transport — used by mcp-remote proxy
///      GET  /sse                   →  opens SSE stream, sends endpoint event
///      POST /messages?sessionId=x  →  JSON-RPC in, response sent over SSE
///
/// Tools exposed: list_transcripts, read_transcript
/// </summary>
public class MCPServer : IDisposable
{
    private readonly SettingsService _settings;
    private HttpListener? _listener;
    private bool _isRunning = false;
    private string _lastStatus = "";
    private Semaphore? _singleInstanceSemaphore;
    private const string MCP_SEMAPHORE_NAME = "Global\\MyStickyMonologues_MCP_Server";

    // Active SSE sessions: sessionId → writer
    private readonly ConcurrentDictionary<string, SseSession> _sessions = new();

    private static readonly JsonSerializerOptions JsonOpts        = new() { WriteIndented = true };
    // SSE data: lines MUST be single-line — indented JSON breaks the SSE frame
    private static readonly JsonSerializerOptions JsonOptsCompact = new() { WriteIndented = false };

    public bool IsRunning  => _isRunning;
    public string LastStatus => _lastStatus;
    public event EventHandler<string>? StatusChanged;

    public MCPServer(SettingsService settings) => _settings = settings;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public void Start()
    {
        if (_isRunning) return;

        // Attempt to acquire singleton semaphore
        try
        {
            _singleInstanceSemaphore = new Semaphore(1, 1, MCP_SEMAPHORE_NAME, out bool createdNew);
            if (!_singleInstanceSemaphore.WaitOne(0))
            {
                // Another instance already acquired the semaphore
                SetStatus($"⚠ MCP: Another server instance is already running");
                System.Diagnostics.Debug.WriteLine("[MCP] Singleton check failed: another instance detected");
                return;
            }
        }
        catch (Exception ex)
        {
            SetStatus($"⚠ MCP: Singleton check error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[MCP] Semaphore error: {ex.Message}");
            return;
        }

        int port = _settings.Settings.MCPServerPort;
        if (IsPortInUse(port))
        {
            SetStatus($"⚠ MCP port {port} already in use");
            ReleaseSemaphore();
            return;
        }

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Start();
            _isRunning = true;
            SetStatus($"✓ MCP server on port {port}");
            System.Diagnostics.Debug.WriteLine($"[MCP] HttpListener started on http://localhost:{port}/");
            _ = ListenAsync();
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 5)
        {
            // Error 5 = Access Denied (admin required on Windows)
            SetStatus($"⚠ MCP: Admin privileges required to bind port {port}. Run app as Administrator.");
            System.Diagnostics.Debug.WriteLine($"[MCP] Admin required: {ex.Message}");
            ReleaseSemaphore();
        }
        catch (Exception ex)
        {
            SetStatus($"⚠ MCP failed: {ex.GetType().Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[MCP] Start error: {ex}");
            ReleaseSemaphore();
        }
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        try { _listener?.Stop(); _listener?.Close(); } catch { }
        ReleaseSemaphore();
    }

    private void ReleaseSemaphore()
    {
        try
        {
            if (_singleInstanceSemaphore != null)
            {
                _singleInstanceSemaphore.Release();
                _singleInstanceSemaphore.Dispose();
                _singleInstanceSemaphore = null;
            }
        }
        catch { }
    }

    private void SetStatus(string status)
    {
        _lastStatus = status;
        StatusChanged?.Invoke(this, status);
        System.Diagnostics.Debug.WriteLine($"[MCP] {status}");
    }

    // ── HTTP listener loop ───────────────────────────────────────────────────

    private async Task ListenAsync()
    {
        while (_isRunning && _listener?.IsListening == true)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                _ = HandleRequestAsync(ctx);
            }
            catch (ObjectDisposedException) { _isRunning = false; break; }
            catch (HttpListenerException)   { _isRunning = false; break; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[MCP] listen error: {ex.Message}"); }
        }
    }

    // ── Request dispatcher ───────────────────────────────────────────────────

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        var req  = ctx.Request;
        var resp = ctx.Response;

        resp.AddHeader("Access-Control-Allow-Origin",  "*");
        resp.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        resp.AddHeader("Access-Control-Allow-Headers", "Content-Type, Accept");

        if (req.HttpMethod == "OPTIONS")
        {
            resp.StatusCode = 200;
            resp.Close();
            return;
        }

        var path = req.Url?.AbsolutePath.TrimEnd('/') ?? "";

        // Health check — verify the server is running the current build
        if (req.HttpMethod == "GET" && (path == "" || path == "/health"))
        {
            resp.ContentType = "application/json; charset=utf-8";
            resp.StatusCode  = 200;
            await WriteAsync(resp, new
            {
                status  = "ok",
                server  = "monologues-mcp",
                version = "1.0.0",
                endpoints = new[] { "GET /sse", "POST /messages?sessionId=x", "POST /mcp", "GET /health" }
            });
            try { resp.Close(); } catch { }
            return;
        }

        // SSE transport — long-running; HandleSseAsync closes the response itself
        if (req.HttpMethod == "GET" && path == "/sse")
        {
            await HandleSseAsync(req, resp);
            return;
        }

        resp.ContentType = "application/json; charset=utf-8";
        try
        {
            if (req.HttpMethod == "POST" && (path == "/mcp" || path == ""))
            {
                await HandleMcpPostAsync(req, resp);
            }
            else if (req.HttpMethod == "POST" && path == "/messages")
            {
                var sessionId = ExtractQueryParam(req.Url?.Query, "sessionId") ?? "";
                await HandleMessagesPostAsync(req, resp, sessionId);
            }
            else
            {
                resp.StatusCode = 404;
                await WriteAsync(resp, RpcError(null, -32601,
                    "Unknown endpoint. POST to /mcp (Streamable HTTP) or use GET /sse + POST /messages (SSE transport)."));
            }
        }
        catch (Exception ex)
        {
            resp.StatusCode = 500;
            await WriteAsync(resp, RpcError(null, -32603, ex.Message));
        }
        finally
        {
            try { resp.Close(); } catch { }
        }
    }

    // ── Streamable HTTP transport ────────────────────────────────────────────

    private async Task HandleMcpPostAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string body;
        using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            body = await reader.ReadToEndAsync();

        System.Diagnostics.Debug.WriteLine($"[MCP/HTTP] ← {body}");

        var (response, isNotification) = await DispatchRpcBodyAsync(body);

        if (isNotification) { resp.StatusCode = 202; return; }

        resp.StatusCode = 200;
        await WriteAsync(resp, response!);
    }

    // ── Legacy SSE transport ─────────────────────────────────────────────────

    /// <summary>GET /sse — opens SSE stream and registers the session.</summary>
    private async Task HandleSseAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var sessionId = Guid.NewGuid().ToString("N")[..16];
        int port      = _settings.Settings.MCPServerPort;

        try
        {
            resp.StatusCode  = 200;
            resp.ContentType = "text/event-stream; charset=utf-8";
            resp.AppendHeader("Cache-Control", "no-cache");
            resp.AppendHeader("X-Accel-Buffering", "no");
            resp.AppendHeader("Connection", "keep-alive");
            resp.SendChunked = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MCP/SSE] failed to set headers: {ex.Message}");
            try { resp.StatusCode = 500; resp.Close(); } catch { }
            return;
        }

        var session = new SseSession(resp.OutputStream);
        _sessions[sessionId] = session;
        System.Diagnostics.Debug.WriteLine($"[MCP/SSE] session {sessionId} opened");

        try
        {
            // Tell the client where to POST its messages
            await session.SendAsync(
                $"event: endpoint\ndata: http://localhost:{port}/messages?sessionId={sessionId}\n\n");

            // Keep connection alive with periodic pings
            while (_isRunning)
            {
                await Task.Delay(20_000);
                if (!_isRunning) break;
                try   { await session.SendAsync(": keepalive\n\n"); }
                catch { break; }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MCP/SSE] session {sessionId} ended: {ex.Message}");
        }
        finally
        {
            _sessions.TryRemove(sessionId, out _);
            System.Diagnostics.Debug.WriteLine($"[MCP/SSE] session {sessionId} removed");
            try { resp.OutputStream.Close(); } catch { }
            try { resp.Close(); }              catch { }
        }
    }

    /// <summary>POST /messages?sessionId=xxx — receives JSON-RPC, returns 202, replies over SSE.</summary>
    private async Task HandleMessagesPostAsync(
        HttpListenerRequest req, HttpListenerResponse resp, string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            resp.StatusCode = 404;
            await WriteAsync(resp, new { error = "Session not found or expired" });
            return;
        }

        string body;
        using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            body = await reader.ReadToEndAsync();

        System.Diagnostics.Debug.WriteLine($"[MCP/SSE] ← {body}");

        // Return 202 immediately; actual response travels back over the SSE stream
        resp.StatusCode = 202;
        resp.Close();

        var (response, isNotification) = await DispatchRpcBodyAsync(body);
        if (!isNotification && response != null)
        {
            // Must be compact (single-line) — SSE splits on \n, multi-line JSON breaks parsing
            var json = JsonSerializer.Serialize(response, JsonOptsCompact);
            System.Diagnostics.Debug.WriteLine($"[MCP/SSE] → {json}");
            await session.SendAsync($"event: message\ndata: {json}\n\n");
        }
    }

    // ── Shared JSON-RPC dispatcher ───────────────────────────────────────────

    private async Task<(object? response, bool isNotification)> DispatchRpcBodyAsync(string body)
    {
        JsonNode? rpc;
        try { rpc = JsonNode.Parse(body); }
        catch { return (RpcError(null, -32700, "Parse error"), false); }

        var method  = rpc?["method"]?.GetValue<string>() ?? "";
        var id      = rpc?["id"];
        var @params = rpc?["params"];

        if (method.StartsWith("notifications/"))
            return (null, true);

        object result = method switch
        {
            "initialize" => HandleInitialize(id, @params),
            "tools/list" => HandleToolsList(id),
            "tools/call" => await HandleToolsCallAsync(id, @params),
            _            => RpcError(id, -32601, $"Method not found: {method}")
        };

        return (result, false);
    }

    // ── MCP method handlers ──────────────────────────────────────────────────

    private static object HandleInitialize(JsonNode? id, JsonNode? @params)
    {
        var clientVersion = @params?["protocolVersion"]?.GetValue<string>() ?? "unknown";
        System.Diagnostics.Debug.WriteLine($"[MCP] initialize — client protocol {clientVersion}");

        return RpcResult(id, new
        {
            protocolVersion = "2025-11-25",
            capabilities = new
            {
                tools = new { listChanged = false }
            },
            serverInfo = new
            {
                name    = "monologues-transcripts",
                version = "1.0.0"
            }
        });
    }

    private static object HandleToolsList(JsonNode? id) => RpcResult(id, new
    {
        tools = new object[]
        {
            new
            {
                name        = "list_transcripts",
                description = "List all transcript files recorded by MyStickyMonologues. Returns file names, paths, dates and previews.",
                inputSchema = new
                {
                    type       = "object",
                    properties = new { },
                    required   = Array.Empty<string>()
                }
            },
            new
            {
                name        = "read_transcript",
                description = "Read the full text content of a specific transcript file.",
                inputSchema = new
                {
                    type       = "object",
                    properties = new
                    {
                        file = new
                        {
                            type        = "string",
                            description = "Absolute file path of the transcript (use the filePath value from list_transcripts)"
                        }
                    },
                    required = new[] { "file" }
                }
            }
        }
    });

    private async Task<object> HandleToolsCallAsync(JsonNode? id, JsonNode? @params)
    {
        var toolName  = @params?["name"]?.GetValue<string>() ?? "";
        var arguments = @params?["arguments"];

        return toolName switch
        {
            "list_transcripts" => ListTranscriptsResult(id),
            "read_transcript"  => await ReadTranscriptResultAsync(id, arguments),
            _                  => RpcError(id, -32602, $"Unknown tool: {toolName}")
        };
    }

    // ── Tool implementations ─────────────────────────────────────────────────

    private object ListTranscriptsResult(JsonNode? id)
    {
        var entries = _settings.GetAllNoteEntries();

        var text = entries.Count == 0
            ? "No transcripts found."
            : string.Join("\n\n", entries.Select(n =>
                $"File: {Path.GetFileName(n.FilePath)}\nDate: {n.Date:yyyy-MM-dd}\nPath: {n.FilePath}\nLines: {n.TotalLines}\nPreview:\n{n.Preview}"));

        return RpcResult(id, new
        {
            content = new[] { new { type = "text", text } },
            isError = false
        });
    }

    private async Task<object> ReadTranscriptResultAsync(JsonNode? id, JsonNode? arguments)
    {
        var filePath = arguments?["file"]?.GetValue<string>();

        if (string.IsNullOrEmpty(filePath))
            return RpcResult(id, new
            {
                content = new[] { new { type = "text", text = "Error: missing required argument 'file'." } },
                isError = true
            });

        var fullPath  = Path.GetFullPath(filePath);
        var notesRoot = Path.GetFullPath(_settings.Settings.NotesFolder);

        if (!fullPath.StartsWith(notesRoot, StringComparison.OrdinalIgnoreCase))
            return RpcResult(id, new
            {
                content = new[] { new { type = "text", text = "Error: access denied — path is outside the notes folder." } },
                isError = true
            });

        if (!File.Exists(fullPath))
            return RpcResult(id, new
            {
                content = new[] { new { type = "text", text = $"Error: file not found — {fullPath}" } },
                isError = true
            });

        var text = await File.ReadAllTextAsync(fullPath);
        return RpcResult(id, new
        {
            content = new[] { new { type = "text", text } },
            isError = false
        });
    }

    // ── SSE session ──────────────────────────────────────────────────────────

    private sealed class SseSession(Stream stream)
    {
        private readonly Stream _stream = stream;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public async Task SendAsync(string data)
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            await _lock.WaitAsync();
            try   { await _stream.WriteAsync(bytes); await _stream.FlushAsync(); }
            finally { _lock.Release(); }
        }
    }

    // ── JSON-RPC 2.0 envelope builders ───────────────────────────────────────

    private static object RpcResult(JsonNode? id, object result) =>
        new { jsonrpc = "2.0", id, result };

    private static object RpcError(JsonNode? id, int code, string message) =>
        new { jsonrpc = "2.0", id, error = new { code, message } };

    // ── I/O helpers ──────────────────────────────────────────────────────────

    private static async Task WriteAsync(HttpListenerResponse resp, object payload)
    {
        var json  = JsonSerializer.Serialize(payload, JsonOpts);
        System.Diagnostics.Debug.WriteLine($"[MCP] → {json}");
        var bytes = Encoding.UTF8.GetBytes(json);
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes);
    }

    private static string? ExtractQueryParam(string? query, string paramName)
    {
        if (string.IsNullOrEmpty(query)) return null;
        foreach (var pair in query.TrimStart('?').Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == paramName)
                return Uri.UnescapeDataString(parts[1]);
        }
        return null;
    }

    private static bool IsPortInUse(int port)
    {
        try
        {
            var probe = new HttpListener();
            probe.Prefixes.Add($"http://localhost:{port}/");
            probe.Start();
            probe.Stop();
            probe.Close();
            return false;
        }
        catch { return true; }
    }

    public void Dispose()
    {
        Stop();
        ReleaseSemaphore();
        _listener?.Close();
        GC.SuppressFinalize(this);
    }
}
