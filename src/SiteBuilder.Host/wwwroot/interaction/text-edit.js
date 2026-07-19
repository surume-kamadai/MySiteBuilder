// ============================================================
// text-edit.js - Label/Button のダブルクリック インラインテキスト編集
// ============================================================
import { stage, layer } from '../canvas/canvas.js';
import { updateInspectorFromNode } from '../inspector/inspector.js';
import { markMobileEdited, updatePcGeom } from '../canvas/display.js';
import { saveHistory } from '../history/history.js';

export function startInlineTextEdit(node) {
    const bData = node.getAttr('bladeData');
    if (!bData) return;

    // 既存inputがあれば閉じる
    document.querySelector('.inline-text-editor')?.remove();

    // Canvas上の絶対位置を計算
    const stageBox = stage.container().getBoundingClientRect();
    const wrapperBox = document.getElementById('canvas-wrapper').getBoundingClientRect();
    const zoom = stage.scaleX();
    const absPos = node.getAbsolutePosition();
    const w = node.width() * zoom;
    const h = node.height() * zoom;

    const input = document.createElement(node.getAttr('uiType') === 'Label' ? 'textarea' : 'input');
    input.className = 'inline-text-editor';
    input.value = bData.text || '';
    Object.assign(input.style, {
        position: 'fixed',
        left: (stageBox.left + absPos.x * zoom) + 'px',
        top:  (stageBox.top  + absPos.y * zoom) + 'px',
        width:  Math.max(80, w) + 'px',
        height: Math.max(30, h) + 'px',
        fontSize: (bData.fontsize || 16) + 'px',
        fontFamily: bData.fontfamily || 'sans-serif',
        color: bData.color || '#000',
        background: '#fff',
        border: '2px solid #007acc',
        borderRadius: '3px',
        padding: '2px 4px',
        boxSizing: 'border-box',
        zIndex: 99999,
        outline: 'none',
        resize: 'none',
        textAlign: bData.align || 'left',
    });
    document.body.appendChild(input);
    input.focus();
    input.select();

    const commit = () => {
        const v = input.value;
        bData.text = v;
        node.setAttr('bladeData', bData);
        // Konva側の描画も更新
        if (node.getAttr('uiType') === 'Label') {
            node.text(v);
        } else if (node.getAttr('uiType') === 'Button') {
            const txt = node.findOne('.btn-text');
            if (txt) txt.text(v);
        }
        layer.batchDraw();
        // インスペクタも更新
        updateInspectorFromNode();
        // 編集デバイス対応のフック
        updatePcGeom(node);
        markMobileEdited(node);
        cleanup();
        saveHistory();
    };
    const cancel = () => cleanup();
    const cleanup = () => { input.remove(); };

    input.addEventListener('blur', commit);
    input.addEventListener('keydown', ev => {
        ev.stopPropagation();
        if (ev.key === 'Enter' && !ev.shiftKey) {
            ev.preventDefault();
            commit();
        } else if (ev.key === 'Escape') {
            ev.preventDefault();
            cancel();
        }
    });
}
