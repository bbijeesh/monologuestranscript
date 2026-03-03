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
    private string _selectedModelId = "";

    public SettingsWindow(SettingsService settings)
    {
        _settings = settings;
        InitializeComponent();
        LoadSettings();
        
        // Populate models after window is fully loaded
        this.Loaded += (s, e) => PopulateModels();
    }

    private void LoadSettings()
    {
        var s = _settings.Settings;
        TxtName.Text = s.UserName;
        TxtEmail.Text = s.UserEmail;
        TxtApiKey.Text = s.AIApiKey;
        TxtKeyFile.Text = s.AIKeyFilePath;
        TxtMaxWindows.Text = s.MaxWindows.ToString();
        TxtNotesFolder.Text = s.NotesFolder;

        _selectedModelId = s.AIModelId ?? "whisper-1";
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
        _selectedModelId = model.ModelId;
        
        // Auto-set provider based on selected model
        _settings.Settings.AIProvider = model.Provider;

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

        if (string.IsNullOrWhiteSpace(_selectedModelId))
        {
            TxtStatus.Text = "Please select a model.";
            return;
        }

        var s = _settings.Settings;
        s.UserName = TxtName.Text.Trim();
        s.UserEmail = TxtEmail.Text.Trim();
        s.AIApiKey = TxtApiKey.Text.Trim();
        s.AIKeyFilePath = TxtKeyFile.Text.Trim();
        s.AIModelId = _selectedModelId;
        s.NotesFolder = TxtNotesFolder.Text.Trim();
        s.MaxWindows = maxW;
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
        if (!string.IsNullOrWhiteSpace(TxtApiKey.Text) && TxtKeyFile != null)
            TxtKeyFile.Text = "";
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var aboutWindow = new AboutWindow();
        aboutWindow.ShowDialog();
    }
}
