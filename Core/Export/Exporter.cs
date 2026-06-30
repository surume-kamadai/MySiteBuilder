using System.Text;
using System.Text.RegularExpressions;
using MySiteBuilder.Core.Models;

namespace MySiteBuilder.Core.Export;

// ============================================================
// exporter.js - シーンデータからプロジェクト一式を組み立てる の C# 移植
//
//   出力タイプ:
//     Static  : さくら等にそのまま置けるHTML一式（*.html + images/）
//     Laravel : Laravelプロジェクト構造（Blade + routes + public/images）
// ============================================================

/// <summary>出力するテキストファイル。</summary>
public sealed record OutputFile(string Path, string Content);

/// <summary>出力する画像（data:URL を保持。実書き出しは GUI/ホスト側）。</summary>
public sealed record OutputImage(string Path, string DataUrl);

/// <summary>ビルド結果（プロジェクト一式）。</summary>
public sealed class BuildResult
{
    public string ProjectName { get; init; } = "";
    public List<OutputFile> Files { get; init; } = new();
    public List<OutputImage> Images { get; init; } = new();
}

public static class Exporter
{
    private static readonly Regex DataImagePrefix =
        new(@"^data:image/\w+;base64,", RegexOptions.Compiled);
    private static readonly Regex DataImageExt =
        new(@"^data:image/(\w+)", RegexOptions.Compiled);

    // ページ＋サイト設定からSEOメタ情報を解決する。
    private static ResolvedSeo ResolveSeo(SiteProject project, SitePage page)
    {
        var site = project.Settings?.Seo ?? new SiteSeo();
        var pseo = page.Seo ?? new PageSeo();

        string baseTitle = TrimmedOr(pseo.Title, page.Name);
        string title = Js.Truthy(site.SiteName) ? $"{baseTitle} | {site.SiteName}" : baseTitle;
        return new ResolvedSeo
        {
            Lang = Js.Or(site.Lang, "ja"),
            Title = title,
            Description = TrimmedOr(pseo.Description, Js.Or(site.Description, "")),
            OgImage = TrimmedOr(pseo.OgImage, Js.Or(site.OgImage, "")),
            SiteName = Js.Or(site.SiteName, ""),
        };
    }

    // JS の `(s && s.trim()) || fallback`：s が非空ならトリム値、空(空白のみ含む)なら fallback。
    private static string TrimmedOr(string? s, string fallback)
    {
        if (!string.IsNullOrEmpty(s))
        {
            var t = s.Trim();
            if (t.Length > 0) return t;
        }
        return fallback;
    }

    // 要素ツリーから最初の送信ボタン(role==='submit')のプロパティを返す。
    private static ElementProperties? FindSubmitButton(IEnumerable<SiteElement>? elements)
    {
        foreach (var el in elements ?? Enumerable.Empty<SiteElement>())
        {
            var p = el.Properties;
            if (p.Visible == false) continue;
            if (el.Type == "Button" && p.Role == "submit") return p;
            if (el.Children is { } children)
            {
                var f = FindSubmitButton(children);
                if (f != null) return f;
            }
        }
        return null;
    }

    // 全ページの要素からBase64画像を集めてパスを割り当てる。
    private static (Dictionary<string, string> imageMap, List<OutputImage> images) CollectImages(SiteProject project)
    {
        var imageMap = new Dictionary<string, string>();
        var images = new List<OutputImage>();
        int counter = 0;

        void Walk(IEnumerable<SiteElement>? elements)
        {
            foreach (var el in elements ?? Enumerable.Empty<SiteElement>())
            {
                if (el.Type == "Image" && el.Properties.Text is { } t && t.StartsWith("data:image"))
                {
                    if (!imageMap.ContainsKey(t))
                    {
                        counter++;
                        var m = DataImageExt.Match(t);
                        string ext = (m.Success ? m.Groups[1].Value : "png").Replace("jpeg", "jpg");
                        string path = $"images/img_{counter}.{ext}";
                        imageMap[t] = path;
                        images.Add(new OutputImage(path, t));
                    }
                }
                if (el.Children != null) Walk(el.Children);
            }
        }

        foreach (var page in project.Pages ?? new List<SitePage>())
            Walk(page.Elements);

        return (imageMap, images);
    }

    /// <summary>静的サイト用の出力（マルチページ・フォルダ対応）。</summary>
    public static BuildResult BuildStaticProject(SiteProject project, string projectName = "my-site")
    {
        var (imageMap, images) = CollectImages(project);
        var folderMap = (project.Folders ?? new List<PageFolder>()).ToDictionary(f => f.Id, f => f.Name);

        var files = new List<OutputFile> { new("README.txt", StaticReadme()) };

        foreach (var page in project.Pages ?? new List<SitePage>())
        {
            string bgColor = Js.Or(Js.Or(page.BgColor, Js.Or(project.Settings?.SiteBgColor, "")), "#f1f2f6");
            var sceneData = new SceneData
            {
                Canvas = project.Settings?.Canvas,
                BgColor = bgColor,
                Elements = page.Elements ?? new List<SiteElement>(),
                Seo = ResolveSeo(project, page),
            };
            var renderer = new HtmlRenderer(sceneData, RenderMode.Static, imageMap);
            string html = renderer.Render();

            string filePath = folderMap.TryGetValue(page.FolderId ?? "", out var folderName)
                ? $"{folderName}/{page.Name}.html"
                : $"{page.Name}.html";

            files.Add(new OutputFile(filePath, html));
        }

        return new BuildResult
        {
            ProjectName = Js.Or(project.Settings?.ProjectName, projectName),
            Files = files,
            Images = images,
        };
    }

    /// <summary>Laravelプロジェクト用の出力（マルチページ・フォルダ対応）。</summary>
    public static BuildResult BuildLaravelProject(SiteProject project, string projectName = "my-laravel-site")
    {
        var (imageMap, images) = CollectImages(project);

        // 画像パスのLaravel用変換
        var laravelImageMap = new Dictionary<string, string>();
        foreach (var (dataUrl, relPath) in imageMap)
            laravelImageMap[dataUrl] = $"{{{{ asset('{relPath}') }}}}";

        var folderMap = (project.Folders ?? new List<PageFolder>()).ToDictionary(f => f.Id, f => f.Name);
        var files = new List<OutputFile> { new("README.txt", LaravelReadme()) };
        var routes = new List<string>();
        var postRoutes = new List<string>();

        foreach (var page in project.Pages ?? new List<SitePage>())
        {
            string bgColor = Js.Or(Js.Or(page.BgColor, Js.Or(project.Settings?.SiteBgColor, "")), "#f1f2f6");

            bool hasFolder = folderMap.TryGetValue(page.FolderId ?? "", out var folderName);
            string viewPathName = hasFolder ? $"{folderName}/{page.Name}" : page.Name;

            var submit = FindSubmitButton(page.Elements);
            string? formAction = null;
            if (submit != null)
            {
                string userAction = (Js.Truthy(submit.Route) && submit.Route != "#") ? submit.Route! : "";
                formAction = Js.Or(userAction, $"/{viewPathName}-submit");
                postRoutes.Add(formAction);
            }

            var sceneData = new SceneData
            {
                Canvas = project.Settings?.Canvas,
                BgColor = bgColor,
                Elements = page.Elements ?? new List<SiteElement>(),
                Seo = ResolveSeo(project, page),
                FormAction = formAction,
            };
            var renderer = new HtmlRenderer(sceneData, RenderMode.Blade, laravelImageMap);
            string blade = renderer.Render();

            files.Add(new OutputFile($"resources/views/{viewPathName}.blade.php", blade));

            string urlPath = viewPathName == "index" ? "/" : $"/{viewPathName}";
            string viewDotName = hasFolder ? $"{folderName}.{page.Name}" : page.Name;
            routes.Add($"Route::get('{urlPath}', fn() => view('{viewDotName}'));");
        }

        if (postRoutes.Count > 0)
        {
            foreach (var action in postRoutes.Distinct())
                routes.Add($"Route::post('{action}', [\\App\\Http\\Controllers\\FormController::class, 'handle']);");
            files.Add(new OutputFile("app/Http/Controllers/FormController.php", FormControllerStub()));
        }

        files.Add(new OutputFile("routes/web.php", LaravelRoutes(routes)));

        return new BuildResult
        {
            ProjectName = Js.Or(project.Settings?.ProjectName, projectName),
            Files = files,
            Images = images.Select(img => new OutputImage($"public/{img.Path}", img.DataUrl)).ToList(),
        };
    }

    /// <summary>data:URL の base64 部分を取り出す（実ファイル書き出し時に使用）。</summary>
    public static byte[] DecodeDataUrl(string dataUrl)
    {
        string base64 = DataImagePrefix.Replace(dataUrl, "");
        return Convert.FromBase64String(base64);
    }

    // フォーム受け口コントローラの雛形
    private static string FormControllerStub() => string.Join("\n", new[]
    {
        "<?php",
        "",
        "namespace App\\Http\\Controllers;",
        "",
        "use Illuminate\\Http\\Request;",
        "",
        "class FormController extends Controller",
        "{",
        "    public function handle(Request $request)",
        "    {",
        "        // 送信された全項目（入力欄の name 属性がキーになります）",
        "        $data = $request->all();",
        "",
        "        // TODO: バリデーション例",
        "        // $request->validate([",
        "        //     'email' => 'required|email',",
        "        // ]);",
        "",
        "        // TODO: メール送信例（config/mail.php 設定後）",
        "        // \\Mail::raw(print_r($data, true), function ($m) {",
        "        //     $m->to('you@example.com')->subject('お問い合わせ');",
        "        // });",
        "",
        "        return back()->with('success', '送信が完了しました。');",
        "    }",
        "}",
        "",
    });

    private static string StaticReadme() => string.Join("\n", new[]
    {
        "さくらレンタルサーバー等への設置手順",
        "================================",
        "",
        "1. このフォルダの中身（.htmlファイルと images/）を",
        "   FTPソフト（FileZilla等）でサーバーの www/ 等にアップロードする。",
        "",
        "2. ブラウザで https://あなたのドメイン/ を開いて確認する。",
        "",
    });

    private static string LaravelRoutes(List<string> routeLines)
    {
        var lines = new List<string>
        {
            "<?php",
            "",
            "use Illuminate\\Support\\Facades\\Route;",
            "",
        };
        lines.AddRange(routeLines);
        lines.Add("");
        return string.Join("\n", lines);
    }

    private static string LaravelReadme() => string.Join("\n", new[]
    {
        "Laravelプロジェクトへの組み込み手順",
        "================================",
        "",
        "1. resources/views/ 内のファイルを既存のLaravelプロジェクトにコピー。",
        "2. routes/web.php のルート定義を追記。",
        "3. public/images/ の画像をコピー。",
        "4. php artisan serve で確認。",
        "",
    });
}
