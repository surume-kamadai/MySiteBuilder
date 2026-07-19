// ============================================================
// clipboard.js - 選択要素のコピー＆ペースト
// ============================================================
import { selectedNodes } from '../app/state.js';
import { applySelectedNodes, spawnElement, makeTypeCounter } from '../nodes/elements.js';
import { layer } from '../canvas/canvas.js';
import { processNode } from '../nodes/converter.js';
import { saveHistory } from '../history/history.js';

let clipboardData = [];

export function copySelected() {
    if (selectedNodes.length === 0) return;
    clipboardData = selectedNodes.map(node => processNode(node));
}

export function pasteClipboard() {
    if (clipboardData.length === 0) return;
    applySelectedNodes([]);

    // 旧ID → 新ID のマップ（イベントターゲット再マッピング用）
    const idMap = new Map();
    const nextNum = makeTypeCounter(); // タイプごとに連番で払い出す

    function regenerateIds(data, isRoot) {
        const newId = data.type.toLowerCase() + '_' + nextNum(data.type);
        idMap.set(data.id, newId);
        data.id = newId;
        if (isRoot) {
            data.properties.name += ' (コピー)';
            data.transform.x += 20;
            data.transform.y += 20;
        }
        data.children?.forEach(child => regenerateIds(child, false));
    }

    // ID再生成（先に全要素のID対応表を作る）
    const cloned = clipboardData.map(data => JSON.parse(JSON.stringify(data)));
    cloned.forEach(data => regenerateIds(data, true));

    // 各要素の events.target を idMap で更新（コピー先要素同士の参照を保つ）
    function remapEvents(data) {
        const events = data.properties?.events;
        if (Array.isArray(events)) {
            events.forEach(ev => {
                // alert以外（show/hide/toggleなど）はtargetが要素ID
                if (ev.action !== 'alert' && idMap.has(ev.target)) {
                    ev.target = idMap.get(ev.target);
                }
            });
        }
        data.children?.forEach(remapEvents);
    }
    cloned.forEach(remapEvents);

    const newSelection = cloned.map(data => spawnElement(data.type, data, layer, false, true));

    applySelectedNodes(newSelection);
    saveHistory();
}
