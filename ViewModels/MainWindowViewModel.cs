using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MySiteBuilder.Controls.Docking;
using MySiteBuilder.Core.Export;
using MySiteBuilder.Core.Models;
using MySiteBuilder.Core.Serialization;
using MySiteBuilder.ViewModels.Docking;

namespace MySiteBuilder.ViewModels;

// ============================================================
// エディタの中核ViewModel（Phase 2）
//   - データは Core の SiteProject / SiteElement をそのまま使う
//   - 要素の追加・選択・削除、Inspector編集、HTML/Blade出力、保存/読込を担う
// ============================================================
public class MainWindowViewModel : ViewModelBase
{
    private const int HistoryLimit = 50;

    private SiteProject _project = CreateInitialProject();
    private SiteElement? _selected;                       // プライマリ選択（Inspector編集対象）
    private readonly List<SiteElement> _selection = new(); // 複数選択集合
    private int _elementCounter;
    private int _pageCounter = 1;
    private int _folderCounter;
    private readonly ObservableCollection<SitePage> _pageTabs = new();
    private readonly ObservableCollection<string> _folderNames = new();
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private bool _restoring;
    private string _statusMessage = "要素を追加してデザインを始めましょう。";

    /// <summary>フォルダ未所属を表す表示名。</summary>
    private const string NoFolder = "（直下）";

    /// <summary>キャンバス再描画を要求するイベント（Viewのキャンバスが購読）。</summary>
    public event Action? RedrawRequested;

    // --- ドッキングレイアウト（自作ドッキングエンジン） ---
    private readonly EditorDockFactory _dockFactory;
    private DockLayout? _dockLayout;

    /// <summary>DockArea にバインドするレイアウト（ドック本体＋浮遊パネル）。</summary>
    public DockLayout? DockLayout
    {
        get => _dockLayout;
        set { _dockLayout = value; OnPropertyChanged(); }
    }

    public MainWindowViewModel()
    {
        RebuildPageTabs();
        RebuildFolderNames();
        // 起動時のサンプル要素
        AddElement("Label");
        if (_selected is { } l) { l.Properties.Text = "見出しテキスト"; l.Properties.Fontsize = 32; }
        AddElement("Button");
        Select(null);
        StatusMessage = "要素を追加してデザインを始めましょう。";

        // ドッキングレイアウトを構築
        _dockFactory = new EditorDockFactory(this);
        BuildLayout();
    }

    private void BuildLayout() => DockLayout = _dockFactory.CreateLayout();

    /// <summary>パネル配置を初期状態に戻す。</summary>
    public void ResetLayout() => BuildLayout();

    private static SiteProject CreateInitialProject() => new()
    {
        Settings = new ProjectSettings
        {
            ProjectName = "my-site",
            Canvas = new CanvasSize { Width = 800, Height = 600, MobileWidth = 375, MobileHeight = 800 },
            OutputType = "static",
            SiteBgColor = "#f1f2f6",
            Seo = new SiteSeo { SiteName = "", Lang = "ja", Description = "", OgImage = "" },
        },
        Folders = new List<PageFolder>(),
        Pages = new List<SitePage>
        {
            new() { Id = "page_1", Name = "index", Elements = new List<SiteElement>(), FolderId = null,
                    BgColor = "", Seo = new PageSeo() },
        },
        ActivePageId = "page_1",
    };

    // --- 公開状態 ---

    public SiteProject Project => _project;

    public SitePage ActivePage =>
        _project.Pages.FirstOrDefault(p => p.Id == _project.ActivePageId) ?? _project.Pages[0];

    public IList<SiteElement> Elements => ActivePage.Elements;

    // --- ページ（複数ページ） ---

    /// <summary>ページタブ用のコレクション（_project.Pages のミラー）。</summary>
    public ObservableCollection<SitePage> PageTabs => _pageTabs;

    /// <summary>選択中ページ（ListBoxのSelectedItemと双方向）。</summary>
    public SitePage? SelectedPage
    {
        get => ActivePage;
        set { if (value != null) SwitchPage(value); }
    }

    /// <summary>アクティブページの名前（ファイル名になる）。編集すると安全な文字に整形。</summary>
    public string ActivePageName
    {
        get => ActivePage.Name;
        set
        {
            var clean = System.Text.RegularExpressions.Regex.Replace(value?.Trim() ?? "", "[^a-zA-Z0-9_-]", "");
            ActivePage.Name = string.IsNullOrEmpty(clean) ? ActivePage.Name : clean;
            OnPropertyChanged();
            RebuildPageTabs();
        }
    }

    public bool CanDeletePage => _project.Pages.Count > 1;

    private void RebuildPageTabs()
    {
        _pageTabs.Clear();
        foreach (var p in _project.Pages) _pageTabs.Add(p);
        OnPropertyChanged(nameof(SelectedPage));
        OnPropertyChanged(nameof(CanDeletePage));
    }

    /// <summary>ページ切替時に更新されるコンテキスト系プロパティをまとめて通知。</summary>
    private void NotifyPageContext()
    {
        OnPropertyChanged(nameof(SelectedPage));
        OnPropertyChanged(nameof(ActivePageName));
        OnPropertyChanged(nameof(ActivePageFolderName));
        OnPropertyChanged(nameof(PageBackground));
        OnPropertyChanged(nameof(PageBg));
        OnPropertyChanged(nameof(PageSeoTitle));
        OnPropertyChanged(nameof(PageSeoDescription));
        OnPropertyChanged(nameof(CanDeletePage));
        NotifyExplorer();
    }

    public void SwitchPage(SitePage page)
    {
        if (page.Id == _project.ActivePageId) return;
        _project.ActivePageId = page.Id;
        Select(null);
        NotifyPageContext();
        RedrawRequested?.Invoke();
        StatusMessage = $"ページ「{page.Name}」を表示中。";
    }

    public void AddPage()
    {
        PushUndo();
        _pageCounter++;
        var id = "page_" + _pageCounter;
        var page = new SitePage
        {
            Id = id, Name = "page" + _pageCounter, Elements = new List<SiteElement>(),
            FolderId = null, BgColor = "", Seo = new PageSeo(),
        };
        _project.Pages.Add(page);
        _project.ActivePageId = id;
        RebuildPageTabs();
        Select(null);
        NotifyPageContext();
        RedrawRequested?.Invoke();
        StatusMessage = $"ページ「{page.Name}」を追加しました。";
    }

    public void DeleteActivePage()
    {
        if (_project.Pages.Count <= 1) return;
        PushUndo();
        var idx = _project.Pages.FindIndex(p => p.Id == _project.ActivePageId);
        if (idx < 0) return;
        var removed = _project.Pages[idx].Name;
        _project.Pages.RemoveAt(idx);
        _project.ActivePageId = _project.Pages[Math.Max(0, idx - 1)].Id;
        RebuildPageTabs();
        Select(null);
        NotifyPageContext();
        RedrawRequested?.Invoke();
        StatusMessage = $"ページ「{removed}」を削除しました。";
    }

    public double CanvasWidth => _project.Settings.Canvas.Width;
    public double CanvasHeight => _project.Settings.Canvas.Height;

    /// <summary>キャンバスの背景色（ページ個別→サイト共通→既定）。</summary>
    public string PageBackground
    {
        get
        {
            if (!string.IsNullOrEmpty(ActivePage.BgColor)) return ActivePage.BgColor!;
            if (!string.IsNullOrEmpty(_project.Settings.SiteBgColor)) return _project.Settings.SiteBgColor!;
            return "#f1f2f6";
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public SiteElement? Selected => _selected;
    public bool HasSelection => _selected != null;
    public bool IsImageSelected => _selected?.Type == "Image";
    public IReadOnlyList<SiteElement> Selection => _selection;

    public bool CanGroup => _selection.Count >= 2;
    public bool CanUngroup => _selected?.Type == "Group";

    /// <summary>ある要素が選択集合に含まれるか（キャンバスの枠描画用）。</summary>
    public bool IsSelected(SiteElement el) => _selection.Contains(el);

    // --- エクスプローラー（要素一覧） ---

    // コピーを返すことで、並べ替え時に NotifyExplorer で ListBox が実際に並び替わる
    // （同一リスト参照だと ItemsSource が更新を検知しないため）。
    public IReadOnlyList<SiteElement> ExplorerItems => ActivePage.Elements.ToList();

    /// <summary>一覧の選択（ListBoxのSelectedItemと双方向。プライマリと同期）。</summary>
    public SiteElement? ExplorerSelected
    {
        get => _selected;
        set { if (value != null) Select(value); }
    }

    private void NotifyExplorer()
    {
        OnPropertyChanged(nameof(ExplorerItems));
        OnPropertyChanged(nameof(ExplorerSelected));
        OnPropertyChanged(nameof(CanGroup));
        OnPropertyChanged(nameof(CanUngroup));
    }

    // --- 選択 ---

    /// <summary>単一選択（既存の複数選択はクリア）。</summary>
    public void Select(SiteElement? el)
    {
        _selection.Clear();
        if (el != null) _selection.Add(el);
        _selected = el;
        AfterSelectionChanged();
    }

    /// <summary>Ctrl/Shift+クリック用: 選択集合へトグル追加。</summary>
    public void ToggleSelect(SiteElement el)
    {
        if (_selection.Contains(el))
        {
            _selection.Remove(el);
            _selected = _selection.Count > 0 ? _selection[^1] : null;
        }
        else
        {
            _selection.Add(el);
            _selected = el;
        }
        AfterSelectionChanged();
    }

    /// <summary>複数選択を保ったままプライマリだけ切り替える。</summary>
    public void SetPrimary(SiteElement el)
    {
        if (!_selection.Contains(el)) { Select(el); return; }
        _selected = el;
        AfterSelectionChanged();
    }

    private void AfterSelectionChanged()
    {
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanGroup));
        OnPropertyChanged(nameof(CanUngroup));
        OnPropertyChanged(nameof(ExplorerSelected));
        NotifyInspector();
        RedrawRequested?.Invoke();
    }

    // --- 重ね順（z-order） ---

    public void BringToFront() => Reorder(list => { list.Remove(_selected!); list.Add(_selected!); });
    public void SendToBack() => Reorder(list => { list.Remove(_selected!); list.Insert(0, _selected!); });
    public void BringForward() => Reorder(list =>
    {
        int i = list.IndexOf(_selected!);
        if (i >= 0 && i < list.Count - 1) { list.RemoveAt(i); list.Insert(i + 1, _selected!); }
    });
    public void SendBackward() => Reorder(list =>
    {
        int i = list.IndexOf(_selected!);
        if (i > 0) { list.RemoveAt(i); list.Insert(i - 1, _selected!); }
    });

    private void Reorder(Action<IList<SiteElement>> op)
    {
        if (_selected == null) return;
        var list = ContainingList(_selected);
        if (list == null) return;
        PushUndo();
        op(list);
        NotifyExplorer();
        RedrawRequested?.Invoke();
    }

    /// <summary>レイヤー一覧のドラッグ&ドロップ並べ替え。target の位置へ dragged を移動。</summary>
    public void MoveElement(SiteElement dragged, SiteElement? target)
    {
        var list = ContainingList(dragged);
        if (list == null) return;
        int di = list.IndexOf(dragged);
        if (di < 0) return;
        // ターゲットが同じリストに無ければ末尾へ
        int ti = target != null && list.Contains(target) ? list.IndexOf(target) : list.Count;
        if (di == ti) return;

        PushUndo();
        list.RemoveAt(di);
        ti = target != null && list.Contains(target) ? list.IndexOf(target) : list.Count;
        if (ti < 0) ti = list.Count;
        list.Insert(ti, dragged);
        Select(dragged);
        NotifyExplorer();
        RedrawRequested?.Invoke();
    }

    /// <summary>ページ一覧のドラッグ&ドロップ並べ替え。target の位置へ dragged を移動。</summary>
    public void MovePage(SitePage dragged, SitePage? target)
    {
        var list = _project.Pages;
        int di = list.IndexOf(dragged);
        if (di < 0) return;
        int ti = target != null && list.Contains(target) ? list.IndexOf(target) : list.Count;
        if (di == ti) return;

        PushUndo();
        list.RemoveAt(di);
        ti = target != null && list.Contains(target) ? list.IndexOf(target) : list.Count;
        if (ti < 0) ti = list.Count;
        list.Insert(ti, dragged);
        RebuildPageTabs();
    }

    // 要素を含むリスト（トップレベル or 親Groupのchildren）
    private IList<SiteElement>? ContainingList(SiteElement el)
    {
        if (ActivePage.Elements.Contains(el)) return ActivePage.Elements;
        foreach (var top in ActivePage.Elements)
            if (top.Children != null && top.Children.Contains(el)) return top.Children;
        return null;
    }

    // --- グループ化 / 解除 ---

    public void GroupSelection()
    {
        if (_selection.Count < 2) return;
        // トップレベルにある要素だけをグループ化対象にする
        var targets = ActivePage.Elements.Where(_selection.Contains).ToList();
        if (targets.Count < 2) return;
        PushUndo();

        double minX = targets.Min(e => e.Transform.X);
        double minY = targets.Min(e => e.Transform.Y);
        double maxX = targets.Max(e => e.Transform.X + e.Transform.Width);
        double maxY = targets.Max(e => e.Transform.Y + e.Transform.Height);

        _elementCounter++;
        var group = new SiteElement
        {
            Id = "group_" + _elementCounter,
            Type = "Group",
            Transform = new ElementTransform { X = minX, Y = minY, Width = maxX - minX, Height = maxY - minY },
            Properties = new ElementProperties { Name = "Group " + _elementCounter, Bgcolor = "transparent" },
            Children = new List<SiteElement>(),
        };

        int insertAt = ActivePage.Elements.IndexOf(targets[0]);
        foreach (var t in targets)
        {
            ActivePage.Elements.Remove(t);
            t.Transform.X -= minX;   // グループ原点に対する相対座標へ
            t.Transform.Y -= minY;
            group.Children.Add(t);
        }
        ActivePage.Elements.Insert(Math.Clamp(insertAt, 0, ActivePage.Elements.Count), group);

        Select(group);
        NotifyExplorer();
        StatusMessage = $"{targets.Count}個の要素をグループ化しました。";
    }

    public void UngroupSelection()
    {
        if (_selected is not { Type: "Group" } g) return;
        PushUndo();
        int idx = ActivePage.Elements.IndexOf(g);
        if (idx < 0) return;
        ActivePage.Elements.RemoveAt(idx);
        var kids = g.Children ?? new List<SiteElement>();
        for (int i = 0; i < kids.Count; i++)
        {
            kids[i].Transform.X += g.Transform.X;   // 絶対座標へ戻す
            kids[i].Transform.Y += g.Transform.Y;
            ActivePage.Elements.Insert(idx + i, kids[i]);
        }
        _selection.Clear();
        _selection.AddRange(kids);
        _selected = kids.Count > 0 ? kids[0] : null;
        AfterSelectionChanged();
        NotifyExplorer();
        StatusMessage = "グループを解除しました。";
    }

    /// <summary>Inspectorに紐づく全プロパティの更新通知（選択変更・ドラッグ時）。</summary>
    public void NotifyInspector()
    {
        OnPropertyChanged(nameof(SelType));
        OnPropertyChanged(nameof(SelName));
        OnPropertyChanged(nameof(SelText));
        OnPropertyChanged(nameof(SelX));
        OnPropertyChanged(nameof(SelY));
        OnPropertyChanged(nameof(SelW));
        OnPropertyChanged(nameof(SelH));
        OnPropertyChanged(nameof(SelBgColor));
        OnPropertyChanged(nameof(SelColor));
        OnPropertyChanged(nameof(SelFontSize));
        OnPropertyChanged(nameof(SelRoute));
        OnPropertyChanged(nameof(SelCornerRadius));
        OnPropertyChanged(nameof(SelStrokeOn));
        OnPropertyChanged(nameof(SelStrokeWidth));
        OnPropertyChanged(nameof(SelStrokeColor));
        OnPropertyChanged(nameof(SelGradientOn));
        OnPropertyChanged(nameof(SelGradientType));
        OnPropertyChanged(nameof(SelGradientDir));
        OnPropertyChanged(nameof(SelGradientC1));
        OnPropertyChanged(nameof(SelGradientC2));
        OnPropertyChanged(nameof(IsLayerStyleSelected));
        OnPropertyChanged(nameof(IsImageSelected));
        OnPropertyChanged(nameof(IsButtonSelected));
        OnPropertyChanged(nameof(IsTextInputSelected));
        OnPropertyChanged(nameof(SelRole));
        OnPropertyChanged(nameof(SelMethod));
        OnPropertyChanged(nameof(SelSuccessMessage));
        OnPropertyChanged(nameof(SelInputName));
        OnPropertyChanged(nameof(SelInputType));
        OnPropertyChanged(nameof(SelRequired));
        OnPropertyChanged(nameof(IsSliderSelected));
        OnPropertyChanged(nameof(IsGridSelected));
        OnPropertyChanged(nameof(IsAccordionSelected));
        OnPropertyChanged(nameof(SliderItems));
        OnPropertyChanged(nameof(GridItems));
        OnPropertyChanged(nameof(AccordionItems));
        OnPropertyChanged(nameof(SliderEffect));
        OnPropertyChanged(nameof(SliderAutoplay));
        OnPropertyChanged(nameof(SliderNavigation));
        OnPropertyChanged(nameof(GridColumns));
        OnPropertyChanged(nameof(GridGap));
        OnPropertyChanged(nameof(AccordionOpenFirst));
    }

    /// <summary>キャンバス側でジオメトリが変わった（ドラッグ）時にInspectorと再描画を更新。</summary>
    public void NotifyGeometryChanged()
    {
        OnPropertyChanged(nameof(SelX));
        OnPropertyChanged(nameof(SelY));
        OnPropertyChanged(nameof(SelW));
        OnPropertyChanged(nameof(SelH));
        RedrawRequested?.Invoke();
    }

    // --- Inspector プロキシ（選択中要素への読み書き）---

    public string SelType => _selected?.Type ?? "";

    public string SelName
    {
        get => _selected?.Properties.Name ?? "";
        set { if (_selected != null) { PushUndo(); _selected.Properties.Name = value; OnPropertyChanged(); } }
    }

    public string SelText
    {
        get => _selected?.Properties.Text ?? "";
        set { if (_selected != null) { PushUndo(); _selected.Properties.Text = value; OnPropertyChanged(); Redraw(); } }
    }

    public double SelX
    {
        get => _selected?.Transform.X ?? 0;
        set { if (_selected != null) { PushUndo(); _selected.Transform.X = value; OnPropertyChanged(); Redraw(); } }
    }

    public double SelY
    {
        get => _selected?.Transform.Y ?? 0;
        set { if (_selected != null) { PushUndo(); _selected.Transform.Y = value; OnPropertyChanged(); Redraw(); } }
    }

    public double SelW
    {
        get => _selected?.Transform.Width ?? 0;
        set { if (_selected != null) { PushUndo(); _selected.Transform.Width = value; OnPropertyChanged(); Redraw(); } }
    }

    public double SelH
    {
        get => _selected?.Transform.Height ?? 0;
        set { if (_selected != null) { PushUndo(); _selected.Transform.Height = value; OnPropertyChanged(); Redraw(); } }
    }

    public string SelBgColor
    {
        get => _selected?.Properties.Bgcolor ?? "";
        set { if (_selected != null) { PushUndo(); _selected.Properties.Bgcolor = value; OnPropertyChanged(); Redraw(); } }
    }

    public string SelColor
    {
        get => _selected?.Properties.Color ?? "";
        set { if (_selected != null) { PushUndo(); _selected.Properties.Color = value; OnPropertyChanged(); Redraw(); } }
    }

    public double SelFontSize
    {
        get => _selected?.Properties.Fontsize ?? 16;
        set { if (_selected != null) { PushUndo(); _selected.Properties.Fontsize = value; OnPropertyChanged(); Redraw(); } }
    }

    public string SelRoute
    {
        get => _selected?.Properties.Route ?? "";
        set { if (_selected != null) { PushUndo(); _selected.Properties.Route = value; OnPropertyChanged(); } }
    }

    // --- レイヤースタイル プロキシ（layer-style.js に対応：塗りグラデ/境界線/角丸）---

    public double SelCornerRadius
    {
        get => _selected?.Properties.CornerRadius ?? 0;
        set { if (_selected != null) { PushUndo(); _selected.Properties.CornerRadius = value; OnPropertyChanged(); Redraw(); } }
    }

    public bool SelStrokeOn
    {
        get => _selected?.Properties.Stroke?.On ?? false;
        set { if (_selected != null) { PushUndo(); (_selected.Properties.Stroke ??= new StrokeConfig()).On = value; OnPropertyChanged(); Redraw(); } }
    }

    public double SelStrokeWidth
    {
        get => _selected?.Properties.Stroke?.Width ?? 2;
        set { if (_selected != null) { PushUndo(); (_selected.Properties.Stroke ??= new StrokeConfig()).Width = value; OnPropertyChanged(); Redraw(); } }
    }

    public string SelStrokeColor
    {
        get => _selected?.Properties.Stroke?.Color ?? "#000000";
        set { if (_selected != null) { PushUndo(); (_selected.Properties.Stroke ??= new StrokeConfig()).Color = value; OnPropertyChanged(); Redraw(); } }
    }

    public bool SelGradientOn
    {
        get => _selected?.Properties.Gradient?.On ?? false;
        set { if (_selected != null) { PushUndo(); (_selected.Properties.Gradient ??= new GradientConfig()).On = value; OnPropertyChanged(); Redraw(); } }
    }

    public string SelGradientType
    {
        get => _selected?.Properties.Gradient?.Type ?? "linear";
        set { if (_selected != null) { PushUndo(); (_selected.Properties.Gradient ??= new GradientConfig()).Type = value; OnPropertyChanged(); Redraw(); } }
    }

    public string SelGradientDir
    {
        get => _selected?.Properties.Gradient?.Dir ?? "v";
        set { if (_selected != null) { PushUndo(); (_selected.Properties.Gradient ??= new GradientConfig()).Dir = value; OnPropertyChanged(); Redraw(); } }
    }

    public string SelGradientC1
    {
        get => _selected?.Properties.Gradient?.C1 ?? "#4facfe";
        set { if (_selected != null) { PushUndo(); (_selected.Properties.Gradient ??= new GradientConfig()).C1 = value; OnPropertyChanged(); Redraw(); } }
    }

    public string SelGradientC2
    {
        get => _selected?.Properties.Gradient?.C2 ?? "#00f2fe";
        set { if (_selected != null) { PushUndo(); (_selected.Properties.Gradient ??= new GradientConfig()).C2 = value; OnPropertyChanged(); Redraw(); } }
    }

    public string[] GradientTypeOptions { get; } = { "linear", "radial" };
    public string[] GradientDirOptions { get; } = { "v", "h", "d1", "d2" };

    // レイヤースタイル欄を表示できる要素種別（キャンバスに実際に描画反映される図形のみ）
    public bool IsLayerStyleSelected => _selected != null && _selected.Type is "Rect" or "Circle" or "Triangle" or "Button";

    // --- フォーム設定プロキシ（Button / TextInput）---

    public bool IsButtonSelected => _selected?.Type == "Button";
    public bool IsTextInputSelected => _selected?.Type == "TextInput";

    public string[] RoleOptions { get; } = { "link", "submit" };
    public string[] MethodOptions { get; } = { "POST", "GET" };
    public string[] InputTypeOptions { get; } = { "text", "email", "tel", "number", "textarea" };

    public string SelRole
    {
        get => _selected?.Properties.Role ?? "link";
        set { if (_selected != null) { PushUndo(); _selected.Properties.Role = value; OnPropertyChanged(); Redraw(); } }
    }

    public string SelMethod
    {
        get => _selected?.Properties.Method ?? "POST";
        set { if (_selected != null) { PushUndo(); _selected.Properties.Method = value; OnPropertyChanged(); } }
    }

    public string SelSuccessMessage
    {
        get => _selected?.Properties.SuccessMessage ?? "";
        set { if (_selected != null) { PushUndo(); _selected.Properties.SuccessMessage = value; OnPropertyChanged(); } }
    }

    public string SelInputName
    {
        get => _selected?.Properties.InputName ?? "";
        set { if (_selected != null) { PushUndo(); _selected.Properties.InputName = value; OnPropertyChanged(); } }
    }

    public string SelInputType
    {
        get => _selected?.Properties.InputType ?? "text";
        set { if (_selected != null) { PushUndo(); _selected.Properties.InputType = value; OnPropertyChanged(); Redraw(); } }
    }

    public bool SelRequired
    {
        get => _selected?.Properties.Required ?? false;
        set { if (_selected != null) { PushUndo(); _selected.Properties.Required = value; OnPropertyChanged(); } }
    }

    // --- プロジェクト / ページ設定プロキシ ---

    public string ProjName
    {
        get => _project.Settings.ProjectName ?? "";
        set { PushUndo(); _project.Settings.ProjectName = value; OnPropertyChanged(); }
    }

    public string ProjSiteName
    {
        get => _project.Settings.Seo?.SiteName ?? "";
        set { PushUndo(); (_project.Settings.Seo ??= new SiteSeo()).SiteName = value; OnPropertyChanged(); }
    }

    public string ProjLang
    {
        get => _project.Settings.Seo?.Lang ?? "ja";
        set { PushUndo(); (_project.Settings.Seo ??= new SiteSeo()).Lang = value; OnPropertyChanged(); }
    }

    public string ProjDescription
    {
        get => _project.Settings.Seo?.Description ?? "";
        set { PushUndo(); (_project.Settings.Seo ??= new SiteSeo()).Description = value; OnPropertyChanged(); }
    }

    public string ProjSiteBgColor
    {
        get => _project.Settings.SiteBgColor ?? "";
        set { PushUndo(); _project.Settings.SiteBgColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageBackground)); Redraw(); }
    }

    public double CanvasWEdit
    {
        get => _project.Settings.Canvas.Width;
        set { PushUndo(); _project.Settings.Canvas.Width = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanvasWidth)); Redraw(); }
    }

    public double CanvasHEdit
    {
        get => _project.Settings.Canvas.Height;
        set { PushUndo(); _project.Settings.Canvas.Height = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanvasHeight)); Redraw(); }
    }

    public string PageBg
    {
        get => ActivePage.BgColor ?? "";
        set { PushUndo(); ActivePage.BgColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageBackground)); Redraw(); }
    }

    public string PageSeoTitle
    {
        get => ActivePage.Seo?.Title ?? "";
        set { PushUndo(); (ActivePage.Seo ??= new PageSeo()).Title = value; OnPropertyChanged(); }
    }

    public string PageSeoDescription
    {
        get => ActivePage.Seo?.Description ?? "";
        set { PushUndo(); (ActivePage.Seo ??= new PageSeo()).Description = value; OnPropertyChanged(); }
    }

    // --- フォルダ分類 ---

    public ObservableCollection<string> FolderNames => _folderNames;

    /// <summary>アクティブページの所属フォルダ名（未所属は「（直下）」）。</summary>
    public string ActivePageFolderName
    {
        get
        {
            var fid = ActivePage.FolderId;
            var f = _project.Folders.FirstOrDefault(x => x.Id == fid);
            return f?.Name ?? NoFolder;
        }
        set
        {
            PushUndo();
            if (value == NoFolder || string.IsNullOrEmpty(value))
                ActivePage.FolderId = null;
            else
                ActivePage.FolderId = _project.Folders.FirstOrDefault(x => x.Name == value)?.Id;
            OnPropertyChanged();
        }
    }

    public void AddFolder()
    {
        PushUndo();
        _folderCounter++;
        var id = "folder_" + _folderCounter;
        var name = "folder" + _folderCounter;
        _project.Folders.Add(new PageFolder { Id = id, Name = name });
        // 追加したフォルダに現在ページを入れる
        ActivePage.FolderId = id;
        RebuildFolderNames();
        OnPropertyChanged(nameof(ActivePageFolderName));
        StatusMessage = $"フォルダ「{name}」を作成し、現在ページを入れました。";
    }

    private void RebuildFolderNames()
    {
        _folderNames.Clear();
        _folderNames.Add(NoFolder);
        foreach (var f in _project.Folders) _folderNames.Add(f.Name);
    }

    // --- Undo / Redo ---

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>変更前の状態をスナップショットとして退避する（redoは無効化）。</summary>
    public void PushUndo()
    {
        if (_restoring) return;
        _undo.Push(ProjectSerializer.Serialize(_project));
        if (_undo.Count > HistoryLimit)
        {
            // 最古を捨てる（Stackなので詰め直し）
            var keep = _undo.ToArray(); // index0が最新
            _undo.Clear();
            for (int i = Math.Min(keep.Length, HistoryLimit) - 1; i >= 0; i--) _undo.Push(keep[i]);
        }
        _redo.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(ProjectSerializer.Serialize(_project));
        RestoreSnapshot(_undo.Pop());
        StatusMessage = "元に戻しました。";
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(ProjectSerializer.Serialize(_project));
        RestoreSnapshot(_redo.Pop());
        StatusMessage = "やり直しました。";
    }

    private void RestoreSnapshot(string json)
    {
        _restoring = true;
        try
        {
            _project = ProjectSerializer.Load(json);
            RecomputeCounters();
            RebuildPageTabs();
            RebuildFolderNames();
            _selection.Clear();
            _selected = null;
            NotifyEverything();
        }
        finally
        {
            _restoring = false;
        }
        RedrawRequested?.Invoke();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void RecomputeCounters()
    {
        _pageCounter = _project.Pages
            .Select(p => int.TryParse(p.Id.Replace("page_", ""), out var n) ? n : 0)
            .DefaultIfEmpty(1).Max();
        _folderCounter = _project.Folders
            .Select(f => int.TryParse(f.Id.Replace("folder_", ""), out var n) ? n : 0)
            .DefaultIfEmpty(0).Max();
        int maxEl = 0;
        void Walk(IEnumerable<SiteElement>? els)
        {
            foreach (var e in els ?? Enumerable.Empty<SiteElement>())
            {
                var us = e.Id.LastIndexOf('_');
                if (us >= 0 && int.TryParse(e.Id[(us + 1)..], out var n)) maxEl = Math.Max(maxEl, n);
                Walk(e.Children);
            }
        }
        foreach (var p in _project.Pages) Walk(p.Elements);
        _elementCounter = Math.Max(_elementCounter, maxEl);
    }

    /// <summary>プロジェクト差し替え後（Undo/Open）に全プロパティを再通知。</summary>
    private void NotifyEverything()
    {
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        OnPropertyChanged(nameof(ProjName));
        OnPropertyChanged(nameof(ProjSiteName));
        OnPropertyChanged(nameof(ProjLang));
        OnPropertyChanged(nameof(ProjDescription));
        OnPropertyChanged(nameof(ProjSiteBgColor));
        OnPropertyChanged(nameof(CanvasWEdit));
        OnPropertyChanged(nameof(CanvasHEdit));
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasSelection));
        NotifyPageContext();
        NotifyInspector();
    }

    // --- 要素の追加・削除 ---

    public void AddElement(string type)
    {
        PushUndo();
        _elementCounter++;
        bool square = type is "Circle" or "Triangle";
        bool composite = type is "Slider" or "ArticleGrid" or "Accordion";
        var el = new SiteElement
        {
            Id = type.ToLowerInvariant() + "_" + _elementCounter,
            Type = type,
            Transform = new ElementTransform
            {
                X = 60, Y = 60,
                Width = composite ? 600 : (square ? 120 : 150),
                Height = composite ? 280 : (square ? 120 : 50),
            },
            Properties = new ElementProperties
            {
                Name = type + " " + _elementCounter,
                Text = type == "Image" ? "https://placehold.co/150x150/png" : "テキスト",
                Bgcolor = DefaultBg(type),
                Color = "#000000",
                Fontsize = 16,
                Align = "left",
                FontFamily = "sans-serif",
                Lock = false,
                Route = "#",
                Method = "POST",
                Shadow = "none",
                Animation = "none",
                Opacity = 1,
                Role = type == "Button" ? "link" : "none",
                SuccessMessage = "送信ありがとうございました。",
                InputName = "",
                InputType = "text",
                Required = false,
            },
        };

        // 複合要素は既定の設定＋サンプル1件を入れておく
        switch (type)
        {
            case "Slider":
                el.Properties.Slider = new SliderConfig
                {
                    Effect = "slide", Speed = 600, Autoplay = true, Delay = 3000, Loop = true,
                    Pagination = true, Navigation = true, SlidesPerView = 1,
                    Slides = new List<SlideItem>
                    {
                        new() { Title = "スライド1", Text = "説明文", Image = "", LinkType = "none", Link = "" },
                    },
                };
                break;
            case "ArticleGrid":
                el.Properties.Grid = new GridConfig
                {
                    Columns = 3, Gap = 20, CardRadius = 8, ArrowColor = "#27ae60",
                    ImgRatio = "16/10", CardPadding = 18, SliderMode = false,
                    Items = new List<GridItem>
                    {
                        new() { Title = "記事1", Text = "本文", Image = "", LinkType = "none", Link = "" },
                    },
                };
                break;
            case "Accordion":
                el.Properties.Accordion = new AccordionConfig
                {
                    HeaderColor = "#2c3e50", HeaderBg = "#f7f9fa", BodyColor = "#555555", OpenFirst = true,
                    Items = new List<AccordionItem>
                    {
                        new() { Title = "質問1", Content = "回答1" },
                    },
                };
                break;
        }

        Elements.Add(el);
        Select(el);
        NotifyExplorer();
        StatusMessage = $"{type} を追加しました。";
    }

    public void DeleteSelected()
    {
        if (_selection.Count == 0) return;
        PushUndo();
        int n = _selection.Count;
        foreach (var el in _selection.ToList())
            ContainingList(el)?.Remove(el);
        Select(null);
        NotifyExplorer();
        StatusMessage = n > 1 ? $"{n}個の要素を削除しました。" : "要素を削除しました。";
    }

    /// <summary>選択集合の全要素を dx, dy だけ動かす（キャンバスの複数ドラッグ用）。</summary>
    public void MoveSelectionBy(double dx, double dy)
    {
        foreach (var el in _selection)
        {
            el.Transform.X = Math.Round(el.Transform.X + dx);
            el.Transform.Y = Math.Round(el.Transform.Y + dy);
        }
    }

    /// <summary>ファイル選択ダイアログで画像を選び、data URL 文字列を返す（キャンセルは null）。</summary>
    public async Task<string?> PickImageDataUrlAsync(TopLevel? top)
    {
        if (top == null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "画像を選択",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("画像")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.svg" },
                },
            },
        });
        if (files.Count == 0) return null;

        await using var stream = await files[0].OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var bytes = ms.ToArray();
        var ext = Path.GetExtension(files[0].Name).TrimStart('.').ToLowerInvariant();
        // main.js と同じ MIME 解決（svg→svg+xml, jpg→jpeg, 不明→png）
        var mime = ext switch { "svg" => "svg+xml", "jpg" => "jpeg", "" => "png", _ => ext };
        return $"data:image/{mime};base64,{Convert.ToBase64String(bytes)}";
    }

    /// <summary>選択中のImage要素に、ファイルから読み込んだ画像をdata URLとして設定する。</summary>
    public async Task PickImageForSelectedAsync(TopLevel? top)
    {
        if (_selected == null || _selected.Type != "Image") return;
        var url = await PickImageDataUrlAsync(top);
        if (url == null) return;
        PushUndo();
        _selected.Properties.Text = url;
        OnPropertyChanged(nameof(SelText));
        RedrawRequested?.Invoke();
        StatusMessage = "画像を読み込みました。";
    }

    // --- 複合要素（Slider / ArticleGrid / Accordion）のアイテム編集 ---

    public bool IsSliderSelected => _selected?.Type == "Slider";
    public bool IsGridSelected => _selected?.Type == "ArticleGrid";
    public bool IsAccordionSelected => _selected?.Type == "Accordion";

    public string[] SliderEffectOptions { get; } = { "slide", "fade", "cube", "coverflow", "cards", "grid" };

    public List<SlideItem>? SliderItems => _selected?.Properties.Slider?.Slides;
    public List<GridItem>? GridItems => _selected?.Properties.Grid?.Items;
    public List<AccordionItem>? AccordionItems => _selected?.Properties.Accordion?.Items;

    // Slider オプション
    public string SliderEffect
    {
        get => _selected?.Properties.Slider?.Effect ?? "slide";
        set { if (_selected?.Properties.Slider is { } s) { PushUndo(); s.Effect = value; OnPropertyChanged(); } }
    }
    public bool SliderAutoplay
    {
        get => _selected?.Properties.Slider?.Autoplay ?? true;
        set { if (_selected?.Properties.Slider is { } s) { PushUndo(); s.Autoplay = value; OnPropertyChanged(); } }
    }
    public bool SliderNavigation
    {
        get => _selected?.Properties.Slider?.Navigation ?? true;
        set { if (_selected?.Properties.Slider is { } s) { PushUndo(); s.Navigation = value; OnPropertyChanged(); } }
    }

    // Grid オプション
    public double GridColumns
    {
        get => _selected?.Properties.Grid?.Columns ?? 3;
        set { if (_selected?.Properties.Grid is { } g) { PushUndo(); g.Columns = value; OnPropertyChanged(); } }
    }
    public double GridGap
    {
        get => _selected?.Properties.Grid?.Gap ?? 20;
        set { if (_selected?.Properties.Grid is { } g) { PushUndo(); g.Gap = value; OnPropertyChanged(); } }
    }

    // Accordion オプション
    public bool AccordionOpenFirst
    {
        get => _selected?.Properties.Accordion?.OpenFirst ?? true;
        set { if (_selected?.Properties.Accordion is { } a) { PushUndo(); a.OpenFirst = value; OnPropertyChanged(); } }
    }

    /// <summary>アイテムの増減後に一覧を再バインドし再描画する。</summary>
    public void RefreshComposite()
    {
        OnPropertyChanged(nameof(SliderItems));
        OnPropertyChanged(nameof(GridItems));
        OnPropertyChanged(nameof(AccordionItems));
        RedrawRequested?.Invoke();
    }

    public void AddSlide()
    {
        if (_selected?.Properties.Slider is not { } s) return;
        PushUndo();
        (s.Slides ??= new List<SlideItem>()).Add(new SlideItem { Title = "新しいスライド", Text = "", Image = "", LinkType = "none", Link = "" });
        RefreshComposite();
    }
    public void RemoveSlide(SlideItem item)
    {
        if (_selected?.Properties.Slider?.Slides is not { } list) return;
        PushUndo();
        list.Remove(item);
        RefreshComposite();
    }

    public void AddCard()
    {
        if (_selected?.Properties.Grid is not { } g) return;
        PushUndo();
        (g.Items ??= new List<GridItem>()).Add(new GridItem { Title = "新しいカード", Text = "", Image = "", LinkType = "none", Link = "" });
        RefreshComposite();
    }
    public void RemoveCard(GridItem item)
    {
        if (_selected?.Properties.Grid?.Items is not { } list) return;
        PushUndo();
        list.Remove(item);
        RefreshComposite();
    }

    public void AddQa()
    {
        if (_selected?.Properties.Accordion is not { } a) return;
        PushUndo();
        (a.Items ??= new List<AccordionItem>()).Add(new AccordionItem { Title = "新しい質問", Content = "回答" });
        RefreshComposite();
    }
    public void RemoveQa(AccordionItem item)
    {
        if (_selected?.Properties.Accordion?.Items is not { } list) return;
        PushUndo();
        list.Remove(item);
        RefreshComposite();
    }

    private static string DefaultBg(string type) => type switch
    {
        "Button" => "#007acc",
        "Rect" => "#cccccc",
        "Circle" => "#e74c3c",
        "Triangle" => "#2ecc71",
        _ => "#ffffff",
    };

    private void Redraw() => RedrawRequested?.Invoke();

    // --- 出力（静的サイト / Laravel）---

    public async Task ExportStaticAsync(TopLevel? top)
    {
        var dir = await PickFolderAsync(top, "静的サイトの保存先フォルダを選択");
        if (dir == null) return;
        var result = Exporter.BuildStaticProject(_project);
        var baseDir = WriteResult(dir, result);
        StatusMessage = $"静的サイトを出力しました: {baseDir}";
    }

    public async Task ExportLaravelAsync(TopLevel? top)
    {
        var dir = await PickFolderAsync(top, "Laravel出力の保存先フォルダを選択");
        if (dir == null) return;
        var result = Exporter.BuildLaravelProject(_project);
        var baseDir = WriteResult(dir, result);
        StatusMessage = $"Laravelプロジェクトを出力しました: {baseDir}";
    }

    private static string WriteResult(string parentDir, BuildResult result)
    {
        var baseDir = Path.Combine(parentDir, result.ProjectName);
        foreach (var f in result.Files)
        {
            var full = Path.Combine(baseDir, f.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, f.Content);
        }
        foreach (var img in result.Images)
        {
            var full = Path.Combine(baseDir, img.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, Exporter.DecodeDataUrl(img.DataUrl));
        }
        return baseDir;
    }

    // --- 保存 / 読込（project.json）---

    public async Task SaveProjectAsync(TopLevel? top)
    {
        if (top == null) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "プロジェクトを保存",
            SuggestedFileName = "project.json",
            DefaultExtension = "json",
            FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } },
        });
        if (file == null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(ProjectSerializer.Serialize(_project));
        StatusMessage = $"保存しました: {file.Name}";
    }

    public async Task OpenProjectAsync(TopLevel? top)
    {
        if (top == null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "プロジェクトを開く",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } },
        });
        if (files.Count == 0) return;
        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        try
        {
            _project = ProjectSerializer.Load(json);
            RecomputeCounters();
            _undo.Clear();
            _redo.Clear();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            RebuildPageTabs();
            RebuildFolderNames();
            _selection.Clear();
            _selected = null;
            NotifyEverything();
            RedrawRequested?.Invoke();
            StatusMessage = $"読み込みました: {files[0].Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"読み込みに失敗しました: {ex.Message}";
        }
    }

    private static async Task<string?> PickFolderAsync(TopLevel? top, string title)
    {
        if (top == null) return null;
        var dirs = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return dirs.Count > 0 ? dirs[0].TryGetLocalPath() : null;
    }
}
