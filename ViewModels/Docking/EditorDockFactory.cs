using System;
using System.Collections.Generic;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace MySiteBuilder.ViewModels.Docking;

// ============================================================
// ドッキングレイアウトの構築（Dock.Model.Mvvm.Factory）。
//   上段: ページパネル
//   下段: [ツール(左)] | [キャンバス(中央)] | [レイヤー/プロパティ タブ(右)]
//   各境界はスプリッタでリサイズ可能、パネルはドラッグで再配置/タブ化/フロート可能
//   （Dock.Avalonia の標準機能。右側のレイヤー/プロパティは Photoshop の
//   Layers/Channels/Paths のように既定でタブにまとめている）。
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

        // 左列: ツールのみ
        var leftColumn = new ToolDock
        {
            Proportion = 0.18,
            Alignment = Alignment.Left,
            ActiveDockable = tools,
            VisibleDockables = CreateList<IDockable>(tools),
        };

        var documentArea = new DocumentDock
        {
            Proportion = 0.60,
            CanCreateDocument = false,
            IsCollapsable = false,
            ActiveDockable = canvas,
            VisibleDockables = CreateList<IDockable>(canvas),
        };

        // 右列: レイヤー（エクスプローラー）とプロパティ（インスペクター）を既定でタブにまとめる
        var rightColumn = new ToolDock
        {
            Proportion = 0.22,
            Alignment = Alignment.Right,
            ActiveDockable = inspector,
            VisibleDockables = CreateList<IDockable>(explorer, inspector),
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

    // タブをドラッグして切り離す／「Float」を選ぶと、切り離し先のネイティブウィンドウを
    // どう生成するかをここで登録する。これが無いとパネルを外部ウィンドウ化できない。
    public override void InitLayout(IDockable layout)
    {
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new HostWindow(),
        };

        base.InitLayout(layout);
    }
}
