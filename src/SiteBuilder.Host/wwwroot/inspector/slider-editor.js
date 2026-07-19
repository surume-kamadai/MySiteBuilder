// ============================================================
// slider-editor.js - スライダー / 記事グリッドのアイテム編集モーダル
// （inspector.js から分離。画像+タイトル+本文+リンクをスライド単位で編集する）
// ============================================================
import { selectedNodes } from '../app/state.js';
import { saveHistory } from '../history/history.js';
import { pickImageDialog } from './image-picker.js';

// ============================================================
// スライダー編集モーダル（画像+タイトル+本文+リンクをスライドごとに編集）
// ============================================================

// スライド/アイテム配列を取得（Slider→slider.slides, ArticleGrid→grid.items）
function getSlides(node) {
    const bData = node.getAttr('bladeData');
    const type = node.getAttr('uiType');
    if (type === 'ArticleGrid') {
        if (!bData.grid) bData.grid = {};
        if (!Array.isArray(bData.grid.items)) bData.grid.items = [];
        return bData.grid.items;
    }
    // Slider
    if (!bData.slider) bData.slider = {};
    if (!Array.isArray(bData.slider.slides)) {
        // 旧データ: text に画像URLがカンマ区切り → 新スキーマに変換
        const legacy = (bData.text || '').split(',').map(s => s.trim()).filter(Boolean);
        bData.slider.slides = legacy.map(url => ({ image: url, title: '', text: '', linkType: 'none', link: '' }));
    }
    return bData.slider.slides;
}

function saveSlides(node, slides) {
    const bData = node.getAttr('bladeData');
    const type = node.getAttr('uiType');
    if (type === 'ArticleGrid') {
        bData.grid = bData.grid || {};
        bData.grid.items = slides;
    } else {
        bData.slider = bData.slider || {};
        bData.slider.slides = slides;
    }
    node.setAttr('bladeData', bData);
}

// 現在のプロジェクトのページ一覧を取得（リンク先候補）
function getPageOptions() {
    // 動的import避け: window 経由で project からページ一覧を取りに行く
    try {
        const proj = window.__getProjectPagesForSlider?.();
        return proj || [];
    } catch { return []; }
}

// モーダルの再描画
function renderSliderEditor(node) {
    const list = document.getElementById('slider-editor-list');
    if (!list) return;
    list.innerHTML = '';

    const slides = getSlides(node);
    const pages  = getPageOptions();

    slides.forEach((sl, idx) => {
        const card = document.createElement('div');
        card.style.cssText = 'display:flex; gap:10px; padding:10px; background:#2d2d2d; border:1px solid #444; border-radius:5px; margin-bottom:8px;';

        // 左: 画像プレビュー+選択
        const left = document.createElement('div');
        left.style.cssText = 'width:120px; flex-shrink:0;';
        const thumb = document.createElement('div');
        thumb.style.cssText = 'width:120px; height:80px; background:#1e1e1e; border:2px dashed #555; border-radius:3px; display:flex; align-items:center; justify-content:center; overflow:hidden; cursor:pointer; color:#888; font-size:11px; text-align:center;';
        if (sl.image) {
            const img = document.createElement('img');
            img.src = sl.image;
            img.style.cssText = 'width:100%; height:100%; object-fit:cover;';
            img.onerror = () => { thumb.innerText = '画像エラー'; };
            thumb.innerHTML = ''; thumb.appendChild(img);
        } else {
            thumb.innerText = 'クリック or D&D\nで画像追加';
        }
        // クリックでファイルダイアログ
        thumb.onclick = async () => {
            const dataUrl = await pickImageDialog();
            if (dataUrl) { sl.image = dataUrl; saveSlides(node, slides); renderSliderEditor(node); saveHistory(); }
        };
        // D&D
        thumb.ondragover = e => { e.preventDefault(); thumb.style.borderColor = '#007acc'; };
        thumb.ondragleave = () => { thumb.style.borderColor = '#555'; };
        thumb.ondrop = async e => {
            e.preventDefault();
            thumb.style.borderColor = '#555';
            const file = e.dataTransfer.files?.[0];
            if (!file || !file.type.startsWith('image/')) return;
            const dataUrl = await new Promise(res => {
                const r = new FileReader();
                r.onload = ev => res(ev.target.result);
                r.readAsDataURL(file);
            });
            sl.image = dataUrl;
            saveSlides(node, slides);
            renderSliderEditor(node);
            saveHistory();
        };
        left.appendChild(thumb);

        const numLabel = document.createElement('div');
        numLabel.style.cssText = 'font-size:10px; color:#aaa; margin-top:4px; text-align:center;';
        numLabel.innerText = `スライド ${idx + 1}`;
        left.appendChild(numLabel);

        card.appendChild(left);

        // 右: タイトル・本文・リンク
        const right = document.createElement('div');
        right.style.cssText = 'flex:1; display:flex; flex-direction:column; gap:5px;';

        const titleIn = document.createElement('input');
        titleIn.type = 'text';
        titleIn.placeholder = 'タイトル（見出し）';
        titleIn.value = sl.title || '';
        titleIn.style.cssText = 'padding:5px; background:#1e1e1e; color:#fff; border:1px solid #555; border-radius:3px; font-size:12px;';
        titleIn.oninput = () => { sl.title = titleIn.value; saveSlides(node, slides); };
        titleIn.onchange = () => saveHistory();
        right.appendChild(titleIn);

        const textIn = document.createElement('textarea');
        textIn.placeholder = '本文';
        textIn.value = sl.text || '';
        textIn.rows = 2;
        textIn.style.cssText = 'padding:5px; background:#1e1e1e; color:#fff; border:1px solid #555; border-radius:3px; font-size:12px; resize:vertical;';
        textIn.oninput = () => { sl.text = textIn.value; saveSlides(node, slides); };
        textIn.onchange = () => saveHistory();
        right.appendChild(textIn);

        // リンク種別 + リンク先
        const linkRow = document.createElement('div');
        linkRow.style.cssText = 'display:flex; gap:5px;';

        const linkSel = document.createElement('select');
        linkSel.style.cssText = 'padding:4px; background:#1e1e1e; color:#fff; border:1px solid #555; border-radius:3px; font-size:11px; min-width:90px;';
        ['none','url','page'].forEach(v => {
            const opt = document.createElement('option');
            opt.value = v;
            opt.innerText = v === 'none' ? 'リンクなし' : v === 'url' ? '外部URL' : '内部ページ';
            if ((sl.linkType || 'none') === v) opt.selected = true;
            linkSel.appendChild(opt);
        });

        const buildLinkInput = () => {
            const old = linkRow.querySelector('.slider-link-target');
            if (old) old.remove();
            const t = linkSel.value;
            if (t === 'none') return;
            let el;
            if (t === 'page') {
                el = document.createElement('select');
                pages.forEach(p => {
                    const opt = document.createElement('option');
                    opt.value = p.path;
                    opt.innerText = p.name;
                    if (sl.link === p.path) opt.selected = true;
                    el.appendChild(opt);
                });
                el.onchange = () => { sl.link = el.value; saveSlides(node, slides); saveHistory(); };
            } else {
                el = document.createElement('input');
                el.type = 'text';
                el.placeholder = 'https://example.com';
                el.value = sl.link || '';
                el.oninput = () => { sl.link = el.value; saveSlides(node, slides); };
                el.onchange = () => saveHistory();
            }
            el.className = 'slider-link-target';
            el.style.cssText = 'flex:1; padding:4px; background:#1e1e1e; color:#fff; border:1px solid #555; border-radius:3px; font-size:11px;';
            linkRow.appendChild(el);
        };

        linkSel.onchange = () => {
            sl.linkType = linkSel.value;
            if (sl.linkType === 'none') sl.link = '';
            saveSlides(node, slides);
            buildLinkInput();
            saveHistory();
        };
        linkRow.appendChild(linkSel);
        buildLinkInput();
        right.appendChild(linkRow);

        card.appendChild(right);

        // 右端: 上下/削除
        const actions = document.createElement('div');
        actions.style.cssText = 'display:flex; flex-direction:column; gap:3px;';
        const upBtn = document.createElement('button');
        upBtn.innerText = '↑'; upBtn.title = '上へ';
        upBtn.style.cssText = 'width:28px; padding:2px; background:#444; border:none; color:#fff; margin:0; font-size:11px;';
        upBtn.onclick = () => {
            if (idx === 0) return;
            [slides[idx - 1], slides[idx]] = [slides[idx], slides[idx - 1]];
            saveSlides(node, slides); renderSliderEditor(node); saveHistory();
        };
        const downBtn = document.createElement('button');
        downBtn.innerText = '↓'; downBtn.title = '下へ';
        downBtn.style.cssText = 'width:28px; padding:2px; background:#444; border:none; color:#fff; margin:0; font-size:11px;';
        downBtn.onclick = () => {
            if (idx === slides.length - 1) return;
            [slides[idx + 1], slides[idx]] = [slides[idx], slides[idx + 1]];
            saveSlides(node, slides); renderSliderEditor(node); saveHistory();
        };
        const delBtn = document.createElement('button');
        delBtn.innerText = '✕'; delBtn.title = '削除';
        delBtn.style.cssText = 'width:28px; padding:2px; background:#cc4545; border:none; color:#fff; margin:0; font-size:11px;';
        delBtn.onclick = () => {
            slides.splice(idx, 1);
            saveSlides(node, slides); renderSliderEditor(node); saveHistory();
        };
        actions.appendChild(upBtn);
        actions.appendChild(downBtn);
        actions.appendChild(delBtn);
        card.appendChild(actions);

        list.appendChild(card);
    });

    if (slides.length === 0) {
        const empty = document.createElement('div');
        empty.style.cssText = 'color:#888; font-size:12px; padding:20px; text-align:center;';
        empty.innerText = 'スライドがありません。下の「＋ スライドを追加」で追加してください。';
        list.appendChild(empty);
    }
}

let editorTargetNode = null;

window.openSliderEditor = () => {
    if (selectedNodes.length !== 1) return;
    const node = selectedNodes[0];
    if (node.getAttr('uiType') !== 'Slider') return;
    editorTargetNode = node;
    document.getElementById('slider-editor-title').innerText = '📋 スライド一覧の編集';
    document.getElementById('slider-editor-overlay').style.display = 'flex';
    renderSliderEditor(node);
};

window.openGridEditor = () => {
    if (selectedNodes.length !== 1) return;
    const node = selectedNodes[0];
    if (node.getAttr('uiType') !== 'ArticleGrid') return;
    editorTargetNode = node;
    document.getElementById('slider-editor-title').innerText = '📋 記事アイテム一覧の編集';
    document.getElementById('slider-editor-overlay').style.display = 'flex';
    renderSliderEditor(node);
};

window.closeSliderEditor = () => {
    document.getElementById('slider-editor-overlay').style.display = 'none';
    if (editorTargetNode) {
        const type = editorTargetNode.getAttr('uiType');
        if (type === 'Slider') {
            const placeholder = editorTargetNode.findOne('.slider-placeholder');
            if (placeholder) {
                const count = (editorTargetNode.getAttr('bladeData')?.slider?.slides || []).length;
                placeholder.text(`🖼️ スライダー\n(現在 ${count} 枚のスライド)\n📋「スライド一覧を編集」で詳細設定`);
                editorTargetNode.getLayer()?.batchDraw();
            }
        } else if (type === 'ArticleGrid') {
            const placeholder = editorTargetNode.findOne('.grid-placeholder');
            if (placeholder) {
                const bData = editorTargetNode.getAttr('bladeData');
                const count = (bData?.grid?.items || []).length;
                const cols  = bData?.grid?.columns ?? 3;
                placeholder.text(`📰 記事グリッド\n(${count} 件 / ${cols} カラム)\n📋「アイテム一覧を編集」で詳細設定`);
                editorTargetNode.getLayer()?.batchDraw();
            }
        }
    }
    editorTargetNode = null;
};

window.addNewSlide = () => {
    if (!editorTargetNode) return;
    const slides = getSlides(editorTargetNode);
    slides.push({ image: '', title: '', text: '', linkType: 'none', link: '' });
    saveSlides(editorTargetNode, slides);
    renderSliderEditor(editorTargetNode);
    saveHistory();
};
