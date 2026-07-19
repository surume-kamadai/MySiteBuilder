// ============================================================
// css-generator.js - HTML/CSS 生成の純粋ヘルパー群
// css-generator.js - pure helpers for generating HTML/CSS.
// renderer.js(HtmlRenderer) から共有。副作用なし・DOM非依存で単体テストしやすい。
// Shared by renderer.js (HtmlRenderer). Side-effect-free and DOM-independent, so easy to unit test.
// ============================================================
export const ANIM_CSS = `
        @keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }
        @keyframes fadeUp { from { opacity: 0; transform: translateY(30px); } to { opacity: 1; transform: translateY(0); } }
        @keyframes scaleIn { from { opacity: 0; transform: scale(0.8); } to { opacity: 1; transform: scale(1); } }
        @keyframes slideLeft { from { opacity: 0; transform: translateX(50px); } to { opacity: 1; transform: translateX(0); } }
        @keyframes slideRight { from { opacity: 0; transform: translateX(-50px); } to { opacity: 1; transform: translateX(0); } }
        .anim-fadein    { animation: fadeIn    1s   cubic-bezier(0.16, 1, 0.3, 1) forwards; opacity: 0; }
        .anim-fadeup    { animation: fadeUp    1s   cubic-bezier(0.16, 1, 0.3, 1) forwards; opacity: 0; }
        .anim-scale     { animation: scaleIn   0.8s cubic-bezier(0.16, 1, 0.3, 1) forwards; opacity: 0; }
        .anim-slideleft { animation: slideLeft  0.8s cubic-bezier(0.16, 1, 0.3, 1) forwards; opacity: 0; }
        .anim-slideright{ animation: slideRight 0.8s cubic-bezier(0.16, 1, 0.3, 1) forwards; opacity: 0; }`;

// HTML特殊文字をエスケープ / Escape HTML special characters.
export function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#039;');
}

// エディタの選択肢と対応する Google Fonts。出力HTMLの<head>へ、使用フォントのみ<link>する。
// Google Fonts matching the editor's choices. Only fonts actually used get <link>ed into the exported <head>.
export const GOOGLE_FONTS = [
    { family: 'Noto Sans JP',      spec: 'Noto+Sans+JP:wght@400;700' },
    { family: 'Noto Serif JP',     spec: 'Noto+Serif+JP:wght@400;700' },
    { family: 'M PLUS Rounded 1c', spec: 'M+PLUS+Rounded+1c:wght@400;700' },
    { family: 'Zen Maru Gothic',   spec: 'Zen+Maru+Gothic:wght@400;700' },
    { family: 'Kosugi Maru',       spec: 'Kosugi+Maru' },
    { family: 'Sawarabi Mincho',   spec: 'Sawarabi+Mincho' },
    { family: 'Yusei Magic',       spec: 'Yusei+Magic' },
    { family: 'Dela Gothic One',   spec: 'Dela+Gothic+One' },
];

// data:URL の画像を出力パスへ解決する（imageMap 経由）。それ以外はそのまま返す。
// Resolve a data:URL image to its output path via imageMap; otherwise return as-is.
export function resolveImageSrc(src, imageMap) {
    if (typeof src === 'string' && src.startsWith('data:image')) {
        return imageMap.get(src) || src;
    }
    return src;
}

// 背景の CSS 宣言を返す。グラデーション on なら background:gradient、それ以外は単色。
// Return the background CSS declaration: a gradient when enabled, else a solid color.
export function gradientBgDecl(props, bgcolorEscaped) {
    const g = props.gradient;
    if (g && g.on) {
        const c1 = escapeHtml(g.c1 || '#4facfe'), c2 = escapeHtml(g.c2 || '#00f2fe');
        if (g.type === 'radial') return `background: radial-gradient(circle, ${c1}, ${c2});`;
        const DEG = { v: 180, h: 90, d1: 135, d2: 225 };
        return `background: linear-gradient(${DEG[g.dir] ?? 180}deg, ${c1}, ${c2});`;
    }
    // bgcolor は #rrggbb か rgba(...)（透明度はピッカー内で色に含まれる）
    // bgcolor is #rrggbb or rgba(...) (opacity is baked into the color by the picker).
    return `background-color: ${bgcolorEscaped};`;
}

// #rrggbb(または#rgb) と不透明度 → rgba() 文字列 / #rrggbb (or #rgb) + opacity → an rgba() string.
function hexToRgba(hex, a) {
    const h = String(hex ?? '#000000').replace('#', '');
    const n = h.length === 3 ? h.split('').map(c => c + c).join('') : h;
    const r = parseInt(n.slice(0, 2), 16) || 0;
    const g = parseInt(n.slice(2, 4), 16) || 0;
    const b = parseInt(n.slice(4, 6), 16) || 0;
    const al = Math.min(1, Math.max(0, a ?? 1));
    return `rgba(${r}, ${g}, ${b}, ${al})`;
}

// 境界線(Stroke)のCSS宣言。図形/ボタン/画像は border、テキストは -webkit-text-stroke。
// Stroke CSS: border for shapes/buttons/images, -webkit-text-stroke for text.
export function strokeDecl(props, type) {
    const s = props.stroke;
    if (!s || !s.on) return '';
    const w = Math.max(0, parseFloat(s.width) || 0);
    if (w <= 0) return '';
    const c = escapeHtml(s.color || '#000000');
    if (type === 'Label') return ` -webkit-text-stroke: ${w}px ${c};`;
    return ` border: ${w}px solid ${c};`;
}

// テキストの 斜体/下線/字間/行間 のCSS（font-weight は各要素側で出力）
// CSS for italic / underline / letter-spacing / line-height (font-weight is emitted per element).
export function textExtraCss(props) {
    let s = '';
    if (props.italic) s += ' font-style: italic;';
    if (props.underline) s += ' text-decoration: underline;';
    const ls = parseFloat(props.letterSpacing); if (ls) s += ` letter-spacing: ${ls}px;`;
    const lh = parseFloat(props.lineHeight); if (lh) s += ` line-height: ${lh};`;
    return s;
}

// シャドウ系プリセット（種別→CSS値）。Label は text-shadow に使う
// Shadow presets (kind → CSS value). For Label these become text-shadow.
const PRESET_SHADOW_CSS = {
    light:    '0 4px 10px rgba(0,0,0,0.15)',
    dark:     '0 8px 15px rgba(0,0,0,0.4)',
    hard:     '5px 5px 0 rgba(0,0,0,0.45)',
    diagonal: '10px 10px 14px rgba(0,0,0,0.3)',
    float:    '0 20px 30px rgba(0,0,0,0.28)',
};

// ドロップシャドウ(自由/プリセット) ＋ 光彩 ＋ 内側シャドウ ＋ ベベル を
// 1つの box-shadow(通常) / text-shadow(Label) に合成して返す。
// Combine drop shadow (custom/preset) + glow + inner shadow + bevel into a single
// box-shadow (normal) or text-shadow (Label) declaration.
export function combinedShadowDecl(props, type) {
    const isText = (type === 'Label');
    const box = [];   // box-shadow の各層 / layers of box-shadow
    const txt = [];   // text-shadow の各層 / layers of text-shadow

    // 1) ドロップシャドウ（自由値優先、無ければプリセット）
    // 1) Drop shadow (custom values take priority, otherwise a preset).
    const ds = props.dropShadow;
    if (ds && ds.on) {
        const dx = parseFloat(ds.x) || 0, dy = parseFloat(ds.y) || 0;
        const dblur = Math.max(0, parseFloat(ds.blur) || 0), dspread = parseFloat(ds.spread) || 0;
        const drgba = hexToRgba(ds.color || '#000000', ds.opacity ?? 0.35);
        if (isText) txt.push(`${dx}px ${dy}px ${dblur}px ${drgba}`);              // text-shadow はスプレッド非対応 / text-shadow has no spread
        else        box.push(`${dx}px ${dy}px ${dblur}px ${dspread}px ${drgba}`);
    } else if (PRESET_SHADOW_CSS[props.shadow]) {
        (isText ? txt : box).push(PRESET_SHADOW_CSS[props.shadow]);
    }

    // 2) 光彩（外側グロー） / 2) Glow (outer glow).
    const gl = props.glow;
    if (gl && gl.on) {
        const grgba = hexToRgba(gl.color || '#00d0ff', gl.opacity ?? 0.8);
        const gblur = Math.max(0, parseFloat(gl.blur) || 0), gspread = parseFloat(gl.spread) || 0;
        if (isText) txt.push(`0 0 ${gblur}px ${grgba}`);
        else        box.push(`0 0 ${gblur}px ${gspread}px ${grgba}`);
    }

    // 3) 内側シャドウ（テキスト非対応） / 3) Inner shadow (not supported for text).
    const is = props.innerShadow;
    if (is && is.on && !isText) {
        const ix = parseFloat(is.x) || 0, iy = parseFloat(is.y) || 0;
        const iblur = Math.max(0, parseFloat(is.blur) || 0);
        const irgba = hexToRgba(is.color || '#000000', is.opacity ?? 0.4);
        box.push(`inset ${ix}px ${iy}px ${iblur}px ${irgba}`);
    }

    // 4) ベベル＆エンボス（テキスト非対応）: 明暗2方向の内側シャドウで立体感
    // 4) Bevel & emboss (not for text): two opposing inner shadows fake a 3D relief.
    const bv = props.bevel;
    if (bv && bv.on && !isText) {
        const d = Math.max(1, parseFloat(bv.depth) || 1);
        const op = Math.min(1, Math.max(0, bv.opacity ?? 0.5));
        const hl = hexToRgba(bv.highlight || '#ffffff', op);
        const sh = hexToRgba(bv.shadow || '#000000', op);
        const blur = d * 2;
        if (bv.dir === 'down') {   // 凹（くぼみ）: 左上=影 / 右下=ハイライト / concave: top-left=shadow, bottom-right=highlight
            box.push(`inset ${d}px ${d}px ${blur}px ${sh}`);
            box.push(`inset -${d}px -${d}px ${blur}px ${hl}`);
        } else {                   // 凸（浮き出し）: 左上=ハイライト / 右下=影 / convex: top-left=highlight, bottom-right=shadow
            box.push(`inset ${d}px ${d}px ${blur}px ${hl}`);
            box.push(`inset -${d}px -${d}px ${blur}px ${sh}`);
        }
    }

    const arr = isText ? txt : box;
    if (arr.length === 0) return '';
    return (isText ? 'text-shadow: ' : 'box-shadow: ') + arr.join(', ') + ';';
}

// グラデーション文字（文字自体をグラデ塗り）。文字を包む <span> に付けるスタイルを返す。
// Gradient text (the glyphs themselves are gradient-filled). Returns the style for the wrapping <span>.
// off または未設定なら ''。背景クリップと衝突するため必ず span へ適用する。
// Returns '' when off/unset. Must be applied to a span since it clashes with background-clip.
function gradTextSpanStyle(props) {
    const g = props.gradText;
    if (!g || !g.on) return '';
    const c1 = escapeHtml(g.c1 || '#ff6ec4'), c2 = escapeHtml(g.c2 || '#7873f5');
    const DEG = { v: 180, h: 90, d1: 135, d2: 225 };
    const deg = DEG[g.dir] ?? 90;
    return `background: linear-gradient(${deg}deg, ${c1}, ${c2}); -webkit-background-clip: text; background-clip: text; -webkit-text-fill-color: transparent; color: transparent;`;
}

// テキストをグラデ文字 span で包む（off ならそのまま返す）
// Wrap text in a gradient-text span (returns text unchanged when off).
export function wrapGradText(text, props) {
    const st = gradTextSpanStyle(props);
    return st ? `<span style="${st}">${text}</span>` : text;
}
