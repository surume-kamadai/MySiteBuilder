// ============================================================
// gradient-overlay.js
// 画像(Image)にグラデーションを「掛ける」ためのエディタ用オーバーレイ層。
// 画像はビットマップなので Konva の塗りでは表現できない。canvas-preview /
// offscreen と同じ方式で、#canvas-wrapper 上に各画像へ重なる div を置き、
// CSS グラデーション + mix-blend-mode:multiply で見た目を出す（出力と一致）。
// ============================================================
import { stage, layer } from './canvas.js';

let overlay = null;
let started = false;
const divs = new Map(); // node.id() -> div

// CSS グラデーション文字列（renderer.js / index.html と方向定義をそろえる）
function gradientCss(g) {
    const c1 = g.c1 || '#4facfe', c2 = g.c2 || '#00f2fe';
    if (g.type === 'radial') return `radial-gradient(circle, ${c1}, ${c2})`;
    const DEG = { v: 180, h: 90, d1: 135, d2: 225 };
    return `linear-gradient(${DEG[g.dir] ?? 180}deg, ${c1}, ${c2})`;
}

export function initGradientOverlay() {
    if (started) return;
    const wrapper = document.getElementById('canvas-wrapper');
    if (!wrapper) { setTimeout(initGradientOverlay, 300); return; }
    if (getComputedStyle(wrapper).position === 'static') wrapper.style.position = 'relative';

    overlay = document.createElement('div');
    overlay.id = 'gradient-overlay-layer';
    overlay.style.cssText = 'position:absolute; left:0; top:0; width:100%; height:100%; pointer-events:none; overflow:hidden; z-index:55;';
    wrapper.appendChild(overlay);

    started = true;
    requestAnimationFrame(tick);
}

if (typeof window !== 'undefined') setTimeout(initGradientOverlay, 600);

function tick() {
    try { sync(); } catch (e) { /* 補助機能なので編集は止めない */ }
    requestAnimationFrame(tick);
}

function sync() {
    if (!overlay) return;
    const zoom = stage.scaleX() || 1;
    const seen = new Set();

    layer.find('.ui-element').forEach(node => {
        if (node.getAttr('uiType') !== 'Image' || node.visible() === false) return;
        const b = node.getAttr('bladeData') || {};
        const g = b.gradient;
        if (!g || !g.on) return;

        const id = node.id();
        seen.add(id);
        let el = divs.get(id);
        if (!el) {
            el = document.createElement('div');
            el.style.cssText = 'position:absolute; mix-blend-mode:multiply; pointer-events:none;';
            overlay.appendChild(el);
            divs.set(id, el);
        }
        const box = node.getClientRect({ relativeTo: layer });
        el.style.left   = (box.x * zoom) + 'px';
        el.style.top    = (box.y * zoom) + 'px';
        el.style.width  = (box.width  * zoom) + 'px';
        el.style.height = (box.height * zoom) + 'px';
        el.style.background = gradientCss(g);
        el.style.opacity = (typeof b.opacity === 'number' ? b.opacity : 1);
    });

    for (const [id, el] of divs) {
        if (!seen.has(id)) { el.remove(); divs.delete(id); }
    }
}
