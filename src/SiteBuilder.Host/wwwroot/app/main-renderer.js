// ============================================================
// main-renderer.js - エディタ画面のエントリーポイント（マルチページ版）
// main-renderer.js - entry point of the editor UI (multi-page build).
// ============================================================
import { spawnElement, groupNodes, ungroupNodes, createEmptyFolder } from '../nodes/elements.js';
import { spawnComponent } from '../nodes/components.js';
import { onInspectorUpdate, deleteSelectedNode, alignNodes, distributeNodes } from '../inspector/inspector.js';
import { saveAndExport, importJSON, startAutoSave } from '../project/api.js';
import { renderExplorer } from '../explorer/explorer.js';
import { saveHistory, clearPageHistory, undo, redo } from '../history/history.js';
import { enterWarpMode, exitWarpMode } from '../nodes/warp.js';
import { selectedNodes } from './state.js';
import { switchDevice } from '../canvas/display.js';
import { playPreview, stopPreview } from '../preview/preview.js';


// マルチページ・設定 / Multi-page and settings modules
import { renderPages, onAddPage, onAddFolder } from '../explorer/pages-ui.js';
import { newProject, initProject, getPages, getFolders } from '../project/project.js';
import { initDockLayout, showPanel, hidePanel } from '../ui/dock-layout.js';
import { initCanvasPreview } from '../canvas/canvas-preview.js';
import { initOffscreenIndicators } from '../canvas/offscreen.js';
import { initRulers, toggleRulers } from '../canvas/rulers.js';
import { initGradientOverlay } from '../canvas/gradient-overlay.js';
import { initEffectOverlay } from '../canvas/effect-overlay.js';
import { initColorPickers } from '../inspector/color-picker.js';
import { initLayerStyleDialog } from '../inspector/layer-style.js';
import {
    onProjectNameChange, onOutputTypeChange, updateSeparateCss,
    onCanvasSizeChange, initSettingsUI,
    updateSiteBgColor, updatePageBgColor, clearPageBgColor, syncBgColorUI,
    updateSiteSeo, updatePageSeo,
} from '../ui/settings-ui.js';

// events.js は副作用としてイベントリスナーを登録する
// events.js registers its event listeners as a side effect of importing.
import '../interaction/events.js';
// スライダー/アコーディオン編集モーダルは window.* 関数を副作用として登録する
// The slider/accordion editor modals register their window.* functions on import.
import '../inspector/slider-editor.js';
import '../inspector/accordion-editor.js';

// インラインの onclick から呼べるよう、各機能を window.* に公開する。
// Expose each feature on window.* so the inline onclick handlers can call it.

// --- 要素操作 / Element operations ---
window.spawnElement       = spawnElement;
window.groupNodes         = groupNodes;
window.ungroupNodes       = ungroupNodes;
window.onInspectorUpdate  = onInspectorUpdate;
window.deleteSelectedNode = deleteSelectedNode;
window.renderExplorer     = renderExplorer;
window.spawnComponent     = spawnComponent;

// --- キャンバス設定（プロジェクトに同期する版に差し替え）---
// --- Canvas controls (swapped for versions that sync to the project) ---
window.switchDevice = switchDevice;
window.playPreview  = playPreview;
window.stopPreview  = stopPreview;
window.toggleRulers = toggleRulers;
window.updateCanvasSize   = onCanvasSizeChange;
window.alignNodes         = alignNodes;
window.distributeNodes    = distributeNodes;

// --- プロジェクト設定 / Project settings ---
window.onProjectNameChange = onProjectNameChange;
window.onOutputTypeChange  = onOutputTypeChange;
window.updateSeparateCss   = updateSeparateCss;
window.updateSiteBgColor   = updateSiteBgColor;
window.updatePageBgColor   = updatePageBgColor;
window.clearPageBgColor    = clearPageBgColor;
window.__syncBgColorUI     = syncBgColorUI;
window.updateSiteSeo       = updateSiteSeo;
window.updatePageSeo       = updatePageSeo;

// --- 履歴（元に戻す / やり直し）/ History (undo / redo) ---
window.undo = undo;
window.redo = redo;


// --- ページ操作 / Page operations ---
window.addPage = onAddPage;
window.addPageFolder = onAddFolder;

// --- エクスプローラー: 空フォルダ追加 / Explorer: add an empty folder ---
window.createEmptyFolder = createEmptyFolder;

// --- プロジェクト保存・読込 / Save and load the project ---
window.saveAndExport = saveAndExport;
window.importJSON = importJSON;

window.newProject = () => {
    if (confirm('新規プロジェクトを作成しますか？（未保存の変更は失われます）')) {
        clearPageHistory();  // 全ページの履歴をリセット / reset every page's history
        newProject();
        initSettingsUI();
        renderPages();
        renderExplorer();
        saveHistory();
    }
};

// --- 出力 / Export ---
// （saveAndExport が統合保存と出力を兼ねるので、ここはそれを使う）
// (saveAndExport does both the unified save and the export, so reuse it here.)
window.exportStatic  = () => saveAndExport();
window.exportLaravel = () => saveAndExport();

// --- 台形変形（パースペクティブ）モード切替 / Toggle warp (perspective) mode ---
window.toggleWarpMode = () => {
    if (selectedNodes.length === 1) {
        enterWarpMode(selectedNodes[0]);
    } else {
        exitWarpMode();
    }
};

// --- 循環参照解消: project.js に spawnElement と renderExplorer を登録 ---
// --- Break a circular import: inject spawnElement and renderExplorer into project.js ---
initProject(spawnElement, renderExplorer);

// --- スライダー編集モーダル用: ページ一覧（path と名前）を返す ---
// --- For the slider editor modal: return the page list (path and name) ---
window.__getProjectPagesForSlider = () => {
    const folders = getFolders();
    const folderMap = new Map(folders.map(f => [f.id, f.name]));
    return getPages().map(p => {
        const fname = folderMap.get(p.folderId);
        const path = fname ? `${fname}/${p.name}.html` : `${p.name}.html`;
        return { name: path, path };
    });
};

// --- 初期化 / Initialization ---
initSettingsUI();
renderPages();

// --- ドッキングレイアウト初期化（パネルをペインに移植）---
// --- Init the docking layout (moves the panels into golden-layout panes) ---
// golden-layout がDOMを移動するので、他の初期化の後に実行する
// golden-layout relocates DOM nodes, so run this after the other init steps.
initDockLayout(() => {
    // ペインのリサイズ時: golden-layoutのレイアウト計算だけ更新（ステージは固定サイズ）
    // On pane resize: only golden-layout recomputes; the Konva stage stays fixed-size.
    // 必要ならここでステージ位置の微調整も可能 / Fine-tune the stage position here if needed.
});

// --- ネイティブメニュー（main.js）からの「表示」パネルトグルを受ける ---
// --- Receive "View" menu panel toggles coming from the native menu (main.js) ---
if (window.electronAPI?.onTogglePanel) {
    window.electronAPI.onTogglePanel(({ id, show }) => {
        if (show) showPanel(id);
        else hidePanel(id);
    });
}
// ファイル/編集メニューのアクション / File and Edit menu actions
if (window.electronAPI?.onMenuAction) {
    window.electronAPI.onMenuAction((action) => {
        switch (action) {
            case 'new-project':  window.newProject?.();   break;
            case 'open-project': window.importJSON?.();   break;
            case 'save-export':  window.exportStatic?.();  break;
            case 'undo':         window.undo?.();          break;
            case 'redo':         window.redo?.();          break;
            case 'reset-layout': window.location.reload(); break;
        }
    });
}

// --- テンプレート編集モーダルのボタンを直接配線 ---
// --- Wire up the template-editor modal buttons directly ---
// （golden-layout のペイン移植後も onclick が確実に効くよう、addEventListener で繋ぐ）
// (Use addEventListener so clicks still fire after golden-layout relocates the panes.)
function wireEditorButtons() {
    const wire = (btnId, fnName) => {
        const btn = document.getElementById(btnId);
        if (btn) {
            btn.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
                if (typeof window[fnName] === 'function') window[fnName]();
            });
        }
    };
    wire('btn-open-slider-editor',    'openSliderEditor');
    wire('btn-open-grid-editor',      'openGridEditor');
    wire('btn-open-accordion-editor', 'openAccordionEditor');
}
// golden-layoutの移植が終わったタイミングで配線（少し遅延）
// Wire things up once golden-layout has finished relocating panes (slight delay).
setTimeout(() => {
    wireEditorButtons();
    initCanvasPreview();        // Slider/Grid/Accordion の実物プレビュー層を起動 / live preview layer for Slider/Grid/Accordion
    initOffscreenIndicators();  // 画面外要素の位置マーカー層を起動 / markers for off-screen elements
    initRulers();               // 定規＆ガイド層を起動 / rulers & guides layer
    initGradientOverlay();      // 画像グラデーションのオーバーレイ層を起動 / image-gradient overlay layer
    initEffectOverlay();        // 内側シャドウ/ベベルのプレビュー層を起動 / inner-shadow / bevel preview layer
    initColorPickers();         // アルファ対応(RGBA)カラーピッカーを配線 / wire up the alpha-capable (RGBA) color pickers
    initLayerStyleDialog();     // レイヤースタイル・ダイアログ / layer-style floating dialog
    // モーダルを確実に body 直下へ移動（隠しコンテナ等に巻き込まれない保険）
    // Make sure the modals sit directly under <body> (so a hidden container can't trap them).
    ['slider-editor-overlay', 'accordion-editor-overlay', 'preview-overlay', 'layer-style-dialog', 'context-menu'].forEach(id => {
        const el = document.getElementById(id);
        if (el && el.parentElement !== document.body) {
            document.body.appendChild(el);
        }
    });
}, 500);

setTimeout(() => saveHistory(), 100);
startAutoSave(); // ★ ここでオートセーブを開始する / start auto-save here