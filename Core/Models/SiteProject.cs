using System.Text.Json.Serialization;

namespace MySiteBuilder.Core.Models;

// ============================================================
// project.js のデータモデル移植
//
//   1プロジェクト = 設定 + フォルダ + 複数ページ。
//   各ページは要素(SiteElement)の配列を持つ。
//   JSON のキー名は Electron版 project.json と互換になるよう
//   JsonPropertyName で固定している（既存プロジェクトを読めるように）。
// ============================================================

/// <summary>プロジェクト全体（保存単位）。</summary>
public sealed class SiteProject
{
    [JsonPropertyName("settings")]
    public ProjectSettings Settings { get; set; } = new();

    [JsonPropertyName("folders")]
    public List<PageFolder> Folders { get; set; } = new();

    [JsonPropertyName("pages")]
    public List<SitePage> Pages { get; set; } = new();

    [JsonPropertyName("activePageId")]
    public string ActivePageId { get; set; } = "page_1";
}

/// <summary>プロジェクト設定。</summary>
public sealed class ProjectSettings
{
    [JsonPropertyName("projectName")]
    public string? ProjectName { get; set; } = "my-site";

    [JsonPropertyName("canvas")]
    public CanvasSize Canvas { get; set; } = new();

    /// <summary>'static' | 'laravel'</summary>
    [JsonPropertyName("outputType")]
    public string? OutputType { get; set; } = "static";

    /// <summary>サイト全体のデフォルト背景色。</summary>
    [JsonPropertyName("siteBgColor")]
    public string? SiteBgColor { get; set; } = "#f1f2f6";

    /// <summary>サイト共通のSEO初期値。</summary>
    [JsonPropertyName("seo")]
    public SiteSeo? Seo { get; set; } = new();
}

/// <summary>キャンバスサイズ（PC基準＋スマホ基準）。</summary>
public sealed class CanvasSize
{
    [JsonPropertyName("width")]
    public double Width { get; set; } = 800;

    [JsonPropertyName("height")]
    public double Height { get; set; } = 600;

    [JsonPropertyName("mobileWidth")]
    public double? MobileWidth { get; set; } = 375;

    [JsonPropertyName("mobileHeight")]
    public double? MobileHeight { get; set; } = 800;
}

/// <summary>サイト共通SEO。</summary>
public sealed class SiteSeo
{
    [JsonPropertyName("siteName")]
    public string? SiteName { get; set; } = "";

    [JsonPropertyName("lang")]
    public string? Lang { get; set; } = "ja";

    [JsonPropertyName("description")]
    public string? Description { get; set; } = "";

    [JsonPropertyName("ogImage")]
    public string? OgImage { get; set; } = "";
}

/// <summary>ページ個別SEO（未指定ならサイト共通値にフォールバック）。</summary>
public sealed class PageSeo
{
    [JsonPropertyName("title")]
    public string? Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; } = "";

    [JsonPropertyName("ogImage")]
    public string? OgImage { get; set; } = "";
}

/// <summary>ページ分類用フォルダ（出力サブフォルダ名になる）。</summary>
public sealed class PageFolder
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

/// <summary>1ページ（出力時に1つのHTML/Bladeファイルになる）。</summary>
public sealed class SitePage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>ファイル名になる。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("elements")]
    public List<SiteElement> Elements { get; set; } = new();

    [JsonPropertyName("folderId")]
    public string? FolderId { get; set; }

    /// <summary>ページ個別背景色（空ならサイト共通色）。</summary>
    [JsonPropertyName("bgColor")]
    public string? BgColor { get; set; } = "";

    [JsonPropertyName("seo")]
    public PageSeo? Seo { get; set; } = new();
}
