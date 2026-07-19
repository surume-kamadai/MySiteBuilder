// ============================================================
// interactions.js - ⚡ インタラクション（イベントトリガー）の管理
// （inspector.js から分離。要素のクリック/ホバーで他要素を表示切替する設定UI）
// ============================================================
import { layer } from '../canvas/canvas.js';
import { selectedNodes } from '../app/state.js';
import { saveHistory } from '../history/history.js';

// ============================================================
// ⚡ インタラクション (イベントトリガー) の管理
// ============================================================

// 現在のキャンバスにある全ての要素から、ターゲット候補(IDと名前)のリストを取得
function getTargetOptions(currentId) {
    const options = [{ id: '', name: '-- ターゲットを選択 --' }];
    layer.getChildren().forEach(n => {
        if (n.hasName('ui-element') && n.id() !== currentId) {
            const bData = n.getAttr('bladeData');
            const type = n.getAttr('uiType');
            options.push({ id: n.id(), name: `${bData.name} (${type})` });
        }
    });
    return options;
}

// イベントリストの再描画
export function renderEventList(node) {
    const listEl = document.getElementById('ins-events-list');
    if (!listEl) return;
    listEl.innerHTML = '';

    const bData = node.getAttr('bladeData');
    const events = bData.events || [];
    const targetOptions = getTargetOptions(node.id());

    events.forEach((ev, index) => {
        const div = document.createElement('div');
        div.style.cssText = 'background: #252526; border: 1px solid #444; padding: 5px; margin-bottom: 5px; border-radius: 3px; position: relative;';

        // 削除ボタン
        const delBtn = document.createElement('button');
        delBtn.innerText = '✕';
        delBtn.style.cssText = 'position: absolute; top: 2px; right: 2px; width: 20px; padding: 0; background: none; border: none; color: #cc4545; font-size: 12px; margin: 0;';
        delBtn.onclick = () => {
            bData.events.splice(index, 1);
            node.setAttr('bladeData', bData);
            renderEventList(node);
            saveHistory();
        };
        div.appendChild(delBtn);

        // トリガー選択 (いつ)
        const row1 = document.createElement('div');
        row1.className = 'form-group'; row1.style.marginBottom = '3px';
        row1.innerHTML = `<label style="margin:0; font-size:10px;">いつ？ (Trigger)</label>
            <select class="ev-trigger" style="padding: 2px;">
                <option value="click" ${ev.trigger === 'click' ? 'selected' : ''}>クリック時 (OnClick)</option>
                <option value="hover" ${ev.trigger === 'hover' ? 'selected' : ''}>マウスホバー時 (OnHover)</option>
            </select>`;
        
        // アクション選択 (どうする)
        const row2 = document.createElement('div');
        row2.className = 'form-group'; row2.style.marginBottom = '3px';
        row2.innerHTML = `<label style="margin:0; font-size:10px;">どうする？ (Action)</label>
            <select class="ev-action" style="padding: 2px;">
                <option value="show" ${ev.action === 'show' ? 'selected' : ''}>表示する (Show)</option>
                <option value="hide" ${ev.action === 'hide' ? 'selected' : ''}>隠す (Hide)</option>
                <option value="toggle" ${ev.action === 'toggle' ? 'selected' : ''}>表示/非表示を切り替え</option>
                <option value="alert" ${ev.action === 'alert' ? 'selected' : ''}>アラートを出す</option>
            </select>`;

        // ターゲット選択 / テキスト入力 (どれを)
        const row3 = document.createElement('div');
        row3.className = 'form-group'; row3.style.marginBottom = '0';
        
        if (ev.action === 'alert') {
            row3.innerHTML = `<label style="margin:0; font-size:10px;">メッセージ (Text)</label>
                <input type="text" class="ev-target" value="${ev.target}" placeholder="アラートの文章" style="padding: 2px;">`;
        } else {
            let optionsHtml = targetOptions.map(opt => 
                `<option value="${opt.id}" ${ev.target === opt.id ? 'selected' : ''}>${opt.name}</option>`
            ).join('');
            row3.innerHTML = `<label style="margin:0; font-size:10px;">対象要素 (Target)</label>
                <select class="ev-target" style="padding: 2px;">${optionsHtml}</select>`;
        }

        div.appendChild(row1);
        div.appendChild(row2);
        div.appendChild(row3);

        // 値が変更されたら保存
        div.querySelectorAll('select, input').forEach(input => {
            input.onchange = () => {
                ev.trigger = div.querySelector('.ev-trigger').value;
                ev.action = div.querySelector('.ev-action').value;
                ev.target = div.querySelector('.ev-target').value;
                
                // アクションが変わった時は対象の入力UIを再描画
                if (input.classList.contains('ev-action')) {
                    ev.target = ''; // リセット
                    renderEventList(node);
                } else {
                    node.setAttr('bladeData', bData);
                }
                saveHistory();
            };
        });

        listEl.appendChild(div);
    });
}

// ボタンから呼び出される「イベント追加」
window.addEventTrigger = () => {
    if (selectedNodes.length !== 1) return;
    const node = selectedNodes[0];
    const bData = node.getAttr('bladeData');
    if (!bData.events) bData.events = [];
    
    // デフォルトのイベントを追加
    bData.events.push({ trigger: 'click', action: 'toggle', target: '' });
    node.setAttr('bladeData', bData);
    
    renderEventList(node);
    saveHistory();
};