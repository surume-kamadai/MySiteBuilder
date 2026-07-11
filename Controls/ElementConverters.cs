using System;
using System.Globalization;
using Avalonia.Data.Converters;
using MySiteBuilder.Core.Models;

namespace MySiteBuilder.Controls;

// ============================================================
// レイヤー（エクスプローラー）一覧の見やすさ用コンバータ群。
//   要素の種類 → アイコン絵文字 / 日本語ラベル、
//   表示フラグ → 行の不透明度、グループ → 子要素数の表示。
// ============================================================

/// <summary>要素タイプ → アイコン絵文字。</summary>
public sealed class ElementTypeIconConverter : IValueConverter
{
    public static readonly ElementTypeIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value as string switch
        {
            "Label" => "🅣",
            "Button" => "🔘",
            "TextInput" => "⌨",
            "Image" => "🖼",
            "Rect" => "▭",
            "Circle" => "⬤",
            "Triangle" => "▲",
            "Group" => "📁",
            "Slider" => "🎞",
            "ArticleGrid" => "▦",
            "Accordion" => "≡",
            "Warp" => "◇",
            _ => "•",
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>要素タイプ → 日本語ラベル（補足表示）。</summary>
public sealed class ElementTypeLabelConverter : IValueConverter
{
    public static readonly ElementTypeLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value as string switch
        {
            "Label" => "テキスト",
            "Button" => "ボタン",
            "TextInput" => "入力欄",
            "Image" => "画像",
            "Rect" => "四角",
            "Circle" => "丸",
            "Triangle" => "三角",
            "Group" => "グループ",
            "Slider" => "スライダー",
            "ArticleGrid" => "記事グリッド",
            "Accordion" => "アコーディオン",
            "Warp" => "ワープ",
            _ => value as string ?? "",
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>表示フラグ(Properties.Visible) → 行の不透明度（非表示は薄く）。</summary>
public sealed class VisibleOpacityConverter : IValueConverter
{
    public static readonly VisibleOpacityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is false ? 0.4 : 1.0;   // null/true は通常表示

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>要素 → グループなら子要素数「(N)」、それ以外は空文字。</summary>
public sealed class GroupChildCountConverter : IValueConverter
{
    public static readonly GroupChildCountConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SiteElement { Type: "Group" } el ? $"({el.Children?.Count ?? 0})" : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
