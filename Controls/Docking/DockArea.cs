using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace MySiteBuilder.Controls.Docking;

// ============================================================
// 自作ドッキングエンジン本体（フローチャート図1・図2）。
//   Dock.Avalonia の代替。DockNode ツリーを受け取り、
//     - Split   → Grid + GridSplitter（リサイズ可能）
//     - TabGroup→ タブ見出し行＋アクティブ Pane の中身
//   に変換して描画する。パネル見出し/タブをドラッグすると、
//   カーソル直下のグループの「上下左右=分割 / 中央=タブ合流」を
//   判定してツリーを組み替える（Photoshop 風）。
//
//   構造: Decorator.Child = Grid[ _host(ドック本体), _overlay(ゴースト/ハイライト) ]
// ============================================================
public class DockArea : Decorator
{
    // ドロップ先ゾーン
    private enum Zone { None, Left, Right, Top, Bottom, Center }

    private const double DragThreshold = 5;   // クリックとドラッグを分ける移動量
    private const double EdgeRatio = 0.25;    // 外周何割を上下左右ゾーンにするか
    private const double SplitterSize = 6;    // スプリッタの太さ

    private readonly Grid _rootGrid = new();
    private readonly ContentControl _host = new();
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };

    // ヒットテスト用: 描画済み TabGroup とその中身ホスト（座標変換の基準）
    private readonly List<(DockTabGroup group, Control content)> _groups = new();

    // ドラッグ状態
    private DockPane? _pendingPane;
    private DockTabGroup? _pendingGroup;
    private Point _pressPoint;
    private bool _dragging;
    private DockTabGroup? _dropTarget;
    private Zone _dropZone;
    private Border? _ghost;
    private Rectangle? _zoneHighlight;

    private bool _suspendRebuild;

    public static readonly StyledProperty<DockNode?> RootProperty =
        AvaloniaProperty.Register<DockArea, DockNode?>(nameof(Root));

    /// <summary>描画するレイアウトツリーのルート。</summary>
    public DockNode? Root
    {
        get => GetValue(RootProperty);
        set => SetValue(RootProperty, value);
    }

    public DockArea()
    {
        _rootGrid.Children.Add(_host);      // 下: ドック本体
        _rootGrid.Children.Add(_overlay);   // 上: ドラッグ中のゴースト/ハイライト
        Child = _rootGrid;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RootProperty && !_suspendRebuild)
            Rebuild();
    }

    // ============================================================
    // ツリー → ビジュアル構築
    // ============================================================
    private void Rebuild()
    {
        _groups.Clear();
        SetParents(Root, null);
        _host.Content = Root is null ? null : BuildNode(Root);
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
        DockTabGroup g => BuildGroup(g),
        _ => new Panel(),
    };

    private Control BuildSplit(DockSplit s)
    {
        var grid = new Grid();
        bool horiz = s.Orientation == Orientation.Horizontal;

        // 子ノードとその間のスプリッタを交互に配置する。
        // 列/行の index を数えながら定義を積む。
        var childDefIndices = new List<int>();
        int defIndex = 0;

        for (int i = 0; i < s.Children.Count; i++)
        {
            if (i > 0)
            {
                // 子と子の間にスプリッタ用の Auto 定義
                AddDef(grid, horiz, GridLength.Auto);
                var splitter = new GridSplitter
                {
                    ResizeDirection = horiz ? GridResizeDirection.Columns : GridResizeDirection.Rows,
                    Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1C)),
                };
                if (horiz) splitter.Width = SplitterSize; else splitter.Height = SplitterSize;
                Place(splitter, horiz, defIndex);
                // スプリッタ操作後に比率をツリーへ書き戻す
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

    // スプリッタで変化した列/行の star 値を読み、比率としてツリーへ保存する。
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

    private Control BuildGroup(DockTabGroup g)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));  // タブ見出し
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star))); // 中身

        // タブ見出し行
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x28)),
        };
        for (int i = 0; i < g.Panes.Count; i++)
            header.Children.Add(BuildTab(g, g.Panes[i], i == g.ActiveIndex));
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        // 中身（アクティブ Pane。DataTemplate が ViewModel を解決）
        var content = new ContentControl { Content = g.Active?.Content };
        Grid.SetRow(content, 1);
        grid.Children.Add(content);

        // ヒットテスト用に中身ホストを記録
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

        // 押下＝クリック候補。移動閾値を超えたらドラッグへ昇格（OnPointerMoved）。
        tab.PointerPressed += (_, e) =>
        {
            _pendingPane = pane;
            _pendingGroup = g;
            _pressPoint = e.GetPosition(this);
            _dragging = false;
            e.Pointer.Capture(this);
            e.Handled = true;
        };
        return tab;
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
            ShowGhost(_pendingPane.Title);
        }
        MoveGhost(p);
        UpdateDropTarget(p);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pendingPane is not null)
        {
            if (_dragging)
                PerformDrop();
            else if (_pendingGroup is not null)
            {
                // ドラッグせず離した＝タブのクリック → アクティブ切替
                int idx = _pendingGroup.Panes.IndexOf(_pendingPane);
                if (idx >= 0) { _pendingGroup.ActiveIndex = idx; Rebuild(); }
            }
        }
        ClearDrag(e);
    }

    // カーソル直下のグループとゾーンを判定し、ハイライトを更新する。
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

    // ドロップ確定: ツリーを組み替え → 正規化 → 再構築（図2 の後半）
    private void PerformDrop()
    {
        var src = _pendingPane;
        var srcGroup = _pendingGroup;
        if (src is null || srcGroup is null || _dropTarget is null || _dropZone == Zone.None)
            return;

        // 自分1枚だけのグループを自分自身へ落とすのは無意味
        bool selfOnly = ReferenceEquals(_dropTarget, srcGroup) && srcGroup.Panes.Count == 1;
        if (selfOnly) return;

        _suspendRebuild = true;
        try
        {
            // 元グループから取り外す
            srcGroup.Panes.Remove(src);
            if (srcGroup.ActiveIndex >= srcGroup.Panes.Count)
                srcGroup.ActiveIndex = srcGroup.Panes.Count - 1;

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

            Root = Normalize(Root);   // 空グループ削除・単一子の畳み込み（suspend中なので再構築されない）
        }
        finally
        {
            _suspendRebuild = false;
        }
        Rebuild();
    }

    // 対象グループの隣に、ドラッグしたパネルを新グループとして分割配置する。
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

    // ツリー内で oldN を newN に差し替える（親のリストを更新、ルートなら Root を差し替え）。
    private void ReplaceNode(DockNode oldN, DockNode newN)
    {
        var parent = oldN.Parent;
        if (parent is null)
        {
            newN.Parent = null;
            Root = newN;   // suspend 中は再構築されない
            return;
        }
        int i = parent.Children.IndexOf(oldN);
        if (i >= 0) parent.Children[i] = newN;
        newN.Parent = parent;
    }

    // 空の TabGroup を除去し、子が1つだけの Split を畳む。比率を合計1へ正規化する。
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
                if (kids.Count == 1) return kids[0];   // 単一子 → 畳む

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
            _ => r,   // Center は全体
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
