// ============================================================
// pages-ui.js - ページパネル（フォルダ対応）
// フォルダ追加 / ページをフォルダにD&Dで出し入れ / 名前変更・削除
// ============================================================
import {
    getPages, getActivePageId, switchPage, addPage,
    deletePage, renamePage, reorderPage,
    getFolders, addFolder, renameFolder, deleteFolder, setPageFolder,
} from '../project/project.js';
import { showToast } from '../ui/toast.js';

let editingPageId   = null;
let editingFolderId = null;
let draggedPageId   = null;

export function renderPages() {
    const list = document.getElementById('page-list');
    if (!list) return;
    list.innerHTML = '';

    const folders = getFolders();

    // フォルダごとに描画
    for (const folder of folders) {
        list.appendChild(renderFolderRow(folder));
        // フォルダ内のページ
        getPages().filter(p => p.folderId === folder.id)
                  .forEach(page => list.appendChild(renderPageRow(page, true)));
    }

    // フォルダに属さない（直下の）ページ
    getPages().filter(p => !p.folderId)
              .forEach(page => list.appendChild(renderPageRow(page, false)));
}

// --- フォルダ行 ---
function renderFolderRow(folder) {
    const div = document.createElement('div');
    div.className = 'folder-item';

    if (editingFolderId === folder.id) {
        const input = document.createElement('input');
        input.type = 'text';
        input.value = folder.name;
        input.className = 'page-rename-input';
        const commit = () => { renameFolder(folder.id, input.value); editingFolderId = null; renderPages(); };
        input.onkeydown = e => {
            e.stopPropagation();
            if (e.key === 'Enter') { e.preventDefault(); commit(); }
            if (e.key === 'Escape') { editingFolderId = null; renderPages(); }
        };
        input.onblur = commit;
        div.appendChild(input);
        setTimeout(() => { input.focus(); input.select(); }, 0);
        return div;
    }

    const name = document.createElement('span');
    name.className = 'folder-name';
    name.innerText = '📁 ' + folder.name;
    div.appendChild(name);

    // フォルダへページをドロップで入れる
    div.ondragover = e => { e.preventDefault(); if (draggedPageId) div.classList.add('drag-over-group'); };
    div.ondragleave = () => div.classList.remove('drag-over-group');
    div.ondrop = e => {
        e.preventDefault();
        div.classList.remove('drag-over-group');
        if (draggedPageId) { setPageFolder(draggedPageId, folder.id); renderPages(); }
    };

    const editBtn = document.createElement('button');
    editBtn.className = 'page-edit';
    editBtn.innerText = '✎';
    editBtn.title = 'フォルダ名を変更';
    editBtn.onclick = e => { e.stopPropagation(); editingFolderId = folder.id; renderPages(); };
    div.appendChild(editBtn);

    const delBtn = document.createElement('button');
    delBtn.className = 'page-del';
    delBtn.innerText = '×';
    delBtn.title = 'フォルダを削除（中のページは直下に戻る）';
    delBtn.onclick = e => {
        e.stopPropagation();
        if (confirm(`フォルダ「${folder.name}」を削除しますか？（中のページは残ります）`)) {
            deleteFolder(folder.id);
            renderPages();
        }
    };
    div.appendChild(delBtn);

    return div;
}

// --- ページ行 ---
function renderPageRow(page, indented) {
    const div = document.createElement('div');
    div.className = 'page-item' + (page.id === getActivePageId() ? ' active' : '');
    if (indented) div.style.marginLeft = '16px';

    if (editingPageId === page.id) {
        const input = document.createElement('input');
        input.type = 'text';
        input.value = page.name;
        input.className = 'page-rename-input';
        const commit = () => { renamePage(page.id, input.value); editingPageId = null; renderPages(); };
        input.onkeydown = e => {
            e.stopPropagation();
            if (e.key === 'Enter') { e.preventDefault(); commit(); }
            if (e.key === 'Escape') { editingPageId = null; renderPages(); }
        };
        input.onblur = commit;
        input.onclick = e => e.stopPropagation();
        div.appendChild(input);
        setTimeout(() => { input.focus(); input.select(); }, 0);
        return div;
    }

    const nameSpan = document.createElement('span');
    nameSpan.className = 'page-name';
    nameSpan.innerText = page.name + '.html';
    nameSpan.title = 'クリックで切替';
    nameSpan.onclick = () => { switchPage(page.id); renderPages(); window.__syncBgColorUI?.(); };
    div.appendChild(nameSpan);

    // D&D: 並び替え＆フォルダ出し入れ
    div.draggable = true;
    div.ondragstart = e => { 
        draggedPageId = page.id; 
        const folders = getFolders();
        const currentFolder = folders.find(f => f.id === page.folderId);
        const staticPath = currentFolder ? `${currentFolder.name}/${page.name}.html` : `${page.name}.html`;
        e.dataTransfer.setData('text/plain', staticPath);     
        e.stopPropagation(); 
        setTimeout(() => div.style.opacity = '0.5', 0); 
    };    
    div.ondragend = () => {
        draggedPageId = null;
        div.style.opacity = '1';
        document.querySelectorAll('.page-item,.folder-item').forEach(el => el.classList.remove('drag-over', 'drag-over-group'));
    };
    div.ondragover = e => { e.preventDefault(); if (draggedPageId && draggedPageId !== page.id) div.classList.add('drag-over'); };
    div.ondragleave = () => div.classList.remove('drag-over');
    div.ondrop = e => {
        e.preventDefault();
        div.classList.remove('drag-over');
        if (!draggedPageId || draggedPageId === page.id) return;
        // ドロップ先ページと同じフォルダに合わせてから並び替え
        setPageFolder(draggedPageId, page.folderId);
        reorderPage(draggedPageId, page.id);
        renderPages();
    };

    const editBtn = document.createElement('button');
    editBtn.className = 'page-edit';
    editBtn.innerText = '✎';
    editBtn.title = '名前を変更';
    editBtn.onclick = e => { e.stopPropagation(); editingPageId = page.id; renderPages(); };
    div.appendChild(editBtn);

    const delBtn = document.createElement('button');
    delBtn.className = 'page-del';
    delBtn.innerText = '×';
    delBtn.title = 'このページを削除';
    delBtn.onclick = e => {
        e.stopPropagation();
        if (getPages().length <= 1) { showToast('最後の1ページは削除できません。', null, true); return; }
        if (confirm(`ページ「${page.name}」を削除しますか？`)) { deletePage(page.id); renderPages(); }
    };
    div.appendChild(delBtn);

    // フォルダ内ページは「外に出す」ボタン
    if (page.folderId) {
        const outBtn = document.createElement('button');
        outBtn.className = 'page-edit';
        outBtn.innerText = '⤴';
        outBtn.title = 'フォルダから出す';
        outBtn.onclick = e => { e.stopPropagation(); setPageFolder(page.id, null); renderPages(); };
        div.appendChild(outBtn);
    }

    return div;
}

// ページ追加
export function onAddPage() {
    addPage();
    renderPages();
    showToast('ページを追加しました。✎ で名前を変更できます。');
}

// フォルダ追加
export function onAddFolder() {
    addFolder();
    renderPages();
    showToast('フォルダを追加しました。ページをドラッグして入れられます。');
}