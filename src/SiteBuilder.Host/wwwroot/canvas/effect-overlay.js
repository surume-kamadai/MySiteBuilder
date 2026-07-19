// ============================================================
// effect-overlay.js
// 内側シャドウ / ベベル＆エンボス のエディタ用プレビュー層。
// これらは box-shadow(inset) ベースで、Konva では描けない（Konvaのシャドウは
// 外側1枚のみ）。そこで gradient-overlay と同じ方式で #canvas-wrapper 上に
// 各要素へ重なる pointer-events:none の div を置き、出力と同じ inset box-shadow を
// 当ててプレビューする（見た目は書き出しHTMLと一致）。
// ============================================================
import { stage, layer } from './canvas.js';

let overlay = null;
let started = false;
const divs = new Map(); // node.id() -> div

// #rrggbb(または#rgb) と不透明度 → rgba()（renderer.js と定義をそろえる）
function hexToRgba(hex, a) {
    const s = String(hex ?? '#000000');
    if (s.startsWith('rgb')) return s;  // すでに rgba(...) ならそのまま
    const h = s.replace('#', '');
    const n = h.length === 3 ? h.split('').map(c => c + c).join('') : h;
    const r = parseInt(n.slice(0, 2), 16) || 0;
    const g = parseInt(n.slice(2, 4), 16) || 0;
    const b = parseInt(n.slice(4, 6), 16) || 0;
    const al = Math.min(1, Math.max(0, a ?? 1));
    return `rgba(${r}, ${g}, ${b}, ${al})`;
}

// 内側シャドウ＋ベベルの inset box-shadow を組み立てる（px はズーム倍率で拡縮）
function insetBoxShadow(b, zoom) {
    const parts = [];
    const is = b.innerShadow;
    if (is && is.on) {
        const x = (parseFloat(is.x) || 0) * zoom;
        const y = (parseFloat(is.y) || 0) * zoom;
        const blur = Math.max(0, parseFloat(is.blur) || 0) * zoom;
        parts.push(`inset ${x}px ${y}px ${blur}px ${hexToRgba(is.color || '#000000', is.opacity ?? 0.4)}`);
    }
    const bv = b.bevel;
    if (bv && bv.on) {
        const d = Math.max(1, parseFloat(bv.depth) || 1) * zoom;
        const blur = d * 2;
        const op = Math.min(1, Math.max(0, bv.opacity ?? 0.5));
        const hl = hexToRgba(bv.highlight || '#ffffff', op);
        const sh = hexToRgba(bv.shadow || '#000000', op);
        if (bv.dir === 'down') {   // 凹（くぼみ）: 左上=影 / 右下=ハイライト
            parts.push(`inset ${d}px ${d}px ${blur}px ${sh}`);
            parts.push(`inset -${d}px -${d}px ${blur}px ${hl}`);
        } else {                   // 凸（浮き出し）: 左上=ハイライト / 右下=影
            parts.push(`inset ${d}px ${d}px ${blur}px ${hl}`);
            parts.push(`inset -${d}px -${d}px ${blur}px ${sh}`);
        }
    }
    return parts.join(', ');
}

// 角丸(px)。Circleは50%、それ以外は cornerRadius（ボタンは既定8）
function radiusCss(node, b, zoom) {
    const type = node.getAttr('uiType');
    if (type === 'Circle') return '50%';
    let r = parseInt(b.cornerRadius);
    if (!Number.isFinite(r)) r = (type === 'Button') ? 8 : 0;
    return Math.max(0, r) * zoom + 'px';
}

export function initEffectOverlay() {
    if (started) return;
    const wrapper = document.getElementById('canvas-wrapper');
    if (!wrapper) { setTimeout(initEffectOverlay, 300); return; }
    if (getComputedStyle(wrapper).position === 'static') wrapper.style.position = 'relative';

    overlay = document.createElement('div');
    overlay.id = 'effect-overlay-layer';
    overlay.style.cssText = 'position:absolute; left:0; top:0; width:100%; height:100%; pointer-events:none; overflow:hidden; z-index:58;';
    wrapper.appendChild(overlay);

    started = true;
    requestAnimationFrame(tick);
}

if (typeof window !== 'undefined') setTimeout(initEffectOverlay, 600);

function tick() {
    try { sync(); } catch (e) { /* 補助機能なので編集は止めない */ }
    requestAnimationFrame(tick);
}

const SUPPORTED = ['Rect', 'Circle', 'Button', 'Image'];

function sync() {
    if (!overlay) return;
    const zoom = stage.scaleX() || 1;
    const seen = new Set();

    layer.find('.ui-element').forEach(node => {
        if (!SUPPORTED.includes(node.getAttr('uiType')) || node.visible() === false) return;
        const b = node.getAttr('bladeData') || {};
        const hasInner = b.innerShadow && b.innerShadow.on;
        const hasBevel = b.bevel && b.bevel.on;
        if (!hasInner && !hasBevel) return;

        const id = node.id();
        seen.add(id);
        let el = divs.get(id);
        if (!el) {
            el = document.createElement('div');
            el.style.cssText = 'position:absolute; pointer-events:none; box-sizing:border-box;';
            overlay.appendChild(el);
            divs.set(id, el);
        }
        const box = node.getClientRect({ relativeTo: layer, skipShadow: true });
        el.style.left       = (box.x * zoom) + 'px';
        el.style.top        = (box.y * zoom) + 'px';
        el.style.width      = (box.width  * zoom) + 'px';
        el.style.height     = (box.height * zoom) + 'px';
        el.style.borderRadius = radiusCss(node, b, zoom);
        el.style.boxShadow  = insetBoxShadow(b, zoom);
        el.style.opacity    = (typeof b.opacity === 'number' ? b.opacity : 1);
    });

    for (const [id, el] of divs) {
        if (!seen.has(id)) { el.remove(); divs.delete(id); }
    }
}
