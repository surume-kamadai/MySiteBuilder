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
}
