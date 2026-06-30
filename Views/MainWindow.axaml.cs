using Avalonia.Controls;
using Avalonia.Interactivity;
using MySiteBuilder.ViewModels;

namespace MySiteBuilder.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string type })
            Vm?.AddElement(type);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e) => Vm?.DeleteSelected();

    private async void OnExportStaticClick(object? sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.ExportStaticAsync(this);
    }

    private async void OnExportLaravelClick(object? sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.ExportLaravelAsync(this);
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.SaveProjectAsync(this);
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.OpenProjectAsync(this);
    }
}
