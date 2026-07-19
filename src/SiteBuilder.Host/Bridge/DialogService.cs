using System.Text;
using System.Text.Json;
using Photino.NET;

namespace SiteBuilder.Host.Bridge;

// ============================================================
// OS ネイティブのファイル/画像ダイアログ（main.js の dialog.* 相当）。
// Native OS file/image dialogs (equivalent to main.js's dialog.* calls).
// ネイティブダイアログなので見た目は Electron 版と同一になる。
// Being native OS dialogs, they look identical to the Electron version.
// ============================================================
public sealed class DialogService
{
    private readonly PhotinoWindow _window;

    public DialogService(PhotinoWindow window) => _window = window;

    // pick-image → { dataUrl, name } / キャンセル時は null
    public object? PickImage()
    {
        var files = _window.ShowOpenFile(
            title: "画像を選択",
            defaultPath: null,
            multiSelect: false,
            filters: new (string, string[])[]
            {
                ("画像", new[] { "png", "jpg", "jpeg", "gif", "webp", "svg" }),
            });

        if (files is null || files.Length == 0) return null;

        var filePath = files[0];
        var bytes = File.ReadAllBytes(filePath);
        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        var mime = ext switch { "svg" => "svg+xml", "jpg" => "jpeg", _ => ext };
        var dataUrl = $"data:image/{mime};base64,{Convert.ToBase64String(bytes)}";

        return new { dataUrl, name = Path.GetFileName(filePath) };
    }

    // load-scene → { content, dirPath } / キャンセル時は null
    public object? LoadScene()
    {
        var files = _window.ShowOpenFile(
            title: "プロジェクトを開く (project.json を選択)",
            defaultPath: null,
            multiSelect: false,
            filters: new (string, string[])[] { ("JSON", new[] { "json" }) });

        if (files is null || files.Length == 0) return null;

        var filePath = files[0];
        var content = File.ReadAllText(filePath, Encoding.UTF8);
        return new { content, dirPath = Path.GetDirectoryName(filePath) ?? string.Empty };
    }

    // save-scene → { success, path } / { success:false }
    public object SaveScene(JsonElement payload)
    {
        // payload は JSON 文字列そのもの（invoke('save-scene', jsonStr)）。
        // The payload is the raw JSON string (from invoke('save-scene', jsonStr)).
        var json = payload.ValueKind == JsonValueKind.String
            ? payload.GetString() ?? string.Empty
            : payload.GetRawText();

        var path = _window.ShowSaveFile(
            title: "プロジェクトを保存",
            defaultPath: "layout_project.json",
            filters: new (string, string[])[] { ("JSON", new[] { "json" }) });

        if (string.IsNullOrEmpty(path)) return new { success = false };

        File.WriteAllText(path, json, new UTF8Encoding(false));
        return new { success = true, path };
    }

    // ヘルプ → バージョン情報（Electron の dialog.showMessageBox 相当）
    public void ShowAbout()
        => _window.ShowMessage("Site Builder", "Site Builder\nGUIサイトビルダー");
}
