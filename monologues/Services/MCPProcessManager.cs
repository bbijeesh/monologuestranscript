using mystickymonologues.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace mystickymonologues.Services;

/// <summary>
/// Manages the MCP server as a separate child process.
/// Allows Start/Stop control from the GUI.
/// </summary>
public class MCPProcessManager : IDisposable
{
    private readonly SettingsService _settings;
    private Process? _serverProcess;
    private bool _isRunning = false;

    public bool IsRunning => _isRunning && _serverProcess?.HasExited == false;
    public int? ProcessId => _serverProcess?.Id;

    public event EventHandler<string>? StatusChanged;

    public MCPProcessManager(SettingsService settings) => _settings = settings;

    /// <summary>Start MCP server as a separate process.</summary>
    public bool Start()
    {
        if (IsRunning)
        {
            SetStatus("MCP server already running");
            return false;
        }

        try
        {
            var dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var exePath = Path.ChangeExtension(dllPath, ".exe"); // Convert .dll to .exe
            var exeDir = Path.GetDirectoryName(exePath) ?? ".";

            _serverProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "--mcp-only",
                    UseShellExecute = true, // Required for Verb = "runas"
                    Verb = "runas", // Trigger UAC elevation to admin
                    CreateNoWindow = false, // Show console window
                    WorkingDirectory = exeDir
                }
            };

            if (_serverProcess.Start())
            {
                _isRunning = true;
                SetStatus($"✓ MCP server started (PID: {_serverProcess.Id})");
                System.Diagnostics.Debug.WriteLine($"[MCP] Process started: {_serverProcess.Id}");
                
                // Capture output from child process
                _ = Task.Run(() => CaptureOutput());
                
                // Monitor process for early exit (startup failure)
                _ = Task.Run(() => MonitorProcessExit());
                
                return true;
            }
            else
            {
                SetStatus("Failed to start MCP server process");
                return false;
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error starting MCP: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[MCP] Start error: {ex.Message}");
            _isRunning = false;
            return false;
        }
    }

    private void MonitorProcessExit()
    {
        if (_serverProcess == null) return;
        
        try
        {
            // Wait a second to see if process exits immediately (startup failure)
            if (_serverProcess.WaitForExit(2000))
            {
                // Process exited
                int exitCode = _serverProcess.ExitCode;
                _isRunning = false;
                
                if (exitCode == 1)
                {
                    SetStatus("⚠ MCP startup failed: Admin privileges required. Run app as Administrator.");
                }
                else if (exitCode != 0)
                {
                    SetStatus($"⚠ MCP exited with code {exitCode}");
                }
            }
        }
        catch { }
    }

    private void CaptureOutput()
    {
        // Output is displayed in the console window now (UseShellExecute = true, not redirected)
        // This method is kept for future use if we need to capture output again
    }

    /// <summary>Stop the MCP server process.</summary>
    public bool Stop()
    {
        try
        {
            if (_serverProcess == null || _serverProcess.HasExited)
            {
                _isRunning = false;
                SetStatus("MCP server not running");
                return true;
            }

            _serverProcess.Kill(true); // Kill process tree
            _serverProcess.WaitForExit(5000); // Wait up to 5s for graceful exit
            _isRunning = false;
            SetStatus("MCP server stopped");
            System.Diagnostics.Debug.WriteLine("[MCP] Process stopped");
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"Error stopping MCP: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[MCP] Stop error: {ex.Message}");
            _isRunning = false;
            return false;
        }
    }

    public void CheckStatus()
    {
        if (_isRunning && (_serverProcess == null || _serverProcess.HasExited))
        {
            _isRunning = false;
            SetStatus("MCP server has exited unexpectedly");
            System.Diagnostics.Debug.WriteLine("[MCP] Process exited unexpectedly");
        }
    }

    private void SetStatus(string status)
    {
        StatusChanged?.Invoke(this, status);
        System.Diagnostics.Debug.WriteLine($"[MCP] {status}");
    }

    public void Dispose()
    {
        try { Stop(); } catch { }
        _serverProcess?.Dispose();
    }
}
