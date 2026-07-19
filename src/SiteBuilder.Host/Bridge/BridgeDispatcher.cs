using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Photino.NET;

namespace SiteBuilder.Host.Bridge;

// ============================================================
// host-bridge.js からのメッセージをルーティングする（計画書 §4.2）。
// Routes messages coming from host-bridge.js (plan §4.2).
// id 付きの要求には window.SendWebMessage で応答を返す。
// Requests carrying an id get a reply via window.SendWebMessage.
// ============================================================
public sealed class BridgeDispatcher
{
    private readonly PhotinoWindow _window;
    private readonly DialogService _dialogs;
    private readonly ExportWriter _export;

    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // 数値丸め・エスケープ・null の JSON 差異を避けるため UnsafeRelaxedJsonEscaping を使う（計画書 §8）。
    // Use UnsafeRelaxedJsonEscaping to avoid JSON escaping/rounding/null mismatches (plan §8).
    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public BridgeDispatcher(PhotinoWindow window)
    {
        _window = window;
        _dialogs = new DialogService(window);
        _export = new ExportWriter(window);
    }

    // Photino の RegisterWebMessageReceivedHandler(EventHandler<string>) に渡すハンドラ。
    // UI スレッド上で呼ばれるため、ネイティブダイアログ呼び出しも安全に行える。
    // Invoked on the UI thread, so native dialog calls made here are safe.
    public void Handle(object? sender, string raw)
    {
        BridgeMessage? msg;
        try { msg = JsonSerializer.Deserialize<BridgeMessage>(raw, ReadOpts); }
        catch { return; }
        if (msg is null || msg.Channel is null) return;

        object? result = null;
        try
        {
            switch (msg.Channel)
            {
                case "export-project": result = _export.Export(msg.Payload); break;   // main.js と同一仕様
                case "pick-image":     result = _dialogs.PickImage(); break;           // → {dataUrl, name}
                case "save-scene":     result = _dialogs.SaveScene(msg.Payload); break; // → {success, path}
                case "load-scene":     result = _dialogs.LoadScene(); break;           // → {content, dirPath}
                case "app-quit":       _window.Close(); return;
                case "show-about":     _dialogs.ShowAbout(); return;
                case "open-external":  OpenExternal(msg.Payload); return;
                case "toggle-devtools": return;   // devtools は起動時フラグ/ F12 で制御（個別処理不要）
                default: return;                  // 未知チャンネルは無視（応答なし）
            }
        }
        catch (Exception ex)
        {
            result = new { success = false, message = ex.Message };
        }

        // 要求(id)があるものだけ応答する。一方向通知(send)には応答しない。
        // Reply only to requests (id present); fire-and-forget sends get no reply.
        if (msg.Id is int id)
        {
            var reply = JsonSerializer.Serialize(new { replyTo = id, result }, WriteOpts);
            _window.SendWebMessage(reply);
        }
    }

    // 外部リンクを既定ブラウザで開く（http/https のみ）/ open external links in the default browser.
    private static void OpenExternal(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return;
        if (!payload.TryGetProperty("url", out var urlEl)) return;
        var url = urlEl.GetString();
        if (string.IsNullOrEmpty(url)) return;
        if (!(url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
              url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))) return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", url);
            else
                Process.Start("xdg-open", url);
        }
        catch { /* リンク起動失敗は致命的でないため無視 / launching a link is best-effort */ }
    }
}
