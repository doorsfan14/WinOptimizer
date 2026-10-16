using System.ComponentModel;
using System.Windows;
using WinOptimizer.ViewModels;

namespace WinOptimizer.Views;

public partial class SetupWindow : Window
{
    private readonly SetupWizardViewModel _viewModel;
    private bool _closingAfterSkip;

    public SetupWindow(SetupWizardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.Begin();
        viewModel.Dismissed += OnDismissed;
        Closing += OnClosing;
    }

    private void OnDismissed() => Dispatcher.BeginInvoke(new Action(Close));

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_viewModel.SetupCompleted || _closingAfterSkip) return;
        e.Cancel = true;
        _closingAfterSkip = true;
        await _viewModel.SkipForNowAsync();
    }
}

