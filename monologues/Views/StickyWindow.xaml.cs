using mystickymonologues.Models;
using mystickymonologues.Services;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using WpfMessageBox = System.Windows.MessageBox;

namespace mystickymonologues.Views;

public partial class StickyWindow : Window
{
    private readonly SettingsService _settings;
    private readonly AIService _aiService;
    private readonly AudioService _audioService;
    private readonly WindowManagerService _windowManager;
    private readonly string _windowId;
    private readonly int _windowIndex;
    private bool _isRecording = false;
    private bool _isInternalUpdate = false; // prevent recursive save on programmatic updates
    private readonly string? _initialContent;  // optional content to pre-fill (e.g. opened from history)

    public StickyWindow(SettingsService settings, AIService aiService,
        AudioService audioService, WindowManagerService windowManager, string? initialContent = null)
    {
        _initialContent = initialContent;
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));

        // Register window and get assigned index
        var (id, index) = _windowManager.RegisterWindow();
        _windowId = id;
        _windowIndex = index;

        InitializeComponent();

        // Load position if available
        try
        {
            var pos = _windowManager.GetPositionByIndex(_windowIndex);
            if (pos != null)
            {
                if (!double.IsNaN(pos.Left) && !double.IsInfinity(pos.Left)) this.Left = pos.Left;
                if (!double.IsNaN(pos.Top) && !double.IsInfinity(pos.Top)) this.Top = pos.Top;
                if (pos.Width > 0) this.Width = pos.Width;
                if (pos.Height > 0) this.Height = pos.Height;
            }
        }
        catch { }

        UpdateUI();
        LoadExistingNote();

        this.LocationChanged += StickyWindow_LocationOrSizeChanged;
        this.SizeChanged += StickyWindow_LocationOrSizeChanged;
        this.Closed += StickyWindow_Closed;
    }

    private void StickyWindow_Closed(object? sender, EventArgs e)
    {
        try
        {
            var p = new WindowPosition { Left = this.Left, Top = this.Top, Width = this.Width, Height = this.Height };
            _windowManager.SetPositionByIndex(_windowIndex, p);
        }
        catch { }

        _windowManager.UnregisterWindow(_windowId);
    }

    private void StickyWindow_LocationOrSizeChanged(object? sender, EventArgs e)
    {
        try
        {
            var p = new WindowPosition { Left = this.Left, Top = this.Top, Width = this.Width, Height = this.Height };
            _windowManager.SetPositionByIndex(_windowIndex, p);
        }
        catch { }
    }

    private void UpdateUI()
    {
        bool setupDone = _settings.Settings.IsSetupComplete;
        BtnSettings.Visibility = setupDone ? Visibility.Visible : Visibility.Collapsed;
        BtnList.Visibility     = setupDone ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnList_Click(object sender, RoutedEventArgs e)
    {
        var listWin = new NotesListWindow(_settings, _aiService, _windowManager)
        {
            Owner = this,
            Left  = this.Left + this.Width + 8,
            Top   = this.Top
        };
        listWin.Show();
    }

    private void LoadExistingNote()
    {
        try
        {
            var text = _initialContent ?? _settings.LoadNote(_windowId);
            _isInternalUpdate = true;
            TxtNotes.IsReadOnly = false; // allow user editing
            TxtNotes.Text = text;
            _isInternalUpdate = false;
            ScrollToBottom();
        }
        catch { }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        if (!_windowManager.CanOpenNewWindow())
        {
            TxtStatus.Text = $"Max windows reached ({_settings.Settings.MaxWindows}).";
            return;
        }

        var win = new StickyWindow(_settings, _aiService, new AudioService(), _windowManager);
        win.Left = this.Left + 30;
        win.Top = this.Top + 30;
        win.Show();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            try { _audioService.StopRecording(); } catch { _audioService.StopRecording(); }
            _isRecording = false;
        }
        Close();
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }

    private void OpenSettings()
    {
        var settingsWin = new SettingsWindow(_settings);
        settingsWin.Owner = this;
        var result = settingsWin.ShowDialog();
        if (result == true)
        {
            // Reload settings from disk to get the updated values
            _settings.Load();
            UpdateUI();
        }
    }

    private async void BtnMic_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.Settings.IsSetupComplete)
        {
            OpenSettings();
            return;
        }

        if (_isRecording)
        {
            await StopAndTranscribe();
            return;
        }

        StartRecording();
    }

    private void StartRecording()
    {
        try
        {
            _audioService.StartRecording();
            _isRecording = true;
            BtnMic.Content = "⏹";
            TxtStatus.Text = "Recording...";
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Could not access microphone:\n{ex.Message}", "Microphone Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task StopAndTranscribe()
    {
        if (!_isRecording) return;

        BtnMic.Content = "🎙";
        TxtStatus.Text = "Transcribing...";

        byte[] audioData;
        try
        {
            audioData = _audioService.StopRecording();
            _isRecording = false;
        }
        catch (Exception ex)
        {
            ShowError($"Recording error: {ex.Message}");
            return;
        }

        if (audioData == null || audioData.Length < 1000)
        {
            //OverlayProcessing.Visibility = Visibility.Collapsed;
            TxtStatus.Text = "No audio captured.";
            return;
        }

        try
        {
            var transcription = await _aiService.TranscribeAndFixAsync(audioData);
            if (!string.IsNullOrWhiteSpace(transcription))
            {
                AppendLine(transcription);
                _settings.AppendToNote(_windowId, transcription);
                TxtStatus.Text = $"✓ Added at {DateTime.Now:HH:mm:ss}";
            }
            else
            {
                TxtStatus.Text = "No speech detected.";
            }
        }
        catch (Exception ex)
        {
            ShowError($"AI error: {ex.Message}");
        }
        finally
        {
            if (!_isRecording) BtnMic.Content = "🎙";
        }
    }

    private void AppendLine(string text)
    {
       if (string.IsNullOrWhiteSpace(text)) return;
        if (text.StartsWith("The quick brown fox jumps over the lazy dog.")) return;
        if(text == "\"\"") return;

        _isInternalUpdate = true;
        if (!string.IsNullOrEmpty(TxtNotes.Text))
            TxtNotes.Text += Environment.NewLine;
        TxtNotes.Text += text + Environment.NewLine;
        _isInternalUpdate = false;
        ScrollToBottom();
    }

    private void TxtNotes_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isInternalUpdate) return;
        try
        {
            _settings.SaveNote(_windowId, TxtNotes.Text);
            TxtStatus.Text = $"Saved at {DateTime.Now:HH:mm:ss}";
        }
        catch
        {
            TxtStatus.Text = "Error saving note.";
        }
    }

    private void ScrollToBottom()
    {
        Scroller.ScrollToEnd();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            try
            {
                _settings.SaveNote(_windowId, TxtNotes.Text);
                TxtStatus.Text = $"✓ Saved at {DateTime.Now:HH:mm:ss}";

            }
            catch
            {
                TxtStatus.Text = "Error saving document.";
            }
        }
    }

    private void ShowError(string msg)
    {
        var message = msg.Length > 50 ? msg.Substring(0, 50).ReplaceLineEndings("") + "..." : msg;
        TxtStatus.Text = $"Error: {message}";
        //WpfMessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
