// ============================================================
// image-picker.js - 画像ファイル選択ダイアログ（共有ヘルパー）
// api.js への循環参照を避けるため、electronAPI を直接叩く薄いラッパにしている。
// ============================================================
import { showToast } from '../ui/toast.js';

// 画像をOSのダイアログから取得し dataURL を返す（キャンセル/失敗時は null）
export async function pickImageDialog() {
    try {
        const result = await window.electronAPI.pickImage();
        return result?.dataUrl ?? null;
    } catch {
        showToast('画像の選択に失敗しました。', null, true);
        return null;
    }
}
