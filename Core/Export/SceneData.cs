using MySiteBuilder.Core.Models;

namespace MySiteBuilder.Core.Export;

/// <summary>
/// HtmlRenderer に渡す1ページ分のシーンデータ。
/// （exporter.js が project から組み立てて渡すオブジェクトに対応）
/// </summary>
public sealed class SceneData
{
    public CanvasSize? Canvas { get; set; }
    public string? BgColor { get; set; }

    /// <summary>解決済みSEO（ページ個別→サイト共通のフォールバック済み）。</summary>
    public ResolvedSeo? Seo { get; set; }

    public List<SiteElement> Elements { get; set; } = new();

    /// <summary>Blade出力時のフォーム action（exporter が決定）。</summary>
    public string? FormAction { get; set; }
}

/// <summary>resolveSeo() の結果。</summary>
public sealed class ResolvedSeo
{
    public string Lang { get; set; } = "ja";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string OgImage { get; set; } = "";
    public string SiteName { get; set; } = "";
}

/// <summary>HtmlRenderer の出力モード。</summary>
public enum RenderMode
{
    /// <summary>素のHTML。</summary>
    Static,

    /// <summary>Laravel Blade（@csrf / asset() を使う）。</summary>
    Blade,
}
