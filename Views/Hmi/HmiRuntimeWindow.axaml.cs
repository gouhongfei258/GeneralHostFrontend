using Avalonia.Controls;
using GeneralHostFrontend.ViewModels.Hmi;

namespace GeneralHostFrontend.Views.Hmi;

public partial class HmiRuntimeWindow : Window
{
    public HmiRuntimeWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is HmiRuntimeViewModel viewModel)
        {
            await viewModel.LoadAsync("main");
        }
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is HmiRuntimeViewModel viewModel)
        {
            await viewModel.DisposeAsync();
        }
    }
}
