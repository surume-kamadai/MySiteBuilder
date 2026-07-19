using System.Text;
using System.Text.Json;
using Photino.NET;

namespace SiteBuilder.Host.Bridge;

// ============================================================
// main.js の 'export-project' ハンドラを仕様として移植。
// Port of main.js's 'export-project' handler (behaviour kept as the spec).
//   ・targetDir 未指定時のみ「保存先フォルダ選択」ダイアログ → 選択フォルダ/プロジェクト名/
//   ・SafeResolve でパストラバーサルを遮断（現行の安全策を維持）
//   ・成否は {success, path} / {success:false, message} で返す
//     キャンセル時のメッセージ「キャンセルされました」も一致（renderer がこの文字列で分岐）
// ============================================================
public sealed class ExportWriter
{
    private readonly PhotinoWindow _window;
    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };

    public ExportWriter(PhotinoWindow window) => _window = window;

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

            // 選択フォルダ内にプロジェクト名のフォルダを作る。
            // Create a folder named after the project inside the chosen folder.
            var projectName = string.IsNullOrEmpty(payload.ProjectName) ? "my-site" : payload.ProjectName;
            baseDir = Path.Combine(folders[0], projectName);
        }

        try
        {
            var utf8NoBom = new UTF8Encoding(false);

            // テキストファイル群（HTML/JSON など）/ text files (HTML, JSON, etc.)
            foreach (var file in payload.Files)
            {
                var full = SafeResolve(baseDir!, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, file.Content, utf8NoBom);
            }

            // 画像（data URL を base64 デコードして書き出す）/ images (decoded from data URLs)
            foreach (var img in payload.Images)
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

    // data:image/xxx;base64,.... から base64 本体を取り出す（main.js の replace と同義）。
    private static string StripDataUrlPrefix(string dataUrl)
    {
        var idx = dataUrl.IndexOf("base64,", StringComparison.Ordinal);
        return idx >= 0 ? dataUrl[(idx + "base64,".Length)..] : dataUrl;
    }

    // ============================================================
    // main.js の safeResolve と同一挙動（パストラバーサル遮断）。
    // Same behaviour as main.js's safeResolve (blocks path traversal).
    // 出力先(baseDir)の外へ抜け出すパスは例外にする。
    // Throws if the resolved path escapes the output dir (baseDir).
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
