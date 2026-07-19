// ============================================================
// canvas-preview.js
// Slider / ArticleGrid / Accordion を、Canvas上に「実物」HTMLで重ねて
// プレビュー表示する（WYSIWYG）。Konva はHTML/CSSを描画できないため、
// #canvas-wrapper 上に pointer-events:none のオーバーレイ層を作り、
// 各ノードの位置・サイズに合わせて実HTMLを配置する。
//
// - クリック/ドラッグは下の Konva に通る（選択・移動・リサイズは従来通り）
// - 選択中のノードはオーバーレイを隠す（変形ハンドルが見えるように）
// - requestAnimationFrame で位置追従。内容は署名が変わった時だけ再生成。
// ============================================================
import { stage, layer } from './canvas.js';
import { selectedNodes } from '../app/state.js';

const PREVIEW_TYPES = ['Slider', 'ArticleGrid', 'Accordion'];

let overlay = null;
const divs = new Map(); // node.id() -> { el, sig }

function esc(v) {
    return String(v ?? '')
        .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;').replace(/'/g, '&#039;');
}

let started = false;

export function initCanvasPreview() {
    if (started) return;
    const wrapper = document.getElementById('canvas-wrapper');
    if (!wrapper) { setTimeout(initCanvasPreview, 300); return; } // まだ無ければ待って再試行
    if (getComputedStyle(wrapper).position === 'static') wrapper.style.position = 'relative';

    overlay = document.createElement('div');
    overlay.id = 'canvas-preview-layer';
    overlay.style.cssText = 'position:absolute; left:0; top:0; width:100%; height:100%; pointer-events:none; overflow:hidden; z-index:50;';
    wrapper.appendChild(overlay);

    started = true;
    requestAnimationFrame(tick);
}

// main-renderer 側の明示呼び出しが取り込み漏れでも動くよう、import時にも自動起動する
if (typeof window !== 'undefined') {
    setTimeout(initCanvasPreview, 600);
}

let loggedErr = false;
function tick() {
    try {
        sync();
    } catch (e) {
        // 補助機能なので編集は止めないが、原因調査のため最初の1回だけログする
        if (!loggedErr) { console.error('[canvas-preview]', e); loggedErr = true; }
    }
    requestAnimationFrame(tick);
}

function sync() {
    if (!overlay) return;

    // 対象ノードを収集（グループ内も含む）
    const targets = layer.find('.ui-element').filter(n =>
        PREVIEW_TYPES.includes(n.getAttr('uiType')) && n.visible() !== false
    );

    const seen = new Set();
    targets.forEach(node => {
        const id = node.id();
        seen.add(id);

        let entry = divs.get(id);
        if (!entry) {
            const el = document.createElement('div');
            el.style.cssText = 'position:absolute; overflow:hidden; box-sizing:border-box;';
            overlay.appendChild(el);
            entry = { el, sig: null };
            divs.set(id, entry);
        }

        // 選択中は隠す（Konvaの変形ハンドルを見せるため）
        const isSelected = selectedNodes.includes(node);
        entry.el.style.display = isSelected ? 'none' : 'block';
        if (isSelected) return;

        // 位置・サイズ：レイヤー基準の箱（グループ入れ子も解決）に zoom を掛けて
        // wrapper のピクセル空間へ変換する（ズーム時もずれない）。
        const zoom = stage.scaleX() || 1;
        const box = node.getClientRect({ relativeTo: layer });
        entry.el.style.left   = (box.x * zoom) + 'px';
        entry.el.style.top    = (box.y * zoom) + 'px';
        entry.el.style.width  = (box.width  * zoom) + 'px';
        entry.el.style.height = (box.height * zoom) + 'px';

        // 内容は署名が変わった時だけ再生成
        const bData = node.getAttr('bladeData') || {};
        const type  = node.getAttr('uiType');
        const sig = type + '|' + Math.round(box.width * zoom) + 'x' + Math.round(box.height * zoom) + '|'
            + JSON.stringify(bData.slider || bData.grid || bData.accordion || {})
            + '|' + (bData.bgcolor || '');
        if (entry.sig !== sig) {
            entry.sig = sig;
            entry.el.innerHTML = buildPreview(type, bData);
        }
    });

    // 消えたノードの div を片付ける
    for (const [id, entry] of divs) {
        if (!seen.has(id)) { entry.el.remove(); divs.delete(id); }
    }
}

function buildPreview(type, bData) {
    if (type === 'Slider')      return buildSlider(bData);
    if (type === 'ArticleGrid') return buildGrid(bData);
    if (type === 'Accordion')   return buildAccordion(bData);
    return '';
}

function buildSlider(bData) {
    const slides = (bData.slider && Array.isArray(bData.slider.slides)) ? bData.slider.slides : [];
    const first  = slides[0];
    let html = `<div style="width:100%;height:100%;position:relative;overflow:hidden;border-radius:5px;background:#222;color:#fff;">`;
    if (first && first.image) {
        html += `<img src="${esc(first.image)}" style="width:100%;height:100%;object-fit:cover;display:block;">`;
    } else {
        html += `<div style="width:100%;height:100%;display:flex;align-items:center;justify-content:center;font-size:13px;opacity:.8;">スライダー（画像未設定）</div>`;
    }
    if (first && (first.title || first.text)) {
        html += `<div style="position:absolute;left:0;right:0;bottom:0;padding:10px 14px;background:linear-gradient(transparent,rgba(0,0,0,.7));">`;
        if (first.title) html += `<div style="font-weight:bold;font-size:16px;">${esc(first.title)}</div>`;
        if (first.text)  html += `<div style="font-size:12px;opacity:.9;">${esc(first.text)}</div>`;
        html += `</div>`;
    }
    html += `<div style="position:absolute;top:6px;right:8px;background:rgba(0,0,0,.55);font-size:11px;padding:2px 8px;border-radius:10px;">▶ ${slides.length}枚</div>`;
    html += `</div>`;
    return html;
}

function buildGrid(bData) {
    const g = bData.grid || {};
    const items   = Array.isArray(g.items) ? g.items : [];
    const cols    = g.columns ?? 3;
    const gap     = g.gap ?? 20;
    const radius  = g.cardRadius ?? 8;
    const ratio   = g.imgRatio || '16/10';
    const pad     = g.cardPadding ?? 18;
    if (items.length === 0) {
        return `<div style="width:100%;height:100%;display:flex;align-items:center;justify-content:center;color:#666;font-size:13px;background:#f1f2f6;border-radius:5px;">記事グリッド（アイテム未設定）</div>`;
    }
    let html = `<div style="display:grid;grid-template-columns:repeat(${cols},1fr);gap:${gap}px;width:100%;height:100%;overflow:hidden;align-items:start;">`;
    items.forEach(it => {
        html += `<div style="background:#fff;border-radius:${radius}px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,.08);">`;
        if (it.image && ratio !== 'none') {
            html += `<div style="aspect-ratio:${ratio};overflow:hidden;"><img src="${esc(it.image)}" style="width:100%;height:100%;object-fit:cover;display:block;"></div>`;
        }
        html += `<div style="padding:${pad}px;">`;
        if (it.title) html += `<div style="font-weight:bold;font-size:14px;color:#222;margin-bottom:6px;line-height:1.4;">${esc(it.title)}</div>`;
        if (it.text)  html += `<div style="font-size:12px;color:#666;line-height:1.6;">${esc(it.text)}</div>`;
        html += `</div></div>`;
    });
    html += `</div>`;
    return html;
}

function buildAccordion(bData) {
    const a = bData.accordion || {};
    const items = Array.isArray(a.items) ? a.items : [];
    const hc = a.headerColor || '#2c3e50';
    const hb = a.headerBg || '#f7f9fa';
    const bc = a.bodyColor || '#555555';
    const openFirst = a.openFirst ?? true;
    if (items.length === 0) {
        return `<div style="width:100%;height:100%;display:flex;align-items:center;justify-content:center;color:#666;font-size:13px;background:#f1f2f6;border-radius:5px;">アコーディオン（項目未設定）</div>`;
    }
    let html = `<div style="width:100%;height:100%;overflow:hidden;">`;
    items.forEach((it, i) => {
        const open = openFirst && i === 0;
        html += `<div style="border:1px solid #e0e0e0;border-radius:6px;margin-bottom:8px;overflow:hidden;">`;
        html += `<div style="padding:12px 16px;background:${hb};color:${hc};font-weight:bold;font-size:14px;display:flex;justify-content:space-between;align-items:center;"><span>${esc(it.title || '')}</span><span>${open ? '▲' : '▼'}</span></div>`;
        if (open) html += `<div style="padding:12px 16px;color:${bc};font-size:13px;line-height:1.7;">${esc(it.content || '').replace(/\n/g, '<br>')}</div>`;
        html += `</div>`;
    });
    html += `</div>`;
    return html;
}
