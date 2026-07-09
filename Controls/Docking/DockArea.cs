using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace MySiteBuilder.Controls.Docking;

// ============================================================
// 自作ドッキングエンジン本体（フローチャート図1・図2）。
//   Dock.Avalonia の代替。DockLayout（ドック本体ツリー＋浮遊パネル群）を
//   受け取り、
//     - Split   → Grid + GridSplitter（リサイズ可能）
//     - TabGroup→ タブ見出し行＋アクティブ Pane の中身
//     - Float   → メインウィンドウ内に重ねる浮遊パネル
//   に変換して描画する。パネル見出し/タブをドラッグすると、
//   カーソル直下のグループの「上下左右=分割 / 中央=タブ合流」を
//   判定してツリーを組み替える。端の外へドラッグ、または右クリック
//   メニューでパネルをフロート（切り離し）でき、フロートを本体上へ
//   ドロップすると再ドッキングする。
//
//   構造: Decorator.Child = Grid[ _host(本体), _floatLayer(浮遊), _overlay(ゴースト/枠) ]
// ============================================================
public class DockArea : Decorator
{
    private enum Zone { None, Left, Right, Top, Bottom, Center }

    private const double DragThreshold = 5;
    private const double EdgeRatio = 0.25;
    private const double SplitterSize = 6;

    private readonly Grid _rootGrid = new();
    private readonly ContentControl _host = new();
    private readonly Canvas _floatLayer = new();                       // 浮遊パネル（操作可）
    private readonly Canvas _overlay = new() { IsHitTestVisible = false }; // ゴースト/ハイライト

    // 再ドッキング先の候補（本体側の TabGroup のみ。フロートは対象外）
    private readonly List<(DockTabGroup group, Control content)> _groups = new();
    private readonly Dictionary<FloatWindow, Border> _floatBorders = new();

    // ドラッグ状態
    private DockPane? _pendingPane;
    private DockTabGroup? _pendingGroup;
    private Point _pressPoint;
    private bool _dragging;
    private FloatWindow? _dragFloat;   // ドラッグ元がフロートならそのウィンドウ
    private Vector _floatGrab;         // フロート左上とつかんだ点のオフセット
    private DockTabGroup? _dropTarget;
    private Zone _dropZone;
    private Border? _ghost;
    private Rectangle? _zoneHighlight;

    public static readonly StyledProperty<DockLayout?> LayoutProperty =
        AvaloniaProperty.Register<DockArea, DockLayout?>(nameof(Layout));

    /// <summary>描画するレイアウト（ドック本体＋浮遊）。</summary>
    public DockLayout? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public DockArea()
    {
        _rootGrid.Children.Add(_host);
        _rootGrid.Children.Add(_floatLayer);
        _rootGrid.Children.Add(_overlay);
        Child = _rootGrid;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LayoutProperty)
            Rebuild();
    }

    // ============================================================
    // ツリー → ビジュアル構築
    // ============================================================
    private void Rebuild()
    {
        _groups.Clear();
        _floatBorders.Clear();
        _floatLayer.Children.Clear();

        var layout = Layout;
        if (layout is null)
        {
            _host.Content = null;
            return;
        }

        // 空になったフロートは掃除（自己修復）
        layout.Floats.RemoveAll(f => f.Group.Panes.Count == 0);

        SetParents(layout.Root, null);
        _host.Content = layout.Root is null ? null : BuildNode(layout.Root);

        foreach (var fw in layout.Floats)
        {
            foreach (var pane in fw.Group.Panes) pane.Owner = fw.Group;
            _floatLayer.Children.Add(BuildFloat(fw));
        }
    }

    private static void SetParents(DockNode? node, DockSplit? parent)
    {
        if (node is null) return;
        node.Parent = parent;
        if (node is DockSplit s)
            foreach (var c in s.Children) SetParents(c, s);
        else if (node is DockTabGroup g)
            foreach (var pane in g.Panes) pane.Owner = g;
    }

    private Control BuildNode(DockNode node) => node switch
    {
        DockSplit s => BuildSplit(s),
        DockTabGroup g => BuildGroup(g, trackForDrop: true),
        _ => new Panel(),
    };

    private Control BuildSplit(DockSplit s)
    {
        var grid = new Grid();
        bool horiz = s.Orientation == Orientation.Horizontal;

        var childDefIndices = new List<int>();
        int defIndex = 0;

        for (int i = 0; i < s.Children.Count; i++)
        {
            if (i > 0)
            {
                AddDef(grid, horiz, GridLength.Auto);
                var splitter = new GridSplitter
                {
                    ResizeDirection = horiz ? GridResizeDirection.Columns : GridResizeDirection.Rows,
                    Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1C)),
                };
                if (horiz) splitter.Width = SplitterSize; else splitter.Height = SplitterSize;
                Place(splitter, horiz, defIndex);
                splitter.DragCompleted += (_, _) => SyncProportions(s, grid, horiz, childDefIndices);
                grid.Children.Add(splitter);
                defIndex++;
            }

            double prop = i < s.Proportions.Count ? s.Proportions[i] : 1.0 / s.Children.Count;
            AddDef(grid, horiz, new GridLength(Math.Max(0.0001, prop), GridUnitType.Star));
            childDefIndices.Add(defIndex);

            var childVisual = BuildNode(s.Children[i]);
            Place(childVisual, horiz, defIndex);
            grid.Children.Add(childVisual);
            defIndex++;
        }

        return grid;
    }

    private static void AddDef(Grid grid, bool horiz, GridLength len)
    {
        if (horiz) grid.ColumnDefinitions.Add(new ColumnDefinition(len));
        else grid.RowDefinitions.Add(new RowDefinition(len));
    }

    private static void Place(Control c, bool horiz, int index)
    {
        if (horiz) Grid.SetColumn(c, index);
        else Grid.SetRow(c, index);
    }

    private static void SyncProportions(DockSplit s, Grid grid, bool horiz, List<int> childDefIndices)
    {
        double total = 0;
        var stars = new List<double>();
        foreach (int idx in childDefIndices)
        {
            double v = horiz ? grid.ColumnDefinitions[idx].Width.Value : grid.RowDefinitions[idx].Height.Value;
            stars.Add(v);
            total += v;
        }
        if (total <= 0) return;
        s.Proportions.Clear();
        foreach (double v in stars) s.Proportions.Add(v / total);
    }

    private Control BuildGroup(DockTabGroup g, bool trackForDrop)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x28)),
        };
        for (int i = 0; i < g.Panes.Count; i++)
            header.Children.Add(BuildTab(g, g.Panes[i], i == g.ActiveIndex));
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        var content = new ContentControl { Content = g.Active?.Content };
        Grid.SetRow(content, 1);
        grid.Children.Add(content);

        if (trackForDrop)
            _groups.Add((g, content));
        return grid;
    }

    private Control BuildTab(DockTabGroup g, DockPane pane, bool active)
    {
        var text = new TextBlock
        {
            Text = pane.Title,
            Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9E)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var tab = new Border
        {
            Child = text,
            Background = active
                ? new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30))
                : Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
            BorderThickness = new Thickness(0, active ? 2 : 0, 0, 0),
            Padding = new Thickness(12, 5),
            Cursor = new Cursor(StandardCursorType.SizeAll),
        };

        // 右クリック: フロート化 / 閉じる
        var flyout = new MenuFlyout();
        var floatItem = new MenuItem { Header = "フロート化" };
        floatItem.Click += (_, _) => FloatPane(g, pane);
        flyout.Items.Add(floatItem);
        if (pane.CanClose)
        {
            var closeItem = new MenuItem { Header = "閉じる" };
            closeItem.Click += (_, _) => ClosePane(g, pane);
            flyout.Items.Add(closeItem);
        }
        tab.ContextFlyout = flyout;

        // 左押下＝クリック候補。移動閾値超えでドラッグへ昇格。
        tab.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _pendingPane = pane;
            _pendingGroup = g;
            _pressPoint = e.GetPosition(this);
            _dragging = false;
            _dragFloat = null;
            e.Pointer.Capture(this);
            e.Handled = true;
        };
        return tab;
    }

    // 浮遊パネル（本体上に重ねる Border）
    private Control BuildFloat(FloatWindow fw)
    {
        var border = new Border
        {
            Child = BuildGroup(fw.Group, trackForDrop: false),
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00)),
            BorderThickness = new Thickness(1),
            Width = fw.Width,
            Height = fw.Height,
        };
        Canvas.SetLeft(border, fw.X);
        Canvas.SetTop(border, fw.Y);
        _floatBorders[fw] = border;
        return border;
    }

    // ============================================================
    // ドラッグ&ドロップ（図2）
    // ============================================================
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_pendingPane is null) return;

        var p = e.GetPosition(this);
        if (!_dragging)
        {
            if (Dist(p, _pressPoint) < DragThreshold) return;
            _dragging = true;
            _dragFloat = FindFloat(_pendingGroup);
            if (_dragFloat is not null)
            {
                var tl = RectInArea(_floatBorders[_dragFloat])?.Position ?? default;
                _floatGrab = _pressPoint - tl;
            }
            else
            {
                ShowGhost(_pendingPane.Title);   // 本体からのドラッグはゴーストを出す
            }
        }

        if (_dragFloat is not null) MoveFloat(_dragFloat, p);
        else MoveGhost(p);
        UpdateDropTarget(p);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pendingPane is not null)
        {
            if (_dragging)
            {
                if (_dropTarget is not null && _dropZone != Zone.None)
                    DropIntoTree();
                else
                    FinalizeFloat(e.GetPosition(this));
            }
            else if (_pendingGroup is not null)
            {
                int idx = _pendingGroup.Panes.IndexOf(_pendingPane);
                if (idx >= 0) { _pendingGroup.ActiveIndex = idx; Rebuild(); }
            }
        }
        ClearDrag(e);
    }

    private FloatWindow? FindFloat(DockTabGroup? group)
    {
        if (group is null || Layout is null) return null;
        return Layout.Floats.FirstOrDefault(f => ReferenceEquals(f.Group, group));
    }

    private void MoveFloat(FloatWindow fw, Point p)
    {
        double nx = p.X - _floatGrab.X;
        double ny = p.Y - _floatGrab.Y;
        fw.X = nx;
        fw.Y = ny;
        if (_floatBorders.TryGetValue(fw, out var b))
        {
            Canvas.SetLeft(b, nx);
            Canvas.SetTop(b, ny);
        }
    }

    private void UpdateDropTarget(Point p)
    {
        _dropTarget = null;
        _dropZone = Zone.None;

        foreach (var (group, content) in _groups)
        {
            var rect = RectInArea(content);
            if (rect is null || !rect.Value.Contains(p)) continue;
            _dropTarget = group;
            _dropZone = ZoneOf(rect.Value, p);
            DrawZoneHighlight(rect.Value, _dropZone);
            return;
        }
        HideZoneHighlight();
    }

    private Rect? RectInArea(Control c)
    {
        var tl = c.TranslatePoint(new Point(0, 0), this);
        if (tl is null) return null;
        return new Rect(tl.Value, c.Bounds.Size);
    }

    private static Zone ZoneOf(Rect r, Point p)
    {
        if (r.Width <= 0 || r.Height <= 0) return Zone.Center;
        double rx = (p.X - r.X) / r.Width;
        double ry = (p.Y - r.Y) / r.Height;
        if (rx < EdgeRatio) return Zone.Left;
        if (rx > 1 - EdgeRatio) return Zone.Right;
        if (ry < EdgeRatio) return Zone.Top;
        if (ry > 1 - EdgeRatio) return Zone.Bottom;
        return Zone.Center;
    }

    // 本体ツリーへドッキング（中央=タブ合流 / 端=分割）。ドラッグ元は本体でもフロートでも可。
    private void DropIntoTree()
    {
        var src = _pendingPane;
        var srcGroup = _pendingGroup;
        if (src is null || srcGroup is null || _dropTarget is null || _dropZone == Zone.None || Layout is null)
            return;

        // 本体の1枚だけのグループを自分自身へ落とすのは無意味（フロート元は除く）
        bool selfOnly = _dragFloat is null
            && ReferenceEquals(_dropTarget, srcGroup) && srcGroup.Panes.Count == 1;
        if (selfOnly) return;

        srcGroup.Panes.Remove(src);
        if (srcGroup.ActiveIndex >= srcGroup.Panes.Count)
            srcGroup.ActiveIndex = srcGroup.Panes.Count - 1;
        if (_dragFloat is not null)
            Layout.Floats.Remove(_dragFloat);

        if (_dropZone == Zone.Center)
        {
            _dropTarget.Panes.Add(src);
            src.Owner = _dropTarget;
            _dropTarget.ActiveIndex = _dropTarget.Panes.Count - 1;
        }
        else
        {
            DockBeside(_dropTarget, src, _dropZone);
        }

        Layout.Root = Normalize(Layout.Root);
        Rebuild();
    }

    // ドロップ先が本体に無い場合: 本体からのドラッグ→新規フロート化 / フロートの移動→そのまま確定
    private void FinalizeFloat(Point p)
    {
        if (Layout is null || _pendingPane is null || _pendingGroup is null) return;

        if (_dragFloat is not null)
        {
            ClampFloat(_dragFloat);
            Rebuild();
            return;
        }

        var src = _pendingPane;
        var srcGroup = _pendingGroup;
        srcGroup.Panes.Remove(src);
        if (srcGroup.ActiveIndex >= srcGroup.Panes.Count)
            srcGroup.ActiveIndex = srcGroup.Panes.Count - 1;

        var fw = new FloatWindow();
        fw.Group.Panes.Add(src);
        fw.Group.ActiveIndex = 0;
        src.Owner = fw.Group;
        fw.X = p.X - fw.Width / 2;
        fw.Y = p.Y - 14;
        ClampFloat(fw);
        Layout.Floats.Add(fw);

        Layout.Root = Normalize(Layout.Root);
        Rebuild();
    }

    private void ClampFloat(FloatWindow fw)
    {
        double maxX = Math.Max(0, Bounds.Width - fw.Width);
        double maxY = Math.Max(0, Bounds.Height - fw.Height);
        fw.X = Math.Min(Math.Max(0, fw.X), maxX);
        fw.Y = Math.Min(Math.Max(0, fw.Y), maxY);
    }

    // 右クリックメニュー: パネルをフロート化
    private void FloatPane(DockTabGroup g, DockPane pane)
    {
        if (Layout is null || FindFloat(g) is not null) return;  // 既にフロートなら何もしない

        g.Panes.Remove(pane);
        if (g.ActiveIndex >= g.Panes.Count) g.ActiveIndex = g.Panes.Count - 1;

        var fw = new FloatWindow { X = 60, Y = 60 };
        fw.Group.Panes.Add(pane);
        fw.Group.ActiveIndex = 0;
        pane.Owner = fw.Group;
        ClampFloat(fw);
        Layout.Floats.Add(fw);

        Layout.Root = Normalize(Layout.Root);
        Rebuild();
    }

    // 右クリックメニュー: パネルを閉じる
    private void ClosePane(DockTabGroup g, DockPane pane)
    {
        if (Layout is null) return;
        g.Panes.Remove(pane);
        if (g.ActiveIndex >= g.Panes.Count) g.ActiveIndex = g.Panes.Count - 1;
        Layout.Root = Normalize(Layout.Root);
        Rebuild();
    }

    private void DockBeside(DockTabGroup target, DockPane pane, Zone zone)
    {
        var newGroup = new DockTabGroup();
        newGroup.Panes.Add(pane);
        newGroup.ActiveIndex = 0;
        pane.Owner = newGroup;

        var orient = zone is Zone.Left or Zone.Right ? Orientation.Horizontal : Orientation.Vertical;
        bool before = zone is Zone.Left or Zone.Top;

        var split = new DockSplit { Orientation = orient };
        if (before)
        {
            split.Children.Add(newGroup);
            split.Children.Add(target);
        }
        else
        {
            split.Children.Add(target);
            split.Children.Add(newGroup);
        }
        split.Proportions.Add(0.5);
        split.Proportions.Add(0.5);

        ReplaceNode(target, split);
    }

    private void ReplaceNode(DockNode oldN, DockNode newN)
    {
        var parent = oldN.Parent;
        if (parent is null)
        {
            newN.Parent = null;
            if (Layout is not null) Layout.Root = newN;
            return;
        }
        int i = parent.Children.IndexOf(oldN);
        if (i >= 0) parent.Children[i] = newN;
        newN.Parent = parent;
    }

    private DockNode? Normalize(DockNode? node)
    {
        switch (node)
        {
            case DockTabGroup g:
                if (g.Panes.Count == 0) return null;
                if (g.ActiveIndex >= g.Panes.Count) g.ActiveIndex = g.Panes.Count - 1;
                if (g.ActiveIndex < 0) g.ActiveIndex = 0;
                return g;

            case DockSplit s:
            {
                var kids = new List<DockNode>();
                var props = new List<double>();
                for (int i = 0; i < s.Children.Count; i++)
                {
                    var n = Normalize(s.Children[i]);
                    if (n is null) continue;
                    kids.Add(n);
                    props.Add(i < s.Proportions.Count ? s.Proportions[i] : 1);
                }
                if (kids.Count == 0) return null;
                if (kids.Count == 1) return kids[0];

                double sum = 0;
                foreach (double p in props) sum += p;
                if (sum <= 0) sum = kids.Count;

                s.Children.Clear();
                s.Children.AddRange(kids);
                s.Proportions.Clear();
                foreach (double p in props) s.Proportions.Add(p / sum);
                return s;
            }

            default:
                return node;
        }
    }

    // ============================================================
    // オーバーレイ（ゴースト＋ゾーンハイライト）
    // ============================================================
    private void ShowGhost(string title)
    {
        _ghost = new Border
        {
            Child = new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontSize = 12,
                Margin = new Thickness(10, 5),
            },
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
            Opacity = 0.75,
            IsHitTestVisible = false,
        };
        _overlay.Children.Add(_ghost);
    }

    private void MoveGhost(Point p)
    {
        if (_ghost is null) return;
        Canvas.SetLeft(_ghost, p.X + 12);
        Canvas.SetTop(_ghost, p.Y + 12);
    }

    private void DrawZoneHighlight(Rect r, Zone zone)
    {
        Rect hi = zone switch
        {
            Zone.Left => new Rect(r.X, r.Y, r.Width / 2, r.Height),
            Zone.Right => new Rect(r.X + r.Width / 2, r.Y, r.Width / 2, r.Height),
            Zone.Top => new Rect(r.X, r.Y, r.Width, r.Height / 2),
            Zone.Bottom => new Rect(r.X, r.Y + r.Height / 2, r.Width, r.Height / 2),
            _ => r,
        };

        _zoneHighlight ??= NewHighlight();
        if (!_overlay.Children.Contains(_zoneHighlight))
            _overlay.Children.Add(_zoneHighlight);

        _zoneHighlight.Width = hi.Width;
        _zoneHighlight.Height = hi.Height;
        Canvas.SetLeft(_zoneHighlight, hi.X);
        Canvas.SetTop(_zoneHighlight, hi.Y);
    }

    private static Rectangle NewHighlight() => new()
    {
        Fill = new SolidColorBrush(Color.FromArgb(0x55, 0x1E, 0x90, 0xFF)),
        Stroke = new SolidColorBrush(Color.FromArgb(0xCC, 0x1E, 0x90, 0xFF)),
        StrokeThickness = 1,
        IsHitTestVisible = false,
    };

    private void HideZoneHighlight()
    {
        if (_zoneHighlight is not null)
            _overlay.Children.Remove(_zoneHighlight);
    }

    private void ClearDrag(PointerReleasedEventArgs e)
    {
        _pendingPane = null;
        _pendingGroup = null;
        _dragging = false;
        _dragFloat = null;
        _dropTarget = null;
        _dropZone = Zone.None;
        if (_ghost is not null) { _overlay.Children.Remove(_ghost); _ghost = null; }
        HideZoneHighlight();
        _zoneHighlight = null;
        e.Pointer.Capture(null);
    }

    private static double Dist(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
