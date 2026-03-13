using mystickymonologues.Models;
using mystickymonologues.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace mystickymonologues.Views;

public partial class NotesListWindow : Window
{
    private readonly SettingsService _settings;
    private readonly AIService _aiService;
    private readonly WindowManagerService _windowManager;

    public NotesListWindow(SettingsService settings, AIService aiService, WindowManagerService windowManager)
    {
        _settings = settings;
        _aiService = aiService;
        _windowManager = windowManager;
        InitializeComponent();
        this.Loaded += (s, e) => LoadNotes();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void LoadNotes()
    {
        NotesPanel.Children.Clear();

        var entries = _settings.GetAllNoteEntries();

        if (entries.Count == 0)
        {
            NotesPanel.Children.Add(new TextBlock
            {
                Text = "No saved notes found.",
                Foreground = (Brush)FindResource("BrushTextMuted"),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 14,
                Margin = new Thickness(0, 48, 0, 0)
            });
            return;
        }

        var grouped = entries.GroupBy(e => e.Date.Date).OrderByDescending(g => g.Key);

        foreach (var group in grouped)
        {
            NotesPanel.Children.Add(new TextBlock
            {
                Text = group.Key.ToString("dddd, MMMM d, yyyy"),
                Foreground = (Brush)FindResource("BrushTextMuted"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2, 16, 0, 4)
            });

            NotesPanel.Children.Add(new Separator
            {
                Background = (Brush)FindResource("BrushBorder"),
                Margin = new Thickness(0, 0, 0, 8)
            });

            foreach (var entry in group.OrderByDescending(e => e.FilePath))
                NotesPanel.Children.Add(BuildNoteCard(entry));
        }
    }

    private UIElement BuildNoteCard(NoteEntry entry)
    {
        var normalBorder = (Brush)FindResource("BrushBorder");
        var activeBorder = (Brush)FindResource("BrushPrimary");

        var card = new Border
        {
            Background = (Brush)FindResource("BrushBackgroundLight"),
            BorderBrush = normalBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 10, 12, 10),
            Cursor = Cursors.Arrow
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Preview text
        var preview = new TextBlock
        {
            Text = entry.Preview,
            Foreground = (Brush)FindResource("BrushText"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 62,
            Margin = new Thickness(0, 0, 0, 10),
            ClipToBounds = true
        };
        Grid.SetRow(preview, 0);
        grid.Children.Add(preview);

        // Footer: line count on the left, open button on the right
        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var meta = new TextBlock
        {
            Text = $"{entry.TotalLines} line{(entry.TotalLines == 1 ? "" : "s")}",
            Foreground = (Brush)FindResource("BrushTextMuted"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(meta, 0);
        footer.Children.Add(meta);

        var openBtn = new Button
        {
            Content = "Open in New Window →",
            Style = (Style)FindResource("ButtonBase"),
            FontSize = 11,
            Padding = new Thickness(10, 4, 10, 4),
            Height = 28
        };
        openBtn.Click += (s, e) => OpenNoteInNewWindow(entry);
        Grid.SetColumn(openBtn, 1);
        footer.Children.Add(openBtn);

        Grid.SetRow(footer, 1);
        grid.Children.Add(footer);

        card.Child = grid;

        // Highlight border on hover
        card.MouseEnter += (s, e) => card.BorderBrush = activeBorder;
        card.MouseLeave += (s, e) => card.BorderBrush = normalBorder;

        return card;
    }

    private void OpenNoteInNewWindow(NoteEntry entry)
    {
        if (!_windowManager.CanOpenNewWindow())
        {
            MessageBox.Show(
                $"Maximum number of sticky windows ({_settings.Settings.MaxWindows}) is already open.",
                "Cannot Open",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var content = File.Exists(entry.FilePath) ? File.ReadAllText(entry.FilePath) : "";
        var win = new StickyWindow(_settings, _aiService, new AudioService(), _windowManager, initialContent: content);
        win.Left = this.Left + 30;
        win.Top = this.Top + 30;
        win.Show();
    }
}
