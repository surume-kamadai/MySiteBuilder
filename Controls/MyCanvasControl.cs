using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MySiteBuilder.Core.Models;
using MySiteBuilder.ViewModels;

namespace MySiteBuilder.Controls;

// ============================================================
// 自前の編集キャンバス（Konva の代替）。
//   - ViewModel の要素(SiteElement)を種類別に描画（画像は実ビットマップ）
//   - クリックで選択、ドラッグで移動、四隅ハンドルでリサイズ、Delete で削除
// ============================================================
public class MyCanvasControl : Control
{
    private enum DragMode { None, Move, Resize }

    private const double HandleHit = 7;   // ハンドルの当たり判定半径
    private const double MinSize = 12;    // 最小サイズ

    public static readonly StyledProperty<MainWindowViewModel?> EditorProperty =
        AvaloniaProperty.Register<MyCanvasControl, MainWindowViewModel?>(nameof(Editor));

    public MainWindowViewModel? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    private DragMode _mode;
    private int _resizeCorner;       // 0=TL 1=TR 2=BL 3=BR
    private Point _anchor;           // リサイズ時に固定する対角コーナー
    private Point _dragStartPointer; // 移動開始時のポインタ位置
    private readonly List<(SiteElement el, double x, double y)> _dragStarts = new(); // 移動開始時の各選択要素の位置
    private bool _pushedThisDrag;    // このドラッグでUndoスナップショット済みか
    private StandardCursorType _cursorType = StandardCursorType.Arrow;

    // 画像のビットマップキャッシュ（data URL / ローカルパス → Bitmap）
    private readonly Dictionary<string, Bitmap?> _imgCache = new();

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
        var bg = vm != null ? CssColor.Brush(vm.PageBackground, Brushes.White) : Brushes.White;
        context.FillRectangle(bg, new Rect(Bounds.Size));

        if (vm == null) return;

        foreach (var el in vm.Elements)
            RenderElement(context, el, 0, 0);

        // 選択枠（複数選択は全てに枠、ハンドルはプライマリのみ）
        foreach (var el in vm.Elements)
        {
            if (!vm.IsSelected(el)) continue;
            var t = el.Transform;
            var rect = new Rect(t.X, t.Y, t.Width, t.Height);
            bool primary = ReferenceEquals(el, vm.Selected);
            context.DrawRectangle(null, new Pen(Brushes.OrangeRed, primary ? 2 : 1), rect);
            if (primary)
            {
                DrawHandle(context, rect.TopLeft);
                DrawHandle(context, rect.TopRight);
                DrawHandle(context, rect.BottomLeft);
                DrawHandle(context, rect.BottomRight);
            }
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
            {
                double cr = Math.Max(0, p.CornerRadius ?? 0);
                ctx.DrawRectangle(FillBrush(p, fill), StrokePen(p), rect, cr, cr);
                break;
            }

            case "Circle":
                ctx.DrawEllipse(FillBrush(p, fill), StrokePen(p), rect.Center, rect.Width / 2, rect.Height / 2);
                break;

            case "Triangle":
                ctx.DrawGeometry(FillBrush(p, fill), StrokePen(p), TriangleGeometry(rect));
                break;

            case "Label":
                DrawText(ctx, p.Text ?? "", rect, fontSize, textBrush, TextAlignment.Left, false);
                break;

            case "Button":
            {
                var btnBg = FillBrush(p, CssColor.Brush(p.Bgcolor, new SolidColorBrush(Color.Parse("#007acc"))));
                double btnR = Math.Max(0, p.CornerRadius ?? 8);
                ctx.DrawRectangle(btnBg, StrokePen(p), rect, btnR, btnR);
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
                var bmp = GetBitmap(p.Text);
                if (bmp != null)
                {
                    var dest = ContainRect(rect, bmp.Size.Width, bmp.Size.Height);
                    ctx.DrawImage(bmp, new Rect(bmp.Size), dest);
                }
                else
                {
                    ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#e8e8e8")), new Pen(Brushes.Gray, 1), rect);
                    DrawText(ctx, "🖼 " + (p.Name ?? "Image"), rect, 13, Brushes.DimGray, TextAlignment.Center, true);
                }
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

    // レイヤースタイル: 塗りグラデーション（node-style.js の applyGradient に対応。off ならそのまま fallback を返す）
    private static IBrush FillBrush(ElementProperties p, IBrush fallback)
    {
        var g = p.Gradient;
        if (g?.On != true) return fallback;

        var stops = new GradientStops
        {
            new GradientStop(CssColor.ParseColor(g.C1, Color.FromRgb(0x4f, 0xac, 0xfe)), 0),
            new GradientStop(CssColor.ParseColor(g.C2, Color.FromRgb(0x00, 0xf2, 0xfe)), 1),
        };

        if (g.Type == "radial")
        {
            return new RadialGradientBrush
            {
                GradientStops = stops,
                Center = RelativePoint.Center,
                GradientOrigin = RelativePoint.Center,
                Radius = 0.5,
            };
        }

        var (start, end) = g.Dir switch
        {
            "h"  => (new RelativePoint(0, 0.5, RelativeUnit.Relative), new RelativePoint(1, 0.5, RelativeUnit.Relative)),
            "d1" => (new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 1, RelativeUnit.Relative)),
            "d2" => (new RelativePoint(1, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative)),
            _    => (new RelativePoint(0.5, 0, RelativeUnit.Relative), new RelativePoint(0.5, 1, RelativeUnit.Relative)),
        };
        return new LinearGradientBrush { GradientStops = stops, StartPoint = start, EndPoint = end };
    }

    // レイヤースタイル: 境界線（css-generator.js の strokeDecl に対応。off/幅0なら null）
    private static Pen? StrokePen(ElementProperties p)
    {
        var s = p.Stroke;
        if (s?.On != true) return null;
        double w = Math.Max(0, s.Width ?? 0);
        if (w <= 0) return null;
        return new Pen(CssColor.Brush(s.Color, Brushes.Black), w);
    }

    // object-fit: contain 相当の収まりrectを計算
    private static Rect ContainRect(Rect box, double bw, double bh)
    {
        if (bw <= 0 || bh <= 0) return box;
        double scale = Math.Min(box.Width / bw, box.Height / bh);
        double w = bw * scale, h = bh * scale;
        return new Rect(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
    }

    private Bitmap? GetBitmap(string? src)
    {
        if (string.IsNullOrEmpty(src)) return null;
        if (_imgCache.TryGetValue(src, out var cached)) return cached;

        Bitmap? bmp = null;
        try
        {
            if (src.StartsWith("data:image"))
            {
                int i = src.IndexOf("base64,", StringComparison.Ordinal);
                if (i >= 0)
                {
                    var bytes = Convert.FromBase64String(src[(i + 7)..]);
                    using var ms = new MemoryStream(bytes);
                    bmp = new Bitmap(ms);
                }
            }
            else if (File.Exists(src))
            {
                bmp = new Bitmap(src);
            }
            // http(s) は同期読込しない（プレースホルダ表示）
        }
        catch
        {
            bmp = null;
        }

        _imgCache[src] = bmp;
        return bmp;
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
    // 操作（選択・移動・リサイズ）
    // ============================================================
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var vm = Editor;
        if (vm == null) return;

        Focus();
        var pos = e.GetPosition(this);

        // 選択中要素のハンドルを最優先でヒットテスト（リサイズ開始）
        if (vm.Selected is { } cur)
        {
            var r = RectOf(cur);
            int corner = HitHandle(pos, r);
            if (corner >= 0)
            {
                _mode = DragMode.Resize;
                _pushedThisDrag = false;
                _resizeCorner = corner;
                _anchor = corner switch
                {
                    0 => r.BottomRight,
                    1 => r.BottomLeft,
                    2 => r.TopRight,
                    _ => r.TopLeft,
                };
                e.Pointer.Capture(this);
                return;
            }
        }

        // 要素の選択（上に描かれている要素を優先）
        SiteElement? hit = null;
        for (int i = vm.Elements.Count - 1; i >= 0; i--)
        {
            if (RectOf(vm.Elements[i]).Contains(pos)) { hit = vm.Elements[i]; break; }
        }

        bool additive = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (hit == null)
        {
            if (!additive) vm.Select(null);
            return;
        }

        if (additive)
        {
            vm.ToggleSelect(hit);   // 追加選択（ドラッグはしない）
            return;
        }

        // 通常クリック: 既に選択集合に含まれるならプライマリ切替（複数選択維持）、そうでなければ単一選択
        if (vm.IsSelected(hit)) vm.SetPrimary(hit);
        else vm.Select(hit);

        // 選択集合まとめてドラッグ開始
        _mode = DragMode.Move;
        _pushedThisDrag = false;
        _dragStartPointer = pos;
        _dragStarts.Clear();
        foreach (var s in vm.Selection) _dragStarts.Add((s, s.Transform.X, s.Transform.Y));
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var vm = Editor;
        var pos = e.GetPosition(this);

        if (_mode == DragMode.None) { UpdateCursor(vm, pos); return; }
        if (vm?.Selected is not { } sel) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _mode = DragMode.None; return; }

        // 実際に動かし始めた時点で一度だけUndoスナップショットを取る
        if (!_pushedThisDrag) { vm.PushUndo(); _pushedThisDrag = true; }

        if (_mode == DragMode.Move)
        {
            double dx = pos.X - _dragStartPointer.X;
            double dy = pos.Y - _dragStartPointer.Y;
            foreach (var (el, sx, sy) in _dragStarts)
            {
                el.Transform.X = Math.Round(sx + dx);
                el.Transform.Y = Math.Round(sy + dy);
            }
        }
        else // Resize（プライマリのみ）
        {
            double w = Math.Max(MinSize, Math.Abs(pos.X - _anchor.X));
            double h = Math.Max(MinSize, Math.Abs(pos.Y - _anchor.Y));
            double x = pos.X < _anchor.X ? _anchor.X - w : _anchor.X;
            double y = pos.Y < _anchor.Y ? _anchor.Y - h : _anchor.Y;
            sel.Transform.X = Math.Round(x);
            sel.Transform.Y = Math.Round(y);
            sel.Transform.Width = Math.Round(w);
            sel.Transform.Height = Math.Round(h);
        }
        vm.NotifyGeometryChanged();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _mode = DragMode.None;
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

    private static Rect RectOf(SiteElement el)
    {
        var t = el.Transform;
        return new Rect(t.X, t.Y, t.Width, t.Height);
    }

    // pos が四隅ハンドルのどれかに当たればそのコーナー番号、無ければ -1
    private static int HitHandle(Point pos, Rect r)
    {
        Point[] corners = { r.TopLeft, r.TopRight, r.BottomLeft, r.BottomRight };
        for (int i = 0; i < corners.Length; i++)
        {
            if (Math.Abs(pos.X - corners[i].X) <= HandleHit && Math.Abs(pos.Y - corners[i].Y) <= HandleHit)
                return i;
        }
        return -1;
    }

    private void UpdateCursor(MainWindowViewModel? vm, Point pos)
    {
        var type = StandardCursorType.Arrow;
        if (vm?.Selected is { } sel)
        {
            var r = RectOf(sel);
            int corner = HitHandle(pos, r);
            type = corner switch
            {
                0 => StandardCursorType.TopLeftCorner,
                1 => StandardCursorType.TopRightCorner,
                2 => StandardCursorType.BottomLeftCorner,
                3 => StandardCursorType.BottomRightCorner,
                _ => r.Contains(pos) ? StandardCursorType.SizeAll : StandardCursorType.Arrow,
            };
        }
        if (type != _cursorType)
        {
            _cursorType = type;
            Cursor = new Cursor(type);
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

    public static Color ParseColor(string? css, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(css)) return fallback;
        css = css.Trim();

        var m = Rgba.Match(css);
        if (m.Success)
        {
            byte r = (byte)Math.Clamp(int.Parse(m.Groups[1].Value), 0, 255);
            byte g = (byte)Math.Clamp(int.Parse(m.Groups[2].Value), 0, 255);
            byte b = (byte)Math.Clamp(int.Parse(m.Groups[3].Value), 0, 255);
            byte a = m.Groups[4].Success
                ? (byte)Math.Clamp((int)Math.Round(double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) * 255), 0, 255)
                : (byte)255;
            return Color.FromArgb(a, r, g, b);
        }

        try { return Color.Parse(css); }
        catch { return fallback; }
    }
}
