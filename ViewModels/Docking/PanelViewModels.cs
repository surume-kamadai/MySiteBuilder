namespace MySiteBuilder.ViewModels.Docking;

// ============================================================
// ドッキングパネルの ViewModel（自作ドッキングエンジン用）。
//   各パネルは共有のエディタVM(MainWindowViewModel)を Editor として持ち、
//   View は MainWindow.axaml の DataTemplate（型で一致）経由で描画される。
//   DockPane.Content にこのインスタンスを入れると、対応する DataTemplate が
//   自動選択される。
// ============================================================

/// <summary>全パネル共通の土台。</summary>
public abstract class PanelBase
{
    public string Id { get; }
    public string Title { get; }
    public MainWindowViewModel Editor { get; }

    protected PanelBase(MainWindowViewModel editor, string id, string title)
    {
        Editor = editor;
        Id = id;
        Title = title;
    }
}

/// <summary>ツール（要素追加）パネル。</summary>
public sealed class ToolsPanel : PanelBase
{
    public ToolsPanel(MainWindowViewModel editor) : base(editor, "Tools", "ツール") { }
}

/// <summary>レイヤー（要素一覧＋重ね順/グループ）パネル。</summary>
public sealed class ExplorerPanel : PanelBase
{
    public ExplorerPanel(MainWindowViewModel editor) : base(editor, "Explorer", "レイヤー") { }
}

/// <summary>プロパティ（Inspector）パネル。</summary>
public sealed class InspectorPanel : PanelBase
{
    public InspectorPanel(MainWindowViewModel editor) : base(editor, "Inspector", "プロパティ") { }
}

/// <summary>ページ（ページタブ＋フォルダ）パネル。</summary>
public sealed class PagesPanel : PanelBase
{
    public PagesPanel(MainWindowViewModel editor) : base(editor, "Pages", "ページ") { }
}

/// <summary>キャンバス（編集領域）パネル。</summary>
public sealed class CanvasPanel : PanelBase
{
    public CanvasPanel(MainWindowViewModel editor) : base(editor, "Canvas", "キャンバス") { }
}
