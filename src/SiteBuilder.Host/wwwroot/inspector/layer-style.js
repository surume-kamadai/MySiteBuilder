// ============================================================
// layer-style.js - レイヤースタイル・フローティングダイアログ
//
// 右クリック→「レイヤースタイル…」で開く、Photoshop風の浮遊パネル。
// 既存のレイヤースタイル系(ドロップシャドウ/境界線/グラデーション)をインスペクターから
// このダイアログへ移設(集約)し、さらに新規4種(グラデ文字/内側シャドウ/光彩/ベベル)を束ねる。
// フィールドの値の読み書きは従来どおり inspector.js の onInspectorUpdate /
// updateInspectorFromNode が id 経由で行うため、DOMの置き場所が変わっても動作は不変。
// ============================================================
import { selectedNodes } from '../app/state.js';
import { updateInspectorFromNode } from './inspector.js';

let dialog, header, content, empty;

export function initLayerStyleDialog() {
    dialog = document.getElementById('layer-style-dialog');
    if (!dialog) return;
    header  = document.getElementById('ls-header');
    content = document.getElementById('ls-content');
    empty   = document.getElementById('ls-empty');
    const slot = document.getElementById('ls-relocate-slot');

    // 既存のレイヤースタイル系グループをダイアログ先頭へ移設（インスペクターから集約）
    ['group-dropshadow', 'group-stroke', 'group-gradient'].forEach(id => {
        const g = document.getElementById(id);
        if (g && slot) { g.classList.add('ls-section'); slot.appendChild(g); }
    });

    // 閉じるボタン
    document.getElementById('ls-close')?.addEventListener('click', () => {
        dialog.style.display = 'none';
    });

    // ヘッダーをドラッグしてダイアログを移動
    let dragging = false, startX = 0, startY = 0, originX = 0, originY = 0;
    header?.addEventListener('mousedown', e => {
        if (e.target.closest('#ls-close')) return;
        dragging = true;
        const r = dialog.getBoundingClientRect();
        originX = r.left; originY = r.top;
        startX = e.clientX; startY = e.clientY;
        e.preventDefault();
    });
    window.addEventListener('mousemove', e => {
        if (!dragging) return;
        let nx = originX + (e.clientX - startX);
        let ny = originY + (e.clientY - startY);
        nx = Math.max(0, Math.min(window.innerWidth  - 60, nx));
        ny = Math.max(0, Math.min(window.innerHeight - 30, ny));
        dialog.style.left = nx + 'px';
        dialog.style.top  = ny + 'px';
    });
    window.addEventListener('mouseup', () => { dragging = false; });

    // グローバル公開（右クリックメニュー / インスペクター側から呼ぶ）
    window.openLayerStyleDialog  = openLayerStyleDialog;
    window.__syncLayerStyleDialog = syncLayerStyleDialog;
}

// ダイアログを表示し、現在の選択でフィールドを埋める
function openLayerStyleDialog() {
    if (!dialog) return;
    if (!dialog.style.left) {   // 初回はキャンバス右上寄りに配置
        dialog.style.left = Math.max(20, window.innerWidth - 340) + 'px';
        dialog.style.top  = '90px';
    }
    dialog.style.display = 'flex';
    updateInspectorFromNode();   // 各フィールドを選択ノードの値で更新（idベース）
    syncLayerStyleDialog();
}

// 選択が単一要素かどうかで、本体 / 「要素を選択」案内 を切り替える
function syncLayerStyleDialog() {
    if (!dialog || dialog.style.display === 'none') return;
    const single = selectedNodes.length === 1;
    if (content) content.style.display = single ? 'block' : 'none';
    if (empty)   empty.style.display   = single ? 'none'  : 'block';
}
