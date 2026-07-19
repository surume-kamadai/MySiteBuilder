// ============================================================
// offscreen.js
// キャンバス外（画面外）に出てしまった要素の位置を、キャンバスの端に
// 「四角マーカー」で示す補助機能。マーカーには要素名と方向矢印を表示し、
// クリックすると その要素を選択し、画面内（左上付近）へ引き戻す。
//
// Konva のステージはキャンバスサイズちょうどで描画され、外側は見えないため、
// #canvas-wrapper 上に pointer-events を通すオーバーレイ層を重ねて表示する。
// requestAnimationFrame で常に最新の配置に追従する。
// ============================================================
import { stage, layer } from './canvas.js';
import { currentCanvasWidth, currentCanvasHeight, selectedNodes } from '../app/state.js';
import { applySelectedNodes } from '../nodes/elements.js';
import { saveHistory } from '../history/history.js';
import { showToast } from '../ui/toast.js';

let overlay = null;
let started = false;
const markers = new Map(); // node.id() -> element

export function initOffscreenIndicators() {
    if (started) return;
    const wrapper = document.getElementById('canvas-wrapper');
    if (!wrapper) { setTimeout(initOffscreenIndicators, 300); return; }
    if (getComputedStyle(wrapper).position === 'static') wrapper.style.position = 'relative';

    overlay = document.createElement('div');
    overlay.id = 'offscreen-layer';
    // マーカー自体はクリックさせたいので、層は none・マーカーは auto にする
    overlay.style.cssText = 'position:absolute; left:0; top:0; width:100%; height:100%; pointer-events:none; overflow:visible; z-index:60;';
    wrapper.appendChild(overlay);

    started = true;
    requestAnimationFrame(tick);
}

// main-renderer 側の呼び出しが無くても動くよう、import 時にも自動起動
if (typeof window !== 'undefined') setTimeout(initOffscreenIndicators, 600);

function tick() {
    try { sync(); } catch (e) { /* 補助機能なので失敗しても編集は止めない */ }
    requestAnimationFrame(tick);
}

function sync() {
    if (!overlay) return;
    const cw = currentCanvasWidth, ch = currentCanvasHeight;
    const zoom = stage.scaleX() || 1;

    const seen = new Set();

    layer.getChildren().forEach(node => {
        if (!node.hasName || !node.hasName('ui-element') || node.visible() === false) return;

        // キャンバス基準の外接矩形（未スケール）
        const box = node.getClientRect({ relativeTo: layer });
        const fullyOff =
            (box.x + box.width)  < 0  || box.x > cw ||
            (box.y + box.height) < 0  || box.y > ch;
        if (!fullyOff) return; // 一部でも見えていれば対象外

        const id = node.id();
        seen.add(id);

        let el = markers.get(id);
        if (!el) {
            el = document.createElement('div');
            el.style.cssText =
                'position:absolute; pointer-events:auto; cursor:pointer; transform:translate(-50%,-50%);' +
                'min-width:18px; height:20px; padding:0 6px; border-radius:4px; background:#e74c3c; color:#fff;' +
                'font-size:11px; font-weight:bold; line-height:20px; white-space:nowrap; box-shadow:0 1px 4px rgba(0,0,0,0.5);' +
                'border:1px solid #fff; max-width:120px; overflow:hidden; text-overflow:ellipsis;';
            el._nodeRef = node;
            el.onclick = (ev) => {
                ev.stopPropagation();
                bringIntoView(el._nodeRef);
            };
            overlay.appendChild(el);
            markers.set(id, el);
        }
        el._nodeRef = node;

        // 要素中心をキャンバス内に丸めて、端にマーカーを置く
        const cx = box.x + box.width / 2;
        const cy = box.y + box.height / 2;
        const margin = 10;
        const clampedX = Math.max(margin, Math.min(cw - margin, cx));
        const clampedY = Math.max(margin, Math.min(ch - margin, cy));

        // どちら方向に外れているかで矢印を選ぶ
        let arrow = '';
        if (cx < 0)        arrow = '◀';
        else if (cx > cw)  arrow = '▶';
        else if (cy < 0)   arrow = '▲';
        else if (cy > ch)  arrow = '▼';

        const bData = node.getAttr('bladeData') || {};
        const name = bData.name || node.getAttr('uiType') || '要素';
        el.textContent = `${arrow} ${name}`;
        el.title = `「${name}」は画面外にあります。クリックで画面内に戻します。`;
        el.style.left = (clampedX * zoom) + 'px';
        el.style.top  = (clampedY * zoom) + 'px';
        el.style.outline = selectedNodes.includes(node) ? '2px solid #00a8ff' : 'none';
    });

    // 画面内に戻った/消えた要素のマーカーを撤去
    for (const [id, el] of markers) {
        if (!seen.has(id)) { el.remove(); markers.delete(id); }
    }
}

// 画面外の要素を、外接矩形が左上(20,20)に来るよう移動して選択する
function bringIntoView(node) {
    if (!node) return;
    const box = node.getClientRect({ relativeTo: layer });
    const dx = 20 - box.x;
    const dy = 20 - box.y;
    node.x(node.x() + dx);
    node.y(node.y() + dy);
    layer.batchDraw();
    applySelectedNodes([node]);
    saveHistory();
    const name = (node.getAttr('bladeData') || {}).name || '要素';
    showToast(`「${name}」を画面内に戻しました。`);
}
