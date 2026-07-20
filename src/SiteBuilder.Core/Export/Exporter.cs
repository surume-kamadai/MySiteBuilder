using System.Text.Json;
using System.Text.RegularExpressions;
using static SiteBuilder.Core.Js;

namespace SiteBuilder.Core.Export;

// 出力ファイル・画像 / an output file / image.
public sealed record OutFile(string Path, string Content);
public sealed record OutImage(string Path, string DataUrl);

// buildStaticProject / buildLaravelProject の返り値 / return of the builders.
public sealed class BuildResult
{
    public string ProjectName = "";
    public List<OutFile> Files = new();
    public List<OutImage> Images = new();
}

// ============================================================
// exporter.js の移植（シーンデータからプロジェクト一式を組み立てる）。
// Port of exporter.js (assembles a full project from the scene data).
// ============================================================
public static class Exporter
{
    // ページ＋サイト設定からSEOメタを解決 / resolve SEO from page + site settings.
    private static JObj ResolveSeo(JObj project, JObj page)
    {
        var site = project.Obj("settings").Obj("seo"); // project.settings?.seo || {}
        var pseo = page.Obj("seo");                    // page.seo || {}

        string trimmedTitle = pseo.Truthy("title") ? Str(pseo.Raw("title")).Trim() : "";
        string pageName = Str(page.Raw("name"));
        string baseTitle = trimmedTitle.Length > 0 ? trimmedTitle : pageName;
        string title = site.Truthy("siteName") ? $"{baseTitle} | {Str(site.Raw("siteName"))}" : baseTitle;

        string trimmedDesc = pseo.Truthy("description") ? Str(pseo.Raw("description")).Trim() : "";
        string description = trimmedDesc.Length > 0 ? trimmedDesc
            : site.Truthy("description") ? Str(site.Raw("description")) : "";

        string trimmedOg = pseo.Truthy("ogImage") ? Str(pseo.Raw("ogImage")).Trim() : "";
        string ogImage = trimmedOg.Length > 0 ? trimmedOg
            : site.Truthy("ogImage") ? Str(site.Raw("ogImage")) : "";

        string lang = site.StrT("lang", "ja");
        string siteName = site.Truthy("siteName") ? Str(site.Raw("siteName")) : "";

        return new JObj(JsonSerializer.SerializeToElement(new { lang, title, description, ogImage, siteName }));
    }

    // 要素ツリーから最初の送信ボタン(role==='submit')を返す / first submit button in the tree.
    private static JObj? FindSubmitButton(JsonElement elements)
    {
        foreach (var el in HtmlRenderer.EnumArr(elements))
        {
            var elO = new JObj(el);
            var p = new JObj(elO.Raw("properties"));
            if (p.Raw("visible").ValueKind == JsonValueKind.False) continue;
            if (elO.Eq("type", "Button") && p.Eq("role", "submit")) return p;
            var ch = elO.Raw("children");
            if (ch.ValueKind == JsonValueKind.Array)
            {
                var found = FindSubmitButton(ch);
                if (found.HasValue) return found;
            }
        }
        return null;
    }

    private static readonly Regex ImageExtRe =
        new(@"^data:image/(\w+)", RegexOptions.Compiled | RegexOptions.ECMAScript);

    // 全ページのBase64画像を集めてパスを割り当て / collect Base64 images and assign paths.
    private static (Dictionary<string, string> ImageMap, List<OutImage> Images) CollectImages(JObj project)
    {
        var imageMap = new Dictionary<string, string>(); // 挿入順保持 / insertion order preserved
        var images = new List<OutImage>();
        int counter = 0;

        void Walk(JsonElement elements)
        {
            foreach (var el in HtmlRenderer.EnumArr(elements))
            {
                var elO = new JObj(el);
                var textRaw = elO.Obj("properties").Raw("text");
                if (elO.Eq("type", "Image") && textRaw.ValueKind == JsonValueKind.String
                    && textRaw.GetString()!.StartsWith("data:image"))
                {
                    var dataUrl = textRaw.GetString()!;
                    if (!imageMap.ContainsKey(dataUrl))
                    {
                        counter++;
                        var m = ImageExtRe.Match(dataUrl);
                        string ext = (m.Success ? m.Groups[1].Value : "png").Replace("jpeg", "jpg");
                        string path = $"images/img_{counter}.{ext}";
                        imageMap[dataUrl] = path;
                        images.Add(new OutImage(path, dataUrl));
                    }
                }
                var ch = elO.Raw("children");
                if (ch.ValueKind == JsonValueKind.Array) Walk(ch);
            }
        }

        foreach (var page in project.Arr("pages"))
            Walk(new JObj(page).Raw("elements"));

        return (imageMap, images);
    }

    // フォルダID→名前の対応（空名は JS の truthy 判定に合わせて未所属扱い）。
    private static Dictionary<string, string> BuildFolderMap(JObj project)
    {
        var map = new Dictionary<string, string>();
        foreach (var f in project.Arr("folders"))
        {
            var fo = new JObj(f);
            var k = KeyOf(fo.Raw("id"));
            if (k != null) map[k] = Str(fo.Raw("name"));
        }
        return map;
    }

    private static string? KeyOf(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => NumStr(e.GetDouble()),
        _ => null,
    };

    // folderMap.get(page.folderId) 相当（空文字は falsy として null に丸める）。
    private static string? LookupFolder(Dictionary<string, string> map, JsonElement folderId)
    {
        var k = KeyOf(folderId);
        if (k != null && map.TryGetValue(k, out var v) && v.Length > 0) return v; // JS: '' folderName is falsy
        return null;
    }

    private static string BgColorOf(JObj page, JObj settings) =>
        page.Truthy("bgColor") ? Str(page.Raw("bgColor"))
        : settings.Truthy("siteBgColor") ? Str(settings.Raw("siteBgColor")) : "#f1f2f6";

    private static string ProjectNameOf(JObj settings, string fallback) =>
        settings.Truthy("projectName") ? Str(settings.Raw("projectName")) : fallback;

    // 静的サイト用の出力 / build the static-site output.
    public static BuildResult BuildStaticProject(JsonElement projectEl, string projectName = "my-site")
    {
        var project = new JObj(projectEl);
        var (imageMap, images) = CollectImages(project);
        var settings = project.Obj("settings");
        var folderMap = BuildFolderMap(project);
        bool separateCss = settings.Truthy("separateCss");

        var files = new List<OutFile> { new("README.txt", StaticReadme()) };
        if (separateCss) files.Add(new("css/common.css", CssHelpers.AnimCss));

        foreach (var pageEl in project.Arr("pages"))
        {
            var page = new JObj(pageEl);
            string? folderName = LookupFolder(folderMap, page.Raw("folderId"));
            string pageName = Str(page.Raw("name"));

            List<string>? cssHrefs = null;
            string? cssFileName = null;
            if (separateCss)
            {
                cssFileName = folderName != null ? $"{folderName}_{pageName}.css" : $"{pageName}.css";
                string prefix = folderName != null ? "../" : "";
                cssHrefs = new List<string> { $"{prefix}css/common.css", $"{prefix}css/{cssFileName}" };
            }

            var scene = new Scene
            {
                Canvas = settings.Obj("canvas"),
                BgColor = BgColorOf(page, settings),
                Elements = page.Raw("elements"),
                Seo = ResolveSeo(project, page),
            };
            var renderer = new HtmlRenderer(scene, "static", imageMap, cssHrefs);
            string html = renderer.Render();

            string filePath = folderName != null ? $"{folderName}/{pageName}.html" : $"{pageName}.html";
            files.Add(new(filePath, html));

            if (separateCss) files.Add(new($"css/{cssFileName}", renderer.GetExtractedCss() ?? ""));
        }

        return new BuildResult
        {
            ProjectName = ProjectNameOf(settings, projectName),
            Files = files,
            Images = images.Select(i => new OutImage(i.Path, i.DataUrl)).ToList(),
        };
    }

    // Laravelプロジェクト用の出力 / build the Laravel-project output.
    public static BuildResult BuildLaravelProject(JsonElement projectEl, string projectName = "my-laravel-site")
    {
        var project = new JObj(projectEl);
        var (imageMap, images) = CollectImages(project);

        var laravelImageMap = new Dictionary<string, string>();
        foreach (var kv in imageMap) laravelImageMap[kv.Key] = $"{{{{ asset('{kv.Value}') }}}}";

        var settings = project.Obj("settings");
        var folderMap = BuildFolderMap(project);
        bool separateCss = settings.Truthy("separateCss");

        var files = new List<OutFile> { new("README.txt", LaravelReadme()) };
        if (separateCss) files.Add(new("public/css/common.css", CssHelpers.AnimCss));

        var routes = new List<string>();
        var postRoutes = new List<string>();

        foreach (var pageEl in project.Arr("pages"))
        {
            var page = new JObj(pageEl);
            string? folderName = LookupFolder(folderMap, page.Raw("folderId"));
            string pageName = Str(page.Raw("name"));
            string viewPathName = folderName != null ? $"{folderName}/{pageName}" : pageName;

            List<string>? cssHrefs = null;
            string? cssFileName = null;
            if (separateCss)
            {
                cssFileName = $"{viewPathName.Replace("/", "_")}.css";
                cssHrefs = new List<string> { "{{ asset('css/common.css') }}", $"{{{{ asset('css/{cssFileName}') }}}}" };
            }

            var submit = FindSubmitButton(page.Raw("elements"));
            string? formAction = null;
            if (submit.HasValue)
            {
                var sp = submit.Value;
                string userAction = (sp.Truthy("route") && !sp.Eq("route", "#")) ? Str(sp.Raw("route")) : "";
                formAction = userAction.Length > 0 ? userAction : $"/{viewPathName}-submit";
                postRoutes.Add(formAction);
            }

            var scene = new Scene
            {
                Canvas = settings.Obj("canvas"),
                BgColor = BgColorOf(page, settings),
                Elements = page.Raw("elements"),
                Seo = ResolveSeo(project, page),
                FormAction = formAction,
            };
            var renderer = new HtmlRenderer(scene, "blade", laravelImageMap, cssHrefs);
            string blade = renderer.Render();

            files.Add(new($"resources/views/{viewPathName}.blade.php", blade));
            if (separateCss) files.Add(new($"public/css/{cssFileName}", renderer.GetExtractedCss() ?? ""));

            string urlPath = viewPathName == "index" ? "/" : $"/{viewPathName}";
            string viewDotName = folderName != null ? $"{folderName}.{pageName}" : pageName;
            routes.Add($"Route::get('{urlPath}', fn() => view('{viewDotName}'));");
        }

        if (postRoutes.Count > 0)
        {
            foreach (var action in DistinctInOrder(postRoutes))
                routes.Add($"Route::post('{action}', [\\App\\Http\\Controllers\\FormController::class, 'handle']);");
            files.Add(new("app/Http/Controllers/FormController.php", FormControllerStub()));
        }

        files.Add(new("routes/web.php", LaravelRoutes(routes)));

        return new BuildResult
        {
            ProjectName = ProjectNameOf(settings, projectName),
            Files = files,
            Images = images.Select(i => new OutImage($"public/{i.Path}", i.DataUrl)).ToList(),
        };
    }

    // [...new Set(arr)]（挿入順を保った一意化）/ unique preserving insertion order.
    private static IEnumerable<string> DistinctInOrder(IEnumerable<string> src)
    {
        var seen = new HashSet<string>();
        foreach (var x in src) if (seen.Add(x)) yield return x;
    }

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
        var lines = new List<string> { "<?php", "", "use Illuminate\\Support\\Facades\\Route;", "" };
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
