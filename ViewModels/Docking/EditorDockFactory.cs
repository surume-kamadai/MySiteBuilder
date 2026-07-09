using Avalonia.Layout;
using MySiteBuilder.Controls.Docking;

namespace MySiteBuilder.ViewModels.Docking;

// ============================================================
// 初期レイアウト（DockNode ツリー）の組み立て。
//   上段: ページ
//   下段: [ツール(左)] | [キャンバス(中央)] | [レイヤー/プロパティ タブ(右)]
//   自作ドッキングエンジン(DockArea)がこのツリーを描画し、
//   以降はユーザーがドラッグで自由に組み替える。
// ============================================================
public class EditorDockFactory
{
    private readonly MainWindowViewModel _editor;

    public EditorDockFactory(MainWindowViewModel editor) => _editor = editor;

    public DockLayout CreateLayout()
    {
        var tools = Solo(new ToolsPanel(_editor), canClose: false);
        // キャンバスは固定パネル（ドキュメント領域相当）。ドラッグ/タブ化/フロート不可・ドロップ先にもならない。
        var canvas = Solo(new CanvasPanel(_editor), canClose: false, locked: true);

        // 右列: レイヤーとプロパティを既定でタブにまとめ、プロパティを前面に
        var right = new DockTabGroup { ActiveIndex = 1 };
        right.Panes.Add(Pane(new ExplorerPanel(_editor)));
        right.Panes.Add(Pane(new InspectorPanel(_editor)));

        var centerRow = new DockSplit { Orientation = Orientation.Horizontal };
        centerRow.Children.Add(tools);
        centerRow.Children.Add(canvas);
        centerRow.Children.Add(right);
        centerRow.Proportions.Add(0.18);
        centerRow.Proportions.Add(0.60);
        centerRow.Proportions.Add(0.22);

        var pages = Solo(new PagesPanel(_editor), canClose: false);

        var root = new DockSplit { Orientation = Orientation.Vertical };
        root.Children.Add(pages);
        root.Children.Add(centerRow);
        root.Proportions.Add(0.08);
        root.Proportions.Add(0.92);
        return new DockLayout { Root = root };
    }

    // パネル1枚だけの TabGroup（見出し付きで統一的に扱う）。
    private static DockTabGroup Solo(PanelBase panel, bool canClose = true, bool locked = false)
    {
        var g = new DockTabGroup { ActiveIndex = 0 };
        g.Panes.Add(Pane(panel, canClose, locked));
        return g;
    }

    private static DockPane Pane(PanelBase panel, bool canClose = true, bool locked = false) => new()
    {
        Id = panel.Id,
        Title = panel.Title,
        Content = panel,
        CanClose = canClose,
        Locked = locked,
    };
}
