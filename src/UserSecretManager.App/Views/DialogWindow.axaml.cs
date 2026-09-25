using Avalonia.Controls;
using UserSecretManager.App.ViewModels;

namespace UserSecretManager.App.Views;

public partial class DialogWindow : Window
{
    private DialogViewModelBase? _viewModel;

    public DialogWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
        }

        _viewModel = DataContext as DialogViewModelBase;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.CloseRequested += OnCloseRequested;
        Width = _viewModel.DialogWidth;
        Height = _viewModel.DialogHeight;
        CanResize = _viewModel.CanResize;
        if (_viewModel.CanResize)
        {
            MinWidth = _viewModel.DialogWidth * 0.6;
            MinHeight = _viewModel.DialogHeight * 0.6;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
