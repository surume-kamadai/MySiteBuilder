using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace SiteBuilder.Core;

// ============================================================
// JavaScript の値セマンティクス（truthy / String() / Number() / parseFloat /
// parseInt / toFixed）を .NET 上で再現する互換層。
// Compatibility layer reproducing JavaScript value semantics (truthy / String() /
// Number() / parseFloat / parseInt / toFixed) on .NET.
//
// これにより renderer の export/ を「?? と || の違い」まで含めてバイト単位で移植できる。
// This lets the renderer's export/ be ported byte-for-byte, including the ?? vs || distinction.
// 数値→文字列は .NET Core 3.0+ の最短往復表現が JS の String(number) と一致することを利用する。
// Number→string relies on .NET Core 3.0+'s shortest round-trippable form matching JS String(number).
// ============================================================
public static class Js
{
    // JS の truthy 判定 / JavaScript truthiness.
    public static bool Truthy(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Undefined => false,
        JsonValueKind.Null => false,
        JsonValueKind.False => false,
        JsonValueKind.True => true,
        JsonValueKind.String => v.GetString()!.Length > 0,
        JsonValueKind.Number => v.TryGetDouble(out var d) && d != 0 && !double.IsNaN(d),
        JsonValueKind.Object => true,
        JsonValueKind.Array => true, // JS: 配列は空でも truthy / arrays are always truthy
        _ => false,
    };

    public static bool IsNullish(JsonElement v) =>
        v.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null;

    // 数値の truthy（NaN と 0 以外）/ numeric truthiness (anything but NaN and 0).
    public static bool TruthyNum(double d) => !double.IsNaN(d) && d != 0;

    // JS の x || 0（数値版）: NaN/0 のとき 0 / x || 0 for a numeric x.
    public static double NumOrZero(double d) => double.IsNaN(d) ? 0 : d;

    // JS の x || 1（数値版）/ x || 1 for a numeric x.
    public static double OrOne(double d) => TruthyNum(d) ? d : 1;

    // JS の String(x) / JavaScript's String(x).
    public static string Str(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Undefined => "undefined",
        JsonValueKind.Null => "null",
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.String => v.GetString()!,
        JsonValueKind.Number => NumStr(v.GetDouble()),
        _ => v.GetRawText(),
    };

    // JS の String(number) / JavaScript's String(number).
    public static string NumStr(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsPositiveInfinity(d)) return "Infinity";
        if (double.IsNegativeInfinity(d)) return "-Infinity";
        if (d == 0) return "0"; // -0 も "0" にする / normalize -0 to "0"
        // .NET Core 3.0+ の既定 ToString は最短往復表現（JS と一致）。
        return d.ToString(CultureInfo.InvariantCulture);
    }

    // JS の Number(x) / JavaScript's Number(x).
    public static double ToNum(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Number => v.GetDouble(),
        JsonValueKind.True => 1,
        JsonValueKind.False => 0,
        JsonValueKind.Null => 0,
        JsonValueKind.String => NumFromString(v.GetString()!),
        _ => double.NaN, // Undefined / Object / Array
    };

    private static double NumFromString(string s)
    {
        var t = s.Trim();
        if (t.Length == 0) return 0; // Number("") === 0
        if (t is "Infinity" or "+Infinity") return double.PositiveInfinity;
        if (t == "-Infinity") return double.NegativeInfinity;
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hx) ? hx : double.NaN;
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
    }

    private static readonly Regex FloatRe =
        new(@"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?", RegexOptions.Compiled);

    // JS の parseFloat(x) / JavaScript's parseFloat(x).
    public static double ParseFloat(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind != JsonValueKind.String) return double.NaN; // parseFloat(String(bool/null/...)) → NaN
        var m = FloatRe.Match(v.GetString()!.TrimStart());
        return m.Success && double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d : double.NaN;
    }

    private static readonly Regex IntRe = new(@"^[+-]?\d+", RegexOptions.Compiled);

    // JS の parseInt(x, 10相当) / JavaScript's parseInt(x) (radix 10; 0x honored).
    public static double ParseInt(JsonElement v)
    {
        string s = v.ValueKind switch
        {
            JsonValueKind.Number => NumStr(v.GetDouble()),
            JsonValueKind.String => v.GetString()!,
            _ => "",
        };
        var t = s.TrimStart();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || t.StartsWith("-0x", StringComparison.OrdinalIgnoreCase))
        {
            bool neg = t.StartsWith("-");
            var hex = t[(neg ? 3 : 2)..];
            var hm = Regex.Match(hex, "^[0-9a-fA-F]+");
            if (!hm.Success) return double.NaN;
            var val = (double)long.Parse(hm.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return neg ? -val : val;
        }
        var m = IntRe.Match(t);
        return m.Success ? double.Parse(m.Value, CultureInfo.InvariantCulture) : double.NaN;
    }

    // JS の Number.prototype.toFixed(digits)。IEEE754 の実値に対して丸める。
    // JavaScript's Number.prototype.toFixed(digits); rounds against the actual IEEE754 value.
    public static string ToFixed(double value, int digits)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsInfinity(value)) return value > 0 ? "Infinity" : "-Infinity";
        double pow = Math.Pow(10, digits);
        // 「x に最も近い n / 10^f。同点なら大きい方の n」→ floor(scaled + 0.5)。
        // "n/10^f closest to x; larger n on ties" → floor(scaled + 0.5).
        double scaled = value * pow;
        double rounded = Math.Floor(scaled + 0.5);
        double result = rounded / pow;
        return result.ToString("F" + digits, CultureInfo.InvariantCulture);
    }
}

// ============================================================
// JSON オブジェクト（またはその欠如）を JS のプロパティアクセス感覚で扱うラッパー。
// Wrapper treating a JSON object (or its absence) like JS property access.
// 欠如キーは JS の undefined として扱う。
// A missing key behaves like JS undefined.
// ============================================================
public readonly struct JObj
{
    public readonly JsonElement El; // Object または Undefined / Object or Undefined

    public JObj(JsonElement el) => El = el.ValueKind == JsonValueKind.Object ? el : default;

    public static readonly JObj None = new(default);

    public bool IsObject => El.ValueKind == JsonValueKind.Object;

    // key の生の値（欠如は Undefined）/ raw value for key (Undefined if missing).
    public JsonElement Raw(string key)
        => El.ValueKind == JsonValueKind.Object && El.TryGetProperty(key, out var v) ? v : default;

    public bool Has(string key) => Raw(key).ValueKind != JsonValueKind.Undefined;
    public bool Truthy(string key) => Js.Truthy(Raw(key));
    public bool IsNullish(string key) => Js.IsNullish(Raw(key));

    // 入れ子オブジェクト（props.slider など。None セーフ）/ nested object (None-safe).
    public JObj Obj(string key) => new(Raw(key));

    // 配列要素の列挙（非配列なら空）/ enumerate array elements (empty if not an array).
    public IEnumerable<JsonElement> Arr(string key)
    {
        var v = Raw(key);
        if (v.ValueKind == JsonValueKind.Array)
            foreach (var e in v.EnumerateArray()) yield return e;
    }

    public bool IsArray(string key) => Raw(key).ValueKind == JsonValueKind.Array;

    // props.key ?? fallback を文字列として / (props.key ?? fallback) as a string.
    public string StrN(string key, string fallback) => IsNullish(key) ? fallback : Js.Str(Raw(key));

    // props.key || fallback を文字列として / (props.key || fallback) as a string.
    public string StrT(string key, string fallback) => Truthy(key) ? Js.Str(Raw(key)) : fallback;

    // props.key ?? fallback を数値として / (props.key ?? fallback) as a number.
    public double NumN(string key, double fallback) => IsNullish(key) ? fallback : Js.ToNum(Raw(key));

    // props.key || fallback を数値として / (props.key || fallback) as a number.
    public double NumT(string key, double fallback) => Truthy(key) ? Js.ToNum(Raw(key)) : fallback;

    // props.key ?? fallback を真偽として / (props.key ?? fallback) as a boolean.
    public bool BoolN(string key, bool fallback) => IsNullish(key) ? fallback : Js.Truthy(Raw(key));

    // props.key === s の厳密等価（文字列）/ strict equality props.key === s (string).
    public bool Eq(string key, string s)
    {
        var v = Raw(key);
        return v.ValueKind == JsonValueKind.String && v.GetString() == s;
    }
}
