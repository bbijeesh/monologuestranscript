using System.Windows;
using System.Windows.Media.Imaging;

namespace mystickymonologues.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        LoadLogo();
    }

    private void LoadLogo()
    {
        try
        {
            var bitmap = Properties.Resources.logo2;
            if (bitmap != null)
            {
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                var memoryStream = new System.IO.MemoryStream();
                bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                memoryStream.Seek(0, System.IO.SeekOrigin.Begin);
                bitmapImage.StreamSource = memoryStream;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();
                LogoImage.Source = bitmapImage;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading logo: {ex.Message}");
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Prevent double-click window maximization for custom window
            return;
        }
        DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
