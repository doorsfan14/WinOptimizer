using System.Windows;
using WinOptimizer.ViewModels;

namespace WinOptimizer.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    public void ShowSetupWizard()
    {
        if (DataContext is not MainViewModel viewModel) return;
        var wizard = new SetupWindow(viewModel.SetupWizard) { Owner = this };
        wizard.ShowDialog();
        viewModel.RefreshPreferenceEffects();
    }
}
