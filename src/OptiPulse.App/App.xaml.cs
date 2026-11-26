using System.Windows;

namespace OptiPulse.App;

public partial class App : Application
{
    public static string[] StartupArgs { get; private set; } = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        StartupArgs = e.Args;

        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            MessageBox.Show(
                $"Произошла непредвиденная ошибка интерфейса:\n{args.Exception.Message}",
                "OptiPulse — Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
        };

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
