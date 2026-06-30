using System.Text.Json;
using System.Text.Json.Serialization;

namespace MySiteBuilder.Core.Models;

// ============================================================
// 要素モデル移植（elements.js の bladeData / converter.js の processNode 出力に対応）
//
//   1要素 = { id, type, transform, properties, children? }
//   properties は Electron版で "bladeData" と呼ばれる緩い JSON オブジェクト。
//   ここでは出力エンジンが参照する全フィールドを型付けし、未知キーは
//   JsonExtensionData で保持してラウンドトリップを壊さない。
// ============================================================

/// <summary>キャンバス上の1要素。</summary>
public sealed class SiteElement
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Button / TextInput / Label / Rect / Circle / Triangle / Group / Image / Warp / Slider / ArticleGrid / Accordion</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("transform")]
    public ElementTransform Transform { get; set; } = new();

    [JsonPropertyName("properties")]
    public ElementProperties Properties { get; set; } = new();

    /// <summary>Group 要素のみ子要素を持つ。</summary>
    [JsonPropertyName("children")]
    public List<SiteElement>? Children { get; set; }
}

/// <summary>要素の位置・サイズ（左上座標基準）。</summary>
public sealed class ElementTransform
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; } = 100;

    [JsonPropertyName("height")]
    public double Height { get; set; } = 50;
}

/// <summary>要素プロパティ（Electron版 bladeData）。</summary>
public sealed class ElementProperties
{
    [JsonPropertyName("text")]        public string? Text { get; set; }
    [JsonPropertyName("name")]        public string? Name { get; set; }
    [JsonPropertyName("bgcolor")]     public string? Bgcolor { get; set; }
    [JsonPropertyName("color")]       public string? Color { get; set; }
    [JsonPropertyName("fontsize")]    public double? Fontsize { get; set; }
    [JsonPropertyName("align")]       public string? Align { get; set; }
    [JsonPropertyName("fontfamily")]  public string? FontFamily { get; set; }
    [JsonPropertyName("lock")]        public bool? Lock { get; set; }
    [JsonPropertyName("route")]       public string? Route { get; set; }
    [JsonPropertyName("method")]      public string? Method { get; set; }
    [JsonPropertyName("event")]       public string? Event { get; set; }
    [JsonPropertyName("shadow")]      public string? Shadow { get; set; }
    [JsonPropertyName("animation")]   public string? Animation { get; set; }
    [JsonPropertyName("opacity")]     public double? Opacity { get; set; }
    [JsonPropertyName("bgimage")]     public string? BgImage { get; set; }

    /// <summary>Button の役割: 'link' | 'submit' | 'none'</summary>
    [JsonPropertyName("role")]        public string? Role { get; set; }

    /// <summary>送信完了メッセージ（空なら遷移のみ）。</summary>
    [JsonPropertyName("successMessage")] public string? SuccessMessage { get; set; }

    // フォーム用（TextInput）
    [JsonPropertyName("inputName")]   public string? InputName { get; set; }
    [JsonPropertyName("inputType")]   public string? InputType { get; set; }
    [JsonPropertyName("required")]    public bool? Required { get; set; }

    /// <summary>false なら出力しない。</summary>
    [JsonPropertyName("visible")]     public bool? Visible { get; set; }

    [JsonPropertyName("events")]      public List<ElementEvent>? Events { get; set; }
    [JsonPropertyName("layouts")]     public ElementLayouts? Layouts { get; set; }
    [JsonPropertyName("mobileEdited")] public bool? MobileEdited { get; set; }

    [JsonPropertyName("warpPoints")]  public List<WarpPoint>? WarpPoints { get; set; }
    [JsonPropertyName("slider")]      public SliderConfig? Slider { get; set; }
    [JsonPropertyName("grid")]        public GridConfig? Grid { get; set; }
    [JsonPropertyName("accordion")]   public AccordionConfig? Accordion { get; set; }

    /// <summary>型付けしていない未知のプロパティ（_pcGeom 等）を保持しラウンドトリップを壊さない。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>イベント定義（クリック/ホバーでの表示操作・alert）。</summary>
public sealed class ElementEvent
{
    [JsonPropertyName("trigger")] public string? Trigger { get; set; }
    [JsonPropertyName("action")]  public string? Action { get; set; }
    [JsonPropertyName("target")]  public string? Target { get; set; }
}

/// <summary>レスポンシブ用レイアウト。</summary>
public sealed class ElementLayouts
{
    [JsonPropertyName("mobile")] public MobileLayout? Mobile { get; set; }
}

/// <summary>スマホ表示時の位置・サイズ上書き。</summary>
public sealed class MobileLayout
{
    [JsonPropertyName("x")]        public double? X { get; set; }
    [JsonPropertyName("y")]        public double? Y { get; set; }
    [JsonPropertyName("w")]        public double? W { get; set; }
    [JsonPropertyName("h")]        public double? H { get; set; }
    [JsonPropertyName("fontsize")] public double? Fontsize { get; set; }
}

/// <summary>Warp（自由四角形）の頂点。</summary>
public sealed class WarpPoint
{
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
}

/// <summary>画像スライダー設定（Swiper）。</summary>
public sealed class SliderConfig
{
    [JsonPropertyName("slides")]        public List<SlideItem>? Slides { get; set; }
    [JsonPropertyName("effect")]        public string? Effect { get; set; }
    [JsonPropertyName("speed")]         public double? Speed { get; set; }
    [JsonPropertyName("autoplay")]      public bool? Autoplay { get; set; }
    [JsonPropertyName("delay")]         public double? Delay { get; set; }
    [JsonPropertyName("loop")]          public bool? Loop { get; set; }
    [JsonPropertyName("pagination")]    public bool? Pagination { get; set; }
    [JsonPropertyName("navigation")]    public bool? Navigation { get; set; }
    [JsonPropertyName("slidesPerView")] public double? SlidesPerView { get; set; }
}

/// <summary>スライド1枚。</summary>
public sealed class SlideItem
{
    [JsonPropertyName("image")]    public string? Image { get; set; }
    [JsonPropertyName("title")]    public string? Title { get; set; }
    [JsonPropertyName("text")]     public string? Text { get; set; }
    [JsonPropertyName("linkType")] public string? LinkType { get; set; }
    [JsonPropertyName("link")]     public string? Link { get; set; }
}

/// <summary>記事グリッド設定。</summary>
public sealed class GridConfig
{
    [JsonPropertyName("items")]       public List<GridItem>? Items { get; set; }
    [JsonPropertyName("columns")]     public double? Columns { get; set; }
    [JsonPropertyName("gap")]         public double? Gap { get; set; }
    [JsonPropertyName("cardRadius")]  public double? CardRadius { get; set; }
    [JsonPropertyName("arrowColor")]  public string? ArrowColor { get; set; }
    [JsonPropertyName("imgRatio")]    public string? ImgRatio { get; set; }
    [JsonPropertyName("cardPadding")] public double? CardPadding { get; set; }
    [JsonPropertyName("sliderMode")]  public bool? SliderMode { get; set; }
    [JsonPropertyName("autoplay")]    public bool? Autoplay { get; set; }
    [JsonPropertyName("delay")]       public double? Delay { get; set; }
    [JsonPropertyName("loop")]        public bool? Loop { get; set; }
    [JsonPropertyName("navigation")]  public bool? Navigation { get; set; }
}

/// <summary>記事グリッドの1カード。</summary>
public sealed class GridItem
{
    [JsonPropertyName("image")]    public string? Image { get; set; }
    [JsonPropertyName("title")]    public string? Title { get; set; }
    [JsonPropertyName("text")]     public string? Text { get; set; }
    [JsonPropertyName("linkType")] public string? LinkType { get; set; }
    [JsonPropertyName("link")]     public string? Link { get; set; }
}

/// <summary>アコーディオン設定。</summary>
public sealed class AccordionConfig
{
    [JsonPropertyName("items")]       public List<AccordionItem>? Items { get; set; }
    [JsonPropertyName("headerColor")] public string? HeaderColor { get; set; }
    [JsonPropertyName("headerBg")]    public string? HeaderBg { get; set; }
    [JsonPropertyName("bodyColor")]   public string? BodyColor { get; set; }
    [JsonPropertyName("openFirst")]   public bool? OpenFirst { get; set; }
}

/// <summary>アコーディオンの1項目。</summary>
public sealed class AccordionItem
{
    [JsonPropertyName("title")]   public string? Title { get; set; }
    [JsonPropertyName("content")] public string? Content { get; set; }
}
