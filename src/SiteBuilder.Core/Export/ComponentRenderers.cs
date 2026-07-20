using System.Text;
using System.Text.Json;
using static SiteBuilder.Core.Js;
using static SiteBuilder.Core.Export.CssHelpers;

namespace SiteBuilder.Core.Export;

// ============================================================
// render-components.js の移植（スライダー/記事グリッド/アコーディオン）。
// Port of render-components.js (slider / article grid / accordion).
// 第1引数 r に HtmlRenderer を受け取り、r.DynamicJs / r.DynamicCss / r.ImageMap を使う。
// Takes the HtmlRenderer as r and uses r.DynamicJs / r.DynamicCss / r.ImageMap.
// ============================================================
public static class ComponentRenderers
{
    private static string N(double d) => NumStr(d);
    private static string B(bool b) => b ? "true" : "false"; // String(boolean)

    // 画像スライダー（Swiper.js）/ image slider (Swiper.js).
    public static string RenderSlider(HtmlRenderer r, string id, string animClass, string baseStyle, JObj props, string indent)
    {
        var sliderObj = props.Obj("slider");
        var slidesRaw = sliderObj.Raw("slides"); // props.slider?.slides

        var slides = new List<JObj>();
        if (slidesRaw.ValueKind == JsonValueKind.Array && slidesRaw.GetArrayLength() > 0)
        {
            foreach (var sl in slidesRaw.EnumerateArray()) slides.Add(new JObj(sl));
        }
        else
        {
            // 旧 text（カンマ区切り画像URL）から変換 / convert from legacy comma-separated URLs.
            string legacyText = props.StrT("text", "");
            var legacy = legacyText.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)
                .Select(url => new { image = url, title = "", text = "", linkType = "none", link = "" }).ToArray();
            var doc = JsonSerializer.SerializeToElement(legacy);
            foreach (var sl in doc.EnumerateArray()) slides.Add(new JObj(sl));
        }

        if (slides.Count == 0)
            return $"{indent}<div id=\"{id}\" style=\"{baseStyle} background:#333; color:#fff; display:flex; align-items:center; justify-content:center;\">スライドが設定されていません</div>\n";

        var s = sliderObj; // const s = props.slider || {}
        string effect = s.StrN("effect", "slide");
        double speed = s.NumN("speed", 600);
        bool autoplay = s.BoolN("autoplay", true);
        double delay = s.NumN("delay", 3000);
        bool loop = s.BoolN("loop", true);
        bool pagination = s.BoolN("pagination", true);
        bool navigation = s.BoolN("navigation", true);
        double slidesPerView = s.NumN("slidesPerView", 1);

        bool useGrid = effect == "grid";
        bool useCards = effect == "cards";
        bool useCover = effect == "coverflow";
        bool useCube = effect == "cube";
        bool useFade = effect == "fade";
        string swiperEffect = useGrid ? "slide" : effect;

        var sb = new StringBuilder();
        sb.Append($"{indent}<div id=\"{id}\" class=\"swiper {animClass}\" style=\"{baseStyle} border-radius: 5px; overflow:hidden;\">\n");
        sb.Append($"{indent}    <div class=\"swiper-wrapper\">\n");

        foreach (var sl in slides)
        {
            string img = sl.Truthy("image") ? EscapeHtml(ResolveImageSrc(sl.Raw("image"), r.ImageMap)) : "";
            string title = EscapeHtml(sl.StrT("title", ""));
            string text = EscapeHtml(sl.StrT("text", ""));
            bool hasOverlay = sl.Truthy("title") || sl.Truthy("text");

            string openTag = "", closeTag = "";
            if (sl.Eq("linkType", "url") && sl.Truthy("link"))
            {
                string url = EscapeHtml(sl.Raw("link"));
                openTag = $"<a href=\"{url}\" target=\"_blank\" rel=\"noopener noreferrer\" style=\"display:block; width:100%; height:100%; text-decoration:none; color:inherit;\">";
                closeTag = "</a>";
            }
            else if (sl.Eq("linkType", "page") && sl.Truthy("link"))
            {
                string url = EscapeHtml(sl.Raw("link"));
                openTag = $"<a href=\"{url}\" style=\"display:block; width:100%; height:100%; text-decoration:none; color:inherit;\">";
                closeTag = "</a>";
            }

            sb.Append($"{indent}        <div class=\"swiper-slide\" style=\"position:relative; background:#222;\">\n");
            sb.Append($"{indent}            {openTag}\n");
            if (img.Length > 0)
                sb.Append($"{indent}                <img src=\"{img}\" style=\"width:100%; height:100%; object-fit:cover; display:block;\">\n");
            if (hasOverlay)
            {
                sb.Append($"{indent}                <div style=\"position:absolute; left:0; right:0; bottom:0; padding:16px 20px; background:linear-gradient(transparent, rgba(0,0,0,0.7)); color:#fff;\">\n");
                if (title.Length > 0)
                    sb.Append($"{indent}                    <div style=\"font-size:18px; font-weight:bold; margin-bottom:4px;\">{title}</div>\n");
                if (text.Length > 0)
                    sb.Append($"{indent}                    <div style=\"font-size:13px; opacity:0.9;\">{text}</div>\n");
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
            $"speed: {N(speed)}",
            $"loop: {B(loop)}",
            $"slidesPerView: {N(slidesPerView)}",
        };
        if (slidesPerView > 1) opts.Add("spaceBetween: 10");
        opts.Add($"effect: '{swiperEffect}'");
        if (useFade) opts.Add("fadeEffect: { crossFade: true }");
        if (useCube) opts.Add("cubeEffect: { shadow: true, slideShadows: true, shadowOffset: 20, shadowScale: 0.94 }");
        if (useCover) opts.Add("coverflowEffect: { rotate: 30, stretch: 0, depth: 100, modifier: 1, slideShadows: true }");
        if (useCards) opts.Add("cardsEffect: { perSlideOffset: 8, perSlideRotate: 2 }");
        if (autoplay) opts.Add($"autoplay: {{ delay: {N(delay)}, disableOnInteraction: false }}");
        if (pagination) opts.Add($"pagination: {{ el: '#{id} .swiper-pagination', clickable: true }}");
        if (navigation) opts.Add($"navigation: {{ nextEl: '#{id} .swiper-button-next', prevEl: '#{id} .swiper-button-prev' }}");

        r.DynamicJs.Add("\n        if (typeof Swiper !== 'undefined') {\n            new Swiper('#" + id + "', {\n                " + string.Join(",\n                ", opts) + "\n            });\n        }");

        return sb.ToString();
    }

    // 記事グリッド / article grid.
    public static string RenderArticleGrid(HtmlRenderer r, string id, string animClass, string baseStyle, JObj props, string indent)
    {
        var g = props.Obj("grid");
        var items = g.IsArray("items") ? g.Arr("items").Select(e => new JObj(e)).ToList() : new List<JObj>();
        double columns = g.NumN("columns", 3);
        double gap = g.NumN("gap", 20);
        double cardRadius = g.NumN("cardRadius", 8);
        string arrowColor = g.StrT("arrowColor", "#27ae60");
        string imgRatio = g.StrT("imgRatio", "16/10");
        double cardPadding = g.NumN("cardPadding", 18);
        bool sliderMode = g.BoolN("sliderMode", false);

        if (items.Count == 0)
            return $"{indent}<div id=\"{id}\" style=\"{baseStyle} background:#f1f2f6; color:#666; display:flex; align-items:center; justify-content:center;\">アイテムが設定されていません</div>\n";

        string BuildCard(JObj it)
        {
            string img = it.Truthy("image") ? EscapeHtml(ResolveImageSrc(it.Raw("image"), r.ImageMap)) : "";
            string title = EscapeHtml(it.StrT("title", ""));
            string text = EscapeHtml(it.StrT("text", ""));

            string href = "", target = "";
            if (it.Eq("linkType", "url") && it.Truthy("link"))
            {
                href = EscapeHtml(it.Raw("link"));
                target = " target=\"_blank\" rel=\"noopener noreferrer\"";
            }
            else if (it.Eq("linkType", "page") && it.Truthy("link"))
            {
                href = EscapeHtml(it.Raw("link"));
            }
            bool isLink = href.Length > 0;

            string cardStyle = $"position: relative; background: #ffffff; border-radius: {N(cardRadius)}px; overflow: hidden; box-shadow: 0 2px 8px rgba(0,0,0,0.06); transition: transform 0.2s, box-shadow 0.2s; height:100%; {(isLink ? "cursor: pointer;" : "")}";
            string tag = isLink ? "a" : "div";
            string linkAttrs = isLink ? $"href=\"{href}\"{target} style=\"text-decoration: none; color: inherit; display: block; height:100%;\"" : "";

            var c = new StringBuilder();
            c.Append($"<{tag} {linkAttrs} class=\"article-card\" style=\"{cardStyle}\">");
            if (img.Length > 0 && imgRatio != "none")
                c.Append($"<div style=\"width:100%; aspect-ratio: {imgRatio}; overflow:hidden;\"><img src=\"{img}\" style=\"width:100%; height:100%; object-fit:cover; display:block;\" alt=\"{title}\"></div>");
            else if (img.Length > 0)
                c.Append($"<div style=\"width:100%; overflow:hidden;\"><img src=\"{img}\" style=\"width:100%; height:auto; object-fit:cover; display:block;\" alt=\"{title}\"></div>");
            c.Append($"<div style=\"padding: {N(cardPadding)}px {N(cardPadding + 2)}px {N(cardPadding + 32)}px {N(cardPadding + 2)}px;\">");
            if (title.Length > 0)
                c.Append($"<div style=\"font-size: 17px; font-weight: bold; color: #222; margin-bottom: 10px; line-height: 1.4;\">{title}</div>");
            if (text.Length > 0)
                c.Append($"<div style=\"font-size: 13px; color: #666; line-height: 1.6;\">{text}</div>");
            c.Append("</div>");
            if (isLink)
                c.Append($"<div style=\"position:absolute; right:14px; bottom:14px; width:36px; height:36px; background:{arrowColor}; border-radius:50%; display:flex; align-items:center; justify-content:center; color:#fff; font-size:18px; line-height:1;\">→</div>");
            c.Append($"</{tag}>");
            return c.ToString();
        }

        r.DynamicCss.Add($"#{id} .article-card:hover {{ transform: translateY(-4px); box-shadow: 0 8px 20px rgba(0,0,0,0.12); }}");

        if (sliderMode)
        {
            bool aAutoplay = g.BoolN("autoplay", false);
            double aDelay = g.NumN("delay", 3000);
            bool aLoop = g.BoolN("loop", true);
            bool aNav = g.BoolN("navigation", true);

            var sb = new StringBuilder();
            sb.Append($"{indent}<div id=\"{id}\" class=\"swiper {animClass}\" style=\"{baseStyle} padding: 0 {(aNav ? "44px" : "0")};\">\n");
            sb.Append($"{indent}    <div class=\"swiper-wrapper\" style=\"padding-bottom:4px;\">\n");
            foreach (var it in items)
                sb.Append($"{indent}        <div class=\"swiper-slide\" style=\"height:auto;\">{BuildCard(it)}</div>\n");
            sb.Append($"{indent}    </div>\n");
            if (aNav)
            {
                sb.Append($"{indent}    <div class=\"swiper-button-prev\" style=\"color:{arrowColor};\"></div>\n");
                sb.Append($"{indent}    <div class=\"swiper-button-next\" style=\"color:{arrowColor};\"></div>\n");
            }
            sb.Append($"{indent}</div>\n");

            var opts = new List<string>
            {
                $"slidesPerView: {N(columns)}",
                $"spaceBetween: {N(gap)}",
                $"loop: {B(aLoop)}",
            };
            if (aAutoplay) opts.Add($"autoplay: {{ delay: {N(aDelay)}, disableOnInteraction: false }}");
            if (aNav) opts.Add($"navigation: {{ nextEl: '#{id} .swiper-button-next', prevEl: '#{id} .swiper-button-prev' }}");
            opts.Add($"breakpoints: {{ 0: {{ slidesPerView: 1 }}, 600: {{ slidesPerView: {N(Math.Min(2, columns))} }}, 900: {{ slidesPerView: {N(columns)} }} }}");

            r.DynamicJs.Add("\n        if (typeof Swiper !== 'undefined') {\n            new Swiper('#" + id + "', {\n                " + string.Join(",\n                ", opts) + "\n            });\n        }");

            return sb.ToString();
        }

        string containerStyle = $"{baseStyle} display: grid; grid-template-columns: repeat({N(columns)}, 1fr); gap: {N(gap)}px; padding: 0; box-sizing: border-box; align-items: start;";
        var g2 = new StringBuilder();
        g2.Append($"{indent}<div id=\"{id}\" class=\"{animClass}\" style=\"{containerStyle}\">\n");
        foreach (var it in items)
            g2.Append($"{indent}    {BuildCard(it)}\n");
        g2.Append($"{indent}</div>\n");

        r.DynamicCss.Add($"@media (max-width: 768px) {{ #{id} {{ grid-template-columns: 1fr !important; }} }}");

        return g2.ToString();
    }

    // アコーディオン / accordion.
    public static string RenderAccordion(HtmlRenderer r, string id, string animClass, string baseStyle, JObj props, string indent)
    {
        var a = props.Obj("accordion");
        var items = a.IsArray("items") ? a.Arr("items").Select(e => new JObj(e)).ToList() : new List<JObj>();
        string headerColor = a.StrT("headerColor", "#2c3e50");
        string headerBg = a.StrT("headerBg", "#f7f9fa");
        string bodyColor = a.StrT("bodyColor", "#555555");
        bool openFirst = a.BoolN("openFirst", true);

        if (items.Count == 0)
            return $"{indent}<div id=\"{id}\" style=\"{baseStyle} background:#f1f2f6; color:#666; display:flex; align-items:center; justify-content:center;\">項目が設定されていません</div>\n";

        var sb = new StringBuilder();
        sb.Append($"{indent}<div id=\"{id}\" class=\"accordion {animClass}\" style=\"{baseStyle} background:transparent;\">\n");
        for (int idx = 0; idx < items.Count; idx++)
        {
            var it = items[idx];
            string title = EscapeHtml(it.StrT("title", ""));
            string content = EscapeHtml(it.StrT("content", "")).Replace("\n", "<br>");
            bool isOpen = openFirst && idx == 0;

            sb.Append($"{indent}    <div class=\"acc-item\" style=\"border:1px solid #e0e0e0; border-radius:6px; margin-bottom:8px; overflow:hidden;\">\n");
            sb.Append($"{indent}        <button class=\"acc-header\" aria-expanded=\"{B(isOpen)}\" style=\"width:100%; text-align:left; padding:16px 20px; background:{headerBg}; color:{headerColor}; border:none; font-size:16px; font-weight:bold; cursor:pointer; display:flex; justify-content:space-between; align-items:center;\">\n");
            sb.Append($"{indent}            <span>{title}</span>\n");
            sb.Append($"{indent}            <span class=\"acc-icon\" style=\"transition:transform 0.3s; transform:rotate({(isOpen ? "180deg" : "0deg")});\">▼</span>\n");
            sb.Append($"{indent}        </button>\n");
            sb.Append($"{indent}        <div class=\"acc-body\" style=\"max-height:{(isOpen ? "500px" : "0")}; overflow:hidden; transition:max-height 0.3s ease;\">\n");
            sb.Append($"{indent}            <div style=\"padding:16px 20px; color:{bodyColor}; font-size:14px; line-height:1.7;\">{content}</div>\n");
            sb.Append($"{indent}        </div>\n");
            sb.Append($"{indent}    </div>\n");
        }
        sb.Append($"{indent}</div>\n");

        r.DynamicJs.Add(
            "\n        (function() {" +
            "\n            var acc = document.getElementById('" + id + "');" +
            "\n            if (!acc) return;" +
            "\n            acc.querySelectorAll('.acc-header').forEach(function(btn) {" +
            "\n                btn.addEventListener('click', function() {" +
            "\n                    var body = btn.nextElementSibling;" +
            "\n                    var icon = btn.querySelector('.acc-icon');" +
            "\n                    var isOpen = btn.getAttribute('aria-expanded') === 'true';" +
            "\n                    if (isOpen) {" +
            "\n                        body.style.maxHeight = '0';" +
            "\n                        btn.setAttribute('aria-expanded', 'false');" +
            "\n                        if (icon) icon.style.transform = 'rotate(0deg)';" +
            "\n                    } else {" +
            "\n                        body.style.maxHeight = body.scrollHeight + 'px';" +
            "\n                        btn.setAttribute('aria-expanded', 'true');" +
            "\n                        if (icon) icon.style.transform = 'rotate(180deg)';" +
            "\n                    }" +
            "\n                });" +
            "\n            });" +
            "\n        })();");

        return sb.ToString();
    }
}
