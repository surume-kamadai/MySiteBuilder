using MySiteBuilder.Core.Export;
using MySiteBuilder.Core.Models;
using Xunit;

namespace MySiteBuilder.Tests;

// exporter.test.js の C# 移植。
public class ExporterTests
{
    private static SiteProject Project() => new()
    {
        Settings = new ProjectSettings
        {
            ProjectName = "my-site",
            Canvas = new CanvasSize { Width = 800, Height = 600, MobileWidth = 375, MobileHeight = 800 },
            OutputType = "static",
            SiteBgColor = "#f1f2f6",
            Seo = new SiteSeo { SiteName = "店", Lang = "ja", Description = "共通説明", OgImage = "" },
        },
        Folders = new List<PageFolder>(),
        Pages = new List<SitePage>
        {
            new()
            {
                Id = "page_1", Name = "index", FolderId = null, BgColor = "#ffffff",
                Seo = new PageSeo { Title = "トップ", Description = "", OgImage = "" },
                Elements = new List<SiteElement>
                {
                    new() { Id = "b1", Type = "Button", Transform = new() { X = 0, Y = 0, Width = 100, Height = 40 },
                        Properties = new() { Text = "送信", Role = "submit", Route = "" } },
                    new() { Id = "ti", Type = "TextInput", Transform = new() { X = 0, Y = 50, Width = 200, Height = 40 },
                        Properties = new() { InputName = "email", InputType = "email" } },
                },
            },
        },
        ActivePageId = "page_1",
    };

    // --- buildStaticProject ---

    [Fact]
    public void Static_GeneratesIndexWithResolvedSeoTitle()
    {
        var outResult = Exporter.BuildStaticProject(Project());
        var idx = outResult.Files.FirstOrDefault(f => f.Path == "index.html");
        Assert.NotNull(idx);
        Assert.Contains("<title>トップ | 店</title>", idx!.Content);
    }

    [Fact]
    public void Static_FallsBackToSiteDescription()
    {
        var outResult = Exporter.BuildStaticProject(Project());
        var idx = outResult.Files.First(f => f.Path == "index.html");
        Assert.Contains("content=\"共通説明\"", idx.Content);
    }

    [Fact]
    public void Static_AppliesPageBgColorToSiteCanvas()
    {
        var outResult = Exporter.BuildStaticProject(Project());
        var idx = outResult.Files.First(f => f.Path == "index.html");
        Assert.Matches(@"\.site-canvas \{[^}]*background-color: #ffffff", idx.Content);
    }

    // --- buildLaravelProject ---

    [Fact]
    public void Laravel_SubmitButtonGeneratesPostRouteAndController()
    {
        var outResult = Exporter.BuildLaravelProject(Project());
        var routes = outResult.Files.First(f => f.Path == "routes/web.php");
        Assert.Contains("Route::post('/index-submit'", routes.Content);
        Assert.Contains(outResult.Files, f => f.Path == "app/Http/Controllers/FormController.php");
    }

    [Fact]
    public void Laravel_BladeViewContainsCsrf()
    {
        var outResult = Exporter.BuildLaravelProject(Project());
        var view = outResult.Files.First(f => f.Path == "resources/views/index.blade.php");
        Assert.Contains("@csrf", view.Content);
    }
}
