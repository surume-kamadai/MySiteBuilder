using Avalonia;

namespace MySiteBuilder.Models;

// 画面に描画する1つのノード（四角形）のデータ
public class NodeData
{
    public string Id { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IsSelected { get; set; }
}