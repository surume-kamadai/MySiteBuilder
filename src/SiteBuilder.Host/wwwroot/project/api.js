// src/renderer/project/api.js
import { showToast } from '../ui/toast.js';
import { saveHistory, clearPageHistory } from '../history/history.js';
import { updateCanvasSize } from '../canvas/display.js';
import { renderExplorer } from '../explorer/explorer.js';
import { serializeProject, loadProject, getProject, getSettings, saveCurrentPage } from './project.js';
import { renderPages } from '../explorer/pages-ui.js';
import { buildStaticProject, buildLaravelProject } from '../export/exporter.js';

// 保存先のパスを記憶する変数
export let currentSavePath = null;
let autoSaveTimer = null;

// ============================================================
// 統合保存（プロジェクトJSON ＋ サイト出力 を一緒に行う）
// ============================================================
export async function saveAndExport() {
    saveCurrentPage();
    const project = getProject();
    const type = project.settings.outputType || 'static';

    // HTMLやBladeのファイルを生成
    const built = type === 'laravel'
        ? buildLaravelProject(project)
        : buildStaticProject(project);

    //プロジェクト自体(JSON)も、書き出しファイルリストに混ぜる
    built.files.push({
        path: 'project.json',
        content: serializeProject()
    });

    // 既に保存先が決まっていれば、上書きパスとして指定する
    if (currentSavePath) {
        built.targetDir = currentSavePath;
    }

    try {
        const result = await window.electronAPI.exportProject(built);
        if (result?.success) {
            currentSavePath = result.path; // パスを記憶
            showToast(`保存完了: ${result.path}`);
        } else if (result?.message !== 'キャンセルされました') {
            showToast('保存に失敗しました: ' + (result?.message ?? ''), null, true);
        }
    } catch {
        showToast('保存中にエラーが発生しました。', null, true);
    }
}

// ============================================================
// オートセーブ機能 (1分ごとに実行)
// ============================================================
export function startAutoSave() {
    if (autoSaveTimer) clearInterval(autoSaveTimer);
    
    autoSaveTimer = setInterval(() => {
        if (currentSavePath) {
            // 保存先が決まっていれば、バックグラウンドで上書き保存
            saveAndExport();
            console.log('オートセーブを実行しました:', currentSavePath);
        } else {
            // 未保存の場合は localStorage に一時バックアップ
            localStorage.setItem('site-builder-backup', serializeProject());
            console.log('ローカルストレージへ一時バックアップしました');
        }
    }, 60000); // 60,000ミリ秒 = 1分
}

// ============================================================
// プロジェクト読込
// ============================================================
export async function importJSON() {
    try {
        const result = await window.electronAPI.loadScene();
        if (!result || !result.content) return;

        loadProject(result.content);
        clearPageHistory();  // 旧プロジェクトの履歴をリセット
        currentSavePath = result.dirPath; // 読み込んだフォルダを上書き対象として記憶

        const s = getSettings();
        document.getElementById('canvas-width').value  = s.canvas.width;
        document.getElementById('canvas-height').value = s.canvas.height;
        const pn = document.getElementById('project-name');
        if (pn) pn.value = s.projectName;
        updateCanvasSize();

        renderPages();
        renderExplorer();
        saveHistory();
        showToast('プロジェクトを読み込みました。');
    } catch {
        showToast('読み込みに失敗しました（無効なファイル）。', null, true);
    }
}

// --- 画像取り込み ---
export async function uploadImage(file) {
    if (file instanceof File) {
        return await new Promise((resolve) => {
            const reader = new FileReader();
            reader.onload  = e => resolve(e.target.result);
            reader.onerror = () => { showToast('画像の読み込みに失敗しました。', null, true); resolve(null); };
            reader.readAsDataURL(file);
        });
    }
    try {
        const result = await window.electronAPI.pickImage();
        return result?.dataUrl ?? null;
    } catch {
        showToast('画像の選択に失敗しました。', null, true);
        return null;
    }
}