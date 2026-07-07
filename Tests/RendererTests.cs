using MySiteBuilder.Core.Export;
using MySiteBuilder.Core.Models;
using Xunit;

namespace MySiteBuilder.Tests;

// renderer.test.js の C# 移植。
public class RendererTests
{
    private static string Render(SceneData scene, RenderMode mode = RenderMode.Static)
        => new HtmlRenderer(scene, mode, new Dictionary<string, string>()).Render();

    private static SceneData Base(
        ResolvedSeo? seo = null,
        List<SiteElement>? elements = null,
        string bgColor = "#ffcc00")
        => new()
        {
            Canvas = new CanvasSize { Width = 800, Height = 600, MobileWidth = 375, MobileHeight = 800 },
            BgColor = bgColor,
            Seo = seo ?? new ResolvedSeo
            {
                Lang = "ja",
                Title = "テストページ",
                Description = "説明",
                OgImage = "https://x/og.png",
                SiteName = "店",
            },
            Elements = elements ?? new List<SiteElement>(),
        };

    // --- SEO ---

    [Fact]
    public void Seo_OutputsTitleDescriptionOgpLang()
    {
        var h = Render(Base());
        Assert.Contains("<html lang=\"ja\">", h);
        Assert.Contains("<title>テストページ</title>", h);
        Assert.Contains("<meta name=\"description\" content=\"説明\">", h);
        Assert.Contains("property=\"og:title\" content=\"テストページ\"", h);
        Assert.Contains("property=\"og:image\" content=\"https://x/og.png\"", h);
        Assert.Contains("name=\"twitter:card\" content=\"summary_large_image\"", h);
    }

    [Fact]
    public void Seo_EscapesHtml()
    {
        var h = Render(Base(seo: new ResolvedSeo { Title = "<x>\"&" }));
        Assert.Contains("<title>&lt;x&gt;&quot;&amp;</title>", h);
    }

    // --- 背景色 ---

    [Fact]
    public void BgColor_AppliedToSiteCanvasAndBody()
    {
        var h = Render(Base());
        Assert.Matches(@"\.site-canvas \{[^}]*background-color: #ffcc00", h);
        Assert.Contains("<body style=\"margin: 0; background-color: #ffcc00;\">", h);
    }

    // --- フォーム ---

    private static SceneData FormScene(Action<ElementProperties>? tweakSubmit = null)
    {
        var submitProps = new ElementProperties
        {
            Text = "送信", Role = "submit", Route = "https://formspree.io/f/x", Method = "POST",
        };
        tweakSubmit?.Invoke(submitProps);

        return Base(elements: new List<SiteElement>
        {
            new() { Id = "ti1", Type = "TextInput", Transform = new() { X = 0, Y = 0, Width = 200, Height = 40 },
                Properties = new() { InputName = "email", InputType = "email", Required = true, Text = "メール" } },
            new() { Id = "ta1", Type = "TextInput", Transform = new() { X = 0, Y = 60, Width = 200, Height = 80 },
                Properties = new() { InputName = "msg", InputType = "textarea", Text = "本文" } },
            new() { Id = "b1", Type = "Button", Transform = new() { X = 0, Y = 160, Width = 120, Height = 40 },
                Properties = submitProps },
            new() { Id = "b2", Type = "Button", Transform = new() { X = 140, Y = 160, Width = 80, Height = 40 },
                Properties = new() { Text = "戻る", Role = "link", Route = "index.html" } },
        });
    }

    [Fact]
    public void Form_WrapsPageAndOutputsInputAttributes()
    {
        var h = Render(FormScene());
        Assert.Contains("<form action=\"https://formspree.io/f/x\"", h);
        Assert.Contains("<button type=\"submit\"", h);
        Assert.Contains("name=\"email\"", h);
        Assert.Contains("type=\"email\"", h);
        Assert.Contains("required", h);
        Assert.Contains("<textarea", h);
        Assert.Contains("name=\"msg\"", h);
    }

    [Fact]
    public void Form_LinkButtonIsAnchorNotSubmit()
    {
        var h = Render(FormScene());
        Assert.Contains("href=\"index.html\"", h);
    }

    [Fact]
    public void Form_DefaultSuccessOverlayWithHiddenIframe()
    {
        var h = Render(FormScene());
        Assert.Contains("id=\"ksb-form-success\"", h);
        Assert.Contains("name=\"ksb_form_target\"", h);
        Assert.Contains("送信ありがとうございました。", h);
    }

    [Fact]
    public void Form_EmptySuccessMessageMeansNavigateOnly()
    {
        var h = Render(FormScene(p => p.SuccessMessage = ""));
        Assert.DoesNotContain("ksb-form-success", h);
        Assert.DoesNotContain("ksb_form_target", h);
    }

    [Fact]
    public void Form_BladeModeOutputsCsrfAndSessionSuccess()
    {
        var scene = FormScene();
        scene.FormAction = "/contact-submit";
        var h = new HtmlRenderer(scene, RenderMode.Blade, new Dictionary<string, string>()).Render();
        Assert.Contains("@csrf", h);
        Assert.Contains("action=\"/contact-submit\"", h);
        Assert.Contains("@if(session('success'))", h);
    }

    [Fact]
    public void Form_NoSubmitButtonMeansNoForm()
    {
        var h = Render(Base(elements: new List<SiteElement>
        {
            new() { Id = "l", Type = "Label", Transform = new() { X = 0, Y = 0, Width = 100, Height = 20 },
                Properties = new() { Text = "hi" } },
        }));
        Assert.DoesNotContain("<form", h);
    }

    // --- レイヤースタイル（css-generator.js の C# 移植分） ---

    private static SceneData OneElement(string type, ElementProperties props) => Base(elements: new List<SiteElement>
    {
        new() { Id = "e1", Type = type, Transform = new() { X = 0, Y = 0, Width = 100, Height = 50 }, Properties = props },
    });

    [Fact]
    public void Gradient_LinearAppliesDirectionDegrees()
    {
        var h = Render(OneElement("Rect", new() { Gradient = new() { On = true, Type = "linear", Dir = "h", C1 = "#111111", C2 = "#222222" } }));
        Assert.Contains("background: linear-gradient(90deg, #111111, #222222);", h);
    }

    [Fact]
    public void Gradient_RadialUsesCircle()
    {
        var h = Render(OneElement("Circle", new() { Gradient = new() { On = true, Type = "radial", C1 = "#aaa", C2 = "#bbb" } }));
        Assert.Contains("background: radial-gradient(circle, #aaa, #bbb);", h);
    }

    [Fact]
    public void Gradient_OffFallsBackToBgcolor()
    {
        var h = Render(OneElement("Rect", new() { Bgcolor = "#ff0000", Gradient = new() { On = false } }));
        Assert.Contains("background-color: #ff0000;", h);
    }

    [Fact]
    public void CornerRadius_AppliedToRect()
    {
        var h = Render(OneElement("Rect", new() { CornerRadius = 12 }));
        Assert.Contains("border-radius: 12px;", h);
    }

    [Fact]
    public void CornerRadius_ButtonDefaultsToEight()
    {
        var h = Render(OneElement("Button", new() { Text = "OK" }));
        Assert.Contains("border-radius: 8px;", h);
    }

    [Fact]
    public void Stroke_RectRendersBorder()
    {
        var h = Render(OneElement("Rect", new() { Stroke = new() { On = true, Width = 3, Color = "#333333" } }));
        Assert.Contains("border: 3px solid #333333;", h);
    }

    [Fact]
    public void Stroke_LabelRendersTextStroke()
    {
        var h = Render(OneElement("Label", new() { Text = "hi", Stroke = new() { On = true, Width = 2, Color = "#000000" } }));
        Assert.Contains("-webkit-text-stroke: 2px #000000;", h);
    }

    [Fact]
    public void DropShadow_FreeValuesProduceBoxShadow()
    {
        var h = Render(OneElement("Rect", new()
        {
            DropShadow = new() { On = true, X = 4, Y = 6, Blur = 10, Spread = 2, Color = "#000000", Opacity = 0.5 },
        }));
        Assert.Contains("box-shadow: 4px 6px 10px 2px rgba(0, 0, 0, 0.5);", h);
    }

    [Fact]
    public void DropShadow_PresetUsedWhenFreeValueOff()
    {
        var h = Render(OneElement("Rect", new() { Shadow = "light" }));
        Assert.Contains("box-shadow: 0 4px 10px rgba(0,0,0,0.15);", h);
    }

    [Fact]
    public void Glow_AddsSecondBoxShadowLayer()
    {
        var h = Render(OneElement("Rect", new()
        {
            DropShadow = new() { On = true, X = 0, Y = 0, Blur = 1, Spread = 0, Color = "#000000", Opacity = 1 },
            Glow = new() { On = true, Color = "#00d0ff", Blur = 20, Spread = 0, Opacity = 0.8 },
        }));
        Assert.Contains("0 0 20px 0px rgba(0, 208, 255, 0.8)", h);
    }

    [Fact]
    public void GradText_WrapsTextInGradientSpan()
    {
        var h = Render(OneElement("Label", new() { Text = "見出し", GradText = new() { On = true, C1 = "#ff6ec4", C2 = "#7873f5" } }));
        Assert.Contains("-webkit-text-fill-color: transparent", h);
        Assert.Contains(">見出し</span>", h);
    }

    [Fact]
    public void FontWeight_LabelDefaultsToNormalButtonToBold()
    {
        var hLabel = Render(OneElement("Label", new() { Text = "x" }));
        Assert.Contains("font-weight: normal;", hLabel);

        var hButton = Render(OneElement("Button", new() { Text = "x" }));
        Assert.Contains("font-weight: bold;", hButton);
    }

    [Fact]
    public void TextExtras_ItalicUnderlineLetterSpacingLineHeight()
    {
        var h = Render(OneElement("Label", new()
        {
            Text = "x", Italic = true, Underline = true, LetterSpacing = 2, LineHeight = 1.8,
        }));
        Assert.Contains("font-style: italic;", h);
        Assert.Contains("text-decoration: underline;", h);
        Assert.Contains("letter-spacing: 2px;", h);
        Assert.Contains("line-height: 1.8;", h);
    }

    [Fact]
    public void Image_GradientOnAddsMultiplyOverlay()
    {
        var h = Render(OneElement("Image", new() { Text = "img.png", Gradient = new() { On = true, C1 = "#111", C2 = "#222" } }));
        Assert.Contains("mix-blend-mode:multiply", h);
    }

    [Fact]
    public void GoogleFont_UsedFontEmitsLinkTag()
    {
        var h = Render(OneElement("Label", new() { Text = "x", FontFamily = "'Noto Sans JP', sans-serif" }));
        Assert.Contains("fonts.googleapis.com/css2?family=Noto+Sans+JP:wght@400;700", h);
    }

    [Fact]
    public void GoogleFont_UnusedWhenFontNotSelected()
    {
        var h = Render(OneElement("Label", new() { Text = "x", FontFamily = "Arial" }));
        Assert.DoesNotContain("fonts.googleapis.com", h);
    }
}
