using mystickymonologues.Services;
using mystickymonologues.Views;
using System.Threading;
using System.Windows;

namespace mystickymonologues;

public partial class App : System.Windows.Application
{
    private SettingsService? _settings;
    private AIService? _aiService;
    private WindowManagerService? _windowManager;
    private MCPServer? _mcpServer; // In-process MCP server
    private MCPProcessManager? _mcpProcessManager; // Separate process MCP server
    private Mutex? _appMutex;
    private const string APP_MUTEX_NAME = "Global\\MyStickyMonologues_SingleInstance";

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        // GUI mode: ensure single instance
        try
        {
            _appMutex = new Mutex(true, APP_MUTEX_NAME, out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "MyStickyMonologues is already running.",
                    "Single Instance",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown(0);
                return;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Mutex error: {ex.Message}");
        }

        _settings = new SettingsService();
        _aiService = new AIService(_settings);
        _windowManager = new WindowManagerService(_settings);

        // Initialize MCP server based on settings
        if (_settings.Settings.MCPServerEnabled)
        {
            if (_settings.Settings.MCPServerSeparateProcess)
            {
                // Separate process mode
                _mcpProcessManager = new MCPProcessManager(_settings);
                try
                {
                    _mcpProcessManager.Start();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to start MCP in separate process: {ex.Message}");
                }
            }
            else
            {
                // In-process mode (default)
                _mcpServer = new MCPServer(_settings);
                _mcpServer.StatusChanged += (s, status) => 
                    System.Diagnostics.Debug.WriteLine($"[MCP] {status}");
                try
                {
                    _mcpServer.Start();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to start MCP in-process: {ex.Message}");
                }
            }
        }

        // Warmup LLM in background to avoid first request delay
        _ = _aiService.WarmupLLMAsync();

        // Pass MCP server wrapper to main window (both in-process and separate process)
        var mainWindow = new StickyWindow(_settings, _aiService, new AudioService(), _windowManager, 
            _mcpServer, _mcpProcessManager);
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mcpServer?.Stop();
        _mcpServer?.Dispose();
        _mcpProcessManager?.Stop();
        _mcpProcessManager?.Dispose();
        _appMutex?.ReleaseMutex();
        _appMutex?.Dispose();
        base.OnExit(e);
    }
}
