// ============================================================
// dock-layout.js
// golden-layout で VS Code 風ドッキングUIを構築する。
// 既存パネルのDOM（#panel-source内）を各ペインへ移植して使う。
// ============================================================

// パネルID → ペインのタイトル
const PANE_DEFS = {
    'pane-tools':     { sourceId: 'panel-tools',     title: 'ツール' },
    'pane-pages':     { sourceId: 'panel-pages',     title: 'ページ' },
    'pane-explorer':  { sourceId: 'panel-explorer',  title: 'エクスプローラー' },
    'pane-canvas':    { sourceId: 'canvas-area',     title: 'キャンバス' },
    'pane-settings':  { sourceId: 'panel-canvas',    title: 'プロジェクト設定' },
    'pane-inspector': { sourceId: 'panel-inspector', title: 'プロパティ' },
};

let glInstance = null;
let onCanvasResize = null; // キャンバスのリサイズ追従コールバック

// パネルの中身（panel-content）だけを取り出してペインに入れる。
// 閉じられた時は中身を #panel-source に戻して状態を保持する。
function mountSource(container, sourceId, compName) {
    const src = document.getElementById(sourceId);
    if (!src) {
        container.getElement().html(`<div style="padding:10px; color:#f55;">${sourceId} が見つかりません</div>`);
        return null;
    }
    const wrapper = document.createElement('div');
    wrapper.style.cssText = 'width:100%; height:100%; overflow:auto; box-sizing:border-box;';

    // 移植する中身のノードを記録（閉じる時に戻すため）
    const movedNodes = [];

    if (sourceId === 'canvas-area') {
        wrapper.style.overflow = 'hidden';
        wrapper.appendChild(src);
        src.style.display = 'block';
        movedNodes.push(src);
    } else {
        const contents = src.querySelectorAll('.panel-content');
        contents.forEach(c => { wrapper.appendChild(c); movedNodes.push(c); });
    }
    container.getElement().append(wrapper);

    // パネルが閉じられたら、中身を元の #panel-source 配下へ戻す
    container.on('destroy', () => {
        movedNodes.forEach(n => src.appendChild(n));
        openState[compName] = false;
        // ネイティブメニューのチェックを同期
        window.electronAPI?.notifyPanelState?.(compName, false);
    });

    return wrapper;
}

// 各ペインの開閉状態
const openState = {
    'pane-tools': true, 'pane-pages': true, 'pane-explorer': true,
    'pane-canvas': true, 'pane-settings': true, 'pane-inspector': true,
};

export function initDockLayout(resizeCallback) {
    onCanvasResize = resizeCallback;

    if (typeof window.GoldenLayout === 'undefined') {
        console.error('[dock-layout] GoldenLayout が読み込まれていません。CDNを確認してください。');
        return null;
    }
    const root = document.getElementById('gl-root');
    if (!root) {
        console.error('[dock-layout] #gl-root が見つかりません');
        return null;
    }

    // gl-root のサイズが確定するまで待ってから初期化する
    const tryInit = (attempt = 0) => {
        const w = root.clientWidth, h = root.clientHeight;
        if ((w === 0 || h === 0) && attempt < 30) {
            // まだサイズが0 → 次フレームで再試行
            requestAnimationFrame(() => tryInit(attempt + 1));
            return;
        }
        console.log(`[dock-layout] 初期化開始 (gl-root: ${w}×${h})`);
        buildLayout(root);
    };
    tryInit();

    return true;
}

function buildLayout(root) {
    const config = {
        settings: {
            showPopoutIcon: false,
            showMaximiseIcon: true,
            showCloseIcon: false, // パネルを誤って閉じられないように
        },
        dimensions: { headerHeight: 28, borderWidth: 4 },
        content: [{
            type: 'row',
            content: [
                {
                    type: 'column', width: 18,
                    content: [
                        { type: 'component', componentName: 'pane-tools',    title: PANE_DEFS['pane-tools'].title,    height: 34 },
                        { type: 'component', componentName: 'pane-pages',    title: PANE_DEFS['pane-pages'].title,    height: 33 },
                        { type: 'component', componentName: 'pane-explorer', title: PANE_DEFS['pane-explorer'].title, height: 33 },
                    ]
                },
                {
                    type: 'column', width: 60,
                    content: [
                        { type: 'component', componentName: 'pane-canvas', title: PANE_DEFS['pane-canvas'].title, isClosable: false },
                    ]
                },
                {
                    type: 'column', width: 22,
                    content: [
                        { type: 'component', componentName: 'pane-settings',  title: PANE_DEFS['pane-settings'].title,  height: 40 },
                        { type: 'component', componentName: 'pane-inspector', title: PANE_DEFS['pane-inspector'].title, height: 60 },
                    ]
                },
            ]
        }]
    };

    const layout = new GoldenLayout(config, root);
    glInstance = layout;

    Object.keys(PANE_DEFS).forEach(compName => {
        const def = PANE_DEFS[compName];
        layout.registerComponent(compName, function(container) {
            mountSource(container, def.sourceId, compName);
            openState[compName] = true;

            if (compName === 'pane-canvas') {
                const fire = () => { if (onCanvasResize) onCanvasResize(); };
                container.on('resize', fire);
                container.on('open', () => {
                    setTimeout(() => {
                        const wrap = document.getElementById('canvas-wrapper');
                        if (wrap && typeof ResizeObserver !== 'undefined') {
                            const ro = new ResizeObserver(fire);
                            ro.observe(wrap);
                        }
                        fire();
                    }, 60);
                });
            }
        });
    });

    try {
        layout.init();
        console.log('[dock-layout] レイアウト初期化完了');
    } catch (err) {
        console.error('[dock-layout] init失敗:', err);
    }

    window.addEventListener('resize', () => layout.updateSize());
}

export function getDockLayout() {
    return glInstance;
}

// ============================================================
// パネルの再表示（閉じたパネルを復活させる）
// ============================================================
export function showPanel(compName) {
    if (!glInstance) return;
    if (openState[compName]) return; // 既に開いている

    const def = PANE_DEFS[compName];
    if (!def) return;

    const root = glInstance.root;
    let target = root.contentItems[0]; // 最上位（row）

    const newItemConfig = {
        type: 'component',
        componentName: compName,
        title: def.title,
    };

    try {
        if (target && target.addChild) {
            target.addChild(newItemConfig);
        } else {
            root.addChild(newItemConfig);
        }
        openState[compName] = true;
        window.electronAPI?.notifyPanelState?.(compName, true);
    } catch (err) {
        console.error('[dock-layout] パネル再表示失敗:', err);
    }
}

// パネルを閉じる
export function hidePanel(compName) {
    if (!glInstance) return;
    if (!openState[compName]) return; // 既に閉じている
    const items = glInstance.root.getItemsByFilter(
        item => item.isComponent && item.componentName === compName
    );
    items.forEach(item => item.remove());
    // destroy ハンドラ側で openState=false と notifyPanelState が走る
}