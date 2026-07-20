using System.Text;
using System.Text.Json;
using static SiteBuilder.Core.Js;
using static SiteBuilder.Core.Export.CssHelpers;

namespace SiteBuilder.Core.Export;

// 1ページ分の入力（renderer.js の sceneData 相当）/ one page of input (renderer.js's sceneData).
public sealed class Scene
{
    public JObj Canvas;            // project.settings.canvas
    public string BgColor = "";    // 解決済み背景色 / already-resolved background color
    public JsonElement Elements;   // 要素配列 / element array
    public JObj Seo;               // 解決済み SEO（生オブジェクトとして参照）/ resolved SEO (accessed as a raw object)
    public string? FormAction;     // Blade のフォーム action / Blade form action
}

// ============================================================
// renderer.js（HtmlRenderer）の移植。sceneData → 完成HTML文字列。
// Port of renderer.js (HtmlRenderer): sceneData → finished HTML string.
// ============================================================
public sealed class HtmlRenderer
{
    // render-components.js 相当から参照される（初期化JS/CSS/画像解決）。
    public readonly List<string> DynamicCss = new();
    public readonly List<string> DynamicJs = new();
    public readonly IReadOnlyDictionary<string, string> ImageMap;

    private readonly Scene _scene;
    private readonly string _mode;
    private readonly IReadOnlyList<string>? _cssHrefs;
    private string? _extractedCss;
    private readonly List<string> _usedFonts = new(); // 挿入順を保つ集合 / insertion-ordered set
    private double _mobileW = 375;
    private double _mobileCanvasH = 800;

    public HtmlRenderer(Scene scene, string mode = "static",
        IReadOnlyDictionary<string, string>? imageMap = null, IReadOnlyList<string>? cssHrefs = null)
    {
        _scene = scene;
        _mode = string.IsNullOrEmpty(mode) ? "static" : mode; // options.mode || 'static'
        ImageMap = imageMap ?? new Dictionary<string, string>();
        _cssHrefs = cssHrefs;                                 // options.cssHrefs || null
    }

    private static string N(double d) => NumStr(d);
    private void AddFont(string spec) { if (!_usedFonts.Contains(spec)) _usedFonts.Add(spec); }

    public string? GetExtractedCss() => _extractedCss;

    public string Render()
    {
        double cw = _scene.Canvas.NumN("width", 800);
        double ch = _scene.Canvas.NumN("height", 600);
        _mobileW = _scene.Canvas.NumN("mobileWidth", 375);
        _mobileCanvasH = _scene.Canvas.NumN("mobileHeight", 800);

        string elementsHtml = RenderElements(_scene.Elements, cw, ch, 1);

        string bgColor = _scene.BgColor.Length > 0 ? _scene.BgColor : "#f1f2f6"; // scene.bgColor || '#f1f2f6'

        DynamicCss.Add($".site-canvas {{ position: relative; width: 100%; max-width: {N(cw)}px; aspect-ratio: {N(cw)} / {N(ch)}; background-color: {bgColor}; box-shadow: 0 0 30px rgba(0,0,0,0.1); overflow: hidden; margin: 0 auto; transition: all 0.3s ease; }}");
        DynamicCss.Add($"@media (max-width: 768px) {{ .site-canvas {{ max-width: 100%; aspect-ratio: {N(_mobileW)} / {N(_mobileCanvasH)}; }} }}");

        string cssString = string.Join("\n    ", DynamicCss);

        string jsString = "";
        if (DynamicJs.Count > 0)
            jsString = "\n<script>\ndocument.addEventListener(\"DOMContentLoaded\", function() {\n    " + string.Join("\n    ", DynamicJs) + "\n});\n</script>\n";

        var seo = _scene.Seo;
        string lang = EscapeHtml(seo.StrT("lang", "ja"));
        string title = EscapeHtml(seo.StrT("title", "ページ"));

        var html = new StringBuilder();
        html.Append($"<!DOCTYPE html>\n<html lang=\"{lang}\">\n<head>\n");
        html.Append("    <meta charset=\"UTF-8\">\n");
        html.Append("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n");
        html.Append($"    <title>{title}</title>\n");
        if (seo.Truthy("description"))
            html.Append($"    <meta name=\"description\" content=\"{EscapeHtml(seo.Raw("description"))}\">\n");
        html.Append("    <meta property=\"og:type\" content=\"website\">\n");
        html.Append($"    <meta property=\"og:title\" content=\"{title}\">\n");
        if (seo.Truthy("description"))
            html.Append($"    <meta property=\"og:description\" content=\"{EscapeHtml(seo.Raw("description"))}\">\n");
        if (seo.Truthy("ogImage"))
            html.Append($"    <meta property=\"og:image\" content=\"{EscapeHtml(seo.Raw("ogImage"))}\">\n");
        if (seo.Truthy("siteName"))
            html.Append($"    <meta property=\"og:site_name\" content=\"{EscapeHtml(seo.Raw("siteName"))}\">\n");
        html.Append($"    <meta name=\"twitter:card\" content=\"{(seo.Truthy("ogImage") ? "summary_large_image" : "summary")}\">\n");

        if (_usedFonts.Count > 0)
        {
            html.Append("    <link rel=\"preconnect\" href=\"https://fonts.googleapis.com\">\n");
            html.Append("    <link rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin>\n");
            string fams = string.Join("&", _usedFonts.Select(s => "family=" + s));
            html.Append($"    <link href=\"https://fonts.googleapis.com/css2?{fams}&display=swap\" rel=\"stylesheet\">\n");
        }

        if (_cssHrefs != null)
        {
            foreach (var href in _cssHrefs)
                html.Append($"    <link rel=\"stylesheet\" href=\"{href.Replace("\"", "&quot;")}\">\n");
            _extractedCss = cssString;
        }
        else
        {
            html.Append("    <style>\n" + AnimCss + "\n    </style>\n");
            html.Append("    <style id=\"dynamic-styles\">\n    " + cssString + "\n    </style>\n");
        }

        if (elementsHtml.Contains("class=\"swiper"))
        {
            html.Append("    <link rel=\"stylesheet\" href=\"https://cdn.jsdelivr.net/npm/swiper@11/swiper-bundle.min.css\" />\n");
            html.Append("    <style>:root { --swiper-theme-color: #ffffff; }</style>\n");
        }

        html.Append($"</head>\n<body style=\"margin: 0; background-color: {bgColor};\">\n\n");

        var submit = FindSubmitButton(_scene.Elements);
        string formOpen = "", formClose = "", afterCanvas = "";
        if (submit.HasValue)
        {
            var s = submit.Value;
            string userAction = (s.Truthy("route") && !s.Eq("route", "#")) ? Str(s.Raw("route")) : "";
            string successMsg = s.StrN("successMessage", "送信ありがとうございました。");
            if (_mode == "blade")
            {
                string action = EscapeHtml(
                    !string.IsNullOrEmpty(_scene.FormAction) ? _scene.FormAction!
                    : userAction.Length > 0 ? userAction : "/contact");
                formOpen = $"<form action=\"{action}\" method=\"POST\">\n@csrf";
                formClose = "</form>";
                if (successMsg.Length > 0) afterCanvas = SuccessOverlay(EscapeHtml(successMsg), true);
            }
            else
            {
                string action = EscapeHtml(userAction.Length > 0 ? userAction : "#");
                string method = s.StrT("method", "POST").ToUpperInvariant() == "GET" ? "GET" : "POST";
                if (successMsg.Length > 0)
                {
                    formOpen = $"<form action=\"{action}\" method=\"{method}\" target=\"ksb_form_target\" onsubmit=\"setTimeout(function(){{var s=document.getElementById('ksb-form-success');if(s)s.style.display='flex';}},400);\">";
                    formClose = "</form>";
                    afterCanvas = "<iframe name=\"ksb_form_target\" style=\"display:none\"></iframe>\n" + SuccessOverlay(EscapeHtml(successMsg), false);
                }
                else
                {
                    formOpen = $"<form action=\"{action}\" method=\"{method}\">";
                    formClose = "</form>";
                }
            }
        }

        html.Append("<div class=\"site-canvas\">\n");
        if (formOpen.Length > 0) html.Append(formOpen + "\n");
        html.Append(elementsHtml);
        if (formClose.Length > 0) html.Append(formClose + "\n");
        html.Append("\n</div>\n");
        if (afterCanvas.Length > 0) html.Append(afterCanvas + "\n");

        if (elementsHtml.Contains("class=\"swiper"))
            html.Append("<script src=\"https://cdn.jsdelivr.net/npm/swiper@11/swiper-bundle.min.js\"></script>\n");

        html.Append(jsString);
        html.Append("\n</body>\n</html>");
        return html.ToString();
    }

    // 最初の送信ボタン(role==='submit')のプロパティを返す（無ければ null）。
    private static JObj? FindSubmitButton(JsonElement elements)
    {
        foreach (var el in EnumArr(elements))
        {
            var elO = new JObj(el);
            var p = new JObj(elO.Raw("properties"));
            if (p.Raw("visible").ValueKind == JsonValueKind.False) continue;
            if (elO.Eq("type", "Button") && p.Eq("role", "submit")) return p;
            var ch = elO.Raw("children");
            if (ch.ValueKind == JsonValueKind.Array)
            {
                var found = FindSubmitButton(ch);
                if (found.HasValue) return found;
            }
        }
        return null;
    }

    private static string SuccessOverlay(string msg, bool isBlade)
    {
        string inner = $"<div style=\"background:#fff; padding:24px 32px; border-radius:10px; font-size:16px; color:#222; box-shadow:0 10px 40px rgba(0,0,0,0.3); max-width:80%; text-align:center;\">{msg}<br><button type=\"button\" onclick=\"document.getElementById('ksb-form-success').style.display='none'\" style=\"margin-top:16px; padding:8px 20px; border:none; border-radius:6px; background:#007acc; color:#fff; cursor:pointer;\">OK</button></div>";
        string style = "position:fixed; inset:0; align-items:center; justify-content:center; background:rgba(0,0,0,0.5); z-index:9999;";
        if (isBlade)
            return $"@if(session('success'))\n<div id=\"ksb-form-success\" style=\"display:flex; {style}\">{inner}</div>\n@endif";
        return $"<div id=\"ksb-form-success\" style=\"display:none; {style}\">{inner}</div>";
    }

    // 要素配列を再帰的にHTML化する中核メソッド。
    public string RenderElements(JsonElement elements, double parentW, double parentH, int depth)
    {
        var sb = new StringBuilder();
        string indent = new string(' ', 4 * depth);

        foreach (var el in EnumArr(elements))
        {
            var elO = new JObj(el);
            string id = Str(elO.Raw("id"));
            string type = Str(elO.Raw("type"));
            var props = new JObj(elO.Raw("properties"));

            if (props.Raw("visible").ValueKind == JsonValueKind.False) continue;

            // transform（真値なら採用、無ければ既定）/ transform (used when truthy, else defaults).
            var tfRaw = elO.Raw("transform");
            double tfx, tfy, pcW, pcH;
            if (Truthy(tfRaw))
            {
                var tf = new JObj(tfRaw);
                tfx = ToNum(tf.Raw("x"));
                tfy = ToNum(tf.Raw("y"));
                pcW = ToNum(tf.Raw("width"));
                pcH = ToNum(tf.Raw("height"));
            }
            else { tfx = 0; tfy = 0; pcW = 100; pcH = 50; }

            double leftPc = parentW > 0 ? (tfx / parentW) * 100 : 0;
            double topPc = parentH > 0 ? (tfy / parentH) * 100 : 0;
            double wPc = parentW > 0 ? (pcW / parentW) * 100 : 0;
            double hPc = parentH > 0 ? (pcH / parentH) * 100 : 0;
            double fontPc = props.NumT("fontsize", 16); // props.fontsize || 16

            double leftMo = leftPc, topMo = topPc, wMo = wPc, hMo = hPc, fontMo = fontPc;
            if (props.Truthy("mobileEdited") && props.Obj("layouts").Truthy("mobile"))
            {
                var lMo = props.Obj("layouts").Obj("mobile");
                double canvasW = _scene.Canvas.NumT("width", 800);   // canvas?.width || 800
                double canvasH = _scene.Canvas.NumT("height", 600);  // canvas?.height || 600
                double parentMoW = (parentW == canvasW) ? _mobileW : parentW;
                double parentMoH = (parentH == canvasH) ? _mobileCanvasH : parentH;
                leftMo = parentMoW > 0 ? (ToNum(lMo.Raw("x")) / parentMoW) * 100 : leftPc;
                topMo = parentMoH > 0 ? (ToNum(lMo.Raw("y")) / parentMoH) * 100 : topPc;
                wMo = parentMoW > 0 ? (ToNum(lMo.Raw("w")) / parentMoW) * 100 : wPc;
                hMo = parentMoH > 0 ? (ToNum(lMo.Raw("h")) / parentMoH) * 100 : hPc;
                fontMo = lMo.NumT("fontsize", fontPc); // lMo.fontsize || fontPc
            }

            string className = $"el-{id}";
            string heightRule = (type == "ArticleGrid" || type == "Accordion")
                ? $"height: auto; min-height: {N(hPc)}%"
                : $"height: {N(hPc)}%";

            double opacity;
            var opRaw = props.Raw("opacity");
            if (opRaw.ValueKind == JsonValueKind.Number && opRaw.TryGetDouble(out var od) && !double.IsNaN(od) && !double.IsInfinity(od))
                opacity = Math.Min(1, Math.Max(0, od));
            else opacity = 1;
            string opacityRule = opacity != 1 ? $" opacity: {N(opacity)};" : "";

            string cssRule = $".{className} {{ left: {N(leftPc)}%; top: {N(topPc)}%; width: {N(wPc)}%; {heightRule}; font-size: {N(fontPc)}px;{opacityRule} transition: all 0.3s ease; }}";
            string fontMoVw = ToFixed(fontMo / _mobileW * 100, 2);
            string heightRuleMo = (type == "ArticleGrid" || type == "Accordion") ? "height: auto" : $"height: {N(hMo)}%";
            cssRule += $"\n    @media (max-width: 768px) {{ .{className} {{ left: {N(leftMo)}%; top: {N(topMo)}%; width: {N(wMo)}%; {heightRuleMo}; font-size: {fontMoVw}vw; }} }}";
            DynamicCss.Add(cssRule);

            // イベント(JS) / event handlers (JS)
            var evsRaw = props.Raw("events");
            if (evsRaw.ValueKind == JsonValueKind.Array && evsRaw.GetArrayLength() > 0)
            {
                foreach (var ev in evsRaw.EnumerateArray())
                {
                    var evO = new JObj(ev);
                    string eventName = evO.Eq("trigger", "hover") ? "mouseenter" : "click";
                    string actionJs = "";
                    if (evO.Eq("action", "alert"))
                    {
                        string safeMsg = (evO.Truthy("target") ? Str(evO.Raw("target")) : "").Replace("\"", "\\\"");
                        actionJs = $"alert(\"{safeMsg}\");";
                    }
                    else if (evO.Truthy("target"))
                    {
                        string showJs = evO.Eq("action", "show") ? "t.style.display = \"block\";" : "";
                        string hideJs = evO.Eq("action", "hide") ? "t.style.display = \"none\";" : "";
                        string toggleJs = evO.Eq("action", "toggle") ? "t.style.display = (t.style.display === \"none\" ? \"block\" : \"none\");" : "";
                        actionJs = "\n            var t = document.getElementById(\"" + Str(evO.Raw("target")) + "\");\n            if (t) {\n                " + showJs + "\n                " + hideJs + "\n                " + toggleJs + "\n            }";
                    }
                    if (actionJs.Length > 0)
                        DynamicJs.Add("\n    var el_" + id + " = document.getElementById(\"" + id + "\");\n    if (el_" + id + ") {\n        el_" + id + ".addEventListener(\"" + eventName + "\", function(e) {\n            e.preventDefault();\n            " + actionJs + "\n        });\n    }");
                }
            }

            // 共通プロパティ / common properties
            string text = EscapeHtml(props.StrN("text", ""));
            string name = EscapeHtml(props.StrN("name", "Unnamed"));
            string bgcolor = EscapeHtml(props.StrN("bgcolor", "transparent"));
            string bgFill = GradientBgDecl(props, bgcolor);
            double cornerR = Math.Max(0, NumOrZero(ParseInt(props.Raw("cornerRadius"))));
            string radiusCss = TruthyNum(cornerR) ? $" border-radius: {N(cornerR)}px;" : "";
            string strokeCss = StrokeDecl(props, type);
            string color = EscapeHtml(props.StrN("color", "inherit"));
            string align = EscapeHtml(props.StrT("align", type == "Button" ? "center" : "left"));
            string fontfam = EscapeHtml(props.StrT("fontfamily", "sans-serif"));
            string rawFam = props.StrT("fontfamily", "");
            foreach (var f in GoogleFonts) if (rawFam.Contains(f.Family)) AddFont(f.Spec);

            string animClass = className;
            if (props.Truthy("animation") && !props.Eq("animation", "none"))
                animClass += " anim-" + Str(props.Raw("animation")).ToLowerInvariant();

            string shadowStyle = CombinedShadowDecl(props, type);

            string baseStyle = "position: absolute; box-sizing: border-box;";
            if (type != "Group" && type != "ArticleGrid" && type != "Accordion" && type != "Triangle")
            {
                baseStyle += $" {bgFill} color: {color}; text-align: {align}; font-family: {fontfam};";
                if (type != "Button" && type != "Image") baseStyle += $" {shadowStyle}";
            }
            baseStyle += strokeCss;

            sb.Append($"{indent}\n");

            switch (type)
            {
                case "Group":
                    sb.Append(RenderGroup(id, animClass, baseStyle, bgcolor, el, pcW, pcH, depth, indent));
                    break;
                case "Button":
                    sb.Append(RenderButton(id, animClass, baseStyle, bgcolor, color, text, props, shadowStyle, indent));
                    break;
                case "TextInput":
                    sb.Append(RenderTextInput(id, animClass, baseStyle, text, props, indent));
                    break;
                case "Label":
                    sb.Append(RenderLabel(id, animClass, baseStyle, color, text, props, shadowStyle, indent));
                    break;
                case "Rect":
                    sb.Append($"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{baseStyle}{radiusCss}\"></div>\n");
                    break;
                case "Circle":
                    sb.Append($"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{baseStyle} border-radius: 50%;\"></div>\n");
                    break;
                case "Warp":
                {
                    var ptsRaw = props.Raw("warpPoints");
                    var pts = ptsRaw.ValueKind == JsonValueKind.Array
                        ? ptsRaw.EnumerateArray().Select(p => new JObj(p)).ToList()
                        : new List<JObj>();
                    if (pts.Count >= 3)
                    {
                        var xs = pts.Select(p => ToNum(p.Raw("x"))).ToList();
                        var ys = pts.Select(p => ToNum(p.Raw("y"))).ToList();
                        double minX = xs.Min(), minY = ys.Min();
                        double bw = OrOne(xs.Max() - minX);
                        double bh = OrOne(ys.Max() - minY);
                        string poly = string.Join(", ", pts.Select(p =>
                        {
                            double px = ToNum(p.Raw("x")), py = ToNum(p.Raw("y"));
                            return $"{ToFixed(((px - minX) / bw) * 100, 2)}% {ToFixed(((py - minY) / bh) * 100, 2)}%";
                        }));
                        string warpStyle = $"position: absolute; left: {N(leftPc)}%; top: {N(topPc)}%; width: {N(wPc)}%; height: {N(hPc)}%; background-color: {bgcolor}; clip-path: polygon({poly}); {shadowStyle}";
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
                    string triStyle = $"position: absolute; width: 100%; height: 100%; box-sizing: border-box; clip-path: polygon(50% 0%, 100% 100%, 0% 100%); {bgFill}";
                    sb.Append($"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{baseStyle}\"><div style=\"{triStyle}\"></div></div>\n");
                    break;
                }
                case "Image":
                    sb.Append(RenderImage(id, animClass, baseStyle, props, name, shadowStyle, indent));
                    break;
                case "Slider":
                    sb.Append(ComponentRenderers.RenderSlider(this, id, animClass, baseStyle, props, indent));
                    break;
                case "ArticleGrid":
                    sb.Append(ComponentRenderers.RenderArticleGrid(this, id, animClass, baseStyle, props, indent));
                    break;
                case "Accordion":
                    sb.Append(ComponentRenderers.RenderAccordion(this, id, animClass, baseStyle, props, indent));
                    break;
            }
        }
        return sb.ToString();
    }

    private string RenderGroup(string id, string animClass, string baseStyle, string bgcolor, JsonElement el, double width, double height, int depth, string indent)
    {
        var sb = new StringBuilder();
        sb.Append($"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{baseStyle} background-color: {bgcolor};\">\n");
        var elO = new JObj(el);
        var ch = elO.Raw("children");
        if (ch.ValueKind == JsonValueKind.Array)
            sb.Append(RenderElements(ch, width, height, depth + 1));
        sb.Append($"{indent}</div>\n");
        return sb.ToString();
    }

    private string RenderButton(string id, string animClass, string baseStyle, string bgcolor, string color, string text, JObj props, string shadowStyle, string indent)
    {
        string bgStyle = GradientBgDecl(props, bgcolor);
        if (props.Truthy("bgimage"))
        {
            string src = EscapeHtml(ResolveImageSrc(props.Raw("bgimage"), ImageMap));
            bgStyle = $"background-image: url('{src}'); background-size: cover; background-position: center;";
        }
        string align = EscapeHtml(props.StrT("align", "center"));
        var crRaw = props.Raw("cornerRadius");
        double crv = IsNullish(crRaw) ? 8 : ParseInt(crRaw); // parseInt(props.cornerRadius ?? 8)
        double btnR = Math.Max(0, NumOrZero(crv));
        string btnStyle = $"width: 100%; height: 100%; box-sizing: border-box; {bgStyle} color: {color}; font-size: inherit; border: none; border-radius: {N(btnR)}px; cursor: pointer; font-weight: {props.StrT("fontWeight", "bold")}; text-align: {align}; {shadowStyle}{StrokeDecl(props, "Button")}{TextExtraCss(props)}";
        string formStyle = "margin: 0; position: absolute; width: 100%; height: 100%;";
        string btnText = WrapGradText(text, props);

        if (props.Eq("role", "submit"))
        {
            var sb = new StringBuilder();
            sb.Append($"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{baseStyle} background:none;\">\n");
            sb.Append($"{indent}    <button type=\"submit\" style=\"{btnStyle} {formStyle}\">{btnText}</button>\n");
            sb.Append($"{indent}</div>\n");
            return sb.ToString();
        }

        string url = EscapeHtml((props.Truthy("route") && !props.Eq("route", "#")) ? Str(props.Raw("route")) : "#");
        var o = new StringBuilder();
        o.Append($"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{baseStyle} background:none;\">\n");
        o.Append($"{indent}    <a href=\"{url}\" style=\"{formStyle} display:block; text-decoration:none;\">\n");
        o.Append($"{indent}        <button type=\"button\" style=\"{btnStyle}\">{btnText}</button>\n");
        o.Append($"{indent}    </a>\n");
        o.Append($"{indent}</div>\n");
        return o.ToString();
    }

    private string RenderTextInput(string id, string animClass, string baseStyle, string text, JObj props, string indent)
    {
        string name = EscapeHtml(props.StrT("inputName", ""));
        string nameAttr = name.Length > 0 ? $" name=\"{name}\"" : "";
        string required = props.Truthy("required") ? " required" : "";
        string ph = EscapeHtml(text); // escapeHtml(text || '') — text is already escaped (double-escape quirk preserved)
        string rawType = props.StrT("inputType", "text");

        if (rawType == "textarea")
        {
            string style = baseStyle + " padding: 10px; border: 1px solid #ccc; border-radius: 4px; background-color: #fff; width: 100%; height: 100%; resize: none; font-family: inherit;";
            return $"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"position:absolute;\"><textarea{nameAttr} placeholder=\"{ph}\"{required} style=\"{style}\"></textarea></div>\n";
        }

        string itype = rawType is "text" or "email" or "tel" or "number" ? rawType : "text";
        string style2 = baseStyle + " padding: 10px; border: 1px solid #ccc; border-radius: 4px; background-color: #fff; width: 100%; height: 100%;";
        return $"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"position:absolute;\"><input type=\"{itype}\"{nameAttr} placeholder=\"{ph}\"{required} style=\"{style2}\"></div>\n";
    }

    private string RenderLabel(string id, string animClass, string baseStyle, string color, string text, JObj props, string shadowStyle, string indent)
    {
        string style = $"{baseStyle} display: block; overflow: hidden; {shadowStyle} font-weight: {props.StrT("fontWeight", "normal")};{TextExtraCss(props)}";
        return $"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{style}\">{WrapGradText(text, props)}</div>\n";
    }

    private string RenderImage(string id, string animClass, string baseStyle, JObj props, string name, string shadowStyle, string indent)
    {
        string src = EscapeHtml(ResolveImageSrc(props.Raw("text"), ImageMap));
        var routeRaw = props.Raw("route");
        string route = IsNullish(routeRaw) ? "#" : Str(routeRaw);
        bool hasLink = route != "#" && route != "" && route != "none";
        double r = Math.Max(0, NumOrZero(ParseInt(props.Raw("cornerRadius"))));
        string rc = TruthyNum(r) ? $" border-radius: {N(r)}px;" : "";
        string imgStyle = $"width: 100%; height: 100%; object-fit: contain; display: block; {shadowStyle}{rc}";
        var g = props.Obj("gradient");
        string overlay = (g.IsObject && g.Truthy("on"))
            ? $"<div style=\"position:absolute; inset:0; {GradientBgDecl(props, "")} mix-blend-mode:multiply; pointer-events:none;\"></div>"
            : "";

        if (hasLink)
        {
            string url = EscapeHtml(route);
            string inner = $"<img src=\"{src}\" alt=\"{name}\" style=\"{imgStyle}\">";
            inner = $"<a href=\"{url}\" style=\"display:block; width:100%; height:100%;\">{inner}</a>";
            return $"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{baseStyle} background:none; overflow:hidden;\">{inner}{overlay}</div>\n";
        }
        if (overlay.Length > 0)
            return $"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{baseStyle} overflow:hidden;\"><img src=\"{src}\" alt=\"{name}\" style=\"{imgStyle}\">{overlay}</div>\n";
        return $"{indent}<img id=\"{id}\" src=\"{src}\" alt=\"{name}\" class=\"{animClass}\" style=\"{baseStyle} {imgStyle}\">\n";
    }

    internal static IEnumerable<JsonElement> EnumArr(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Array)
            foreach (var e in el.EnumerateArray()) yield return e;
    }
}
