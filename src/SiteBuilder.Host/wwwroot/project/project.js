// ============================================================
// project.js - プロジェクト（複数ページ）の管理
//
// 1プロジェクト = 設定 + 複数ページ。
// 各ページは elements の配列を持つ。
// ページ切替時は、現在のキャンバスを保存→別ページを読込する。
// ============================================================
import { layer, tr } from '../canvas/canvas.js';
import { setSelectedNodes } from '../app/state.js';
import { generateSceneData } from '../nodes/converter.js';
import { hideInspector } from '../inspector/inspector.js';

// 循環参照を避けるため、spawnElement と renderExplorer は
// 外部から init() で登録する方式にする
let _spawnElement   = () => {};
let _renderExplorer = () => {};

export function initProject(spawnElementFn, renderExplorerFn) {
    _spawnElement   = spawnElementFn;
    _renderExplorer = renderExplorerFn;
}

// --- プロジェクト状態 ---
let project = createEmptyProject();

function createEmptyProject() {
    return {
        settings: {
            projectName: 'my-site',
            canvas: { width: 800, height: 600, mobileWidth: 375, mobileHeight: 800 },
            outputType: 'static',   // 'static' | 'laravel'
            separateCss: true,      // true: CSSを css/ フォルダへ分離出力（既定でON）
            siteBgColor: '#f1f2f6', // サイト全体のデフォルト背景色
            // サイト共通のSEO初期値（各ページが未指定ならこれを使う）
            seo: { siteName: '', lang: 'ja', description: '', ogImage: '' },
        },
        folders: [],                // [{ id, name }] ページ分類用フォルダ
        pages: [
            { id: 'page_1', name: 'index', elements: [], folderId: null, bgColor: '',
              seo: { title: '', description: '', ogImage: '' } },
        ],
        activePageId: 'page_1',
    };
}

let pageCounter   = 1;
let folderCounter = 0;

// --- 取得系 ---

export function getProject()      { return project; }
export function getSettings()     { return project.settings; }
export function getPages()        { return project.pages; }
export function getActivePageId() { return project.activePageId; }
export function getActivePage()   { return project.pages.find(p => p.id === project.activePageId); }

// --- 現在のキャンバス内容をアクティブページに保存 ---

export function saveCurrentPage() {
    const page = getActivePage();
    if (!page) return;
    page.elements = generateSceneData(false).elements;
}

// --- キャンバスを指定ページの内容で再描画 ---

function loadPageToCanvas(page) {
    layer.find('.ui-element').forEach(n => n.destroy());
    setSelectedNodes([]);
    tr.nodes([]);
    hideInspector();

    (page.elements || []).forEach(el => _spawnElement(el.type, el, layer, true));
    layer.batchDraw();
    _renderExplorer();
}

// --- ページ切替 ---

export function switchPage(pageId) {
    if (pageId === project.activePageId) return;
    saveCurrentPage();                       // 今のページを保存
    project.activePageId = pageId;
    const page = getActivePage();
    if (page) loadPageToCanvas(page);        // 新しいページを描画
}

// --- ページ追加 ---

export function addPage(name) {
    saveCurrentPage();
    pageCounter++;
    const id = 'page_' + pageCounter;
    const pageName = name || ('page' + pageCounter);
    project.pages.push({ id, name: pageName, elements: [], folderId: null, bgColor: '',
                         seo: { title: '', description: '', ogImage: '' } });
    project.activePageId = id;
    loadPageToCanvas(getActivePage());
    return id;
}

// --- ページ削除 ---

export function deletePage(pageId) {
    if (project.pages.length <= 1) return false;   // 最低1ページは残す
    const idx = project.pages.findIndex(p => p.id === pageId);
    if (idx < 0) return false;

    project.pages.splice(idx, 1);

    // 削除したのがアクティブページなら、隣のページに移る
    if (project.activePageId === pageId) {
        const next = project.pages[Math.max(0, idx - 1)];
        project.activePageId = next.id;
        loadPageToCanvas(next);
    }
    return true;
}

// --- ページ名変更（ファイル名になる）---

export function renamePage(pageId, newName) {
    const page = project.pages.find(p => p.id === pageId);
    if (!page) return;
    // ファイル名に使えない文字を除去
    page.name = String(newName).trim().replace(/[^a-zA-Z0-9_\-]/g, '') || page.name;
}

// --- ページ並び替え（D&D用）---
// draggedId を targetId の位置へ移動する

export function reorderPage(draggedId, targetId) {
    if (draggedId === targetId) return;
    const from = project.pages.findIndex(p => p.id === draggedId);
    const to   = project.pages.findIndex(p => p.id === targetId);
    if (from < 0 || to < 0) return;

    const [moved] = project.pages.splice(from, 1);
    project.pages.splice(to, 0, moved);
}

// --- ページフォルダ（分類用）---

export function getFolders() { return project.folders || (project.folders = []); }

// 空フォルダを追加
export function addFolder() {
    folderCounter++;
    const id = 'folder_' + folderCounter;
    getFolders().push({ id, name: 'folder' + folderCounter });
    return id;
}

// フォルダ名変更（出力サブフォルダ名になる）
export function renameFolder(folderId, newName) {
    const f = getFolders().find(x => x.id === folderId);
    if (!f) return;
    f.name = String(newName).trim().replace(/[^a-zA-Z0-9_\-]/g, '') || f.name;
}

// フォルダ削除（中のページはフォルダ外＝直下に戻す）
export function deleteFolder(folderId) {
    project.folders = getFolders().filter(x => x.id !== folderId);
    project.pages.forEach(p => { if (p.folderId === folderId) p.folderId = null; });
}

// ページをフォルダへ入れる / 出す（folderId に null で直下）
export function setPageFolder(pageId, folderId) {
    const page = project.pages.find(p => p.id === pageId);
    if (page) page.folderId = folderId;
}

// --- 設定更新 ---

export function updateSettings(partial) {
    Object.assign(project.settings, partial);
}

export function updateCanvasSetting(width, height, mobileWidth, mobileHeight) {
    project.settings.canvas.width  = width;
    project.settings.canvas.height = height;
    if (mobileWidth  !== undefined) project.settings.canvas.mobileWidth  = mobileWidth;
    if (mobileHeight !== undefined) project.settings.canvas.mobileHeight = mobileHeight;
}

// --- プロジェクト全体のシリアライズ（保存用）---

export function serializeProject() {
    saveCurrentPage();   // 保存前に現在ページを反映
    return JSON.stringify(project, null, 2);
}

// --- プロジェクト読込（ロード用）---

export function loadProject(jsonStr) {
    const data = JSON.parse(jsonStr);

    // 後方互換: 旧形式（pagesが無く elements 直下）を1ページに変換
    if (!data.pages && data.elements) {
        project = {
            settings: {
                projectName: 'my-site',
                canvas: data.canvas || { width: 800, height: 600 },
                outputType: 'static',
            },
            pages: [{ id: 'page_1', name: 'index', elements: data.elements }],
            activePageId: 'page_1',
        };
    } else {
        project = data;
    }

    // 後方互換: folders が無い旧データを補う
    if (!project.folders) project.folders = [];
    project.pages.forEach(p => { if (p.folderId === undefined) p.folderId = null; });

    // 後方互換: スマホ用キャンバスサイズが無ければ補う
    if (project.settings?.canvas) {
        if (project.settings.canvas.mobileWidth === undefined)  project.settings.canvas.mobileWidth = 375;
        if (project.settings.canvas.mobileHeight === undefined) project.settings.canvas.mobileHeight = 800;
    }

    // 後方互換: 背景色設定が無ければ補う
    if (project.settings && project.settings.siteBgColor === undefined) {
        project.settings.siteBgColor = '#f1f2f6';
    }

    // CSS分離は常時ONが既定。旧プロジェクト(設定なし)を開いた場合もONで補完する
    if (project.settings && project.settings.separateCss === undefined) {
        project.settings.separateCss = true;
    }
    (project.pages || []).forEach(p => {
        if (p.bgColor === undefined) p.bgColor = '';
    });

    // 後方互換: SEO設定が無ければ補う
    if (project.settings && !project.settings.seo) {
        project.settings.seo = { siteName: '', lang: 'ja', description: '', ogImage: '' };
    }
    (project.pages || []).forEach(p => {
        if (!p.seo) p.seo = { title: '', description: '', ogImage: '' };
    });

    // 方針A移行: 古い不正な layouts/mobileEdited を一掃する。
    // transform を正とし、mobile編集は今後やり直してもらう。
    const cleanLayouts = (els) => {
        (els || []).forEach(el => {
            if (el.properties) {
                delete el.properties.layouts;
                delete el.properties.mobileEdited;
            }
            if (el.children) cleanLayouts(el.children);
        });
    };
    project.pages.forEach(p => cleanLayouts(p.elements));

    // pageCounter を既存IDの最大値に合わせる
    pageCounter = project.pages.reduce((max, p) => {
        const n = parseInt(String(p.id).replace('page_', '')) || 0;
        return Math.max(max, n);
    }, 0);

    // folderCounter も復元
    folderCounter = project.folders.reduce((max, f) => {
        const n = parseInt(String(f.id).replace('folder_', '')) || 0;
        return Math.max(max, n);
    }, 0);

    loadPageToCanvas(getActivePage());
}

// --- 新規プロジェクト ---

export function newProject() {
    project = createEmptyProject();
    pageCounter   = 1;
    folderCounter = 0;
    loadPageToCanvas(getActivePage());
}