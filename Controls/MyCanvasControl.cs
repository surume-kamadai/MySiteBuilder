using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MySiteBuilder.Core.Models;
using MySiteBuilder.ViewModels;

namespace MySiteBuilder.Controls;

// ============================================================
// 自前の編集キャンバス（Konva の代替）。
//   - ViewModel の要素(SiteElement)を種類別に描画
//   - クリックで選択、ドラッグで移動、Delete で削除
// ============================================================
public class MyCanvasControl : Control
{
    public static readonly StyledProperty<MainWindowViewModel?> EditorProperty =
        AvaloniaProperty.Register<MyCanvasControl, MainWindowViewModel?>(nameof(Editor));

    public MainWindowViewModel? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    private bool _dragging;
    private Point _dragOffset;

    public MyCanvasControl()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorProperty)
        {
            if (change.OldValue is MainWindowViewModel oldVm) oldVm.RedrawRequested -= OnRedraw;
            if (change.NewValue is MainWindowViewModel newVm) newVm.RedrawRequested += OnRedraw;
            InvalidateVisual();
        }
    }

    private void OnRedraw() => InvalidateVisual();

    // ============================================================
    // 描画
    // ============================================================
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var vm = Editor;
        // ページ背景
        var bg = vm != null ? CssColor.Brush(vm.PageBackground, Brushes.White) : Brushes.White;
        context.FillRectangle(bg, new Rect(Bounds.Size));

        if (vm == null) return;

        foreach (var el in vm.Elements)
            RenderElement(context, el, 0, 0);

        // 選択枠
        if (vm.Selected is { } sel)
        {
            var t = sel.Transform;
            var rect = new Rect(t.X, t.Y, t.Width, t.Height);
            var pen = new Pen(Brushes.OrangeRed, 2);
            context.DrawRectangle(null, pen, rect);
            DrawHandle(context, rect.TopLeft);
            DrawHandle(context, rect.TopRight);
            DrawHandle(context, rect.BottomLeft);
            DrawHandle(context, rect.BottomRight);
        }
    }

    private static void DrawHandle(DrawingContext ctx, Point p)
    {
        var r = new Rect(p.X - 4, p.Y - 4, 8, 8);
        ctx.DrawRectangle(Brushes.White, new Pen(Brushes.OrangeRed, 1.5), r);
    }

    private void RenderElement(DrawingContext ctx, SiteElement el, double ox, double oy)
    {
        var t = el.Transform;
        var rect = new Rect(ox + t.X, oy + t.Y, Math.Max(1, t.Width), Math.Max(1, t.Height));
        var p = el.Properties;

        var fill = CssColor.Brush(p.Bgcolor, Brushes.Transparent);
        var textBrush = CssColor.Brush(p.Color, Brushes.Black);
        double fontSize = p.Fontsize is > 0 ? p.Fontsize.Value : 16;

        switch (el.Type)
        {
            case "Rect":
                ctx.DrawRectangle(fill, null, rect);
                break;

            case "Circle":
                ctx.DrawEllipse(fill, null, rect.Center, rect.Width / 2, rect.Height / 2);
                break;

            case "Triangle":
                ctx.DrawGeometry(fill, null, TriangleGeometry(rect));
                break;

            case "Label":
                DrawText(ctx, p.Text ?? "", rect, fontSize, textBrush, TextAlignment.Left, false);
                break;

            case "Button":
            {
                var btnBg = CssColor.Brush(p.Bgcolor, new SolidColorBrush(Color.Parse("#007acc")));
                ctx.DrawRectangle(btnBg, null, rect, 5, 5);
                DrawText(ctx, p.Text ?? "", rect, fontSize, textBrush, TextAlignment.Center, true);
                break;
            }

            case "TextInput":
            {
                ctx.DrawRectangle(Brushes.White, new Pen(Brushes.Gray, 1), rect, 4, 4);
                var ph = string.IsNullOrEmpty(p.Text) ? (p.InputName ?? "") : p.Text!;
                DrawText(ctx, ph, rect.Deflate(new Thickness(8, 0)), fontSize, Brushes.Gray, TextAlignment.Left, true);
                break;
            }

            case "Image":
            {
                ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#e8e8e8")), new Pen(Brushes.Gray, 1), rect);
                DrawText(ctx, "🖼 " + (p.Name ?? "Image"), rect, 13, Brushes.DimGray, TextAlignment.Center, true);
                break;
            }

            case "Group":
            {
                ctx.DrawRectangle(fill, new Pen(new SolidColorBrush(Color.Parse("#888888")), 1) { DashStyle = DashStyle.Dash }, rect);
                if (el.Children != null)
                    foreach (var child in el.Children)
                        RenderElement(ctx, child, rect.X, rect.Y);
                break;
            }

            default: // Slider / ArticleGrid / Accordion / Warp 等はプレースホルダ表示
            {
                ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#dfe4ea")),
                    new Pen(new SolidColorBrush(Color.Parse("#888888")), 1) { DashStyle = DashStyle.Dash }, rect);
                DrawText(ctx, el.Type, rect, 14, Brushes.DimGray, TextAlignment.Center, true);
                break;
            }
        }
    }

    private static Geometry TriangleGeometry(Rect r)
    {
        var geo = new StreamGeometry();
        using var c = geo.Open();
        c.BeginFigure(new Point(r.X + r.Width / 2, r.Y), true);
        c.LineTo(new Point(r.Right, r.Bottom));
        c.LineTo(new Point(r.X, r.Bottom));
        c.EndFigure(true);
        return geo;
    }

    private static void DrawText(DrawingContext ctx, string text, Rect rect, double size,
        IBrush brush, TextAlignment align, bool centerVertically)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface.Default, size, brush)
        {
            MaxTextWidth = Math.Max(1, rect.Width),
            MaxTextHeight = Math.Max(1, rect.Height),
            TextAlignment = align,
        };
        double y = centerVertically ? rect.Y + Math.Max(0, (rect.Height - ft.Height) / 2) : rect.Y;
        ctx.DrawText(ft, new Point(rect.X, y));
    }

    // ============================================================
    // 操作（選択・ドラッグ）
    // ============================================================
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var vm = Editor;
        if (vm == null) return;

        Focus();
        var pos = e.GetPosition(this);
        SiteElement? hit = null;

        // 上に描かれている要素を優先（リスト末尾から）
        for (int i = vm.Elements.Count - 1; i >= 0; i--)
        {
            var t = vm.Elements[i].Transform;
            if (new Rect(t.X, t.Y, t.Width, t.Height).Contains(pos)) { hit = vm.Elements[i]; break; }
        }

        vm.Select(hit);

        if (hit != null)
        {
            _dragging = true;
            _dragOffset = new Point(pos.X - hit.Transform.X, pos.Y - hit.Transform.Y);
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var vm = Editor;
        if (!_dragging || vm?.Selected is not { } sel) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _dragging = false; return; }

        var pos = e.GetPosition(this);
        sel.Transform.X = Math.Round(pos.X - _dragOffset.X);
        sel.Transform.Y = Math.Round(pos.Y - _dragOffset.Y);
        vm.NotifyGeometryChanged();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if ((e.Key == Key.Delete || e.Key == Key.Back) && Editor?.Selected != null)
        {
            Editor.DeleteSelected();
            e.Handled = true;
        }
    }
}

// CSS風カラー文字列を Avalonia ブラシへ変換する小さなヘルパ。
internal static class CssColor
{
    private static readonly Regex Rgba =
        new(@"rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+)\s*)?\)", RegexOptions.Compiled);

    public static IBrush Brush(string? css, IBrush fallback)
    {
        if (string.IsNullOrWhiteSpace(css)) return fallback;
        css = css.Trim();
        if (css.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return Brushes.Transparent;
        if (css.Equals("inherit", StringComparison.OrdinalIgnoreCase)) return fallback;

        var m = Rgba.Match(css);
        if (m.Success)
        {
            byte r = (byte)Math.Clamp(int.Parse(m.Groups[1].Value), 0, 255);
            byte g = (byte)Math.Clamp(int.Parse(m.Groups[2].Value), 0, 255);
            byte b = (byte)Math.Clamp(int.Parse(m.Groups[3].Value), 0, 255);
            byte a = m.Groups[4].Success
                ? (byte)Math.Clamp((int)Math.Round(double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) * 255), 0, 255)
                : (byte)255;
            return new SolidColorBrush(Color.FromArgb(a, r, g, b));
        }

        try { return new SolidColorBrush(Color.Parse(css)); }
        catch { return fallback; }
    }
}
