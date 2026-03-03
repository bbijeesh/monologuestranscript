using mystickymonologues.Services;
using mystickymonologues.Views;
using System.Windows;

namespace mystickymonologues;

public partial class App : System.Windows.Application
{
    private SettingsService? _settings;
    private AIService? _aiService;
    private WindowManagerService? _windowManager;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = new SettingsService();
        _aiService = new AIService(_settings);
        _windowManager = new WindowManagerService(_settings);

        // Warmup LLM in background to avoid first request delay
        _ = _aiService.WarmupLLMAsync();

        var mainWindow = new StickyWindow(_settings, _aiService, new AudioService(), _windowManager);
        mainWindow.Show();
    }
}
