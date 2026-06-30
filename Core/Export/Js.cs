using System.Globalization;

namespace MySiteBuilder.Core.Export;

// ============================================================
// JavaScript のセマンティクスを C# で忠実に再現するためのヘルパ群。
//
//   renderer.js / exporter.js は `||`(falsy) `??`(nullish) `String(n)`
//   `toFixed` などに依存している。出力文字列を一致させるため、ここで
//   それらの挙動を集約する。
// ============================================================
internal static class Js
{
    /// <summary>JS の HTML エスケープ（escapeHtml）と同一。&amp; を最初に置換する順序が重要。</summary>
    public static string EscapeHtml(object? value)
    {
        var s = value?.ToString() ?? "";
        return s
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#039;");
    }

    /// <summary>
    /// 文字列に対する JS の `a || b`。a が null または空文字(falsy)なら b。
    /// 文字列 "0" は truthy なのでそのまま採用される。
    /// </summary>
    public static string Or(string? a, string b) => string.IsNullOrEmpty(a) ? b : a;

    /// <summary>
    /// 数値に対する JS の `a || b`。a が null / 0 / NaN(falsy)なら b。
    /// </summary>
    public static double OrNum(double? a, double b)
        => (a is null || a.Value == 0 || double.IsNaN(a.Value)) ? b : a.Value;

    /// <summary>JS の truthy 判定（文字列）。null/空文字以外は true。</summary>
    public static bool Truthy(string? s) => !string.IsNullOrEmpty(s);

    /// <summary>
    /// JS の Number → String（`${n}` 補間相当）。
    /// .NET Core 3.0+ の double.ToString は ECMAScript と同じく最短往復表現を返すため、
    /// 通常の CSS 数値域では出力が一致する。
    /// </summary>
    public static string Num(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsPositiveInfinity(d)) return "Infinity";
        if (double.IsNegativeInfinity(d)) return "-Infinity";
        return d.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>JS の number.toFixed(digits)。</summary>
    public static string ToFixed(double d, int digits)
        => d.ToString("F" + digits, CultureInfo.InvariantCulture);

    /// <summary>JS の Boolean → String（"true" / "false"。C# 既定の "True"/"False" との差を吸収）。</summary>
    public static string Bool(bool b) => b ? "true" : "false";
}
