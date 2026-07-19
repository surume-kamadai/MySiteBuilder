// ============================================================
// node-style.js - ノードへスタイルを適用する関数群
// （塗り/グラデ/境界線/影/光彩/角丸/画像フィット/テキスト装飾）
// elements.js(生成) から分離。node/Konva のみに依存し編集モジュールへは依存しない。
// ============================================================
export function applyNodeShadow(node, shadowType) {
    if (!node) return;
    const uiType = node.getAttr('uiType');
    let target = node;

    // タイプごとにシャドウを付ける実描画ターゲットを選ぶ
    if (uiType === 'Button') {
        target = node.findOne('.btn-bg');
    } else if (uiType === 'Slider' || uiType === 'Accordion' || uiType === 'ArticleGrid') {
        // これらは Konva.Group。内部の Rect(背景) にシャドウを付ける
        target = node.findOne('Rect');
    }

    // Group や、シャドウを直接持てないノードはスキップ
    if (uiType === 'Group' || !target || typeof target.shadowColor !== 'function') return;

    // シャドウ種別ごとの設定（offsetX, offsetY, blur, opacity）
    const SHADOWS = {
        light:    { x: 0,  y: 4,  blur: 10, opacity: 0.15 },
        dark:     { x: 0,  y: 8,  blur: 15, opacity: 0.4  },
        hard:     { x: 5,  y: 5,  blur: 0,  opacity: 0.45 }, // くっきり（ぼかしなし）
        diagonal: { x: 10, y: 10, blur: 14, opacity: 0.3  }, // 斜め
        float:    { x: 0,  y: 20, blur: 30, opacity: 0.28 }, // 浮遊（大きめ）
    };

    if (!shadowType || shadowType === 'none' || !SHADOWS[shadowType]) {
        target.shadowOpacity(0);
    } else {
        const s = SHADOWS[shadowType];
        target.shadowColor('#000000');
        target.shadowOffsetX(s.x);
        target.shadowOffsetY(s.y);
        target.shadowBlur(s.blur);
        target.shadowOpacity(s.opacity);
    }
}

// グラデーション（線形/放射状）をノードへ適用。off なら単色塗りに戻す。
// 対象: Rect / Circle / Triangle / Button(内部bg)。Image はDOMオーバーレイ側で描画する。
export function applyGradient(node, bData) {
    if (!node) return;
    const type = node.getAttr('uiType');
    let target = null;
    if (type === 'Rect' || type === 'Circle' || type === 'Triangle') target = node;
    else if (type === 'Button') target = node.findOne('.btn-bg');
    if (!target || typeof target.fillLinearGradientColorStops !== 'function') return;

    // いったん両グラデをクリア
    target.fillLinearGradientColorStops(null);
    target.fillRadialGradientColorStops(null);

    const g = bData.gradient;
    if (!g || !g.on) {
        target.fillPriority('color');
        const bg = bData.bgcolor;
        target.fill(type === 'Button' ? (bData.bgimage ? null : bg) : bg);
        node.getLayer()?.batchDraw();
        return;
    }

    // 形状のローカル境界（Circleは中心原点）
    let x0 = 0, y0 = 0, w = node.width(), h = node.height();
    if (type === 'Circle') { const rx = node.radiusX(), ry = node.radiusY(); x0 = -rx; y0 = -ry; w = rx * 2; h = ry * 2; }

    const c1 = g.c1 || '#4facfe', c2 = g.c2 || '#00f2fe';
    if (g.type === 'radial') {
        const cx = x0 + w / 2, cy = y0 + h / 2, R = Math.max(w, h) / 2 || 1;
        target.fillRadialGradientStartPoint({ x: cx, y: cy });
        target.fillRadialGradientEndPoint({ x: cx, y: cy });
        target.fillRadialGradientStartRadius(0);
        target.fillRadialGradientEndRadius(R);
        target.fillRadialGradientColorStops([0, c1, 1, c2]);
        target.fillPriority('radial-gradient');
    } else {
        const DIRS = { v: [[0.5, 0], [0.5, 1]], h: [[0, 0.5], [1, 0.5]], d1: [[0, 0], [1, 1]], d2: [[1, 0], [0, 1]] };
        const [s, e] = DIRS[g.dir] || DIRS.v;
        target.fillLinearGradientStartPoint({ x: x0 + s[0] * w, y: y0 + s[1] * h });
        target.fillLinearGradientEndPoint({ x: x0 + e[0] * w, y: y0 + e[1] * h });
        target.fillLinearGradientColorStops([0, c1, 1, c2]);
        target.fillPriority('linear-gradient');
    }
    node.getLayer()?.batchDraw();
}

// 境界線(Stroke)をノードへ適用。対象: Rect/Circle/Button(内部bg)/Image/Label(文字の縁取り)。
// TextInput は独自の枠を持つので対象外。
export function applyStroke(node, bData) {
    if (!node) return;
    const type = node.getAttr('uiType');
    const SUPPORTED = ['Rect', 'Circle', 'Button', 'Image', 'Label'];
    if (!SUPPORTED.includes(type)) return;
    const s = bData.stroke || {};
    const on = !!s.on;
    const w = on ? Math.max(0, parseFloat(s.width) || 0) : 0;
    const c = s.color || '#000000';
    const target = (type === 'Button') ? node.findOne('.btn-bg') : node;
    if (!target || typeof target.stroke !== 'function') return;
    target.stroke(c);
    target.strokeWidth(w);
    if (typeof target.strokeEnabled === 'function') target.strokeEnabled(w > 0);
    node.getLayer()?.batchDraw();
}

// ドロップシャドウ(自由値)を適用。on の時だけプリセットを上書きする（off時は applyNodeShadow に委ねる）。
// Konva はスプレッド未対応のためエディタ表示は近似（スプレッドは出力CSSにのみ反映）。
export function applyDropShadow(node, bData) {
    const d = bData && bData.dropShadow;
    if (!d || !d.on) return;
    const uiType = node.getAttr('uiType');
    let target = node;
    if (uiType === 'Button') target = node.findOne('.btn-bg');
    else if (uiType === 'Slider' || uiType === 'Accordion' || uiType === 'ArticleGrid') target = node.findOne('Rect');
    if (uiType === 'Group' || !target || typeof target.shadowColor !== 'function') return;
    target.shadowColor(d.color || '#000000');
    target.shadowOffsetX(parseFloat(d.x) || 0);
    target.shadowOffsetY(parseFloat(d.y) || 0);
    target.shadowBlur(Math.max(0, parseFloat(d.blur) || 0));
    target.shadowOpacity(Math.min(1, Math.max(0, d.opacity ?? 0.35)));
    node.getLayer()?.batchDraw();
}

// グラデーション文字（-webkit-background-clip:text 相当）をエディタで再現。
// Konva.Text は fillLinearGradient に対応するので、文字自体をグラデ塗りにする。
// 対象: Label（本体）/ Button（内部 .btn-text）。off なら単色(bData.color)へ戻す。
export function applyGradText(node, bData) {
    if (!node) return;
    const type = node.getAttr('uiType');
    let t = null;
    if (type === 'Label') t = node;
    else if (type === 'Button') t = node.findOne('.btn-text');
    if (!t || typeof t.fillLinearGradientColorStops !== 'function') return;

    const g = bData.gradText;
    if (!g || !g.on) {
        t.fillLinearGradientColorStops(null);
        t.fillPriority('color');
        t.fill(bData.color || '#000000');
        node.getLayer()?.batchDraw();
        return;
    }
    const w = t.width() || node.width() || 1;
    const h = t.height() || node.height() || 1;
    const DIRS = { v: [[0.5, 0], [0.5, 1]], h: [[0, 0.5], [1, 0.5]], d1: [[0, 0], [1, 1]], d2: [[1, 0], [0, 1]] };
    const [s, e] = DIRS[g.dir] || DIRS.h;
    t.fillLinearGradientStartPoint({ x: s[0] * w, y: s[1] * h });
    t.fillLinearGradientEndPoint({ x: e[0] * w, y: e[1] * h });
    t.fillLinearGradientColorStops([0, g.c1 || '#ff6ec4', 1, g.c2 || '#7873f5']);
    t.fillPriority('linear-gradient');
    node.getLayer()?.batchDraw();
}

// 光彩（外側グロー）をエディタで近似。Konvaのシャドウ枠は1つだけなので、
// ドロップシャドウが有効なときはそちらを優先し、グローはキャンバスに出さない（出力CSSでは両方反映）。
// off時は applyNodeShadow / applyDropShadow の結果に任せる（ここでは触らない）。
export function applyGlow(node, bData) {
    const g = bData && bData.glow;
    if (!g || !g.on) return;
    if (bData.dropShadow && bData.dropShadow.on) return;  // ドロップシャドウ優先
    const uiType = node.getAttr('uiType');
    let target = node;
    if (uiType === 'Button') target = node.findOne('.btn-bg');
    else if (uiType === 'Slider' || uiType === 'Accordion' || uiType === 'ArticleGrid') target = node.findOne('Rect');
    if (uiType === 'Group' || !target || typeof target.shadowColor !== 'function') return;
    target.shadowColor(g.color || '#00d0ff');
    target.shadowOffsetX(0);
    target.shadowOffsetY(0);
    target.shadowBlur(Math.max(0, parseFloat(g.blur) || 0));
    target.shadowOpacity(Math.min(1, Math.max(0, g.opacity ?? 0.8)));
    node.getLayer()?.batchDraw();
}

// 角の丸みをノードへ適用（Rect / Image / Button内部bg・bg画像）
export function applyCornerRadius(node, bData) {
    if (!node) return;
    const type = node.getAttr('uiType');
    const r = Math.max(0, parseInt(bData.cornerRadius) || 0);
    if (type === 'Rect' || type === 'Image') {
        if (typeof node.cornerRadius === 'function') node.cornerRadius(r);
    } else if (type === 'Button') {
        const bg = node.findOne('.btn-bg');
        const im = node.findOne('.btn-bgimage');
        if (bg) bg.cornerRadius(r);
        if (im) im.cornerRadius(r);
    }
    node.getLayer()?.batchDraw();
}

// 画像を「拡大・切り取りせず、縦横比を保って全体表示」する。
// 以前は cover(crop) で枠を埋めていたが画像が拡大表示されてしまうため、
// クロップを解除し、高さを幅に対する画像の比率へ合わせて歪み・拡大を防ぐ。
// （幅を基準に高さが追従＝アスペクト固定。これで拡大も歪みも起きない）
export function applyImageCover(node) {
    if (!node || node.getAttr('uiType') !== 'Image') return;
    const img = node.image();
    if (!img || !img.width || !img.height) return;
    // クロップ解除＝画像全体を指す crop にする（null を渡すと Konva が例外を投げるため）
    node.crop({ x: 0, y: 0, width: img.width, height: img.height });
    const w = node.width();
    if (w > 0) {
        node.height(Math.max(1, Math.round(w * img.height / img.width)));
    }
}

// テキストノード(Konva.Text)へ 太さ/斜体/下線/字間/行間 を適用
function applyTextExtras(t, bData) {
    const parts = [];
    if (bData.fontWeight === 'bold') parts.push('bold');
    if (bData.italic) parts.push('italic');
    t.fontStyle(parts.join(' ') || 'normal');
    t.textDecoration(bData.underline ? 'underline' : '');
    t.letterSpacing(parseFloat(bData.letterSpacing) || 0);
    if (bData.lineHeight) t.lineHeight(parseFloat(bData.lineHeight) || 1);
}

export function applyTextStyle(node, bData) {
    if (!node) return;
    const type = node.getAttr('uiType');

    if (type === 'Label') {
        node.align(bData.align || 'left');
        node.fontFamily(bData.fontfamily || 'sans-serif');
        applyTextExtras(node, bData);
    } else if (type === 'Button') {
        const txt = node.findOne('.btn-text');
        if (txt) {
            txt.align(bData.align || 'center');
            txt.fontFamily(bData.fontfamily || 'sans-serif');
            applyTextExtras(txt, bData);
        }
    }
}
