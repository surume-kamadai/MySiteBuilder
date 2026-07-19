// ============================================================
// インスペクター (プロパティパネル)
// ============================================================
import { layer, tr } from '../canvas/canvas.js';
import { selectedNodes, setSelectedNodes, currentDevice } from '../app/state.js';
import { saveHistory } from '../history/history.js';
import { renderExplorer } from '../explorer/explorer.js';
import { applyNodeShadow, applyTextStyle, applyImageCover, applyGradient, applyCornerRadius, applyStroke, applyDropShadow, applyGradText, applyGlow } from '../nodes/node-style.js';
import { markMobileEdited, updatePcGeom } from '../canvas/display.js';
import { pickImageDialog } from './image-picker.js';
import { renderEventList } from './interactions.js';

// type=color 入力に安全に値を設定する（#rrggbb 以外は既定値にフォールバック）
function setColorInput(id, val, def) {
    const el = document.getElementById(id);
    if (!el) return;
    const v = (typeof val === 'string' && val) ? val : def;
    // Pickr のスウォッチ(.color-field＋隠しinput)があればそちらへ、無ければ従来 type=color(hex)
    if (window.__setColorField && document.querySelector(`.color-field[data-for="${id}"]`)) {
        window.__setColorField(id, v);
    } else {
        el.value = /^#[0-9a-fA-F]{6}$/.test(v) ? v : def;
    }
}


// いま各入力フィールドが表しているノードのid。
// 選択直後などに、まだ前ノードの値が残ったフィールドで onInspectorUpdate が
// 走って設定が別オブジェクトへ「移ってしまう」のを防ぐためのガードに使う。
let inspectorNodeId = null;

export function hideInspector() {
    inspectorNodeId = null;
    document.getElementById('ins-fields').style.display = 'none';
    document.getElementById('ins-empty').style.display  = 'block';
}

export function updateInspectorFromNode() {
    if (selectedNodes.length === 0) { hideInspector(); return; }

    document.getElementById('ins-fields').style.display = 'block';
    document.getElementById('ins-empty').style.display  = 'none';

    if (selectedNodes.length > 1) {
        inspectorNodeId = null;
        document.getElementById('ins-fields-single').style.display = 'none';
        document.getElementById('ins-fields-multi').style.display  = 'block';
        return;
    }

    document.getElementById('ins-fields-single').style.display = 'block';
    document.getElementById('ins-fields-multi').style.display  = 'none';

    const node  = selectedNodes[0];
    // これ以降のフィールドはこのノードの値で埋める（ガード用に記録）
    inspectorNodeId = node.id();
    const bData = node.getAttr('bladeData');
    const type  = node.getAttr('uiType');

    document.getElementById('ins-name').value     = bData.name;
    document.getElementById('ins-x').value        = Math.round(node.x());
    document.getElementById('ins-y').value        = Math.round(node.y());
    document.getElementById('ins-w').value        = Math.round(node.width());
    document.getElementById('ins-h').value        = Math.round(node.height());
    document.getElementById('ins-text').value     = bData.text ?? '';
    // type=color は #rrggbb 以外（undefined / 'transparent' 等）を入れるとコンソール
    // エラーになるので、妥当な16進カラーのみ反映し、それ以外は無難な既定値にする。
    setColorInput('ins-bgcolor', bData.bgcolor, '#ffffff');
    setColorInput('ins-color',   bData.color,   '#000000');
    // 不透明度（0=透明〜1=不透明）。未設定なら1。
    // ※ この行は必ず const bData の宣言より後に置くこと（TDZエラー防止）
    const opacityInput = document.getElementById('ins-opacity');
    if (opacityInput) opacityInput.value = bData.opacity ?? 1;
    document.getElementById('ins-align').value      = bData.align || 'left';
    document.getElementById('ins-fontfamily').value = bData.fontfamily || 'sans-serif';
    const _fw = document.getElementById('ins-fontweight'); if (_fw) _fw.value = bData.fontWeight || 'normal';
    const _it = document.getElementById('ins-italic');      if (_it) _it.checked = !!bData.italic;
    const _ul = document.getElementById('ins-underline');   if (_ul) _ul.checked = !!bData.underline;
    const _ls = document.getElementById('ins-letterspacing'); if (_ls) _ls.value = bData.letterSpacing ?? 0;
    const _lh = document.getElementById('ins-lineheight');    if (_lh) _lh.value = bData.lineHeight ?? 1.2;
    
    if (type === 'Label' || type === 'Button') {
        document.getElementById('group-text-align').style.display = 'block';
    } else {
        document.getElementById('group-text-align').style.display = 'none';
    }
    // ボタンの時だけ背景画像UIを表示
    const groupBgImage = document.getElementById('group-btn-bgimage');
    if (groupBgImage) {
        groupBgImage.style.display = (type === 'Button') ? 'block' : 'none';
        document.getElementById('ins-bgimage-preview').innerText = bData.bgimage ? '画像セット済み' : 'なし';
    }

    // ▼ スライダー専用設定の表示と値同期 ▼
    const groupSlider = document.getElementById('group-slider');
    if (groupSlider) {
        groupSlider.style.display = (type === 'Slider') ? 'block' : 'none';
        if (type === 'Slider') {
            const s = bData.slider || {};
            document.getElementById('ins-slider-effect').value     = s.effect ?? 'slide';
            document.getElementById('ins-slider-speed').value      = s.speed ?? 600;
            document.getElementById('ins-slider-autoplay').checked = s.autoplay ?? true;
            document.getElementById('ins-slider-delay').value      = s.delay ?? 3000;
            document.getElementById('ins-slider-loop').checked     = s.loop ?? true;
            document.getElementById('ins-slider-pagination').checked = s.pagination ?? true;
            document.getElementById('ins-slider-navigation').checked = s.navigation ?? true;
            document.getElementById('ins-slider-perview').value    = s.slidesPerView ?? 1;
        }
    }

    // ▼ 記事グリッド専用設定の表示と値同期 ▼
    const groupGrid = document.getElementById('group-grid');
    if (groupGrid) {
        groupGrid.style.display = (type === 'ArticleGrid') ? 'block' : 'none';
        if (type === 'ArticleGrid') {
            const g = bData.grid || {};
            document.getElementById('ins-grid-columns').value = g.columns ?? 3;
            document.getElementById('ins-grid-gap').value     = g.gap ?? 20;
            document.getElementById('ins-grid-radius').value  = g.cardRadius ?? 8;
            setColorInput('ins-grid-arrow', g.arrowColor, '#27ae60');
            document.getElementById('ins-grid-imgratio').value = g.imgRatio ?? '16/10';
            document.getElementById('ins-grid-padding').value  = g.cardPadding ?? 18;
            document.getElementById('ins-grid-slidermode').checked = g.sliderMode ?? false;
            document.getElementById('ins-grid-autoplay').checked   = g.autoplay ?? false;
            document.getElementById('ins-grid-delay').value        = g.delay ?? 3000;
            document.getElementById('ins-grid-loop').checked       = g.loop ?? true;
            document.getElementById('ins-grid-nav').checked        = g.navigation ?? true;
            // スライダーモードのサブパネル表示切替
            document.getElementById('group-grid-slider').style.display = (g.sliderMode ? 'block' : 'none');
        }
    }

    // ▼ アコーディオン専用設定の表示と値同期 ▼
    const groupAcc = document.getElementById('group-accordion');
    if (groupAcc) {
        groupAcc.style.display = (type === 'Accordion') ? 'block' : 'none';
        if (type === 'Accordion') {
            const a = bData.accordion || {};
            setColorInput('ins-acc-headercolor', a.headerColor, '#2c3e50');
            setColorInput('ins-acc-headerbg',    a.headerBg,    '#f7f9fa');
            setColorInput('ins-acc-bodycolor',   a.bodyColor,   '#555555');
            document.getElementById('ins-acc-openfirst').checked = a.openFirst ?? true;
        }
    }

    // フォントサイズはデバイスに応じた値を出す
    const displayFontsize = (currentDevice === 'mobile' && bData.layouts?.mobile?.fontsize !== undefined)
        ? bData.layouts.mobile.fontsize
        : (bData.fontsize || 16);
    document.getElementById('ins-fontsize').value = displayFontsize;
    document.getElementById('ins-route').value    = bData.route || '#';
    document.getElementById('ins-method').value   = bData.method;

    document.getElementById('group-backend').style.display = (type === 'Button' || type === 'Image') ? 'block' : 'none';
    document.getElementById('btn-ungroup').style.display   = (type === 'Group')  ? 'block' : 'none';
    document.getElementById('group-text').style.display    = (type === 'Image')  ? 'none'  : 'block';

    // ▼ フォーム: ボタンの役割（リンク / 送信ボタン）
    const groupBtnRole = document.getElementById('group-btn-role');
    if (groupBtnRole) {
        groupBtnRole.style.display = (type === 'Button') ? 'block' : 'none';
        const roleSel = document.getElementById('ins-btn-role');
        if (roleSel) roleSel.value = bData.role || 'link';
        // 送信完了メッセージ欄は「送信ボタン」時のみ表示
        const groupSuccess = document.getElementById('group-success-msg');
        if (groupSuccess) {
            groupSuccess.style.display = (type === 'Button' && bData.role === 'submit') ? 'block' : 'none';
            const sm = document.getElementById('ins-success-message');
            if (sm) sm.value = bData.successMessage ?? '送信ありがとうございました。';
        }
    }
    // ▼ フォーム: 入力欄（TextInput）の項目設定
    const groupInput = document.getElementById('group-input');
    if (groupInput) {
        groupInput.style.display = (type === 'TextInput') ? 'block' : 'none';
        if (type === 'TextInput') {
            document.getElementById('ins-input-name').value       = bData.inputName || '';
            document.getElementById('ins-input-type').value       = bData.inputType || 'text';
            document.getElementById('ins-input-required').checked = !!bData.required;
        }
    }
    // ▼ 送信ボタン時はリンク先ラベルを「送信先URL」に切り替える
    const routeLabel = document.getElementById('ins-route-label');
    if (routeLabel) {
        routeLabel.innerText = (type === 'Button' && bData.role === 'submit')
            ? '送信先URL（フォームの action）'
            : 'リンク先URL（Googleフォーム等）';
    }

    const warpBtn = document.getElementById('btn-warp');
    if (warpBtn) {
        warpBtn.style.display = ['Rect', 'Circle', 'Triangle', 'Warp'].includes(type) ? 'block' : 'none';
    }

    document.getElementById('ins-shadow').value    = bData.shadow || 'none';
    document.getElementById('ins-animation').value = bData.animation || 'none';

    // ▼ ドロップシャドウ（自由値）
    const dsGroup = document.getElementById('group-dropshadow');
    if (dsGroup) {
        const d = bData.dropShadow || { on: false, x: 4, y: 4, blur: 10, spread: 0, color: '#000000', opacity: 0.35 };
        document.getElementById('ins-ds-on').checked   = !!d.on;
        document.getElementById('ins-ds-x').value       = parseFloat(d.x) || 0;
        document.getElementById('ins-ds-y').value       = parseFloat(d.y) || 0;
        document.getElementById('ins-ds-blur').value    = Math.max(0, parseFloat(d.blur) || 0);
        document.getElementById('ins-ds-spread').value  = parseFloat(d.spread) || 0;
        setColorInput('ins-ds-color', d.color, '#000000');
        document.getElementById('ins-ds-opacity').value = Math.min(1, Math.max(0, d.opacity ?? 0.35));
        document.getElementById('dropshadow-fields').style.display = d.on ? 'block' : 'none';
    }

    // ▼ 境界線（Stroke）: 四角・丸・ボタン・画像・テキストで表示
    const strokeGroup = document.getElementById('group-stroke');
    if (strokeGroup) {
        const supportsStroke = ['Rect', 'Circle', 'Button', 'Image', 'Label'].includes(type);
        strokeGroup.style.display = supportsStroke ? 'block' : 'none';
        if (supportsStroke) {
            const s = bData.stroke || { on: false, width: 2, color: '#000000' };
            document.getElementById('ins-stroke-on').checked = !!s.on;
            document.getElementById('ins-stroke-width').value = Math.max(0, parseFloat(s.width) || 0);
            setColorInput('ins-stroke-color', s.color, '#000000');
            document.getElementById('stroke-fields').style.display = s.on ? 'block' : 'none';
        }
    }

    // ▼ 角の丸み（四角・ボタン・画像で表示）
    const cornerGroup = document.getElementById('group-corner');
    if (cornerGroup) {
        const supportsCorner = ['Rect', 'Button', 'Image'].includes(type);
        cornerGroup.style.display = supportsCorner ? 'block' : 'none';
        if (supportsCorner) {
            const cr = document.getElementById('ins-corner');
            if (cr) cr.value = Math.max(0, parseInt(bData.cornerRadius) || 0);
        }
    }

    // ▼ グラデーション設定（図形・ボタン・画像で表示）
    const gradGroup = document.getElementById('group-gradient');
    if (gradGroup) {
        const supportsGrad = ['Rect', 'Circle', 'Triangle', 'Button', 'Image'].includes(type);
        gradGroup.style.display = supportsGrad ? 'block' : 'none';
        if (supportsGrad) {
            const g = bData.gradient || { on: false, type: 'linear', c1: '#4facfe', c2: '#00f2fe', dir: 'v' };
            document.getElementById('ins-grad-on').checked   = !!g.on;
            document.getElementById('ins-grad-type').value   = g.type || 'linear';
            document.getElementById('ins-grad-dir').value    = g.dir  || 'v';
            setColorInput('ins-grad-c1', g.c1, '#4facfe');
            setColorInput('ins-grad-c2', g.c2, '#00f2fe');
            document.getElementById('grad-fields').style.display   = g.on ? 'block' : 'none';
            document.getElementById('grad-dir-wrap').style.display = (g.type === 'radial') ? 'none' : 'block';
        }
    }

    // ▼ グラデーション文字（テキスト系）
    const gtGroup = document.getElementById('group-gradtext');
    if (gtGroup) {
        const supports = ['Label', 'Button'].includes(type);
        gtGroup.style.display = supports ? 'block' : 'none';
        if (supports) {
            const g = bData.gradText || {};
            document.getElementById('ins-gt-on').checked = !!g.on;
            document.getElementById('ins-gt-dir').value  = g.dir || 'h';
            setColorInput('ins-gt-c1', g.c1, '#ff6ec4');
            setColorInput('ins-gt-c2', g.c2, '#7873f5');
            document.getElementById('gradtext-fields').style.display = g.on ? 'block' : 'none';
        }
    }

    // ▼ 内側シャドウ（図形/ボタン/画像）
    const isGroup = document.getElementById('group-innershadow');
    if (isGroup) {
        const supports = ['Rect', 'Circle', 'Button', 'Image'].includes(type);
        isGroup.style.display = supports ? 'block' : 'none';
        if (supports) {
            const s = bData.innerShadow || {};
            document.getElementById('ins-is-on').checked    = !!s.on;
            document.getElementById('ins-is-x').value       = parseFloat(s.x ?? 0);
            document.getElementById('ins-is-y').value       = parseFloat(s.y ?? 3);
            document.getElementById('ins-is-blur').value    = Math.max(0, parseFloat(s.blur ?? 6));
            document.getElementById('ins-is-opacity').value = Math.min(1, Math.max(0, s.opacity ?? 0.4));
            setColorInput('ins-is-color', s.color, '#000000');
            document.getElementById('innershadow-fields').style.display = s.on ? 'block' : 'none';
        }
    }

    // ▼ 光彩（グロー）（図形/ボタン/画像/テキスト）
    const glowGroup = document.getElementById('group-glow');
    if (glowGroup) {
        const supports = ['Rect', 'Circle', 'Button', 'Image', 'Label'].includes(type);
        glowGroup.style.display = supports ? 'block' : 'none';
        if (supports) {
            const g = bData.glow || {};
            document.getElementById('ins-glow-on').checked    = !!g.on;
            document.getElementById('ins-glow-blur').value    = Math.max(0, parseFloat(g.blur ?? 12));
            document.getElementById('ins-glow-spread').value  = parseFloat(g.spread ?? 0);
            document.getElementById('ins-glow-opacity').value = Math.min(1, Math.max(0, g.opacity ?? 0.8));
            setColorInput('ins-glow-color', g.color, '#00d0ff');
            document.getElementById('glow-fields').style.display = g.on ? 'block' : 'none';
        }
    }

    // ▼ ベベル＆エンボス（図形/ボタン/画像）
    const bevelGroup = document.getElementById('group-bevel');
    if (bevelGroup) {
        const supports = ['Rect', 'Circle', 'Button', 'Image'].includes(type);
        bevelGroup.style.display = supports ? 'block' : 'none';
        if (supports) {
            const b = bData.bevel || {};
            document.getElementById('ins-bevel-on').checked    = !!b.on;
            document.getElementById('ins-bevel-depth').value   = Math.max(1, parseFloat(b.depth ?? 4));
            document.getElementById('ins-bevel-dir').value     = b.dir || 'up';
            setColorInput('ins-bevel-hl', b.highlight, '#ffffff');
            setColorInput('ins-bevel-sh', b.shadow, '#000000');
            document.getElementById('ins-bevel-opacity').value = Math.min(1, Math.max(0, b.opacity ?? 0.5));
            document.getElementById('bevel-fields').style.display = b.on ? 'block' : 'none';
        }
    }

    renderEventList(node);
    // レイヤースタイル・ダイアログが開いていれば本体/案内の表示を同期
    window.__syncLayerStyleDialog?.();
}

export function onInspectorUpdate(shouldSaveHistory = true) {
    if (selectedNodes.length !== 1) return;
    const node  = selectedNodes[0];

    // フィールドがまだこのノードの値で埋まっていない（＝選択直後に前ノード由来の
    // stale なイベントが来た）場合は書き込まず、正しい値で作り直す。
    // これで「Aの設定を触った直後にBを選ぶとBへ移る」現象を防ぐ。
    if (inspectorNodeId !== node.id()) { updateInspectorFromNode(); return; }

    const bData = node.getAttr('bladeData');

    bData.name     = document.getElementById('ins-name').value;
    bData.text     = document.getElementById('ins-text').value;
    bData.bgcolor  = document.getElementById('ins-bgcolor').value;   // #rrggbb または rgba(...)
    bData.color    = document.getElementById('ins-color').value;

    // 不透明度: parseFloat が NaN を返しても ?? は NaN を素通りさせてしまうため、
    // Number.isFinite で判定し、0〜1にクランプする（不正値で要素が消えるのを防ぐ）。
    const opacityRaw = parseFloat(document.getElementById('ins-opacity')?.value);
    bData.opacity = Number.isFinite(opacityRaw) ? Math.min(1, Math.max(0, opacityRaw)) : 1;
    node.opacity(bData.opacity); // Konvaノードに透明度を適用

    const newFontsize = parseInt(document.getElementById('ins-fontsize').value) || 16;
    // フォントサイズはデバイスごとに分離: PCはbData.fontsize、スマホは layouts.mobile.fontsize
    if (currentDevice === 'mobile') {
        if (!bData.layouts) bData.layouts = {};
        if (!bData.layouts.mobile) {
            // 初回: 現状のジオメトリで作る
            bData.layouts.mobile = {
                x: node.x(), y: node.y(), w: node.width(), h: node.height(),
                fontsize: bData.fontsize || 16,
            };
        }
        bData.layouts.mobile.fontsize = newFontsize;
        bData.mobileEdited = true;
        // 見た目だけは即時反映用にfontsizeをノードへ。ただしbData.fontsize(=PC正)は触らない
    } else {
        bData.fontsize = newFontsize;
    }
    bData.align      = document.getElementById('ins-align').value;
    bData.fontfamily = document.getElementById('ins-fontfamily').value;
    bData.fontWeight = document.getElementById('ins-fontweight')?.value || 'normal';
    bData.italic     = !!document.getElementById('ins-italic')?.checked;
    bData.underline  = !!document.getElementById('ins-underline')?.checked;
    bData.letterSpacing = parseFloat(document.getElementById('ins-letterspacing')?.value) || 0;
    bData.lineHeight = parseFloat(document.getElementById('ins-lineheight')?.value) || 1.2;
    
    bData.route    = document.getElementById('ins-route').value;
    bData.method   = document.getElementById('ins-method').value;

    bData.shadow    = document.getElementById('ins-shadow').value;
    bData.animation = document.getElementById('ins-animation').value;

    // ドロップシャドウ(自由値)の保存
    if (!bData.dropShadow) bData.dropShadow = {};
    bData.dropShadow.on      = document.getElementById('ins-ds-on').checked;
    bData.dropShadow.x       = parseFloat(document.getElementById('ins-ds-x').value) || 0;
    bData.dropShadow.y       = parseFloat(document.getElementById('ins-ds-y').value) || 0;
    bData.dropShadow.blur    = Math.max(0, parseFloat(document.getElementById('ins-ds-blur').value) || 0);
    bData.dropShadow.spread  = parseFloat(document.getElementById('ins-ds-spread').value) || 0;
    bData.dropShadow.color   = document.getElementById('ins-ds-color').value;
    bData.dropShadow.opacity = Math.min(1, Math.max(0, parseFloat(document.getElementById('ins-ds-opacity').value) || 0));
    document.getElementById('dropshadow-fields').style.display = bData.dropShadow.on ? 'block' : 'none';

    // フォーム関連の保存
    const roleSel = document.getElementById('ins-btn-role');
    if (roleSel && node.getAttr('uiType') === 'Button') {
        bData.role = roleSel.value;
        const sm = document.getElementById('ins-success-message');
        if (sm) bData.successMessage = sm.value;
    }
    if (node.getAttr('uiType') === 'TextInput') {
        bData.inputName = document.getElementById('ins-input-name').value;
        bData.inputType = document.getElementById('ins-input-type').value;
        bData.required  = document.getElementById('ins-input-required').checked;
    }

    // スライダー設定の保存
    if (node.getAttr('uiType') === 'Slider') {
        bData.slider = bData.slider || {};
        bData.slider.effect       = document.getElementById('ins-slider-effect').value;
        bData.slider.speed        = parseInt(document.getElementById('ins-slider-speed').value) || 600;
        bData.slider.autoplay     = document.getElementById('ins-slider-autoplay').checked;
        bData.slider.delay        = parseInt(document.getElementById('ins-slider-delay').value) || 3000;
        bData.slider.loop         = document.getElementById('ins-slider-loop').checked;
        bData.slider.pagination   = document.getElementById('ins-slider-pagination').checked;
        bData.slider.navigation   = document.getElementById('ins-slider-navigation').checked;
        bData.slider.slidesPerView = parseInt(document.getElementById('ins-slider-perview').value) || 1;
    }

    // 記事グリッド設定の保存
    if (node.getAttr('uiType') === 'ArticleGrid') {
        bData.grid = bData.grid || {};
        bData.grid.columns    = parseInt(document.getElementById('ins-grid-columns').value) || 3;
        bData.grid.gap        = parseInt(document.getElementById('ins-grid-gap').value) || 20;
        bData.grid.cardRadius = parseInt(document.getElementById('ins-grid-radius').value) || 0;
        bData.grid.arrowColor = document.getElementById('ins-grid-arrow').value || '#27ae60';
        bData.grid.imgRatio   = document.getElementById('ins-grid-imgratio').value || '16/10';
        bData.grid.cardPadding = parseInt(document.getElementById('ins-grid-padding').value) || 18;
        bData.grid.sliderMode = document.getElementById('ins-grid-slidermode').checked;
        bData.grid.autoplay   = document.getElementById('ins-grid-autoplay').checked;
        bData.grid.delay      = parseInt(document.getElementById('ins-grid-delay').value) || 3000;
        bData.grid.loop       = document.getElementById('ins-grid-loop').checked;
        bData.grid.navigation = document.getElementById('ins-grid-nav').checked;
        // プレースホルダ更新
        const placeholder = node.findOne('.grid-placeholder');
        if (placeholder) {
            const itemCount = (bData.grid.items || []).length;
            placeholder.text(`📰 記事グリッド\n(${itemCount} 件 / ${bData.grid.columns} カラム)\n📋「アイテム一覧を編集」で詳細設定`);
        }
    }

    // アコーディオン設定の保存
    if (node.getAttr('uiType') === 'Accordion') {
        bData.accordion = bData.accordion || {};
        bData.accordion.headerColor = document.getElementById('ins-acc-headercolor').value || '#2c3e50';
        bData.accordion.headerBg    = document.getElementById('ins-acc-headerbg').value || '#f7f9fa';
        bData.accordion.bodyColor   = document.getElementById('ins-acc-bodycolor').value || '#555555';
        bData.accordion.openFirst   = document.getElementById('ins-acc-openfirst').checked;
        const placeholder = node.findOne('.accordion-placeholder');
        if (placeholder) {
            const itemCount = (bData.accordion.items || []).length;
            placeholder.text(`🪗 アコーディオン\n(${itemCount} 項目)\n📋「項目一覧を編集」で詳細設定`);
        }
    }

    applyNodeShadow(node, bData.shadow);
    applyDropShadow(node, bData);  // on の時だけプリセットを上書き
    applyTextStyle(node, bData);

    node.x(parseInt(document.getElementById('ins-x').value));    
    node.y(parseInt(document.getElementById('ins-y').value));
    node.width(parseInt(document.getElementById('ins-w').value));
    node.height(parseInt(document.getElementById('ins-h').value));

    const type = node.getAttr('uiType');
    // 画面上の見た目に反映するフォントサイズ（スマホ表示中はnewFontsize、PCはbData.fontsize）
    const displayFontsize = currentDevice === 'mobile' ? newFontsize : bData.fontsize;

    if (type === 'Label') {
        node.text(bData.text);
        node.fill(bData.color);
        node.fontSize(displayFontsize);
    } else if (type === 'Button') {
        const bg = node.findOne('.btn-bg');
        const txt = node.findOne('.btn-text');
        const bgImgNode = node.findOne('.btn-bgimage');

        if (bg) {
            bg.width(node.width());
            bg.height(node.height());
            bg.fill(bData.bgimage ? null : bData.bgcolor);
        }
        if (bgImgNode) {
            bgImgNode.width(node.width());
            bgImgNode.height(node.height());
        }
        if (txt) {
            txt.width(node.width());
            txt.height(node.height());
            txt.text(bData.text);
            txt.fill(bData.color);
            txt.fontSize(displayFontsize);
        }
    } else if (type === 'Rect') {
        node.fill(bData.bgcolor);
    } else if (type === 'Circle') {
        node.fill(bData.bgcolor);
        node.radiusX(parseInt(document.getElementById('ins-w').value) / 2);
        node.radiusY(parseInt(document.getElementById('ins-h').value) / 2);
    } else if (type === 'Triangle') {
        node.fill(bData.bgcolor);
        // 幅・高さは上の node.width()/height() で設定済み（sceneFunc が箱に合わせて再描画）
    } else if (type === 'Image') {
        applyImageCover(node);
    }

    // 角の丸みの保存と適用
    if (['Rect', 'Button', 'Image'].includes(type)) {
        bData.cornerRadius = Math.max(0, parseInt(document.getElementById('ins-corner').value) || 0);
        applyCornerRadius(node, bData);
    }

    // 境界線(Stroke)の保存と適用
    if (['Rect', 'Circle', 'Button', 'Image', 'Label'].includes(type)) {
        if (!bData.stroke) bData.stroke = {};
        bData.stroke.on    = document.getElementById('ins-stroke-on').checked;
        bData.stroke.width = Math.max(0, parseFloat(document.getElementById('ins-stroke-width').value) || 0);
        bData.stroke.color = document.getElementById('ins-stroke-color').value;
        applyStroke(node, bData);
        document.getElementById('stroke-fields').style.display = bData.stroke.on ? 'block' : 'none';
    }

    // グラデーション設定の保存と適用（単色塗りの後に上書き）
    if (['Rect', 'Circle', 'Triangle', 'Button', 'Image'].includes(type)) {
        if (!bData.gradient) bData.gradient = {};
        bData.gradient.on   = document.getElementById('ins-grad-on').checked;
        bData.gradient.type = document.getElementById('ins-grad-type').value;
        bData.gradient.dir  = document.getElementById('ins-grad-dir').value;
        bData.gradient.c1   = document.getElementById('ins-grad-c1').value;
        bData.gradient.c2   = document.getElementById('ins-grad-c2').value;
        applyGradient(node, bData);  // Image は no-op（DOMオーバーレイ側で描画）
        // チェックON/種類変更に応じて詳細欄の表示を即時に切り替える（再選択を待たない）
        document.getElementById('grad-fields').style.display   = bData.gradient.on ? 'block' : 'none';
        document.getElementById('grad-dir-wrap').style.display = (bData.gradient.type === 'radial') ? 'none' : 'block';
    }

    // グラデーション文字（テキスト系）の保存と適用（単色fillの後に上書き）
    if (['Label', 'Button'].includes(type)) {
        if (!bData.gradText) bData.gradText = {};
        bData.gradText.on  = document.getElementById('ins-gt-on').checked;
        bData.gradText.dir = document.getElementById('ins-gt-dir').value;
        bData.gradText.c1  = document.getElementById('ins-gt-c1').value;
        bData.gradText.c2  = document.getElementById('ins-gt-c2').value;
        applyGradText(node, bData);
        document.getElementById('gradtext-fields').style.display = bData.gradText.on ? 'block' : 'none';
    }

    // 内側シャドウ / ベベル（Konva非対応。effect-overlay.js のDOM層でプレビュー＋出力CSSで反映）
    if (['Rect', 'Circle', 'Button', 'Image'].includes(type)) {
        if (!bData.innerShadow) bData.innerShadow = {};
        bData.innerShadow.on      = document.getElementById('ins-is-on').checked;
        bData.innerShadow.x       = parseFloat(document.getElementById('ins-is-x').value) || 0;
        bData.innerShadow.y       = parseFloat(document.getElementById('ins-is-y').value) || 0;
        bData.innerShadow.blur    = Math.max(0, parseFloat(document.getElementById('ins-is-blur').value) || 0);
        bData.innerShadow.color   = document.getElementById('ins-is-color').value;
        bData.innerShadow.opacity = Math.min(1, Math.max(0, parseFloat(document.getElementById('ins-is-opacity').value) || 0));
        document.getElementById('innershadow-fields').style.display = bData.innerShadow.on ? 'block' : 'none';

        if (!bData.bevel) bData.bevel = {};
        bData.bevel.on        = document.getElementById('ins-bevel-on').checked;
        bData.bevel.depth     = Math.max(1, parseFloat(document.getElementById('ins-bevel-depth').value) || 1);
        bData.bevel.dir       = document.getElementById('ins-bevel-dir').value;
        bData.bevel.highlight = document.getElementById('ins-bevel-hl').value;
        bData.bevel.shadow    = document.getElementById('ins-bevel-sh').value;
        bData.bevel.opacity   = Math.min(1, Math.max(0, parseFloat(document.getElementById('ins-bevel-opacity').value) || 0));
        document.getElementById('bevel-fields').style.display = bData.bevel.on ? 'block' : 'none';
    }

    // 光彩（グロー）の保存と適用（テキスト含む。ドロップシャドウが優先）
    if (['Rect', 'Circle', 'Button', 'Image', 'Label'].includes(type)) {
        if (!bData.glow) bData.glow = {};
        bData.glow.on      = document.getElementById('ins-glow-on').checked;
        bData.glow.blur    = Math.max(0, parseFloat(document.getElementById('ins-glow-blur').value) || 0);
        bData.glow.spread  = parseFloat(document.getElementById('ins-glow-spread').value) || 0;
        bData.glow.color   = document.getElementById('ins-glow-color').value;
        bData.glow.opacity = Math.min(1, Math.max(0, parseFloat(document.getElementById('ins-glow-opacity').value) || 0));
        applyGlow(node, bData);
        document.getElementById('glow-fields').style.display = bData.glow.on ? 'block' : 'none';
    }

    node.setAttr('bladeData', bData);
    tr.forceUpdate();
    layer.batchDraw();
    renderExplorer();

    // 編集デバイスに応じて、PC配置のバックアップ or スマホ配置を更新
    updatePcGeom(node);
    markMobileEdited(node);

    if (shouldSaveHistory) {
        saveHistory();
    }
}

// 背景画像の選択ダイアログ処理
window.changeButtonBgImage = async () => {
    if (selectedNodes.length !== 1 || selectedNodes[0].getAttr('uiType') !== 'Button') return;
    const node = selectedNodes[0];
    const bData = node.getAttr('bladeData');

    const dataUrl = await pickImageDialog();
    if (dataUrl) {
        bData.bgimage = dataUrl;
        const bgImgNode = node.findOne('.btn-bgimage');
        const bg = node.findOne('.btn-bg');
        
        if (bgImgNode) {
            const img = new Image();
            img.crossOrigin = 'Anonymous';
            img.onload = () => {
                bgImgNode.image(img);
                bgImgNode.visible(true);
                if (bg) bg.fill(null);
                layer.batchDraw();
            };
            img.src = dataUrl;
        }
        node.setAttr('bladeData', bData);
        updateInspectorFromNode();
        saveHistory();
    }
};

// 背景画像のクリア処理
window.clearButtonBgImage = () => {
    if (selectedNodes.length !== 1 || selectedNodes[0].getAttr('uiType') !== 'Button') return;
    const node = selectedNodes[0];
    const bData = node.getAttr('bladeData');

    bData.bgimage = '';
    const bgImgNode = node.findOne('.btn-bgimage');
    const bg = node.findOne('.btn-bg');

    if (bgImgNode) bgImgNode.visible(false);
    if (bg) bg.fill(bData.bgcolor);

    node.setAttr('bladeData', bData);
    layer.batchDraw();
    updateInspectorFromNode();
    saveHistory();
};


export function deleteSelectedNode() {
    if (selectedNodes.length === 0) return;
    selectedNodes.forEach(node => node.destroy());
    setSelectedNodes([]);
    tr.nodes([]);
    hideInspector();
    renderExplorer();
    layer.batchDraw();
    saveHistory();
}

export function alignNodes(alignType) {
    if (selectedNodes.length < 2) return;
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    
    selectedNodes.forEach(node => {
        const x = node.x(); const y = node.y(); const w = node.width(); const h = node.height();
        if (x < minX) minX = x; if (y < minY) minY = y;
        if (x + w > maxX) maxX = x + w; if (y + h > maxY) maxY = y + h;
    });

    selectedNodes.forEach(node => {
        const w = node.width(); const h = node.height();
        switch (alignType) {
            case 'left': node.x(minX); break;
            case 'center': node.x(minX + (maxX - minX) / 2 - w / 2); break;
            case 'right': node.x(maxX - w); break;
            case 'top': node.y(minY); break;
            case 'middle': node.y(minY + (maxY - minY) / 2 - h / 2); break;
            case 'bottom': node.y(maxY - h); break;
        }
        const bData = node.getAttr('bladeData');
        if (bData) node.setAttr('bladeData', bData);
    });

    tr.forceUpdate();
    layer.batchDraw();
    renderExplorer();
    saveHistory();
}

// 複数選択した要素を、指定間隔(px)で横/縦に等間隔配置する
export function distributeNodes(axis) {
    if (selectedNodes.length < 2) return;
    const gap = parseInt(document.getElementById('ins-multi-gap')?.value) || 0;
    const sorted = [...selectedNodes].sort((a, b) => (axis === 'x' ? a.x() - b.x() : a.y() - b.y()));
    let cursor = axis === 'x' ? sorted[0].x() : sorted[0].y();
    sorted.forEach(node => {
        if (axis === 'x') { node.x(cursor); cursor += node.width()  + gap; }
        else              { node.y(cursor); cursor += node.height() + gap; }
        const bData = node.getAttr('bladeData');
        if (bData) node.setAttr('bladeData', bData);
    });
    tr.forceUpdate();
    layer.batchDraw();
    renderExplorer();
    saveHistory();
}
