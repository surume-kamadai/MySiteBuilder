// ============================================================
// accordion-editor.js - アコーディオン項目の編集モーダル
// （inspector.js から分離）
// ============================================================
import { selectedNodes } from '../app/state.js';
import { saveHistory } from '../history/history.js';

// ============================================================
// アコーディオン項目編集モーダル
// ============================================================
let accordionTargetNode = null;

function getAccordionItems(node) {
    const bData = node.getAttr('bladeData');
    if (!bData.accordion) bData.accordion = {};
    if (!Array.isArray(bData.accordion.items)) bData.accordion.items = [];
    return bData.accordion.items;
}

function renderAccordionEditor(node) {
    const list = document.getElementById('accordion-editor-list');
    if (!list) return;
    list.innerHTML = '';
    const items = getAccordionItems(node);

    items.forEach((it, idx) => {
        const card = document.createElement('div');
        card.style.cssText = 'padding:10px; background:#2d2d2d; border:1px solid #444; border-radius:5px; margin-bottom:8px;';

        const head = document.createElement('div');
        head.style.cssText = 'display:flex; gap:6px; align-items:center; margin-bottom:6px;';
        const num = document.createElement('span');
        num.innerText = `項目 ${idx + 1}`;
        num.style.cssText = 'font-size:11px; color:#aaa; flex:1;';

        const upBtn = document.createElement('button');
        upBtn.innerText = '↑'; upBtn.style.cssText = 'width:26px; padding:2px; background:#444; border:none; color:#fff; margin:0; font-size:11px;';
        upBtn.onclick = () => { if (idx===0) return; [items[idx-1],items[idx]]=[items[idx],items[idx-1]]; node.setAttr('bladeData', node.getAttr('bladeData')); renderAccordionEditor(node); saveHistory(); };
        const downBtn = document.createElement('button');
        downBtn.innerText = '↓'; downBtn.style.cssText = 'width:26px; padding:2px; background:#444; border:none; color:#fff; margin:0; font-size:11px;';
        downBtn.onclick = () => { if (idx===items.length-1) return; [items[idx+1],items[idx]]=[items[idx],items[idx+1]]; node.setAttr('bladeData', node.getAttr('bladeData')); renderAccordionEditor(node); saveHistory(); };
        const delBtn = document.createElement('button');
        delBtn.innerText = '✕'; delBtn.style.cssText = 'width:26px; padding:2px; background:#cc4545; border:none; color:#fff; margin:0; font-size:11px;';
        delBtn.onclick = () => { items.splice(idx,1); node.setAttr('bladeData', node.getAttr('bladeData')); renderAccordionEditor(node); saveHistory(); };

        head.appendChild(num); head.appendChild(upBtn); head.appendChild(downBtn); head.appendChild(delBtn);
        card.appendChild(head);

        const titleIn = document.createElement('input');
        titleIn.type = 'text';
        titleIn.placeholder = '質問・見出し';
        titleIn.value = it.title || '';
        titleIn.style.cssText = 'width:100%; padding:6px; background:#1e1e1e; color:#fff; border:1px solid #555; border-radius:3px; font-size:12px; margin-bottom:5px; box-sizing:border-box;';
        titleIn.oninput = () => { it.title = titleIn.value; node.setAttr('bladeData', node.getAttr('bladeData')); };
        titleIn.onchange = () => saveHistory();
        card.appendChild(titleIn);

        const contentIn = document.createElement('textarea');
        contentIn.placeholder = '回答・本文';
        contentIn.value = it.content || '';
        contentIn.rows = 3;
        contentIn.style.cssText = 'width:100%; padding:6px; background:#1e1e1e; color:#fff; border:1px solid #555; border-radius:3px; font-size:12px; resize:vertical; box-sizing:border-box;';
        contentIn.oninput = () => { it.content = contentIn.value; node.setAttr('bladeData', node.getAttr('bladeData')); };
        contentIn.onchange = () => saveHistory();
        card.appendChild(contentIn);

        list.appendChild(card);
    });

    if (items.length === 0) {
        const empty = document.createElement('div');
        empty.style.cssText = 'color:#888; font-size:12px; padding:20px; text-align:center;';
        empty.innerText = '項目がありません。下の「＋ 項目を追加」で追加してください。';
        list.appendChild(empty);
    }
}

window.openAccordionEditor = () => {
    if (selectedNodes.length !== 1) return;
    const node = selectedNodes[0];
    if (node.getAttr('uiType') !== 'Accordion') return;
    accordionTargetNode = node;
    document.getElementById('accordion-editor-overlay').style.display = 'flex';
    renderAccordionEditor(node);
};

window.closeAccordionEditor = () => {
    document.getElementById('accordion-editor-overlay').style.display = 'none';
    if (accordionTargetNode) {
        const placeholder = accordionTargetNode.findOne('.accordion-placeholder');
        if (placeholder) {
            const count = (accordionTargetNode.getAttr('bladeData')?.accordion?.items || []).length;
            placeholder.text(`🪗 アコーディオン\n(${count} 項目)\n📋「項目一覧を編集」で詳細設定`);
            accordionTargetNode.getLayer()?.batchDraw();
        }
    }
    accordionTargetNode = null;
};

window.addAccordionItem = () => {
    if (!accordionTargetNode) return;
    const items = getAccordionItems(accordionTargetNode);
    items.push({ title: '新しい質問', content: '回答を入力してください。' });
    accordionTargetNode.setAttr('bladeData', accordionTargetNode.getAttr('bladeData'));
    renderAccordionEditor(accordionTargetNode);
    saveHistory();
};
