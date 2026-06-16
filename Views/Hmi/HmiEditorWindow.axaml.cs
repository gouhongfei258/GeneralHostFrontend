using Avalonia.Controls;
using GeneralHostFrontend.ViewModels.Hmi;

namespace GeneralHostFrontend.Views.Hmi;

public partial class HmiEditorWindow : Window
{
    public HmiEditorWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is HmiEditorViewModel viewModel)
        {
            await viewModel.DisposeAsync();
        }
    }

}
