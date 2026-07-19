// ============================================================
// トースト通知
// ============================================================

// アニメーション用スタイルを一度だけ挿入する
const style = document.createElement('style');
style.textContent = `
    @keyframes toastIn {
        from { opacity: 0; transform: translateX(-50%) translateY(10px); }
        to   { opacity: 1; transform: translateX(-50%) translateY(0); }
    }
`;
document.head.appendChild(style);

/**
 * @param {string}      message    - 表示するメッセージ
 * @param {string|null} previewUrl - プレビューリンクURL（null なら非表示）
 * @param {boolean}     isError    - true のとき赤背景で表示
 */
export function showToast(message, previewUrl = null, isError = false) {
    document.getElementById('editor-toast')?.remove();

    const toast = document.createElement('div');
    toast.id = 'editor-toast';
    toast.style.cssText = `
        position: fixed; bottom: 30px; left: 50%; transform: translateX(-50%);
        background: ${isError ? '#cc4545' : '#27ae60'};
        color: #fff; padding: 12px 20px; border-radius: 8px;
        font-size: 13px; font-family: 'Segoe UI', sans-serif;
        box-shadow: 0 4px 16px rgba(0,0,0,0.4);
        display: flex; align-items: center; gap: 12px;
        z-index: 9999; animation: toastIn 0.3s ease;
    `;

    const text = document.createElement('span');
    text.textContent = message;
    toast.appendChild(text);

    if (previewUrl) {
        const btn = document.createElement('a');
        btn.href            = previewUrl;
        btn.target          = '_blank';
        btn.rel             = 'noopener noreferrer';
        btn.textContent     = 'プレビューを開く →';
        btn.style.cssText   = 'color:#fff; font-weight:bold; text-decoration:underline; white-space:nowrap;';
        toast.appendChild(btn);
    }

    const close = document.createElement('span');
    close.textContent   = '✕';
    close.style.cssText = 'cursor:pointer; opacity:0.7; font-size:12px;';
    close.onclick = () => toast.remove();
    toast.appendChild(close);

    document.body.appendChild(toast);
    setTimeout(() => toast.remove(), 6000);
}
