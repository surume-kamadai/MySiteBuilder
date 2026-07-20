using System.Text;
using System.Text.Json;
using Photino.NET;
using SiteBuilder.Core.Export;

namespace SiteBuilder.Host.Bridge;

// ============================================================
// main.js の 'export-project' ハンドラを仕様として移植。
// Port of main.js's 'export-project' handler (behaviour kept as the spec).
//   ・targetDir 未指定時のみ「保存先フォルダ選択」ダイアログ → 選択フォルダ/プロジェクト名/
//   ・SafeResolve でパストラバーサルを遮断（現行の安全策を維持）
//   ・成否は {success, path} / {success:false, message} で返す
//     キャンセル時のメッセージ「キャンセルされました」も一致（renderer がこの文字列で分岐）
//
// Step 2: engine=csharp/shadow のとき、ペイロード内の project.json から
// SiteBuilder.Core で files を再生成/照合する（renderer は無改変）。
// When engine=csharp/shadow, regenerate/verify files from the payload's project.json
// via SiteBuilder.Core (the renderer stays untouched).
// ============================================================
public sealed class ExportWriter
{
    private readonly PhotinoWindow _window;
    private readonly EngineMode _engine;
    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };

    public ExportWriter(PhotinoWindow window, EngineMode engine)
    {
        _window = window;
        _engine = engine;
    }

    public object Export(JsonElement rawPayload)
    {
        var payload = rawPayload.Deserialize<ExportPayload>(ReadOpts) ?? new ExportPayload();

        // レンダラーが上書きパス(targetDir)を送ってきたらそれを使う。
        // Use targetDir (an overwrite path) if the renderer supplied one.
        var baseDir = payload.TargetDir;

        if (string.IsNullOrEmpty(baseDir))
        {
            // 新規保存時はフォルダ選択ダイアログ / On first save, show a folder picker.
            var folders = _window.ShowOpenFolder("保存先フォルダを選択", null, false);
            if (folders is null || folders.Length == 0)
                return new { success = false, message = "キャンセルされました" };

            var projectName = string.IsNullOrEmpty(payload.ProjectName) ? "my-site" : payload.ProjectName;
            baseDir = Path.Combine(folders[0], projectName);
        }

        // エンジン選択に応じて、実際に書き出す files/images を決める。
        // Decide which files/images to actually write, based on the engine mode.
        var files = payload.Files;
        var images = payload.Images;
        if (_engine != EngineMode.Js)
            (files, images) = ApplyEngine(payload);

        try
        {
            var utf8NoBom = new UTF8Encoding(false);

            foreach (var file in files)
            {
                var full = SafeResolve(baseDir!, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, file.Content, utf8NoBom);
            }

            foreach (var img in images)
            {
                var full = SafeResolve(baseDir!, img.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllBytes(full, Convert.FromBase64String(StripDataUrlPrefix(img.DataUrl)));
            }

            return new { success = true, path = baseDir };
        }
        catch (Exception ex)
        {
            return new { success = false, message = ex.Message };
        }
    }

    // C# / Shadow エンジンの適用。project.json が無ければ JS 出力へフォールバック。
    // Apply the C#/Shadow engine. Falls back to the JS output if project.json is missing.
    private (List<ExportFile> files, List<ExportImage> images) ApplyEngine(ExportPayload payload)
    {
        var projectJson = payload.Files.FirstOrDefault(f => f.Path == "project.json")?.Content;
        if (projectJson is null) return (payload.Files, payload.Images); // 古い renderer 等 / older renderer, etc.

        BuildResult result;
        try
        {
            using var doc = JsonDocument.Parse(projectJson);
            var root = doc.RootElement;
            // api.js と同じく settings.outputType で分岐（既定 static）。
            string outputType = "static";
            if (root.TryGetProperty("settings", out var s) && s.ValueKind == JsonValueKind.Object
                && s.TryGetProperty("outputType", out var ot) && ot.ValueKind == JsonValueKind.String)
                outputType = ot.GetString()!;

            result = outputType == "laravel"
                ? Exporter.BuildLaravelProject(root)
                : Exporter.BuildStaticProject(root);
        }
        catch (Exception ex)
        {
            ShadowLog($"engine regeneration failed, falling back to JS output: {ex.Message}");
            return (payload.Files, payload.Images);
        }

        // Core が生成した files に、renderer と同様 project.json を同梱する。
        // Bundle project.json alongside Core's files, just as the renderer does.
        var coreFiles = result.Files.Select(f => new ExportFile { Path = f.Path, Content = f.Content }).ToList();
        coreFiles.Add(new ExportFile { Path = "project.json", Content = projectJson });
        var coreImages = result.Images.Select(i => new ExportImage { Path = i.Path, DataUrl = i.DataUrl }).ToList();

        if (_engine == EngineMode.CSharp)
            return (coreFiles, coreImages);

        // Shadow: JS 出力をそのまま書き出しつつ、C# 出力との差分を記録する。
        ShadowCompare(payload, coreFiles);
        return (payload.Files, payload.Images);
    }

    // JS 出力(payload) と C# 出力(core) を比較し、不一致をログに残す（project.json は比較対象外）。
    private void ShadowCompare(ExportPayload payload, List<ExportFile> coreFiles)
    {
        var js = payload.Files.Where(f => f.Path != "project.json").ToDictionary(f => f.Path, f => f.Content);
        var cs = coreFiles.Where(f => f.Path != "project.json").ToDictionary(f => f.Path, f => f.Content);

        var mismatches = new List<string>();
        foreach (var path in js.Keys.Union(cs.Keys))
        {
            bool inJs = js.TryGetValue(path, out var jc);
            bool inCs = cs.TryGetValue(path, out var cc);
            if (!inJs) mismatches.Add($"  only in C#: {path}");
            else if (!inCs) mismatches.Add($"  only in JS: {path}");
            else if (jc != cc)
            {
                int i = 0, min = Math.Min(jc!.Length, cc!.Length);
                while (i < min && jc[i] == cc[i]) i++;
                mismatches.Add($"  differs at index {i}: {path}");
            }
        }

        if (mismatches.Count == 0)
            ShadowLog($"shadow OK: {cs.Count} files byte-identical (JS == C#)");
        else
            ShadowLog($"shadow MISMATCH ({mismatches.Count}):\n" + string.Join("\n", mismatches));
    }

    private static void ShadowLog(string message)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "sitebuilder-shadow.log");
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { /* ログ失敗は致命的でない / logging failure is non-fatal */ }
    }

    // data:image/xxx;base64,.... から base64 本体を取り出す（main.js の replace と同義）。
    private static string StripDataUrlPrefix(string dataUrl)
    {
        var idx = dataUrl.IndexOf("base64,", StringComparison.Ordinal);
        return idx >= 0 ? dataUrl[(idx + "base64,".Length)..] : dataUrl;
    }

    // ============================================================
    // main.js の safeResolve と同一挙動（パストラバーサル遮断）。
    // Same behaviour as main.js's safeResolve (blocks path traversal).
    // ============================================================
    public static string SafeResolve(string baseDir, string relPath)
    {
        var baseFull = Path.GetFullPath(baseDir);
        var target = Path.GetFullPath(Path.Combine(baseFull, relPath));
        var rel = Path.GetRelativePath(baseFull, target);
        if (rel == "." || rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
            throw new InvalidOperationException($"不正な出力パスです: {relPath}");
        return target;
    }
}
