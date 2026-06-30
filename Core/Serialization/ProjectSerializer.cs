using System.Text.Json;
using System.Text.Json.Nodes;
using MySiteBuilder.Core.Models;

namespace MySiteBuilder.Core.Serialization;

// ============================================================
// project.js の loadProject / serializeProject 相当。
//   - 旧形式（pages が無く elements 直下）を1ページへ変換
//   - 欠落フィールドの後方互換補完
//   - 方針A移行: 古い layouts / mobileEdited を一掃
// ============================================================
public static class ProjectSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // 既定値も含めて出力（Electron版 JSON.stringify 互換）
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    /// <summary>JSON 文字列からプロジェクトを読み込む（後方互換つき）。</summary>
    public static SiteProject Load(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject()
                   ?? throw new JsonException("プロジェクトJSONがオブジェクトではありません。");

        // 後方互換: 旧形式（pages 無し・elements 直下）を1ページに変換
        if (root["pages"] is null && root["elements"] is not null)
        {
            var canvas = root["canvas"];
            var wrapped = new JsonObject
            {
                ["settings"] = new JsonObject
                {
                    ["projectName"] = "my-site",
                    ["canvas"] = canvas?.DeepClone() ?? new JsonObject { ["width"] = 800, ["height"] = 600 },
                    ["outputType"] = "static",
                },
                ["pages"] = new JsonArray(new JsonObject
                {
                    ["id"] = "page_1",
                    ["name"] = "index",
                    ["elements"] = root["elements"]!.DeepClone(),
                }),
                ["activePageId"] = "page_1",
            };
            root = wrapped;
        }

        var project = root.Deserialize<SiteProject>(Options)
                      ?? throw new JsonException("プロジェクトのデシリアライズに失敗しました。");

        // 後方互換の補完
        project.Folders ??= new List<PageFolder>();
        project.Settings ??= new ProjectSettings();
        project.Settings.Canvas ??= new CanvasSize();
        project.Settings.Canvas.MobileWidth ??= 375;
        project.Settings.Canvas.MobileHeight ??= 800;
        project.Settings.SiteBgColor ??= "#f1f2f6";
        project.Settings.Seo ??= new SiteSeo();

        foreach (var p in project.Pages)
        {
            p.BgColor ??= "";
            p.Seo ??= new PageSeo();
            CleanLayouts(p.Elements);
        }

        return project;
    }

    /// <summary>プロジェクトを JSON 文字列へ（保存用、整形あり）。</summary>
    public static string Serialize(SiteProject project)
        => JsonSerializer.Serialize(project, Options);

    // 方針A移行: 古い不正な layouts / mobileEdited を一掃する（transform を正とする）。
    private static void CleanLayouts(IEnumerable<SiteElement>? elements)
    {
        foreach (var el in elements ?? Enumerable.Empty<SiteElement>())
        {
            el.Properties.Layouts = null;
            el.Properties.MobileEdited = null;
            if (el.Children != null) CleanLayouts(el.Children);
        }
    }
}
