using mystickymonologues.Services;
using mystickymonologues.Models;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfButton = System.Windows.Controls.Button;
using WinFormsDialogs = System.Windows.Forms;
using Win32Dialogs = Microsoft.Win32;

namespace mystickymonologues.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly MCPServer? _mcpServer; // In-process MCP server
    private readonly MCPProcessManager? _mcpProcessManager; // Separate process MCP server
    private string _selectedModelId = "";

    // In-memory per-model API keys – flushed to settings only on Save
    private readonly Dictionary<string, string> _pendingApiKeys = new();
    // Suppresses the ApiKey_Changed side-effect while we swap keys programmatically
    private bool _loadingModelKey = false;

    public SettingsWindow(SettingsService settings, MCPServer? mcpServer = null, MCPProcessManager? mcpProcessManager = null)
    {
        _settings = settings;
        _mcpServer = mcpServer;
        _mcpProcessManager = mcpProcessManager;
        InitializeComponent();
        LoadSettings();
        WireMCPEvents();
        UpdateMCPStatus();
        
        // Populate models after window is fully loaded
        this.Loaded += (s, e) => PopulateModels();
    }

    private void LoadSettings()
    {
        var s = _settings.Settings;
        TxtName.Text = s.UserName;
        TxtEmail.Text = s.UserEmail;
        TxtKeyFile.Text = s.AIKeyFilePath;
        TxtMaxWindows.Text = s.MaxWindows.ToString();
        TxtNotesFolder.Text = s.NotesFolder;
        ChkMCPEnabled.IsChecked = s.MCPServerEnabled;
        TxtMCPPort.Text = s.MCPServerPort.ToString();
        ChkMCPSeparateProcess.IsChecked = s.MCPServerSeparateProcess;

        _selectedModelId = s.AIModelId ?? "whisper-1";

        // Seed the in-memory dict from stored keys
        _pendingApiKeys.Clear();
        foreach (var kv in s.AIApiKeys)
            _pendingApiKeys[kv.Key] = kv.Value;

        // Show the key for the initially-selected model
        LoadApiKeyForCurrentModel();
    }

    /// <summary>Reads the current model's key into the API key text box.</summary>
    private void LoadApiKeyForCurrentModel()
    {
        _loadingModelKey = true;
        try
        {
            TxtApiKey.Text = _pendingApiKeys.TryGetValue(_selectedModelId, out var k) ? k : "";
            UpdateApiKeyLabel();
        }
        finally { _loadingModelKey = false; }
    }

    /// <summary>Updates the label above the API key field to reflect the active model.</summary>
    private void UpdateApiKeyLabel()
    {
        if (TxtApiKeyLabel == null) return;
        var allModels = AIModelCatalog.OpenAIModels.Concat(AIModelCatalog.GeminiModels);
        var current = allModels.FirstOrDefault(m => m.ModelId == _selectedModelId);
        TxtApiKeyLabel.Text = current != null
            ? $"API Key  ({current.DisplayName})"
            : "API Key";
    }

    private void PopulateModels()
    {
        if (ModelsContainer == null)
        {
            return; // Not initialized yet
        }

        // Get all models (OpenAI + Gemini combined)
        var allModels = new List<AIModel>();
        allModels.AddRange(AIModelCatalog.OpenAIModels);
        allModels.AddRange(AIModelCatalog.GeminiModels);

        ModelsContainer.Items.Clear();

        foreach (var model in allModels)
        {
            var btn = new WpfButton
            {
                Content = model.DisplayName,
                Tag = model, // Store entire model object including provider and endpoint
                Style = _selectedModelId == model.ModelId
                    ? (Style)FindResource("ModelCardBtnSelected")
                    : (Style)FindResource("ModelCardBtn")
            };
            btn.Click += (s, e) => SelectModel(model, btn);
            ModelsContainer.Items.Add(btn);
        }
    }

    private void SelectModel(AIModel model, WpfButton btn)
    {
        // Persist whatever the user typed for the current model before switching
        _pendingApiKeys[_selectedModelId] = TxtApiKey.Text.Trim();

        _selectedModelId = model.ModelId;

        // Auto-set provider based on selected model
        _settings.Settings.AIProvider = model.Provider;

        // Load this model's key (or empty) into the text box
        LoadApiKeyForCurrentModel();

        foreach (WpfButton modelBtn in ModelsContainer.Items)
        {
            modelBtn.Style = modelBtn == btn
                ? (Style)FindResource("ModelCardBtnSelected")
                : (Style)FindResource("ModelCardBtn");
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        TxtStatus.Text = "";

        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            TxtStatus.Text = "Please enter your name.";
            return;
        }

        if (string.IsNullOrWhiteSpace(TxtApiKey.Text) && string.IsNullOrWhiteSpace(TxtKeyFile.Text))
        {
            TxtStatus.Text = "Please provide an API key or key file path.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(TxtKeyFile.Text) && !File.Exists(TxtKeyFile.Text))
        {
            TxtStatus.Text = "Key file not found at the specified path.";
            return;
        }

        if (!int.TryParse(TxtMaxWindows.Text, out int maxW) || maxW < 1 || maxW > 10)
        {
            TxtStatus.Text = "Max windows must be between 1 and 10.";
            return;
        }

        if (!int.TryParse(TxtMCPPort.Text, out int mcpPort) || mcpPort < 1024 || mcpPort > 65535)
        {
            TxtStatus.Text = "MCP port must be between 1024 and 65535.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedModelId))
        {
            TxtStatus.Text = "Please select a model.";
            return;
        }

        // Flush the currently-visible API key into the pending dict before saving
        _pendingApiKeys[_selectedModelId] = TxtApiKey.Text.Trim();

        var s = _settings.Settings;
        s.UserName = TxtName.Text.Trim();
        s.UserEmail = TxtEmail.Text.Trim();
        // Write all per-model keys (skip blank entries to keep the file tidy)
        s.AIApiKeys = _pendingApiKeys
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        s.AIApiKey = ""; // ensure legacy field stays clear
        s.AIKeyFilePath = TxtKeyFile.Text.Trim();
        s.AIModelId = _selectedModelId;
        s.NotesFolder = TxtNotesFolder.Text.Trim();
        s.MaxWindows = maxW;
        s.MCPServerEnabled = ChkMCPEnabled.IsChecked ?? false;
        s.MCPServerPort = mcpPort;
        s.MCPServerSeparateProcess = ChkMCPSeparateProcess.IsChecked ?? false;
        s.IsSetupComplete = true;
        // AIProvider is already set in SelectModel method

        _settings.Save();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void BrowseKeyFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Win32Dialogs.OpenFileDialog
        {
            Title = "Select API Key File",
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
            TxtKeyFile.Text = dlg.FileName;
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new WinFormsDialogs.FolderBrowserDialog
        {
            Description = "Select Notes Folder",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() == WinFormsDialogs.DialogResult.OK)
            TxtNotesFolder.Text = dlg.SelectedPath;
    }

    private void ApiKey_Changed(object sender, TextChangedEventArgs e)
    {
        // Don't clear the key-file path when we're swapping keys programmatically
        if (_loadingModelKey) return;
        if (!string.IsNullOrWhiteSpace(TxtApiKey.Text) && TxtKeyFile != null)
            TxtKeyFile.Text = "";
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var aboutWindow = new AboutWindow();
        aboutWindow.ShowDialog();
    }

    // ── MCP Server Management ────────────────────────────────────────────────

    private void WireMCPEvents()
    {
        if (_mcpServer != null)
        {
            _mcpServer.StatusChanged += (s, status) => TxtMCPStatus.Text = status;
        }
        if (_mcpProcessManager != null)
        {
            _mcpProcessManager.StatusChanged += (s, status) => TxtMCPStatus.Text = status;
        }
    }

    private void UpdateMCPStatus()
    {
        // Check in-process MCP server
        if (_mcpServer != null)
        {
            TxtMCPStatus.Text = _mcpServer.IsRunning
                ? "Running (in-process)"
                : "Not running";
            return;
        }

        // Check separate process MCP server
        if (_mcpProcessManager != null)
        {
            TxtMCPStatus.Text = _mcpProcessManager.IsRunning
                ? $"Running (PID: {_mcpProcessManager.ProcessId})"
                : "Not running";
            return;
        }

        TxtMCPStatus.Text = "";
    }

    private void StartMCP_Click(object sender, RoutedEventArgs e)
    {
        // In-process MCP server
        if (_mcpServer != null)
        {
            _mcpServer.Start();
            UpdateMCPStatus();
            TxtStatus.Text = "";
            return;
        }

        // Separate process MCP server
        if (_mcpProcessManager != null)
        {
            if (_mcpProcessManager.Start())
            {
                UpdateMCPStatus();
                TxtStatus.Text = "";
            }
            else
            {
                TxtStatus.Text = "Failed to start MCP server";
            }
            return;
        }

        TxtStatus.Text = "MCP server not available";
    }

    private void StopMCP_Click(object sender, RoutedEventArgs e)
    {
        // In-process MCP server
        if (_mcpServer != null)
        {
            _mcpServer.Stop();
            UpdateMCPStatus();
            TxtStatus.Text = "";
            return;
        }

        // Separate process MCP server
        if (_mcpProcessManager != null)
        {
            if (_mcpProcessManager.Stop())
            {
                UpdateMCPStatus();
                TxtStatus.Text = "";
            }
            else
            {
                TxtStatus.Text = "Failed to stop MCP server";
            }
            return;
        }

        TxtStatus.Text = "MCP server not available";
    }
}