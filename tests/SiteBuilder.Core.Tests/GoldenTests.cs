using System.Text.Json;
using System.Text.Json.Nodes;
using SiteBuilder.Core.Export;
using Xunit;

namespace SiteBuilder.Core.Tests;

// ============================================================
// ゴールデンテスト: C# 版 SiteBuilder.Core の出力が JS 版とバイト一致することを検証する。
// Golden tests: verify SiteBuilder.Core's output byte-matches the JS engine.
// フィクスチャは tests/golden/dump.mjs が JS 版から生成する。
// Fixtures are produced from the JS engine by tests/golden/dump.mjs.
// ============================================================
public class GoldenTests
{
    private static readonly string BaseDir = AppContext.BaseDirectory;
    private static string ProjectsDir => Path.Combine(BaseDir, "projects");
    private static string FixturesDir => Path.Combine(BaseDir, "fixtures");

    public static IEnumerable<object[]> Cases()
    {
        foreach (var pf in Directory.GetFiles(ProjectsDir, "*.json").OrderBy(x => x))
        {
            var name = Path.GetFileNameWithoutExtension(pf);
            foreach (var mode in new[] { "static", "laravel" })
                foreach (var sep in new[] { false, true })
                    yield return new object[] { name, mode, sep };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Output_matches_js_golden(string name, string mode, bool sep)
    {
        var projPath = Path.Combine(ProjectsDir, name + ".json");
        var projectEl = CloneWithSeparateCss(File.ReadAllText(projPath), sep);

        var result = mode == "static"
            ? Exporter.BuildStaticProject(projectEl)
            : Exporter.BuildLaravelProject(projectEl);

        var sfx = sep ? "sep1" : "sep0";
        using var fx = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixturesDir, $"{name}.{mode}.{sfx}.json")));
        var root = fx.RootElement;

        Assert.Equal(root.GetProperty("projectName").GetString(), result.ProjectName);

        var expectedFiles = root.GetProperty("files").EnumerateArray()
            .ToDictionary(f => f.GetProperty("path").GetString()!, f => f.GetProperty("content").GetString()!);
        var actualFiles = result.Files.ToDictionary(f => f.Path, f => f.Content);

        Assert.Equal(
            expectedFiles.Keys.OrderBy(x => x, StringComparer.Ordinal),
            actualFiles.Keys.OrderBy(x => x, StringComparer.Ordinal));

        foreach (var (path, expected) in expectedFiles)
        {
            var actual = actualFiles[path];
            if (actual != expected)
                Assert.Fail(DiffMessage(path, expected, actual));
        }

        var expectedImgs = root.GetProperty("imagePaths").EnumerateArray().Select(x => x.GetString()!).OrderBy(x => x, StringComparer.Ordinal);
        var actualImgs = result.Images.Select(i => i.Path).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(expectedImgs, actualImgs);
    }

    // dump.mjs と同じく separateCss を切り替えたクローンを作る。
    private static JsonElement CloneWithSeparateCss(string projectJson, bool sep)
    {
        var node = JsonNode.Parse(projectJson)!.AsObject();
        if (node["settings"] is not JsonObject settings)
        {
            settings = new JsonObject();
            node["settings"] = settings;
        }
        settings["separateCss"] = sep;
        return JsonSerializer.SerializeToElement(node);
    }

    private static string DiffMessage(string path, string expected, string actual)
    {
        int i = 0;
        int min = Math.Min(expected.Length, actual.Length);
        while (i < min && expected[i] == actual[i]) i++;
        return $"file '{path}' differs at index {i} (expected len={expected.Length}, actual len={actual.Length})\n" +
               $"  expected: …{Snip(expected, i)}…\n" +
               $"  actual:   …{Snip(actual, i)}…";
    }

    private static string Snip(string s, int at)
    {
        int start = Math.Max(0, at - 30);
        int end = Math.Min(s.Length, at + 30);
        return s[start..end].Replace("\n", "\\n").Replace("\t", "\\t");
    }
}
