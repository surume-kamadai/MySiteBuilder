// ============================================================
// preview.js - エディタ内プレビュー（プレイモード）機能
// ============================================================
import { getProject, getActivePage, saveCurrentPage } from '../project/project.js';
import { currentDevice } from '../app/state.js';
import { HtmlRenderer } from '../export/renderer.js'; // 出力エンジンを直接呼ぶ

export function playPreview() {
    // 1. 最新の配置を page.elements に確定保存する（generateSceneData経由で
    //    スマホ表示中ならPC位置に戻したtransform + layouts.mobile が保存される）
    saveCurrentPage();

    const project = getProject();
    const activePage = getActivePage();
    if (!activePage) return;

    // SEO解決（ページ個別→サイト共通の順でフォールバック）
    const site = project.settings?.seo || {};
    const pseo = activePage.seo || {};
    const baseTitle = (pseo.title && pseo.title.trim()) || activePage.name;
    const sceneData = {
        canvas: project.settings?.canvas,
        bgColor: activePage.bgColor || project.settings?.siteBgColor || '#f1f2f6',
        elements: activePage.elements || [],
        seo: {
            lang: site.lang || 'ja',
            title: site.siteName ? `${baseTitle} | ${site.siteName}` : baseTitle,
            description: (pseo.description && pseo.description.trim()) || site.description || '',
            ogImage: (pseo.ogImage && pseo.ogImage.trim()) || site.ogImage || '',
            siteName: site.siteName || '',
        },
    };

    // 2. 出力エンジンで現在のページのHTMLを生成（画像はBase64のまま埋め込む）
    const renderer = new HtmlRenderer(sceneData, { mode: 'static', imageMap: new Map() });
    const html = renderer.render();

    // 3. オーバーレイとiframeを取得してHTMLを流し込む
    const overlay = document.getElementById('preview-overlay');
    const iframe = document.getElementById('preview-frame');
    
    if (overlay && iframe) {
        iframe.srcdoc = html; // 生成したHTMLを直接注入
        
        // 現在の編集モード（PC/スマホ）に合わせてプレビュー枠のサイズを切り替え
        if (currentDevice === 'mobile') {
            iframe.style.width = '375px';
            iframe.style.height = '812px'; // 一般的なスマホの高さ
            iframe.style.maxWidth = '100%';
        } else {
            iframe.style.width = '100%';
            iframe.style.height = '100%';
            iframe.style.maxWidth = 'none';
        }
        
        overlay.style.display = 'flex';
    }
}

export function stopPreview() {
    const overlay = document.getElementById('preview-overlay');
    const iframe = document.getElementById('preview-frame');
    if (overlay && iframe) {
        overlay.style.display = 'none';
        iframe.srcdoc = ''; // メモリ解放のために中身を消す
    }
}