using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using MySiteBuilder.Models;

namespace MySiteBuilder.Controls;

public class MyCanvasControl : Control
{
    // ViewModelsからデータを受け取るための「依存関係プロパティ（AvaloniaProperty）」の定義
    public static readonly StyledProperty<IEnumerable<NodeData>?> NodesProperty =
        AvaloniaProperty.Register<MyCanvasControl, IEnumerable<NodeData>?>(nameof(Nodes));

    public IEnumerable<NodeData>? Nodes
    {
        get => GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    public MyCanvasControl()
    {
        // データ（Nodes）が変更されたら再描画するように設定
        AffectsRender<MyCanvasControl>(NodesProperty);
    }

    // 描画ループ（Konvaの代わり）
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // 1. 背景のクリア
        context.FillRectangle(Brushes.DarkGray, Bounds);

        // 2. ノードのデータが無ければ何もしない
        if (Nodes == null) return;

        // 3. データをループして描画していく
        foreach (var node in Nodes)
        {
            var rect = new Rect(node.X, node.Y, node.Width, node.Height);
            
            var fillBrush = node.IsSelected ? Brushes.Orange : Brushes.SteelBlue;
            var borderPen = new ImmutablePen(Brushes.White, 2);

            // 描画命令
            context.DrawRectangle(fillBrush, borderPen, rect);
        }
    }

    // マウスクリック時の処理
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (Nodes == null) return;

        var point = e.GetPosition(this);
        
        // 当たり判定処理
        foreach (var node in Nodes)
        {
            var rect = new Rect(node.X, node.Y, node.Width, node.Height);
            
            // 四角形の中にクリック座標が含まれていれば選択状態にする
            if (rect.Contains(point))
            {
                node.IsSelected = true;
            }
            else
            {
                node.IsSelected = false;
            }
        }

        // 次のフレームで再描画を要求する
        InvalidateVisual();
    }
}