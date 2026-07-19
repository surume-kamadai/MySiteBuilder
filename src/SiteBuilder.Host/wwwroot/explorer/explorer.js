// ============================================================
// レイヤーエクスプローラー (D&D対応)
// ボタン構成をページパネルと統一: 👁️(表示) 🔒(ロック) ✎(名前変更) ×(削除)
// ============================================================
import { layer, tr } from '../canvas/canvas.js';
import { selectedNodes, setSelectedNodes, lastClickedNode, setLastClickedNode } from '../app/state.js';
import { applySelectedNodes } from '../nodes/elements.js';
import { updateInspectorFromNode, hideInspector } from '../inspector/inspector.js';
import { saveHistory } from '../history/history.js';

const LAYER_ICONS = { Group: '📁', Image: '🖼️', Label: 'T', Button: '🔘', Rect: '🟦', Circle: '⭕', Triangle: '🔺', Warp: '🔷', TextInput: '📝' };

let draggedNode   = null;
let editingNodeId = null;   // 名前編集中の要素

export function renderExplorer() {
    const list = document.getElementById('layer-list');
    if (!list) return;
    list.innerHTML = '';

    layer.getChildren()
        .filter(c => c.hasName('ui-element'))
        .slice()
        .reverse()
        .forEach(node => createLayerItem(node, list, 0));
}

function createLayerItem(node, container, depth) {
    const bData = node.getAttr('bladeData') || {};
    const icon  = LAYER_ICONS[node.getAttr('uiType')] ?? '📄';

    const div = document.createElement('div');
    div.className        = 'layer-item' + (selectedNodes.includes(node) ? ' active' : '');
    div.style.marginLeft = (depth * 15) + 'px';

    // --- 編集モード（名前変更）---
    if (editingNodeId === node.id()) {
        const input = document.createElement('input');
        input.type      = 'text';
        input.value     = bData.name;
        input.className  = 'layer-rename-input';

        const commit = () => {
            const v = input.value.trim();
            if (v) { bData.name = v; node.setAttr('bladeData', bData); }
            editingNodeId = null;
            renderExplorer();
            updateInspectorFromNode();
            saveHistory();
        };
        const cancel = () => { editingNodeId = null; renderExplorer(); };

        input.onkeydown = e => {
            e.stopPropagation();
            if (e.key === 'Enter')  { e.preventDefault(); commit(); }
            if (e.key === 'Escape') { cancel(); }
        };
        input.onblur  = commit;
        input.onclick = e => e.stopPropagation();

        div.appendChild(input);
        container.appendChild(div);
        setTimeout(() => { input.focus(); input.select(); }, 0);

        // 編集中でもグループの子は表示する
        renderChildren(node, container, depth);
        return;
    }

    // --- 通常モード ---
    const nameSpan = document.createElement('span');
    nameSpan.className = 'layer-name';
    nameSpan.innerText = `${icon} ${bData.name}`;
    div.appendChild(nameSpan);

    // クリックで選択（ロックされていない時のみ）
    div.onclick = e => {
        if (bData.lock) return;
        let sel = [...selectedNodes];
        const ctrlKey = e.ctrlKey || e.metaKey;
        const shiftKey = e.shiftKey;

        if (ctrlKey) {
            // Ctrl+クリック: 個別トグル
            const idx = sel.indexOf(node);
            if (idx >= 0) sel.splice(idx, 1);
            else sel.push(node);
        } else if (shiftKey && lastClickedNode) {
            // Shift+クリック: 表示順で「直前ノード〜今ノード」の間を一括選択
            const visibleNodes = collectVisibleOrder();
            const idxA = visibleNodes.indexOf(lastClickedNode);
            const idxB = visibleNodes.indexOf(node);
            if (idxA >= 0 && idxB >= 0) {
                const [from, to] = idxA < idxB ? [idxA, idxB] : [idxB, idxA];
                const range = visibleNodes.slice(from, to + 1);
                const set = new Set(sel);
                range.forEach(r => set.add(r));
                sel = Array.from(set);
            } else {
                sel = [node];
            }
        } else {
            sel = [node];
        }
        setLastClickedNode(node);
        applySelectedNodes(sel);
    };

    // D&Dで並び替え・グループ移動
    div.draggable   = true;
    div.ondragstart = e => {
        draggedNode = node;
        e.stopPropagation();
        setTimeout(() => div.style.opacity = '0.5', 0);
    };
    div.ondragend = () => {
        draggedNode = null;
        div.style.opacity = '1';
        document.querySelectorAll('.layer-item').forEach(el => el.classList.remove('drag-over', 'drag-over-group'));
    };
    div.ondragover = e => {
        e.preventDefault();
        e.stopPropagation();
        if (!draggedNode || draggedNode === node) return;
        div.classList.add(node.getAttr('uiType') === 'Group' ? 'drag-over-group' : 'drag-over');
    };
    div.ondragleave = () => div.classList.remove('drag-over', 'drag-over-group');
    div.ondrop = e => {
        e.preventDefault();
        e.stopPropagation();
        if (!draggedNode || draggedNode === node) return;

        // 複数選択中で、掴んだ要素が選択に含まれていれば「選択全体」を移動する。
        // そうでなければ単一ノードのみ移動。
        const nodesToMove = (selectedNodes.includes(draggedNode) && selectedNodes.length > 1)
            ? [...selectedNodes]
            : [draggedNode];

        const targetIsGroup = node.getAttr('uiType') === 'Group';
        const destParent    = targetIsGroup ? node : node.parent;

        // エクスプローラー表示順を保つため、元の重なり順でまとめて移動
        nodesToMove.forEach(moving => {
            if (moving === node) return;  // ターゲット自身は除外

            // 祖先（自分自身を含む）への移動は禁止（循環防止）
            let p = destParent, bad = false;
            while (p) { if (p === moving) { bad = true; break; } p = p.parent; }
            if (bad) return;

            const absPos = moving.getAbsolutePosition();
            moving.moveTo(destParent);
            if (!targetIsGroup) moving.setZIndex(node.getZIndex() + 1);
            moving.absolutePosition(absPos);
        });

        tr.moveToTop();
        updateInspectorFromNode();
        renderExplorer();
        layer.batchDraw();
        saveHistory();
    };

    // ▼▼ 【表示・ロックボタン】 ▼▼
    const isVisible = node.visible();
    const isLocked  = bData.lock || false;

    // 👁️ 可視性(表示/非表示)ボタン
    const visBtn = document.createElement('button');
    visBtn.className = 'layer-edit';
    visBtn.innerText = isVisible ? '👁️' : '👁️‍🗨️';
    visBtn.style.opacity = isVisible ? '1' : '0.4';
    visBtn.title = isVisible ? '非表示にする' : '表示する';
    visBtn.onclick = e => {
        e.stopPropagation();
        const nextState = !node.visible();
        node.visible(nextState);
        bData.visible = nextState;
        node.setAttr('bladeData', bData);
        
        // 非表示にする場合は選択を解除
        if (!nextState && selectedNodes.includes(node)) {
            const index = selectedNodes.indexOf(node);
            selectedNodes.splice(index, 1);
            applySelectedNodes([...selectedNodes]);
        }
        
        renderExplorer();
        layer.batchDraw();
        saveHistory();
    };
    div.appendChild(visBtn);

    // 🔒 ロックボタン
    const lockBtn = document.createElement('button');
    lockBtn.className = 'layer-edit';
    lockBtn.innerText = isLocked ? '🔒' : '🔓';
    lockBtn.style.opacity = isLocked ? '1' : '0.4';
    lockBtn.title = isLocked ? 'ロックを解除' : '編集をロック';
    lockBtn.onclick = e => {
        e.stopPropagation();
        const nextLock = !isLocked;
        bData.lock = nextLock;
        node.setAttr('bladeData', bData);
        
        // ロック時はドラッグとクリックイベントを無効化
        node.draggable(!nextLock);
        node.listening(!nextLock);
        
        // ロックする場合は選択を即座に外す
        if (nextLock && selectedNodes.includes(node)) {
            const index = selectedNodes.indexOf(node);
            selectedNodes.splice(index, 1);
            applySelectedNodes([...selectedNodes]);
        }
        
        renderExplorer();
        layer.batchDraw();
        saveHistory();
    };
    div.appendChild(lockBtn);
    // ▲▲ 追加ここまで ▲▲

    // ボタン群: ✎ 編集 / × 削除
    const editBtn = document.createElement('button');
    editBtn.className = 'layer-edit';
    editBtn.innerText = '✎';
    editBtn.title = '名前を変更';
    editBtn.onclick = e => {
        e.stopPropagation();
        editingNodeId = node.id();
        renderExplorer();
    };
    div.appendChild(editBtn);

    const delBtn = document.createElement('button');
    delBtn.className = 'layer-del';
    delBtn.innerText = '×';
    delBtn.title = 'この要素を削除';
    delBtn.onclick = e => {
        e.stopPropagation();
        const wasSelected = selectedNodes.includes(node);
        node.destroy();
        if (wasSelected) {
            setSelectedNodes([]);
            tr.nodes([]);
            hideInspector();
        }
        renderExplorer();
        layer.batchDraw();
        saveHistory();
    };
    div.appendChild(delBtn);

    container.appendChild(div);

    // グループの子要素を再帰表示
    renderChildren(node, container, depth);
}

function renderChildren(node, container, depth) {
    if (node.getAttr('uiType') === 'Group') {
        node.getChildren()
            .filter(c => c.hasName('ui-element'))
            .slice()
            .reverse()
            .forEach(child => createLayerItem(child, container, depth + 1));
    }
}

// エクスプローラーの表示順に従って全ノードをフラットに並べる（Shift範囲選択用）
function collectVisibleOrder() {
    const result = [];
    function walk(node) {
        result.push(node);
        if (node.getAttr('uiType') === 'Group') {
            node.getChildren()
                .filter(c => c.hasName('ui-element'))
                .slice()
                .reverse()
                .forEach(walk);
        }
    }
    layer.getChildren()
        .filter(c => c.hasName('ui-element'))
        .slice()
        .reverse()
        .forEach(walk);
    return result;
}