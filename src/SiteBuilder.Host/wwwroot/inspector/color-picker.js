// ============================================================
// color-picker.js - Pickr(色相＋SV＋透明度A 一体型ピッカー)の配線
//
// 各色は「色見本(.color-field)＋隠しinput(実値)」。色見本をクリックすると Pickr の
// ポップアップ（SV四角・色相バー・透明度Aバー）が開き、選んだ色(rgba)を隠しinputへ
// 書き戻して input/change を発火する（既存ハンドラがそのまま動く）。
// ============================================================

const pickrs = new Map();
// __setColorField による「プログラム的な」色設定中は true。
// この間の Pickr の change/changestop で hidden input へ commit イベントを
// 飛ばさない（＝選択切替の値流し込みが onInspectorUpdate を誤発火させ、
// 前オブジェクトの設定が別オブジェクトへ移るのを防ぐ）。
let suppressDispatch = false;

// Pickr の色 → 'rgba(r, g, b, a)' 文字列
function toStr(color) {
    return color.toRGBA().toString(2);
}

function writeValue(hiddenId, swatch, val, commit) {
    const hidden = document.getElementById(hiddenId);
    if (hidden) {
        hidden.value = val;
        hidden.dispatchEvent(new Event(commit ? 'change' : 'input', { bubbles: true }));
    }
    if (swatch) swatch.style.background = val;
}

export function initColorPickers() {
    if (typeof window === 'undefined') return;
    if (typeof window.Pickr === 'undefined') { setTimeout(initColorPickers, 300); return; }

    document.querySelectorAll('.color-field').forEach(swatch => {
        if (swatch.dataset.wired) return;
        swatch.dataset.wired = '1';
        const hiddenId = swatch.dataset.for;
        const hidden = document.getElementById(hiddenId);
        const initial = (hidden && hidden.value) || '#ffffff';
        const useAlpha = !swatch.hasAttribute('data-noalpha');  // data-noalpha は透明度なし(hex)
        swatch.style.background = initial;

        const pickr = window.Pickr.create({
            el: swatch,
            theme: 'nano',
            useAsButton: true,     // 用意した色見本ボタンをトグルに使う
            default: initial,
            position: 'left-middle',
            components: {
                preview: true,
                opacity: useAlpha,   // ← 透明度(A)をピッカー内で操作（hex色はオフ）
                hue: true,
                interaction: { hex: true, rgba: useAlpha, input: true, save: false, clear: false },
            },
        });

        pickr.on('change', (color) => {
            if (suppressDispatch) return;   // プログラム的なsetColor中は無視
            const val = useAlpha ? color.toRGBA().toString(2) : color.toHEXA().toString().slice(0, 7);
            writeValue(hiddenId, swatch, val, false);
        });
        pickr.on('changestop', () => {
            if (suppressDispatch) return;
            const h = document.getElementById(hiddenId);
            if (h) h.dispatchEvent(new Event('change', { bubbles: true }));
        });
        pickrs.set(hiddenId, pickr);
    });
}

// 選択要素の切替時などに、外部から値を流し込む（隠しinput＋色見本＋Pickrを同期）
window.__setColorField = (id, value) => {
    const hidden = document.getElementById(id);
    if (hidden) hidden.value = value;
    const swatch = document.querySelector(`.color-field[data-for="${id}"]`);
    if (swatch) swatch.style.background = value;
    const p = pickrs.get(id);
    if (p && value) {
        suppressDispatch = true;
        try { p.setColor(value, true); } catch (e) {}
        suppressDispatch = false;
    }
};

if (typeof window !== 'undefined') setTimeout(() => { try { initColorPickers(); } catch (e) {} }, 700);
