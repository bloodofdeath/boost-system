using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OptiPulse.App.ViewModels;
using OptiPulse.App.Views;

namespace OptiPulse.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        _viewModel.ShowNotRecommendedModal = card =>
        {
            var dlg = new NotRecommendedDialog(card) { Owner = this };
            dlg.ShowDialog();
            return Task.CompletedTask;
        };

        _viewModel.RequestAdminConfirmationModal = (title, explanation) =>
        {
            var dlg = new AdminRequiredDialog(title, explanation) { Owner = this };
            return dlg.ShowDialog() == true;
        };

        _viewModel.ShutdownApplication = () => Application.Current.Shutdown();

        Loaded += async (_, _) =>
        {
            UpdateMaximizedChromeLayout();
            await _viewModel.InitializeAsync(App.StartupArgs);
            await TryCaptureScreenshotIfRequestedAsync(App.StartupArgs);
        };

        StateChanged += (_, _) => UpdateMaximizedChromeLayout();
        Closed += (_, _) => _viewModel.Dispose();
    }

    private void UpdateMaximizedChromeLayout()
    {
        bool isMax = WindowState == WindowState.Maximized;
        MaximizeBtn.Content = isMax ? "❐" : "▢";
        // Compensate for WindowChrome border overhang when maximized so edges & taskbar aren't clipped
        RootBorder.Padding = isMax ? new Thickness(7) : new Thickness(0);
        RootBorder.BorderThickness = isMax ? new Thickness(0) : new Thickness(1);
        MaxHeight = SystemParameters.MaximizedPrimaryScreenHeight;
        MaxWidth = SystemParameters.MaximizedPrimaryScreenWidth;
    }

    private async Task TryCaptureScreenshotIfRequestedAsync(string[] args)
    {
        var cli = CliOptions.Parse(args);
        if (string.IsNullOrWhiteSpace(cli.CaptureScreenshotPath)) return;

        var outPath = cli.CaptureScreenshotPath;
        await Task.Delay(350);
        await Dispatcher.InvokeAsync(() =>
        {
            UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(this);
            int width = Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX));
            int height = Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY));

            var rtb = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            rtb.Render(this);

            var dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(outPath);
            encoder.Save(fs);
        }, DispatcherPriority.ContextIdle);

        Close();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
