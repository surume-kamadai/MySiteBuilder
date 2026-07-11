using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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

    // ============================================================
    // エクスプローラーのドラッグ&ドロップ並べ替え（レイヤー / ページ）
    //   ListBox の Loaded で D&D を仕込み、項目をドラッグして順序を入れ替える。
    // ============================================================
    private const string LayerFormat = "ksb.layer";
    private const string PageFormat = "ksb.page";
    private const double DndThreshold = 5;

    private object? _dndItem;      // ドラッグ中の要素/ページ
    private string? _dndFormat;
    private Point _dndStart;
    private bool _dndActive;

    private void OnLayerListLoaded(object? sender, RoutedEventArgs e) => SetupExplorerDnd(sender, LayerFormat);
    private void OnPageListLoaded(object? sender, RoutedEventArgs e) => SetupExplorerDnd(sender, PageFormat);

    private void SetupExplorerDnd(object? sender, string format)
    {
        if (sender is not Control c || DragDrop.GetAllowDrop(c)) return;   // 二重登録防止
        DragDrop.SetAllowDrop(c, true);
        // ListBox の選択処理に負けないよう handledEventsToo で確実に拾う
        c.AddHandler(PointerPressedEvent, (_, ev) => RecordDrag(ev, format),
            Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        c.AddHandler(PointerMovedEvent, (_, ev) => TryStartDrag(ev, format),
            Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        c.AddHandler(DragDrop.DragOverEvent, (_, ev) =>
        {
            ev.DragEffects = ev.Data.Contains(format) ? DragDropEffects.Move : DragDropEffects.None;
            ev.Handled = true;
        });
        c.AddHandler(DragDrop.DropEvent, (_, ev) => OnExplorerDrop(ev, format));
    }

    private void RecordDrag(PointerPressedEventArgs e, string format)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _dndItem = null; return; }
        _dndItem = AncestorItemData(e.Source);
        _dndFormat = format;
        _dndStart = e.GetPosition(this);
        _dndActive = false;
    }

    private async void TryStartDrag(PointerEventArgs e, string format)
    {
        if (_dndItem is null || _dndFormat != format || _dndActive) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _dndItem = null; return; }
        if (Dist(e.GetPosition(this), _dndStart) < DndThreshold) return;

        _dndActive = true;
        var data = new DataObject();
        data.Set(format, _dndItem);
        try { await DragDrop.DoDragDrop(e, data, DragDropEffects.Move); }
        finally { _dndItem = null; _dndFormat = null; _dndActive = false; }
    }

    private void OnExplorerDrop(DragEventArgs e, string format)
    {
        var vm = Vm;
        if (vm is null) return;
        var dragged = e.Data.Get(format);
        var target = AncestorItemData(e.Source);
        if (format == LayerFormat && dragged is SiteElement de)
            vm.MoveElement(de, target as SiteElement);
        else if (format == PageFormat && dragged is SitePage dp)
            vm.MovePage(dp, target as SitePage);
        e.Handled = true;
    }

    // ポインタ直下の ListBoxItem の DataContext（要素/ページ）を取り出す。
    private static object? AncestorItemData(object? source) =>
        (source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext;

    private static double Dist(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

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
