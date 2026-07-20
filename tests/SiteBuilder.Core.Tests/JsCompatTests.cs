using SiteBuilder.Core;
using Xunit;

namespace SiteBuilder.Core.Tests;

// JS 互換層（数値→文字列 / toFixed）の要となる挙動を固定する。
// Lock the key behaviours of the JS-compat layer (number→string / toFixed).
public class JsCompatTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(-0.0, "0")]      // JS String(-0) === "0"
    [InlineData(800, "800")]
    [InlineData(12.5, "12.5")]
    [InlineData(0.1, "0.1")]
    [InlineData(1.0 / 3.0 * 100, "33.33333333333333")] // 最短往復表現 / shortest round-trip
    public void NumStr_matches_js(double value, string expected)
        => Assert.Equal(expected, Js.NumStr(value));

    [Theory]
    [InlineData(4.2666666, 2, "4.27")]
    [InlineData(16.0 / 375.0 * 100, 2, "4.27")]
    [InlineData(0, 2, "0.00")]
    [InlineData(2.5, 0, "3")]     // JS (2.5).toFixed(0) === "3"
    [InlineData(33.335, 2, "33.34")]
    public void ToFixed_matches_js(double value, int digits, string expected)
        => Assert.Equal(expected, Js.ToFixed(value, digits));
}
