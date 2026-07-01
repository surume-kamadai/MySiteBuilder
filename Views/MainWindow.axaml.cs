using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MySiteBuilder.Core.Models;
using MySiteBuilder.ViewModels;

namespace MySiteBuilder.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Vm == null) return;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (ctrl && e.Key == Key.Z) { Vm.Undo(); e.Handled = true; }
        else if (ctrl && (e.Key == Key.Y || (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift))))
        { Vm.Redo(); e.Handled = true; }
    }

    private void OnUndoClick(object? sender, RoutedEventArgs e) => Vm?.Undo();
    private void OnRedoClick(object? sender, RoutedEventArgs e) => Vm?.Redo();
    private void OnAddFolderClick(object? sender, RoutedEventArgs e) => Vm?.AddFolder();
    private void OnResetLayoutClick(object? sender, RoutedEventArgs e) => Vm?.ResetLayout();

    // --- 重ね順 / グループ化 ---

    private void OnBringToFrontClick(object? sender, RoutedEventArgs e) => Vm?.BringToFront();
    private void OnBringForwardClick(object? sender, RoutedEventArgs e) => Vm?.BringForward();
    private void OnSendBackwardClick(object? sender, RoutedEventArgs e) => Vm?.SendBackward();
    private void OnSendToBackClick(object? sender, RoutedEventArgs e) => Vm?.SendToBack();
    private void OnGroupClick(object? sender, RoutedEventArgs e) => Vm?.GroupSelection();
    private void OnUngroupClick(object? sender, RoutedEventArgs e) => Vm?.UngroupSelection();

    // --- 複合要素（Slider / ArticleGrid / Accordion）のアイテム操作 ---

    private void OnAddSlideClick(object? sender, RoutedEventArgs e) => Vm?.AddSlide();
    private void OnAddCardClick(object? sender, RoutedEventArgs e) => Vm?.AddCard();
    private void OnAddQaClick(object? sender, RoutedEventArgs e) => Vm?.AddQa();

    private void OnRemoveSlideClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SlideItem item) Vm?.RemoveSlide(item);
    }

    private void OnRemoveCardClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is GridItem item) Vm?.RemoveCard(item);
    }

    private void OnRemoveQaClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is AccordionItem item) Vm?.RemoveQa(item);
    }

    private async void OnPickSlideImageClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || (sender as Control)?.DataContext is not SlideItem item) return;
        var url = await Vm.PickImageDataUrlAsync(this);
        if (url == null) return;
        Vm.PushUndo();
        item.Image = url;
        Vm.RefreshComposite();
    }

    private async void OnPickCardImageClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || (sender as Control)?.DataContext is not GridItem item) return;
        var url = await Vm.PickImageDataUrlAsync(this);
        if (url == null) return;
        Vm.PushUndo();
        item.Image = url;
        Vm.RefreshComposite();
    }

    private void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string type })
            Vm?.AddElement(type);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e) => Vm?.DeleteSelected();

    private async void OnPickImageClick(object? sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.PickImageForSelectedAsync(this);
    }

    private void OnAddPageClick(object? sender, RoutedEventArgs e) => Vm?.AddPage();

    private void OnDeletePageClick(object? sender, RoutedEventArgs e) => Vm?.DeleteActivePage();

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
