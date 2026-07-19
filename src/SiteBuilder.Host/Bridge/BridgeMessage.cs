using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiteBuilder.Host.Bridge;

// ============================================================
// host-bridge.js から届くメッセージの受け皿 / Envelope for messages from host-bridge.js
//   { id?: number, channel: string, payload?: any }
// id が付いていれば応答を返す（invoke）。無ければ一方向通知（send）。
// If id is present the caller expects a reply (invoke); otherwise fire-and-forget (send).
// ============================================================
public sealed class BridgeMessage
{
    public int? Id { get; set; }
    public string? Channel { get; set; }
    public JsonElement Payload { get; set; }
}

// export-project のペイロード / payload of export-project
// { files:[{path,content}], images:[{path,dataUrl}], projectName, targetDir? }
public sealed class ExportPayload
{
    public List<ExportFile> Files { get; set; } = new();
    public List<ExportImage> Images { get; set; } = new();
    public string? ProjectName { get; set; }
    public string? TargetDir { get; set; }
}

public sealed class ExportFile
{
    public string Path { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public sealed class ExportImage
{
    public string Path { get; set; } = string.Empty;
    [JsonPropertyName("dataUrl")]
    public string DataUrl { get; set; } = string.Empty;
}
