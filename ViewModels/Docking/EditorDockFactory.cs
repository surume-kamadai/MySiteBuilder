using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace MySiteBuilder.ViewModels.Docking;

// ============================================================
// ドッキングレイアウトの構築（Dock.Model.Mvvm.Factory）。
//   上段: ページパネル
//   下段: [ツール/レイヤー(左)] | [キャンバス(中央)] | [プロパティ(右)]
//   各境界はスプリッタでリサイズ可能、パネルはドラッグで再配置/フロート可能。
// ============================================================
public class EditorDockFactory : Factory
{
    private readonly MainWindowViewModel _editor;

    public EditorDockFactory(MainWindowViewModel editor) => _editor = editor;

    public override IRootDock CreateLayout()
    {
        var tools = new ToolsPanel(_editor);
        var explorer = new ExplorerPanel(_editor);
        var inspector = new InspectorPanel(_editor);
        var pages = new PagesPanel(_editor);
        var canvas = new CanvasPanel(_editor);

        // 左列: ツール（上）＋レイヤー（下）を縦に積む
        var leftColumn = new ProportionalDock
        {
            Proportion = 0.18,
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(
                new ToolDock
                {
                    Proportion = 0.62,
                    Alignment = Alignment.Left,
                    ActiveDockable = tools,
                    VisibleDockables = CreateList<IDockable>(tools),
                },
                new ProportionalDockSplitter(),
                new ToolDock
                {
                    Proportion = 0.38,
                    Alignment = Alignment.Left,
                    ActiveDockable = explorer,
                    VisibleDockables = CreateList<IDockable>(explorer),
                }),
        };

        var documentArea = new DocumentDock
        {
            Proportion = 0.60,
            CanCreateDocument = false,
            IsCollapsable = false,
            ActiveDockable = canvas,
            VisibleDockables = CreateList<IDockable>(canvas),
        };

        var rightColumn = new ToolDock
        {
            Proportion = 0.22,
            Alignment = Alignment.Right,
            ActiveDockable = inspector,
            VisibleDockables = CreateList<IDockable>(inspector),
        };

        var centerRow = new ProportionalDock
        {
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(
                leftColumn,
                new ProportionalDockSplitter(),
                documentArea,
                new ProportionalDockSplitter(),
                rightColumn),
        };

        var pagesRow = new ToolDock
        {
            Proportion = 0.08,
            Alignment = Alignment.Top,
            ActiveDockable = pages,
            VisibleDockables = CreateList<IDockable>(pages),
        };

        var mainLayout = new ProportionalDock
        {
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(
                pagesRow,
                new ProportionalDockSplitter(),
                centerRow),
        };

        var root = CreateRootDock();
        root.Id = "Root";
        root.Title = "Root";
        root.ActiveDockable = mainLayout;
        root.DefaultDockable = mainLayout;
        root.VisibleDockables = CreateList<IDockable>(mainLayout);
        return root;
    }
}
