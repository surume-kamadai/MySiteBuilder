using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MySiteBuilder.Core.Export;
using MySiteBuilder.Core.Models;
using MySiteBuilder.Core.Serialization;

namespace MySiteBuilder.ViewModels;

// ============================================================
// エディタの中核ViewModel（Phase 2）
//   - データは Core の SiteProject / SiteElement をそのまま使う
//   - 要素の追加・選択・削除、Inspector編集、HTML/Blade出力、保存/読込を担う
// ============================================================
public class MainWindowViewModel : ViewModelBase
{
    private SiteProject _project = CreateInitialProject();
    private SiteElement? _selected;
    private int _elementCounter;
    private string _statusMessage = "要素を追加してデザインを始めましょう。";

    /// <summary>キャンバス再描画を要求するイベント（Viewのキャンバスが購読）。</summary>
    public event Action? RedrawRequested;

    public MainWindowViewModel()
    {
        // 起動時のサンプル要素
        AddElement("Label");
        if (_selected is { } l) { l.Properties.Text = "見出しテキスト"; l.Properties.Fontsize = 32; }
        AddElement("Button");
        Select(null);
        StatusMessage = "要素を追加してデザインを始めましょう。";
    }

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
    public SitePage ActivePage => _project.Pages[0];
    public IList<SiteElement> Elements => ActivePage.Elements;

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

    // --- 選択 ---

    public void Select(SiteElement? el)
    {
        _selected = el;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasSelection));
        NotifyInspector();
        RedrawRequested?.Invoke();
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
        set { if (_selected != null) { _selected.Properties.Name = value; OnPropertyChanged(); } }
    }

    public string SelText
    {
        get => _selected?.Properties.Text ?? "";
        set { if (_selected != null) { _selected.Properties.Text = value; OnPropertyChanged(); Redraw(); } }
    }

    public double SelX
    {
        get => _selected?.Transform.X ?? 0;
        set { if (_selected != null) { _selected.Transform.X = value; OnPropertyChanged(); Redraw(); } }
    }

    public double SelY
    {
        get => _selected?.Transform.Y ?? 0;
        set { if (_selected != null) { _selected.Transform.Y = value; OnPropertyChanged(); Redraw(); } }
    }

    public double SelW
    {
        get => _selected?.Transform.Width ?? 0;
        set { if (_selected != null) { _selected.Transform.Width = value; OnPropertyChanged(); Redraw(); } }
    }

    public double SelH
    {
        get => _selected?.Transform.Height ?? 0;
        set { if (_selected != null) { _selected.Transform.Height = value; OnPropertyChanged(); Redraw(); } }
    }

    public string SelBgColor
    {
        get => _selected?.Properties.Bgcolor ?? "";
        set { if (_selected != null) { _selected.Properties.Bgcolor = value; OnPropertyChanged(); Redraw(); } }
    }

    public string SelColor
    {
        get => _selected?.Properties.Color ?? "";
        set { if (_selected != null) { _selected.Properties.Color = value; OnPropertyChanged(); Redraw(); } }
    }

    public double SelFontSize
    {
        get => _selected?.Properties.Fontsize ?? 16;
        set { if (_selected != null) { _selected.Properties.Fontsize = value; OnPropertyChanged(); Redraw(); } }
    }

    public string SelRoute
    {
        get => _selected?.Properties.Route ?? "";
        set { if (_selected != null) { _selected.Properties.Route = value; OnPropertyChanged(); } }
    }

    // --- 要素の追加・削除 ---

    public void AddElement(string type)
    {
        _elementCounter++;
        bool square = type is "Circle" or "Triangle";
        var el = new SiteElement
        {
            Id = type.ToLowerInvariant() + "_" + _elementCounter,
            Type = type,
            Transform = new ElementTransform
            {
                X = 60, Y = 60,
                Width = square ? 120 : 150,
                Height = square ? 120 : 50,
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
        Elements.Add(el);
        Select(el);
        StatusMessage = $"{type} を追加しました。";
    }

    public void DeleteSelected()
    {
        if (_selected == null) return;
        var t = _selected.Type;
        Elements.Remove(_selected);
        Select(null);
        StatusMessage = $"{t} を削除しました。";
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
            Select(null);
            OnPropertyChanged(nameof(CanvasWidth));
            OnPropertyChanged(nameof(CanvasHeight));
            OnPropertyChanged(nameof(PageBackground));
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
