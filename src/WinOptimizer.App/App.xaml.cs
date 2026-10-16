using System.Windows;
using System.Windows.Threading;
using WinOptimizer.Services;
using WinOptimizer.ViewModels;
using WinOptimizer.Views;

namespace WinOptimizer;

public partial class App : Application
{
    private MainViewModel? _viewModel;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        var settings = new SettingsStore();
        var preferences = settings.Load();
        ThemeService.Apply(preferences.Theme);
        DispatcherUnhandledException += OnDispatcherException;
        var telemetry = new WindowsTelemetryProvider();
        _viewModel = new MainViewModel(telemetry, settings, preferences);
        var window = new MainWindow { DataContext = _viewModel };
        _viewModel.SetupRequested += window.ShowSetupWizard;
        MainWindow = window;
        window.Loaded += async (_, _) =>
        {
            if (!preferences.SetupCompleted) _viewModel.OpenSetupWizard();
            await _viewModel.StartMonitoringAsync();
        };
        window.StateChanged += (_, _) => _viewModel.SetWindowVisible(window.WindowState != WindowState.Minimized);
        window.Show();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _viewModel?.Dispose();
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        AppLog.Write("An unhandled interface error occurred.", e.Exception);
}

