using System.Windows;
using WinOptimizer.ViewModels;

namespace WinOptimizer.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    public void ShowSetupWizard()
    {
        if (DataContext is not MainViewModel viewModel) return;
        var wizard = new SetupWindow(viewModel.SetupWizard) { Owner = this };
        wizard.ShowDialog();
        viewModel.RefreshPreferenceEffects();
    }
}

