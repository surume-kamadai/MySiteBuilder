using System.Globalization;
using System.Text.Json;
using static SiteBuilder.Core.Js;

namespace SiteBuilder.Core.Export;

// ============================================================
// css-generator.js の移植（HTML/CSS 生成の純ヘルパー群）。
// Port of css-generator.js (pure helpers for generating HTML/CSS).
// ============================================================
public static class CssHelpers
{
    // 全ページ共通のアニメーションCSS。バイト一致のため JS の値をそのまま保持。
    // Shared animation CSS. Kept byte-identical to the JS value.
    public const string AnimCss =
        "\n" +
        "        @keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }\n" +
        "        @keyframes fadeUp { from { opacity: 0; transform: translateY(30px); } to { opacity: 1; transform: translateY(0); } }\n" +
        "        @keyframes scaleIn { from { opacity: 0; transform: scale(0.8); } to { opacity: 1; transform: scale(1); } }\n" +
        "        @keyframes slideLeft { from { opacity: 0; transform: translateX(50px); } to { opacity: 1; transform: translateX(0); } }\n" +
        "        @keyframes slideRight { from { opacity: 0; transform: translateX(-50px); } to { opacity: 1; transform: translateX(0); } }\n" +
        "        .anim-fadein    { animation: fadeIn    1s   cubic-bezier(0.16, 1, 0.3, 1) forwards; opacity: 0; }\n" +
        "        .anim-fadeup    { animation: fadeUp    1s   cubic-bezier(0.16, 1, 0.3, 1) forwards; opacity: 0; }\n" +
        "        .anim-scale     { animation: scaleIn   0.8s cubic-bezier(0.16, 1, 0.3, 1) forwards; opacity: 0; }\n" +
        "        .anim-slideleft { animation: slideLeft  0.8s cubic-bezier(0.16, 1, 0.3, 1) forwards; opacity: 0; }\n" +
        "        .anim-slideright{ animation: slideRight 0.8s cubic-bezier(0.16, 1, 0.3, 1) forwards; opacity: 0; }";

    // HTML特殊文字をエスケープ（JS escapeHtml と同順）/ escape HTML (same order as JS escapeHtml).
    public static string EscapeHtml(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&#039;");

    // escapeHtml(x) = escape(String(x ?? '')) / matches JS escapeHtml(value) with the String(value ?? '') coercion.
    public static string EscapeHtml(JsonElement v) => EscapeHtml(IsNullish(v) ? "" : Str(v));

    // エディタの選択肢に対応する Google Fonts。/ Google Fonts matching the editor's choices.
    public static readonly (string Family, string Spec)[] GoogleFonts =
    {
        ("Noto Sans JP", "Noto+Sans+JP:wght@400;700"),
        ("Noto Serif JP", "Noto+Serif+JP:wght@400;700"),
        ("M PLUS Rounded 1c", "M+PLUS+Rounded+1c:wght@400;700"),
        ("Zen Maru Gothic", "Zen+Maru+Gothic:wght@400;700"),
        ("Kosugi Maru", "Kosugi+Maru"),
        ("Sawarabi Mincho", "Sawarabi+Mincho"),
        ("Yusei Magic", "Yusei+Magic"),
        ("Dela Gothic One", "Dela+Gothic+One"),
    };

    // data:URL 画像を出力パスへ解決（imageMap 経由）/ resolve a data:URL image via imageMap.
    public static string ResolveImageSrc(JsonElement src, IReadOnlyDictionary<string, string> imageMap)
    {
        if (src.ValueKind == JsonValueKind.String)
        {
            var s = src.GetString()!;
            if (s.StartsWith("data:image"))
                return imageMap.TryGetValue(s, out var p) && p.Length > 0 ? p : s; // imageMap.get(src) || src
            return s;
        }
        return Str(src); // typeof src !== 'string' の分岐は本アプリでは発生しない / non-string branch is unused here
    }

    // DEG マップ / the DEG lookup ({ v:180, h:90, d1:135, d2:225 }).
    private static double Deg(JsonElement dir, double fallback) =>
        dir.ValueKind == JsonValueKind.String
            ? dir.GetString() switch { "v" => 180, "h" => 90, "d1" => 135, "d2" => 225, _ => fallback }
            : fallback;

    // 背景CSS宣言（グラデ or 単色）/ background CSS declaration (gradient or solid).
    public static string GradientBgDecl(JObj props, string bgcolorEscaped)
    {
        var g = props.Obj("gradient");
        if (g.IsObject && g.Truthy("on"))
        {
            var c1 = EscapeHtml(g.StrT("c1", "#4facfe"));
            var c2 = EscapeHtml(g.StrT("c2", "#00f2fe"));
            if (g.Eq("type", "radial")) return $"background: radial-gradient(circle, {c1}, {c2});";
            return $"background: linear-gradient({NumStr(Deg(g.Raw("dir"), 180))}deg, {c1}, {c2});";
        }
        return $"background-color: {bgcolorEscaped};";
    }

    // #rrggbb(#rgb) + 不透明度 → rgba() / hex + opacity → rgba() string.
    private static string HexToRgba(string hex, double a)
    {
        var h = (hex ?? "#000000").Replace("#", "");
        var n = h.Length == 3 ? string.Concat(h[0], h[0], h[1], h[1], h[2], h[2]) : h;
        int r = HexByte(Slice(n, 0, 2));
        int g = HexByte(Slice(n, 2, 4));
        int b = HexByte(Slice(n, 4, 6));
        var al = Math.Min(1, Math.Max(0, a));
        return $"rgba({r}, {g}, {b}, {NumStr(al)})";
    }

    private static string Slice(string s, int start, int end)
        => start >= s.Length ? "" : s[start..Math.Min(end, s.Length)];

    private static int HexByte(string s)
        => int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var x) ? x : 0;

    // 境界線CSS / stroke CSS.
    public static string StrokeDecl(JObj props, string type)
    {
        var s = props.Obj("stroke");
        if (!(s.IsObject && s.Truthy("on"))) return "";
        var w = Math.Max(0, NumOrZero(ParseFloat(s.Raw("width"))));
        if (w <= 0) return "";
        var c = EscapeHtml(s.StrT("color", "#000000"));
        if (type == "Label") return $" -webkit-text-stroke: {NumStr(w)}px {c};";
        return $" border: {NumStr(w)}px solid {c};";
    }

    // 斜体/下線/字間/行間 CSS / italic / underline / letter-spacing / line-height CSS.
    public static string TextExtraCss(JObj props)
    {
        var s = "";
        if (props.Truthy("italic")) s += " font-style: italic;";
        if (props.Truthy("underline")) s += " text-decoration: underline;";
        var ls = ParseFloat(props.Raw("letterSpacing"));
        if (TruthyNum(ls)) s += $" letter-spacing: {NumStr(ls)}px;";
        var lh = ParseFloat(props.Raw("lineHeight"));
        if (TruthyNum(lh)) s += $" line-height: {NumStr(lh)};";
        return s;
    }

    private static readonly Dictionary<string, string> PresetShadow = new()
    {
        ["light"] = "0 4px 10px rgba(0,0,0,0.15)",
        ["dark"] = "0 8px 15px rgba(0,0,0,0.4)",
        ["hard"] = "5px 5px 0 rgba(0,0,0,0.45)",
        ["diagonal"] = "10px 10px 14px rgba(0,0,0,0.3)",
        ["float"] = "0 20px 30px rgba(0,0,0,0.28)",
    };

    // ドロップシャドウ+光彩+内側シャドウ+ベベルの合成 / combined shadow declaration.
    public static string CombinedShadowDecl(JObj props, string type)
    {
        bool isText = type == "Label";
        var box = new List<string>();
        var txt = new List<string>();

        // 1) ドロップシャドウ（自由値優先、無ければプリセット）
        var ds = props.Obj("dropShadow");
        if (ds.IsObject && ds.Truthy("on"))
        {
            var dx = NumOrZero(ParseFloat(ds.Raw("x")));
            var dy = NumOrZero(ParseFloat(ds.Raw("y")));
            var dblur = Math.Max(0, NumOrZero(ParseFloat(ds.Raw("blur"))));
            var dspread = NumOrZero(ParseFloat(ds.Raw("spread")));
            var drgba = HexToRgba(ds.StrT("color", "#000000"), ds.NumN("opacity", 0.35));
            if (isText) txt.Add($"{NumStr(dx)}px {NumStr(dy)}px {NumStr(dblur)}px {drgba}");
            else box.Add($"{NumStr(dx)}px {NumStr(dy)}px {NumStr(dblur)}px {NumStr(dspread)}px {drgba}");
        }
        else
        {
            var sh = props.Raw("shadow");
            if (sh.ValueKind == JsonValueKind.String && PresetShadow.TryGetValue(sh.GetString()!, out var preset))
                (isText ? txt : box).Add(preset);
        }

        // 2) 光彩
        var gl = props.Obj("glow");
        if (gl.IsObject && gl.Truthy("on"))
        {
            var grgba = HexToRgba(gl.StrT("color", "#00d0ff"), gl.NumN("opacity", 0.8));
            var gblur = Math.Max(0, NumOrZero(ParseFloat(gl.Raw("blur"))));
            var gspread = NumOrZero(ParseFloat(gl.Raw("spread")));
            if (isText) txt.Add($"0 0 {NumStr(gblur)}px {grgba}");
            else box.Add($"0 0 {NumStr(gblur)}px {NumStr(gspread)}px {grgba}");
        }

        // 3) 内側シャドウ（テキスト非対応）
        var inr = props.Obj("innerShadow");
        if (inr.IsObject && inr.Truthy("on") && !isText)
        {
            var ix = NumOrZero(ParseFloat(inr.Raw("x")));
            var iy = NumOrZero(ParseFloat(inr.Raw("y")));
            var iblur = Math.Max(0, NumOrZero(ParseFloat(inr.Raw("blur"))));
            var irgba = HexToRgba(inr.StrT("color", "#000000"), inr.NumN("opacity", 0.4));
            box.Add($"inset {NumStr(ix)}px {NumStr(iy)}px {NumStr(iblur)}px {irgba}");
        }

        // 4) ベベル＆エンボス（テキスト非対応）
        var bv = props.Obj("bevel");
        if (bv.IsObject && bv.Truthy("on") && !isText)
        {
            var d = Math.Max(1, OrOne(ParseFloat(bv.Raw("depth"))));
            var op = Math.Min(1, Math.Max(0, bv.NumN("opacity", 0.5)));
            var hl = HexToRgba(bv.StrT("highlight", "#ffffff"), op);
            var sh = HexToRgba(bv.StrT("shadow", "#000000"), op);
            var blur = d * 2;
            if (bv.Eq("dir", "down"))
            {
                box.Add($"inset {NumStr(d)}px {NumStr(d)}px {NumStr(blur)}px {sh}");
                box.Add($"inset -{NumStr(d)}px -{NumStr(d)}px {NumStr(blur)}px {hl}");
            }
            else
            {
                box.Add($"inset {NumStr(d)}px {NumStr(d)}px {NumStr(blur)}px {hl}");
                box.Add($"inset -{NumStr(d)}px -{NumStr(d)}px {NumStr(blur)}px {sh}");
            }
        }

        var arr = isText ? txt : box;
        if (arr.Count == 0) return "";
        return (isText ? "text-shadow: " : "box-shadow: ") + string.Join(", ", arr) + ";";
    }

    // グラデ文字 span のスタイル / gradient-text span style.
    private static string GradTextSpanStyle(JObj props)
    {
        var g = props.Obj("gradText");
        if (!(g.IsObject && g.Truthy("on"))) return "";
        var c1 = EscapeHtml(g.StrT("c1", "#ff6ec4"));
        var c2 = EscapeHtml(g.StrT("c2", "#7873f5"));
        var deg = Deg(g.Raw("dir"), 90);
        return $"background: linear-gradient({NumStr(deg)}deg, {c1}, {c2}); -webkit-background-clip: text; background-clip: text; -webkit-text-fill-color: transparent; color: transparent;";
    }

    // テキストをグラデ文字 span で包む / wrap text in a gradient-text span.
    public static string WrapGradText(string text, JObj props)
    {
        var st = GradTextSpanStyle(props);
        return st.Length > 0 ? $"<span style=\"{st}\">{text}</span>" : text;
    }
}
