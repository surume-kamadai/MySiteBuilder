using System.Text;
using MySiteBuilder.Core.Models;

namespace MySiteBuilder.Core.Export;

// ============================================================
// renderer.js - HTML生成エンジン の C# 移植（レスポンシブ＆インタラクション対応版）
//
//   シーンデータ（1ページ分: canvas/bgColor/seo/elements）を受け取り、
//   完成した HTML / Laravel Blade 文字列を生成する。
//   各要素を絶対配置(%)で並べ、レスポンシブは CSS クラス + @media、
//   動きのある部品（スライダー/アコーディオン/イベント）は dynamicJs に初期化JSを溜める。
// ============================================================
public sealed class HtmlRenderer
{
    private const string AnimCss =
        "\n        @keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }" +
        "\n        @keyframes fadeUp { from { opacity: 0; transform: translateY(30px); } to { opacity: 1; transform: translateY(0); } }" +
        "\n        @keyframes scaleIn { from { opacity: 0; transform: scale(0.8); } to { opacity: 1; transform: scale(1); } }" +
        "\n        @keyframes slideLeft { from { opacity: 0; transform: translateX(50px); } to { opacity: 1; transform: translateX(0); } }" +
        "\n        @keyframes slideRight { from { opacity: 0; transform: translateX(-50px); } to { opacity: 1; transform: translateX(0); } }" +
        "\n        .anim-fadein    { animation: fadeIn    1s   cubic-bezier(0.16, 1, 0.3, 1) forwards; opacity: 0; }" +
        "\n        .anim-fadeup    { animation: fadeUp    1s   cubic-bezier(0.16, 1, 0.3, 1) forwards; opacity: 0; }" +
        "\n        .anim-scale     { animation: scaleIn   0.8s cubic-bezier(0.16, 1, 0.3, 1) forwards; opacity: 0; }" +
        "\n        .anim-slideleft { animation: slideLeft  0.8s cubic-bezier(0.16, 1, 0.3, 1) forwards; opacity: 0; }" +
        "\n        .anim-slideright{ animation: slideRight 0.8s cubic-bezier(0.16, 1, 0.3, 1) forwards; opacity: 0; }";

    // シャドウ種別 → CSS値
    private static readonly Dictionary<string, string> ShadowCss = new()
    {
        ["light"]    = "0 4px 10px rgba(0,0,0,0.15)",
        ["dark"]     = "0 8px 15px rgba(0,0,0,0.4)",
        ["hard"]     = "5px 5px 0 rgba(0,0,0,0.45)",
        ["diagonal"] = "10px 10px 14px rgba(0,0,0,0.3)",
        ["float"]    = "0 20px 30px rgba(0,0,0,0.28)",
    };

    private readonly SceneData _scene;
    private readonly RenderMode _mode;
    private readonly IReadOnlyDictionary<string, string> _imageMap;

    private readonly List<string> _dynamicCss = new();
    private readonly List<string> _dynamicJs = new();
    private double _mobileW = 375;          // スマホ表示の基準幅
    private double _mobileCanvasH = 800;    // スマホ表示の基準高さ

    public HtmlRenderer(SceneData scene, RenderMode mode = RenderMode.Static,
        IReadOnlyDictionary<string, string>? imageMap = null)
    {
        _scene = scene;
        _mode = mode;
        _imageMap = imageMap ?? new Dictionary<string, string>();
    }

    public string Render()
    {
        double cw = _scene.Canvas?.Width ?? 800;
        double ch = _scene.Canvas?.Height ?? 600;

        _mobileW = _scene.Canvas?.MobileWidth ?? 375;
        _mobileCanvasH = _scene.Canvas?.MobileHeight ?? 800;

        string elementsHtml = RenderElements(_scene.Elements, cw, ch, 1);

        // 背景色（ページ個別→サイト共通の解決済み値）
        string bgColor = Js.Or(_scene.BgColor, "#f1f2f6");

        _dynamicCss.Add(
            $".site-canvas {{ position: relative; width: 100%; max-width: {Js.Num(cw)}px; aspect-ratio: {Js.Num(cw)} / {Js.Num(ch)}; background-color: {bgColor}; box-shadow: 0 0 30px rgba(0,0,0,0.1); overflow: hidden; margin: 0 auto; transition: all 0.3s ease; }}");
        _dynamicCss.Add(
            $"@media (max-width: 768px) {{ .site-canvas {{ max-width: 100%; aspect-ratio: {Js.Num(_mobileW)} / {Js.Num(_mobileCanvasH)}; }} }}");

        string cssString = string.Join("\n    ", _dynamicCss);

        string jsString = "";
        if (_dynamicJs.Count > 0)
        {
            jsString = $"\n<script>\ndocument.addEventListener(\"DOMContentLoaded\", function() {{\n    {string.Join("\n    ", _dynamicJs)}\n}});\n</script>\n";
        }

        // ▼▼ SEO メタタグの構築 ▼▼
        ResolvedSeo seo = _scene.Seo ?? new ResolvedSeo();
        string lang = Js.EscapeHtml(Js.Or(seo.Lang, "ja"));
        string title = Js.EscapeHtml(Js.Or(seo.Title, "ページ"));

        var html = new StringBuilder();
        html.Append($"<!DOCTYPE html>\n<html lang=\"{lang}\">\n<head>\n");
        html.Append("    <meta charset=\"UTF-8\">\n");
        html.Append("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n");
        html.Append($"    <title>{title}</title>\n");
        if (Js.Truthy(seo.Description))
            html.Append($"    <meta name=\"description\" content=\"{Js.EscapeHtml(seo.Description)}\">\n");
        // OGP / Twitter カード
        html.Append("    <meta property=\"og:type\" content=\"website\">\n");
        html.Append($"    <meta property=\"og:title\" content=\"{title}\">\n");
        if (Js.Truthy(seo.Description))
            html.Append($"    <meta property=\"og:description\" content=\"{Js.EscapeHtml(seo.Description)}\">\n");
        if (Js.Truthy(seo.OgImage))
            html.Append($"    <meta property=\"og:image\" content=\"{Js.EscapeHtml(seo.OgImage)}\">\n");
        if (Js.Truthy(seo.SiteName))
            html.Append($"    <meta property=\"og:site_name\" content=\"{Js.EscapeHtml(seo.SiteName)}\">\n");
        html.Append($"    <meta name=\"twitter:card\" content=\"{(Js.Truthy(seo.OgImage) ? "summary_large_image" : "summary")}\">\n");
        // ▲▲ SEO メタタグ構築ここまで ▲▲

        html.Append("    <style>\n" + AnimCss + "\n    </style>\n");
        html.Append("    <style id=\"dynamic-styles\">\n    " + cssString + "\n    </style>\n");

        // スライダーがあれば Swiper の CSS を読み込む
        bool hasSwiper = elementsHtml.Contains("class=\"swiper");
        if (hasSwiper)
        {
            html.Append("    <link rel=\"stylesheet\" href=\"https://cdn.jsdelivr.net/npm/swiper@11/swiper-bundle.min.css\" />\n");
            html.Append("    <style>:root { --swiper-theme-color: #ffffff; }</style>\n");
        }

        html.Append($"</head>\n<body style=\"margin: 0; background-color: {bgColor};\">\n\n");

        // ▼▼ フォーム: 送信ボタンがあればページ全体を <form> でラップ ▼▼
        ElementProperties? submit = FindSubmitButton(_scene.Elements);
        string formOpen = "", formClose = "", afterCanvas = "";
        if (submit != null)
        {
            string userAction = (Js.Truthy(submit.Route) && submit.Route != "#") ? submit.Route! : "";
            // successMessage は nullish フォールバック（空文字はそのまま空＝オーバーレイ無し）
            string successMsg = submit.SuccessMessage ?? "送信ありがとうございました。";
            if (_mode == RenderMode.Blade)
            {
                string action = Js.EscapeHtml(Js.Or(Js.Or(_scene.FormAction, userAction), "/contact"));
                formOpen = $"<form action=\"{action}\" method=\"POST\">\n@csrf";
                formClose = "</form>";
                if (Js.Truthy(successMsg)) afterCanvas = SuccessOverlay(Js.EscapeHtml(successMsg), true);
            }
            else
            {
                string action = Js.EscapeHtml(Js.Or(userAction, "#"));
                string method = Js.Or(submit.Method, "POST").ToUpperInvariant() == "GET" ? "GET" : "POST";
                if (Js.Truthy(successMsg))
                {
                    formOpen = $"<form action=\"{action}\" method=\"{method}\" target=\"ksb_form_target\" onsubmit=\"setTimeout(function(){{var s=document.getElementById('ksb-form-success');if(s)s.style.display='flex';}},400);\">";
                    formClose = "</form>";
                    afterCanvas = "<iframe name=\"ksb_form_target\" style=\"display:none\"></iframe>\n" + SuccessOverlay(Js.EscapeHtml(successMsg), false);
                }
                else
                {
                    formOpen = $"<form action=\"{action}\" method=\"{method}\">";
                    formClose = "</form>";
                }
            }
        }

        html.Append("<div class=\"site-canvas\">\n");
        if (formOpen != "") html.Append(formOpen + "\n");
        html.Append(elementsHtml);
        if (formClose != "") html.Append(formClose + "\n");
        html.Append("\n</div>\n");
        if (afterCanvas != "") html.Append(afterCanvas + "\n");

        if (hasSwiper)
            html.Append("<script src=\"https://cdn.jsdelivr.net/npm/swiper@11/swiper-bundle.min.js\"></script>\n");

        html.Append(jsString);
        html.Append("\n</body>\n</html>");

        return html.ToString();
    }

    // 要素ツリーを再帰的にたどり、最初の「送信ボタン」(role==='submit')のプロパティを返す。
    private ElementProperties? FindSubmitButton(IEnumerable<SiteElement>? elements)
    {
        foreach (var el in elements ?? Enumerable.Empty<SiteElement>())
        {
            var p = el.Properties;
            if (p.Visible == false) continue;
            if (el.Type == "Button" && p.Role == "submit") return p;
            if (el.Children is { } children)
            {
                var found = FindSubmitButton(children);
                if (found != null) return found;
            }
        }
        return null;
    }

    // 送信完了メッセージの中央オーバーレイ。
    private static string SuccessOverlay(string msg, bool isBlade)
    {
        string inner = $"<div style=\"background:#fff; padding:24px 32px; border-radius:10px; font-size:16px; color:#222; box-shadow:0 10px 40px rgba(0,0,0,0.3); max-width:80%; text-align:center;\">{msg}<br><button type=\"button\" onclick=\"document.getElementById('ksb-form-success').style.display='none'\" style=\"margin-top:16px; padding:8px 20px; border:none; border-radius:6px; background:#007acc; color:#fff; cursor:pointer;\">OK</button></div>";
        string style = "position:fixed; inset:0; align-items:center; justify-content:center; background:rgba(0,0,0,0.5); z-index:9999;";
        if (isBlade)
            return $"@if(session('success'))\n<div id=\"ksb-form-success\" style=\"display:flex; {style}\">{inner}</div>\n@endif";
        return $"<div id=\"ksb-form-success\" style=\"display:none; {style}\">{inner}</div>";
    }

    // data:URL 画像を imageMap で出力パスに解決する。
    private string ResolveImageSrc(string? src)
    {
        if (src != null && src.StartsWith("data:image"))
            return _imageMap.TryGetValue(src, out var v) && !string.IsNullOrEmpty(v) ? v : src;
        return src ?? "";
    }

    // 要素配列を再帰的に HTML 文字列へ変換する中核メソッド。
    private string RenderElements(IEnumerable<SiteElement>? elements, double parentW, double parentH, int depth)
    {
        var sb = new StringBuilder();
        string indent = string.Concat(Enumerable.Repeat("    ", depth));

        foreach (var el in elements ?? Enumerable.Empty<SiteElement>())
        {
            string id = el.Id;
            string type = el.Type;
            var props = el.Properties;

            if (props.Visible == false) continue;

            // ▼▼ レスポンシブ座標の抽出とCSS構築（方針A: transformを正とする）▼▼
            var tf = el.Transform;
            double pcW = tf.Width;
            double pcH = tf.Height;

            double leftPc = parentW > 0 ? tf.X / parentW * 100 : 0;
            double topPc  = parentH > 0 ? tf.Y / parentH * 100 : 0;
            double wPc    = parentW > 0 ? pcW / parentW * 100 : 0;
            double hPc    = parentH > 0 ? pcH / parentH * 100 : 0;
            double fontPc = Js.OrNum(props.Fontsize, 16);

            // スマホ用：mobileEdited が true の要素だけ mobile レイアウトで上書き
            double leftMo = leftPc, topMo = topPc, wMo = wPc, hMo = hPc, fontMo = fontPc;
            if (props.MobileEdited == true && props.Layouts?.Mobile is { } lMo)
            {
                double canvasW = Js.OrNum(_scene.Canvas?.Width, 800);
                double canvasH = Js.OrNum(_scene.Canvas?.Height, 600);
                double parentMoW = parentW == canvasW ? _mobileW : parentW;
                double parentMoH = parentH == canvasH ? _mobileCanvasH : parentH;
                double mx = lMo.X ?? double.NaN, my = lMo.Y ?? double.NaN, mw = lMo.W ?? double.NaN, mh = lMo.H ?? double.NaN;
                leftMo = parentMoW > 0 ? mx / parentMoW * 100 : leftPc;
                topMo  = parentMoH > 0 ? my / parentMoH * 100 : topPc;
                wMo    = parentMoW > 0 ? mw / parentMoW * 100 : wPc;
                hMo    = parentMoH > 0 ? mh / parentMoH * 100 : hPc;
                fontMo = Js.OrNum(lMo.Fontsize, fontPc);
            }

            string className = $"el-{id}";

            // PC用スタイル (デフォルト)
            string heightRule = (type == "ArticleGrid" || type == "Accordion")
                ? $"height: auto; min-height: {Js.Num(hPc)}%"
                : $"height: {Js.Num(hPc)}%";
            double opacity = (props.Opacity is { } op && !double.IsNaN(op) && !double.IsInfinity(op))
                ? Math.Min(1, Math.Max(0, op)) : 1;
            string opacityRule = opacity != 1 ? $" opacity: {Js.Num(opacity)};" : "";
            string cssRule = $".{className} {{ left: {Js.Num(leftPc)}%; top: {Js.Num(topPc)}%; width: {Js.Num(wPc)}%; {heightRule}; font-size: {Js.Num(fontPc)}px;{opacityRule} transition: all 0.3s ease; }}";

            // スマホ: font-size を vw 基準にする
            string fontMoVw = Js.ToFixed(fontMo / _mobileW * 100, 2);
            string heightRuleMo = (type == "ArticleGrid" || type == "Accordion")
                ? "height: auto"
                : $"height: {Js.Num(hMo)}%";
            cssRule += $"\n    @media (max-width: 768px) {{ .{className} {{ left: {Js.Num(leftMo)}%; top: {Js.Num(topMo)}%; width: {Js.Num(wMo)}%; {heightRuleMo}; font-size: {fontMoVw}vw; }} }}";

            _dynamicCss.Add(cssRule);
            // ▲▲ レスポンシブCSS構築ここまで ▲▲

            // ▼▼ イベント(JS)の構築 ▼▼
            if (props.Events is { Count: > 0 } events)
            {
                foreach (var ev in events)
                {
                    string eventName = ev.Trigger == "hover" ? "mouseenter" : "click";
                    string actionJs = "";

                    if (ev.Action == "alert")
                    {
                        string safeMsg = Js.Or(ev.Target, "").Replace("\"", "\\\"");
                        actionJs = $"alert(\"{safeMsg}\");";
                    }
                    else if (Js.Truthy(ev.Target))
                    {
                        actionJs = $@"
            var t = document.getElementById(""{ev.Target}"");
            if (t) {{
                {(ev.Action == "show" ? "t.style.display = \"block\";" : "")}
                {(ev.Action == "hide" ? "t.style.display = \"none\";" : "")}
                {(ev.Action == "toggle" ? "t.style.display = (t.style.display === \"none\" ? \"block\" : \"none\");" : "")}
            }}";
                    }

                    if (actionJs != "")
                    {
                        _dynamicJs.Add($@"
    var el_{id} = document.getElementById(""{id}"");
    if (el_{id}) {{
        el_{id}.addEventListener(""{eventName}"", function(e) {{
            e.preventDefault();
            {actionJs}
        }});
    }}");
                    }
                }
            }
            // ▲▲ イベント(JS)構築ここまで ▲▲

            // 共通プロパティの展開
            string text    = Js.EscapeHtml(props.Text ?? "");
            string name    = Js.EscapeHtml(props.Name ?? "Unnamed");
            string bgcolor = Js.EscapeHtml(props.Bgcolor ?? "transparent");
            string color   = Js.EscapeHtml(props.Color ?? "inherit");
            string align   = Js.EscapeHtml(Js.Or(props.Align, type == "Button" ? "center" : "left"));
            string fontfam = Js.EscapeHtml(Js.Or(props.FontFamily, "sans-serif"));

            string animClass = className;
            if (Js.Truthy(props.Animation) && props.Animation != "none")
                animClass += " anim-" + props.Animation!.ToLowerInvariant();

            string shadow = Js.Or(props.Shadow, "none");
            string shadowStyle = "";
            if (ShadowCss.TryGetValue(shadow, out var shadowVal))
                shadowStyle = (type == "Label" ? "text-shadow: " : "box-shadow: ") + shadowVal + ";";

            // width等を除いたベーススタイル
            string baseStyle = "position: absolute; box-sizing: border-box;";
            if (type != "Group" && type != "ArticleGrid" && type != "Accordion" && type != "Triangle")
            {
                baseStyle += $" background-color: {bgcolor}; color: {color}; text-align: {align}; font-family: {fontfam};";
                if (type != "Button" && type != "Image") baseStyle += $" {shadowStyle}";
            }

            sb.Append($"{indent}\n");

            switch (type)
            {
                case "Group":
                    sb.Append(RenderGroup(id, animClass, baseStyle, bgcolor, el, pcW, pcH, depth, indent));
                    break;
                case "Button":
                    sb.Append(RenderButton(id, animClass, baseStyle, color, text, props, shadowStyle, indent));
                    break;
                case "TextInput":
                    sb.Append(RenderTextInput(id, animClass, baseStyle, text, props, indent));
                    break;
                case "Label":
                    sb.Append(RenderLabel(id, animClass, baseStyle, text, shadowStyle, indent));
                    break;
                case "Rect":
                    sb.Append($"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{baseStyle}\"></div>\n");
                    break;
                case "Circle":
                    sb.Append($"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{baseStyle} border-radius: 50%;\"></div>\n");
                    break;
                case "Warp":
                {
                    var pts = props.WarpPoints ?? new List<WarpPoint>();
                    if (pts.Count >= 3)
                    {
                        double minX = pts.Min(p => p.X), minY = pts.Min(p => p.Y);
                        double bw = pts.Max(p => p.X) - minX; if (bw == 0) bw = 1;
                        double bh = pts.Max(p => p.Y) - minY; if (bh == 0) bh = 1;
                        string poly = string.Join(", ", pts.Select(p =>
                            $"{Js.ToFixed((p.X - minX) / bw * 100, 2)}% {Js.ToFixed((p.Y - minY) / bh * 100, 2)}%"));
                        string warpStyle = $"position: absolute; left: {Js.Num(leftPc)}%; top: {Js.Num(topPc)}%; width: {Js.Num(wPc)}%; height: {Js.Num(hPc)}%; background-color: {bgcolor}; clip-path: polygon({poly}); {shadowStyle}";
                        sb.Append($"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{warpStyle}\"></div>\n");
                    }
                    else
                    {
                        sb.Append($"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{baseStyle}\"></div>\n");
                    }
                    break;
                }
                case "Triangle":
                {
                    string triStyle = $"position: absolute; width: 100%; height: 100%; box-sizing: border-box; clip-path: polygon(50% 0%, 100% 100%, 0% 100%); background-color: {bgcolor};";
                    sb.Append($"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{baseStyle}\"><div style=\"{triStyle}\"></div></div>\n");
                    break;
                }
                case "Image":
                    sb.Append(RenderImage(id, animClass, baseStyle, props, name, shadowStyle, indent));
                    break;
                case "Slider":
                    sb.Append(RenderSlider(id, animClass, baseStyle, props, indent));
                    break;
                case "ArticleGrid":
                    sb.Append(RenderArticleGrid(id, animClass, baseStyle, props, indent));
                    break;
                case "Accordion":
                    sb.Append(RenderAccordion(id, animClass, baseStyle, props, indent));
                    break;
            }
        }
        return sb.ToString();
    }

    // グループ（入れ子コンテナ）
    private string RenderGroup(string id, string animClass, string baseStyle, string bgcolor,
        SiteElement el, double width, double height, int depth, string indent)
    {
        var sb = new StringBuilder();
        sb.Append($"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{baseStyle} background-color: {bgcolor};\">\n");
        if (el.Children != null)
            sb.Append(RenderElements(el.Children, width, height, depth + 1));
        sb.Append($"{indent}</div>\n");
        return sb.ToString();
    }

    // ボタン。role==='submit' は <form> 送信ボタン、それ以外は <a> リンク。
    private string RenderButton(string id, string animClass, string baseStyle, string color,
        string text, ElementProperties props, string shadowStyle, string indent)
    {
        string bgcolor = Js.EscapeHtml(props.Bgcolor ?? "transparent");
        string bgStyle = $"background-color: {bgcolor};";
        if (Js.Truthy(props.BgImage))
        {
            string src = Js.EscapeHtml(ResolveImageSrc(props.BgImage));
            bgStyle = $"background-image: url('{src}'); background-size: cover; background-position: center;";
        }

        string align = Js.EscapeHtml(Js.Or(props.Align, "center"));
        string btnStyle = $"width: 100%; height: 100%; box-sizing: border-box; {bgStyle} color: {color}; font-size: inherit; border: none; border-radius: 5px; cursor: pointer; font-weight: bold; text-align: {align}; {shadowStyle}";
        string formStyle = "margin: 0; position: absolute; width: 100%; height: 100%;";

        if (props.Role == "submit")
        {
            var sub = new StringBuilder();
            sub.Append($"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{baseStyle} background:none;\">\n");
            sub.Append($"{indent}    <button type=\"submit\" style=\"{btnStyle} {formStyle}\">{text}</button>\n");
            sub.Append($"{indent}</div>\n");
            return sub.ToString();
        }

        string url = Js.EscapeHtml(Js.Truthy(props.Route) && props.Route != "#" ? props.Route! : "#");
        var sb = new StringBuilder();
        sb.Append($"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{baseStyle} background:none;\">\n");
        sb.Append($"{indent}    <a href=\"{url}\" style=\"{formStyle} display:block; text-decoration:none;\">\n");
        sb.Append($"{indent}        <button type=\"button\" style=\"{btnStyle}\">{text}</button>\n");
        sb.Append($"{indent}    </a>\n");
        sb.Append($"{indent}</div>\n");
        return sb.ToString();
    }

    // 入力欄。
    private string RenderTextInput(string id, string animClass, string baseStyle,
        string text, ElementProperties props, string indent)
    {
        string name = Js.EscapeHtml(Js.Or(props.InputName, ""));
        string nameAttr = Js.Truthy(name) ? $" name=\"{name}\"" : "";
        string required = props.Required == true ? " required" : "";
        string ph = Js.EscapeHtml(Js.Or(text, ""));
        var allowed = new[] { "text", "email", "tel", "number" };
        string rawType = Js.Or(props.InputType, "text");

        if (rawType == "textarea")
        {
            string taStyle = baseStyle + " padding: 10px; border: 1px solid #ccc; border-radius: 4px; background-color: #fff; width: 100%; height: 100%; resize: none; font-family: inherit;";
            return $"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"position:absolute;\"><textarea{nameAttr} placeholder=\"{ph}\"{required} style=\"{taStyle}\"></textarea></div>\n";
        }

        string itype = allowed.Contains(rawType) ? rawType : "text";
        string style = baseStyle + " padding: 10px; border: 1px solid #ccc; border-radius: 4px; background-color: #fff; width: 100%; height: 100%;";
        return $"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"position:absolute;\"><input type=\"{itype}\"{nameAttr} placeholder=\"{ph}\"{required} style=\"{style}\"></div>\n";
    }

    // テキスト（見出し・本文）
    private string RenderLabel(string id, string animClass, string baseStyle,
        string text, string shadowStyle, string indent)
    {
        string style = $"{baseStyle} display: block; overflow: hidden; {shadowStyle}";
        return $"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{style}\">{text}</div>\n";
    }

    // 画像。object-fit:contain で全体表示。route があればリンク化。
    private string RenderImage(string id, string animClass, string baseStyle,
        ElementProperties props, string name, string shadowStyle, string indent)
    {
        string src = Js.EscapeHtml(ResolveImageSrc(props.Text));
        string route = props.Route ?? "#";
        bool hasLink = route != "#" && route != "" && route != "none";
        string imgStyle = $"width: 100%; height: 100%; object-fit: contain; display: block; {shadowStyle}";

        if (hasLink)
        {
            string url = Js.EscapeHtml(route);
            var sb = new StringBuilder();
            sb.Append($"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{baseStyle} background:none;\">\n");
            sb.Append($"{indent}    <a href=\"{url}\" style=\"display:block; width:100%; height:100%;\">\n");
            sb.Append($"{indent}        <img src=\"{src}\" alt=\"{name}\" style=\"{imgStyle}\">\n");
            sb.Append($"{indent}    </a>\n");
            sb.Append($"{indent}</div>\n");
            return sb.ToString();
        }

        return $"{indent}<img id=\"{id}\" src=\"{src}\" alt=\"{name}\" class=\"{animClass}\" style=\"{baseStyle} {imgStyle}\">\n";
    }

    // 画像スライダー（Swiper）
    private string RenderSlider(string id, string animClass, string baseStyle,
        ElementProperties props, string indent)
    {
        List<SlideItem> slides = props.Slider?.Slides ?? new List<SlideItem>();
        if (slides.Count == 0)
        {
            var legacy = Js.Or(props.Text, "")
                .Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s));
            slides = legacy.Select(url => new SlideItem
            { Image = url, Title = "", Text = "", LinkType = "none", Link = "" }).ToList();
        }

        if (slides.Count == 0)
            return $"{indent}<div id=\"{id}\" style=\"{baseStyle} background:#333; color:#fff; display:flex; align-items:center; justify-content:center;\">スライドが設定されていません</div>\n";

        var s = props.Slider ?? new SliderConfig();
        string effect    = s.Effect ?? "slide";
        double speed     = s.Speed ?? 600;
        bool autoplay    = s.Autoplay ?? true;
        double delay     = s.Delay ?? 3000;
        bool loop        = s.Loop ?? true;
        bool pagination  = s.Pagination ?? true;
        bool navigation  = s.Navigation ?? true;
        double slidesPerView = s.SlidesPerView ?? 1;

        bool useGrid  = effect == "grid";
        bool useCards = effect == "cards";
        bool useCover = effect == "coverflow";
        bool useCube  = effect == "cube";
        bool useFade  = effect == "fade";

        string swiperEffect = useGrid ? "slide" : effect;

        var sb = new StringBuilder();
        sb.Append($"{indent}<div id=\"{id}\" class=\"swiper {animClass}\" style=\"{baseStyle} border-radius: 5px; overflow:hidden;\">\n");
        sb.Append($"{indent}    <div class=\"swiper-wrapper\">\n");

        foreach (var sl in slides)
        {
            string img = Js.Truthy(sl.Image) ? Js.EscapeHtml(ResolveImageSrc(sl.Image)) : "";
            string title = Js.EscapeHtml(Js.Or(sl.Title, ""));
            string text = Js.EscapeHtml(Js.Or(sl.Text, ""));
            bool hasOverlay = Js.Truthy(sl.Title) || Js.Truthy(sl.Text);

            string openTag = "", closeTag = "";
            if (sl.LinkType == "url" && Js.Truthy(sl.Link))
            {
                string url = Js.EscapeHtml(sl.Link);
                openTag = $"<a href=\"{url}\" target=\"_blank\" rel=\"noopener noreferrer\" style=\"display:block; width:100%; height:100%; text-decoration:none; color:inherit;\">";
                closeTag = "</a>";
            }
            else if (sl.LinkType == "page" && Js.Truthy(sl.Link))
            {
                string url = Js.EscapeHtml(sl.Link);
                openTag = $"<a href=\"{url}\" style=\"display:block; width:100%; height:100%; text-decoration:none; color:inherit;\">";
                closeTag = "</a>";
            }

            sb.Append($"{indent}        <div class=\"swiper-slide\" style=\"position:relative; background:#222;\">\n");
            sb.Append($"{indent}            {openTag}\n");
            if (Js.Truthy(img))
                sb.Append($"{indent}                <img src=\"{img}\" style=\"width:100%; height:100%; object-fit:cover; display:block;\">\n");
            if (hasOverlay)
            {
                sb.Append($"{indent}                <div style=\"position:absolute; left:0; right:0; bottom:0; padding:16px 20px; background:linear-gradient(transparent, rgba(0,0,0,0.7)); color:#fff;\">\n");
                if (Js.Truthy(title)) sb.Append($"{indent}                    <div style=\"font-size:18px; font-weight:bold; margin-bottom:4px;\">{title}</div>\n");
                if (Js.Truthy(text))  sb.Append($"{indent}                    <div style=\"font-size:13px; opacity:0.9;\">{text}</div>\n");
                sb.Append($"{indent}                </div>\n");
            }
            sb.Append($"{indent}            {closeTag}\n");
            sb.Append($"{indent}        </div>\n");
        }
        sb.Append($"{indent}    </div>\n");

        if (pagination) sb.Append($"{indent}    <div class=\"swiper-pagination\"></div>\n");
        if (navigation)
        {
            sb.Append($"{indent}    <div class=\"swiper-button-prev\"></div>\n");
            sb.Append($"{indent}    <div class=\"swiper-button-next\"></div>\n");
        }
        sb.Append($"{indent}</div>\n");

        var opts = new List<string>
        {
            $"speed: {Js.Num(speed)}",
            $"loop: {Js.Bool(loop)}",
            $"slidesPerView: {Js.Num(slidesPerView)}",
        };
        if (slidesPerView > 1) opts.Add("spaceBetween: 10");
        opts.Add($"effect: '{swiperEffect}'");
        if (useFade)  opts.Add("fadeEffect: { crossFade: true }");
        if (useCube)  opts.Add("cubeEffect: { shadow: true, slideShadows: true, shadowOffset: 20, shadowScale: 0.94 }");
        if (useCover) opts.Add("coverflowEffect: { rotate: 30, stretch: 0, depth: 100, modifier: 1, slideShadows: true }");
        if (useCards) opts.Add("cardsEffect: { perSlideOffset: 8, perSlideRotate: 2 }");
        if (autoplay) opts.Add($"autoplay: {{ delay: {Js.Num(delay)}, disableOnInteraction: false }}");
        if (pagination) opts.Add($"pagination: {{ el: '#{id} .swiper-pagination', clickable: true }}");
        if (navigation) opts.Add($"navigation: {{ nextEl: '#{id} .swiper-button-next', prevEl: '#{id} .swiper-button-prev' }}");

        _dynamicJs.Add($@"
        if (typeof Swiper !== 'undefined') {{
            new Swiper('#{id}', {{
                {string.Join(",\n                ", opts)}
            }});
        }}");

        return sb.ToString();
    }

    // 記事グリッド
    private string RenderArticleGrid(string id, string animClass, string baseStyle,
        ElementProperties props, string indent)
    {
        var g = props.Grid ?? new GridConfig();
        var items = g.Items ?? new List<GridItem>();
        double columns    = g.Columns ?? 3;
        double gap        = g.Gap ?? 20;
        double cardRadius = g.CardRadius ?? 8;
        string arrowColor = Js.Or(g.ArrowColor, "#27ae60");
        string imgRatio   = Js.Or(g.ImgRatio, "16/10");
        double cardPadding = g.CardPadding ?? 18;
        bool sliderMode   = g.SliderMode ?? false;

        if (items.Count == 0)
            return $"{indent}<div id=\"{id}\" style=\"{baseStyle} background:#f1f2f6; color:#666; display:flex; align-items:center; justify-content:center;\">アイテムが設定されていません</div>\n";

        string BuildCard(GridItem it)
        {
            string img = Js.Truthy(it.Image) ? Js.EscapeHtml(ResolveImageSrc(it.Image)) : "";
            string title = Js.EscapeHtml(Js.Or(it.Title, ""));
            string text = Js.EscapeHtml(Js.Or(it.Text, ""));

            string href = "", target = "";
            if (it.LinkType == "url" && Js.Truthy(it.Link))
            {
                href = Js.EscapeHtml(it.Link);
                target = " target=\"_blank\" rel=\"noopener noreferrer\"";
            }
            else if (it.LinkType == "page" && Js.Truthy(it.Link))
            {
                href = Js.EscapeHtml(it.Link);
            }
            bool isLink = Js.Truthy(href);

            string cardStyle = $"position: relative; background: #ffffff; border-radius: {Js.Num(cardRadius)}px; overflow: hidden; box-shadow: 0 2px 8px rgba(0,0,0,0.06); transition: transform 0.2s, box-shadow 0.2s; height:100%; {(isLink ? "cursor: pointer;" : "")}";
            string tag = isLink ? "a" : "div";
            string linkAttrs = isLink ? $"href=\"{href}\"{target} style=\"text-decoration: none; color: inherit; display: block; height:100%;\"" : "";

            var c = new StringBuilder();
            c.Append($"<{tag} {linkAttrs} class=\"article-card\" style=\"{cardStyle}\">");
            if (Js.Truthy(img) && imgRatio != "none")
                c.Append($"<div style=\"width:100%; aspect-ratio: {imgRatio}; overflow:hidden;\"><img src=\"{img}\" style=\"width:100%; height:100%; object-fit:cover; display:block;\" alt=\"{title}\"></div>");
            else if (Js.Truthy(img))
                c.Append($"<div style=\"width:100%; overflow:hidden;\"><img src=\"{img}\" style=\"width:100%; height:auto; object-fit:cover; display:block;\" alt=\"{title}\"></div>");
            c.Append($"<div style=\"padding: {Js.Num(cardPadding)}px {Js.Num(cardPadding + 2)}px {Js.Num(cardPadding + 32)}px {Js.Num(cardPadding + 2)}px;\">");
            if (Js.Truthy(title)) c.Append($"<div style=\"font-size: 17px; font-weight: bold; color: #222; margin-bottom: 10px; line-height: 1.4;\">{title}</div>");
            if (Js.Truthy(text))  c.Append($"<div style=\"font-size: 13px; color: #666; line-height: 1.6;\">{text}</div>");
            c.Append("</div>");
            if (isLink)
                c.Append($"<div style=\"position:absolute; right:14px; bottom:14px; width:36px; height:36px; background:{arrowColor}; border-radius:50%; display:flex; align-items:center; justify-content:center; color:#fff; font-size:18px; line-height:1;\">→</div>");
            c.Append($"</{tag}>");
            return c.ToString();
        }

        _dynamicCss.Add($"#{id} .article-card:hover {{ transform: translateY(-4px); box-shadow: 0 8px 20px rgba(0,0,0,0.12); }}");

        if (sliderMode)
        {
            bool autoplay   = g.Autoplay ?? false;
            double delay    = g.Delay ?? 3000;
            bool loop       = g.Loop ?? true;
            bool navigation = g.Navigation ?? true;

            var sb2 = new StringBuilder();
            sb2.Append($"{indent}<div id=\"{id}\" class=\"swiper {animClass}\" style=\"{baseStyle} padding: 0 {(navigation ? "44px" : "0")};\">\n");
            sb2.Append($"{indent}    <div class=\"swiper-wrapper\" style=\"padding-bottom:4px;\">\n");
            foreach (var it in items)
                sb2.Append($"{indent}        <div class=\"swiper-slide\" style=\"height:auto;\">{BuildCard(it)}</div>\n");
            sb2.Append($"{indent}    </div>\n");
            if (navigation)
            {
                sb2.Append($"{indent}    <div class=\"swiper-button-prev\" style=\"color:{arrowColor};\"></div>\n");
                sb2.Append($"{indent}    <div class=\"swiper-button-next\" style=\"color:{arrowColor};\"></div>\n");
            }
            sb2.Append($"{indent}</div>\n");

            var opts = new List<string>
            {
                $"slidesPerView: {Js.Num(columns)}",
                $"spaceBetween: {Js.Num(gap)}",
                $"loop: {Js.Bool(loop)}",
            };
            if (autoplay) opts.Add($"autoplay: {{ delay: {Js.Num(delay)}, disableOnInteraction: false }}");
            if (navigation) opts.Add($"navigation: {{ nextEl: '#{id} .swiper-button-next', prevEl: '#{id} .swiper-button-prev' }}");
            opts.Add($"breakpoints: {{ 0: {{ slidesPerView: 1 }}, 600: {{ slidesPerView: {Js.Num(Math.Min(2, columns))} }}, 900: {{ slidesPerView: {Js.Num(columns)} }} }}");

            _dynamicJs.Add($@"
        if (typeof Swiper !== 'undefined') {{
            new Swiper('#{id}', {{
                {string.Join(",\n                ", opts)}
            }});
        }}");

            return sb2.ToString();
        }

        string containerStyle = $"{baseStyle} display: grid; grid-template-columns: repeat({Js.Num(columns)}, 1fr); gap: {Js.Num(gap)}px; padding: 0; box-sizing: border-box; align-items: start;";
        var sb = new StringBuilder();
        sb.Append($"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{containerStyle}\">\n");
        foreach (var it in items)
            sb.Append($"{indent}    {BuildCard(it)}\n");
        sb.Append($"{indent}</div>\n");

        _dynamicCss.Add($"@media (max-width: 768px) {{ #{id} {{ grid-template-columns: 1fr !important; }} }}");

        return sb.ToString();
    }

    // アコーディオン
    private string RenderAccordion(string id, string animClass, string baseStyle,
        ElementProperties props, string indent)
    {
        var a = props.Accordion ?? new AccordionConfig();
        var items = a.Items ?? new List<AccordionItem>();
        string headerColor = Js.Or(a.HeaderColor, "#2c3e50");
        string headerBg    = Js.Or(a.HeaderBg, "#f7f9fa");
        string bodyColor   = Js.Or(a.BodyColor, "#555555");
        bool openFirst     = a.OpenFirst ?? true;

        if (items.Count == 0)
            return $"{indent}<div id=\"{id}\" style=\"{baseStyle} background:#f1f2f6; color:#666; display:flex; align-items:center; justify-content:center;\">項目が設定されていません</div>\n";

        var sb = new StringBuilder();
        sb.Append($"{indent}<div id=\"{id}\" class=\"accordion {animClass}\" style=\"{baseStyle} background:transparent;\">\n");
        for (int idx = 0; idx < items.Count; idx++)
        {
            var it = items[idx];
            string title = Js.EscapeHtml(Js.Or(it.Title, ""));
            string content = Js.EscapeHtml(Js.Or(it.Content, "")).Replace("\n", "<br>");
            bool isOpen = openFirst && idx == 0;

            sb.Append($"{indent}    <div class=\"acc-item\" style=\"border:1px solid #e0e0e0; border-radius:6px; margin-bottom:8px; overflow:hidden;\">\n");
            sb.Append($"{indent}        <button class=\"acc-header\" aria-expanded=\"{Js.Bool(isOpen)}\" style=\"width:100%; text-align:left; padding:16px 20px; background:{headerBg}; color:{headerColor}; border:none; font-size:16px; font-weight:bold; cursor:pointer; display:flex; justify-content:space-between; align-items:center;\">\n");
            sb.Append($"{indent}            <span>{title}</span>\n");
            sb.Append($"{indent}            <span class=\"acc-icon\" style=\"transition:transform 0.3s; transform:rotate({(isOpen ? "180deg" : "0deg")});\">▼</span>\n");
            sb.Append($"{indent}        </button>\n");
            sb.Append($"{indent}        <div class=\"acc-body\" style=\"max-height:{(isOpen ? "500px" : "0")}; overflow:hidden; transition:max-height 0.3s ease;\">\n");
            sb.Append($"{indent}            <div style=\"padding:16px 20px; color:{bodyColor}; font-size:14px; line-height:1.7;\">{content}</div>\n");
            sb.Append($"{indent}        </div>\n");
            sb.Append($"{indent}    </div>\n");
        }
        sb.Append($"{indent}</div>\n");

        _dynamicJs.Add($@"
        (function() {{
            var acc = document.getElementById('{id}');
            if (!acc) return;
            acc.querySelectorAll('.acc-header').forEach(function(btn) {{
                btn.addEventListener('click', function() {{
                    var body = btn.nextElementSibling;
                    var icon = btn.querySelector('.acc-icon');
                    var isOpen = btn.getAttribute('aria-expanded') === 'true';
                    if (isOpen) {{
                        body.style.maxHeight = '0';
                        btn.setAttribute('aria-expanded', 'false');
                        if (icon) icon.style.transform = 'rotate(0deg)';
                    }} else {{
                        body.style.maxHeight = body.scrollHeight + 'px';
                        btn.setAttribute('aria-expanded', 'true');
                        if (icon) icon.style.transform = 'rotate(180deg)';
                    }}
                }});
            }});
        }})();");

        return sb.ToString();
    }
}
