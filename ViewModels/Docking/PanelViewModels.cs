using Dock.Model.Mvvm.Controls;

namespace MySiteBuilder.ViewModels.Docking;

// ============================================================
// ドッキングパネルのViewModel（Dock.Model.Mvvm）。
//   各パネルは共有のエディタVM(MainWindowViewModel)を Editor として持ち、
//   Viewは DataTemplate 経由で Editor にバインドする。
// ============================================================

/// <summary>ツール（要素追加）パネル。</summary>
public class ToolsPanel : Tool
{
    public MainWindowViewModel Editor { get; }
    public ToolsPanel(MainWindowViewModel editor)
    {
        Editor = editor;
        Id = "Tools";
        Title = "ツール";
        CanClose = false;
    }
}

/// <summary>レイヤー（要素一覧＋重ね順/グループ）パネル。</summary>
public class ExplorerPanel : Tool
{
    public MainWindowViewModel Editor { get; }
    public ExplorerPanel(MainWindowViewModel editor)
    {
        Editor = editor;
        Id = "Explorer";
        Title = "レイヤー";
        CanClose = false;
    }
}

/// <summary>プロパティ（Inspector）パネル。</summary>
public class InspectorPanel : Tool
{
    public MainWindowViewModel Editor { get; }
    public InspectorPanel(MainWindowViewModel editor)
    {
        Editor = editor;
        Id = "Inspector";
        Title = "プロパティ";
        CanClose = false;
    }
}

/// <summary>ページ（ページタブ＋フォルダ）パネル。</summary>
public class PagesPanel : Tool
{
    public MainWindowViewModel Editor { get; }
    public PagesPanel(MainWindowViewModel editor)
    {
        Editor = editor;
        Id = "Pages";
        Title = "ページ";
        CanClose = false;
    }
}

/// <summary>キャンバス（ドキュメント）パネル。</summary>
public class CanvasPanel : Document
{
    public MainWindowViewModel Editor { get; }
    public CanvasPanel(MainWindowViewModel editor)
    {
        Editor = editor;
        Id = "Canvas";
        Title = "キャンバス";
        CanClose = false;
    }
}
