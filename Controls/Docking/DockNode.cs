using System.Collections.Generic;
using Avalonia.Layout;

namespace MySiteBuilder.Controls.Docking;

// ============================================================
// 自作ドッキングエンジンのデータモデル（フローチャート図3）。
//   レイアウト全体を「Split / TabGroup / Pane」の3種で表現する。
//     - DockSplit    : 方向（横/縦）＋子ノードの比率。子は Split か TabGroup。
//     - DockTabGroup : タブとして重なる Pane 群＋アクティブ番号。
//     - DockPane     : 1枚のパネル。中身(Content)だけ差し替える“同じ箱”。
//   Photoshop 同様、単独パネルも「1枚だけの TabGroup」として扱い、
//   すべてのパネルを同一の見た目・操作で統一する。
// ============================================================

/// <summary>レイアウトツリーのノード（Split か TabGroup）。</summary>
public abstract class DockNode
{
    /// <summary>親 Split（ルートなら null）。組み替え時の差し替えに使う。</summary>
    public DockSplit? Parent { get; set; }
}

/// <summary>子ノードを一定方向に並べ、境界をスプリッタでリサイズできる分割ノード。</summary>
public sealed class DockSplit : DockNode
{
    /// <summary>Horizontal=左右に並べる（列）／Vertical=上下に積む（行）。</summary>
    public Orientation Orientation { get; set; } = Orientation.Horizontal;

    /// <summary>子ノード（Split または TabGroup）。</summary>
    public List<DockNode> Children { get; } = new();

    /// <summary>各子の比率（Children と同じ数・合計1.0に正規化）。</summary>
    public List<double> Proportions { get; } = new();
}

/// <summary>複数の Pane をタブとして重ね、1枚をアクティブ表示するノード。</summary>
public sealed class DockTabGroup : DockNode
{
    /// <summary>この場所に重なっているパネル群。</summary>
    public List<DockPane> Panes { get; } = new();

    /// <summary>アクティブ（前面表示）なパネルの添字。</summary>
    public int ActiveIndex { get; set; }

    /// <summary>現在アクティブなパネル（無ければ null）。</summary>
    public DockPane? Active =>
        ActiveIndex >= 0 && ActiveIndex < Panes.Count ? Panes[ActiveIndex] : null;
}

/// <summary>1枚のパネル。中身(Content)は DataTemplate で解決される ViewModel。</summary>
public sealed class DockPane
{
    public string Id { get; set; } = "";

    /// <summary>タブ見出しに表示する名前。</summary>
    public string Title { get; set; } = "";

    /// <summary>パネルの中身（ツール/レイヤー/プロパティ等の ViewModel）。</summary>
    public object? Content { get; set; }

    /// <summary>閉じる操作を許可するか（キャンバス等は false）。</summary>
    public bool CanClose { get; set; }

    /// <summary>所属する TabGroup（ドラッグ元の特定に使う）。</summary>
    public DockTabGroup? Owner { get; set; }
}
