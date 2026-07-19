// ============================================================
// rulers.js - 定規（ルーラー）とガイド線
//
// キャンバス上端・左端に定規を表示し、そこからドラッグして任意の位置に
// ガイド線を引ける。ガイドは canvas 座標で保持し、
//   window.__getGuideXs() / window.__getGuideYs()
// で公開する。これを events.js のスナップ（移動・リサイズ）が参照して吸着する。
//
// 操作:
//   - 上の定規から下へドラッグ → 縦ガイド作成
//   - 左の定規から右へドラッグ → 横ガイド作成
//   - ガイドをドラッグ → 移動（定規へ戻す or 画面外で削除）
//   - ガイドをダブルクリック → 削除
//   - 左上の角をクリック → 全ガイド消去
//   - 📏ボタン（toggleRulers）で表示/非表示
// ============================================================
import { stage } from './canvas.js';
import { currentCanvasWidth, currentCanvasHeight } from '../app/state.js';

const RULER = 18;              // 定規の太さ(px)
const guidesX = [];            // 縦ガイドの x 座標（canvas基準）
const guidesY = [];            // 横ガイドの y 座標（canvas基準）

// スナップ側（events.js）が参照する受け口
window.__getGuideXs = () => guidesX.slice();
window.__getGuideYs = () => guidesY.slice();

let wrapper, area, topCanvas, leftCanvas, corner, guideLayer;
let started = false;
let visible = true;
let lastSig = '';
let drag = null;

export function initRulers() {
    if (started) return;
    wrapper = document.getElementById('canvas-wrapper');
    area    = document.getElementById('canvas-area');
    if (!wrapper || !area) { setTimeout(initRulers, 300); return; }
    if (getComputedStyle(wrapper).position === 'static') wrapper.style.position = 'relative';
    if (getComputedStyle(area).position === 'static') area.style.position = 'relative';

    // ガイド層（層自体はクリックを通し、各ガイド線だけ pointer-events:auto）
    guideLayer = document.createElement('div');
    guideLayer.id = 'guide-layer';
    guideLayer.style.cssText = 'position:absolute;left:0;top:0;width:100%;height:100%;pointer-events:none;overflow:visible;z-index:70;';
    wrapper.appendChild(guideLayer);

    // 上の定規
    topCanvas = document.createElement('canvas');
    topCanvas.style.cssText = `position:absolute;left:0;top:0;height:${RULER}px;z-index:80;pointer-events:auto;cursor:col-resize;`;
    topCanvas.title = 'ここから下へドラッグして縦ガイドを作成';
    topCanvas.addEventListener('mousedown', e => startCreate(e, 'x'));

    // 左の定規
    leftCanvas = document.createElement('canvas');
    leftCanvas.style.cssText = `position:absolute;left:0;top:0;width:${RULER}px;z-index:80;pointer-events:auto;cursor:row-resize;`;
    leftCanvas.title = 'ここから右へドラッグして横ガイドを作成';
    leftCanvas.addEventListener('mousedown', e => startCreate(e, 'y'));

    // 左上の角（全消去）
    corner = document.createElement('div');
    corner.style.cssText = `position:absolute;left:0;top:0;width:${RULER}px;height:${RULER}px;background:#3a3a3a;border-right:1px solid #555;border-bottom:1px solid #555;z-index:82;pointer-events:auto;cursor:pointer;`;
    corner.title = 'すべてのガイドを消去';
    corner.onclick = () => { guidesX.length = 0; guidesY.length = 0; lastSig = ''; };

    // 定規はキャンバス本体(wrapper)の外＝canvas-areaの余白に置く（デザインを隠さない）
    area.appendChild(topCanvas);
    area.appendChild(leftCanvas);
    area.appendChild(corner);

    started = true;
    requestAnimationFrame(tick);
}

// 取り込み漏れでも動くよう自動起動
if (typeof window !== 'undefined') setTimeout(initRulers, 600);

export function toggleRulers() {
    visible = !visible;
    const d = visible ? 'block' : 'none';
    [topCanvas, leftCanvas, corner, guideLayer].forEach(el => { if (el) el.style.display = d; });
    return visible;
}
window.toggleRulers = toggleRulers;

function zoom() { return stage.scaleX() || 1; }
function clamp(v, lo, hi) { return Math.max(lo, Math.min(hi, v)); }

function localFromEvent(e) {
    const r = wrapper.getBoundingClientRect();
    const z = zoom();
    return { x: (e.clientX - r.left) / z, y: (e.clientY - r.top) / z, rawX: e.clientX - r.left, rawY: e.clientY - r.top };
}

// 定規からドラッグして新規ガイドを作る
function startCreate(e, axis) {
    e.preventDefault();
    const p = localFromEvent(e);
    let idx;
    if (axis === 'x') idx = guidesX.push(clamp(p.x, 0, currentCanvasWidth))  - 1;
    else              idx = guidesY.push(clamp(p.y, 0, currentCanvasHeight)) - 1;
    lastSig = '';
    beginDrag(axis, idx, e);
}

function beginDrag(axis, idx, e) {
    drag = { axis, idx, del: false };
    document.addEventListener('mousemove', onDragMove);
    document.addEventListener('mouseup', onDragUp);
    onDragMove(e);
}

function onDragMove(e) {
    if (!drag) return;
    const p = localFromEvent(e);
    const z = zoom();
    if (drag.axis === 'x') {
        // 上の定規に戻す or 画面外で削除予定
        drag.del = p.rawY < RULER || p.x < -RULER / z || p.x > currentCanvasWidth + RULER / z;
        guidesX[drag.idx] = clamp(p.x, 0, currentCanvasWidth);
    } else {
        drag.del = p.rawX < RULER || p.y < -RULER / z || p.y > currentCanvasHeight + RULER / z;
        guidesY[drag.idx] = clamp(p.y, 0, currentCanvasHeight);
    }
    lastSig = '';
}

function onDragUp() {
    if (drag && drag.del) {
        if (drag.axis === 'x') guidesX.splice(drag.idx, 1);
        else                   guidesY.splice(drag.idx, 1);
    }
    drag = null;
    document.removeEventListener('mousemove', onDragMove);
    document.removeEventListener('mouseup', onDragUp);
    lastSig = '';
}

function tick() {
    try { render(); } catch (e) { /* 補助機能なので編集は止めない */ }
    requestAnimationFrame(tick);
}

function render() {
    if (!visible || !wrapper) return;
    const z = zoom();
    const cw = currentCanvasWidth, ch = currentCanvasHeight;
    // wrapper(キャンバス本体)の area 内での位置・サイズ。定規はこの外周に貼る。
    const oL = wrapper.offsetLeft, oT = wrapper.offsetTop;
    const W = wrapper.offsetWidth, H = wrapper.offsetHeight;
    const sig = `${z}|${cw}x${ch}|${oL},${oT}|${W}x${H}|${guidesX.join(',')}|${guidesY.join(',')}`;
    if (sig === lastSig) return;
    lastSig = sig;

    // 上の定規：キャンバスのすぐ上、左の定規：すぐ左、角：左上の交点
    topCanvas.style.left  = oL + 'px';        topCanvas.style.top  = (oT - RULER) + 'px';
    leftCanvas.style.left = (oL - RULER) + 'px'; leftCanvas.style.top = oT + 'px';
    corner.style.left = (oL - RULER) + 'px';  corner.style.top = (oT - RULER) + 'px';

    drawRuler(topCanvas, 'top', W, z);
    drawRuler(leftCanvas, 'left', H, z);
    drawGuides(z);
}

// 画面上で約40px以上の間隔になる目盛り幅を選ぶ
function pickStep(z) {
    const cands = [10, 20, 50, 100, 200, 500, 1000];
    for (const t of cands) if (t * z >= 40) return t;
    return 2000;
}

function drawRuler(cv, side, sizePx, z) {
    const horizontal = side === 'top';
    if (horizontal) { cv.width = sizePx; cv.height = RULER; cv.style.width = sizePx + 'px'; }
    else            { cv.width = RULER; cv.height = sizePx; cv.style.height = sizePx + 'px'; }

    const ctx = cv.getContext('2d');
    ctx.clearRect(0, 0, cv.width, cv.height);
    ctx.fillStyle = '#2b2b2b';
    ctx.fillRect(0, 0, cv.width, cv.height);
    ctx.strokeStyle = '#888';
    ctx.fillStyle = '#bbb';
    ctx.font = '9px sans-serif';
    ctx.textBaseline = 'top';
    ctx.lineWidth = 1;

    const max = horizontal ? currentCanvasWidth : currentCanvasHeight;
    const step = pickStep(z);
    ctx.beginPath();
    for (let c = 0; c <= max; c += step) {
        const px = Math.round(c * z) + 0.5;
        const major = (c % (step * 2)) === 0;
        const len = major ? 10 : 6;
        if (horizontal) { ctx.moveTo(px, RULER); ctx.lineTo(px, RULER - len); }
        else            { ctx.moveTo(RULER, px); ctx.lineTo(RULER - len, px); }
        if (major && c > 0) {
            if (horizontal) ctx.fillText(String(c), c * z + 2, 2);
            else            ctx.fillText(String(c), 1, c * z + 1);
        }
    }
    ctx.stroke();
}

function drawGuides(z) {
    guideLayer.innerHTML = '';
    guidesX.forEach((gx, i) => {
        const el = document.createElement('div');
        el.style.cssText = `position:absolute;top:0;left:${gx * z - 1}px;width:3px;height:100%;background:#00a8ff;opacity:0.85;pointer-events:auto;cursor:col-resize;z-index:71;`;
        el.title = `x=${Math.round(gx)} ／ ドラッグで移動・定規へ戻すか画面外で削除・ダブルクリックで削除`;
        el.addEventListener('mousedown', e => { e.preventDefault(); e.stopPropagation(); beginDrag('x', i, e); });
        el.addEventListener('dblclick', e => { e.preventDefault(); e.stopPropagation(); guidesX.splice(i, 1); lastSig = ''; });
        guideLayer.appendChild(el);
    });
    guidesY.forEach((gy, i) => {
        const el = document.createElement('div');
        el.style.cssText = `position:absolute;left:0;top:${gy * z - 1}px;height:3px;width:100%;background:#00a8ff;opacity:0.85;pointer-events:auto;cursor:row-resize;z-index:71;`;
        el.title = `y=${Math.round(gy)} ／ ドラッグで移動・定規へ戻すか画面外で削除・ダブルクリックで削除`;
        el.addEventListener('mousedown', e => { e.preventDefault(); e.stopPropagation(); beginDrag('y', i, e); });
        el.addEventListener('dblclick', e => { e.preventDefault(); e.stopPropagation(); guidesY.splice(i, 1); lastSig = ''; });
        guideLayer.appendChild(el);
    });
}
