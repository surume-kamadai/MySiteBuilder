// ============================================================
// キャンバスサイズ / ズーム / パンニング
// ============================================================
import { stage, layer } from './canvas.js';
import { setCurrentCanvasWidth, setCurrentCanvasHeight, currentDevice, setCurrentDevice, selectedNodes } from '../app/state.js';
import { syncNodeToLayout, applySelectedNodes } from '../nodes/elements.js';
import { updateInspectorFromNode } from '../inspector/inspector.js';

// Webフォント(Google Fonts)の読込完了後にキャンバスを再描画してテキストへ反映
if (typeof document !== 'undefined' && document.fonts && document.fonts.ready) {
    document.fonts.ready.then(() => { try { layer.batchDraw(); } catch (e) {} });
}

// --- キャンバスサイズ・ズーム ---

export function updateCanvasSize() {
    const zoom = (parseFloat(document.getElementById('canvas-zoom').value) || 100) / 100;

    // 現在のデバイスに応じて、PC欄かスマホ欄のサイズを使う
    let w, h;
    if (currentDevice === 'mobile') {
        w = parseInt(document.getElementById('canvas-mobile-width').value)  || 375;
        h = parseInt(document.getElementById('canvas-mobile-height').value) || 800;
    } else {
        w = parseInt(document.getElementById('canvas-width').value)  || 800;
        h = parseInt(document.getElementById('canvas-height').value) || 600;
    }

    setCurrentCanvasWidth(w);
    setCurrentCanvasHeight(h);

    stage.scale({ x: zoom, y: zoom });
    stage.width(w  * zoom);
    stage.height(h * zoom);
    document.getElementById('canvas-wrapper').style.width  = (w * zoom) + 'px';
    document.getElementById('canvas-wrapper').style.height = (h * zoom) + 'px';
    layer.batchDraw();
}

// Ctrl + ホイールでズーム
window.addEventListener('wheel', e => {
    if (!(e.ctrlKey || e.metaKey)) return;
    if (!e.target.closest('#canvas-area') && !e.target.closest('#canvas-wrapper')) return;
    e.preventDefault();

    const zoomInput = document.getElementById('canvas-zoom');
    let z = parseFloat(zoomInput.value) || 100;
    z = Math.min(500, Math.max(10, z + (e.deltaY < 0 ? 10 : -10)));
    zoomInput.value = z;
    updateCanvasSize();
}, { passive: false });

// --- パンニング (Space + ドラッグ / ホイールクリック) ---

const canvasArea = document.getElementById('canvas-area');
let isPanning = false, isSpaceDown = false;
let panStartX, panStartY, panScrollLeft, panScrollTop;

window.addEventListener('keydown', e => {
    if (e.code === 'Space'
        && document.activeElement.tagName !== 'INPUT'
        && document.activeElement.tagName !== 'TEXTAREA') {
        e.preventDefault();
        if (!isSpaceDown) {   // 押しっぱなしの繰り返しkeydownでは1回だけ処理
            isSpaceDown = true;
            // グローバルフラグ: applySelectedNodes等がSpace中にdraggableを復活させないようガードする
            window.__spaceDown = true;
            canvasArea.style.cursor = 'grab';
            // Space中はパン専用にし、オブジェクトを掴んで動かせないようにする
            layer.find('.ui-element').forEach(n => n.draggable(false));
        }
    }
});

window.addEventListener('keyup', e => {
    if (e.code !== 'Space') return;
    isSpaceDown = false;
    window.__spaceDown = false;
    if (!isPanning) canvasArea.style.cursor = 'default';
    // ドラッグ可否を選択状態から正しく再計算して元に戻す
    applySelectedNodes(selectedNodes);
});

canvasArea.addEventListener('mousedown', e => {
    if (e.button !== 1 && !(e.button === 0 && isSpaceDown)) return;
    e.preventDefault();
    isPanning     = true;
    panStartX     = e.pageX - canvasArea.offsetLeft;
    panStartY     = e.pageY - canvasArea.offsetTop;
    panScrollLeft = canvasArea.scrollLeft;
    panScrollTop  = canvasArea.scrollTop;
    canvasArea.style.cursor = 'grabbing';
});

window.addEventListener('mousemove', e => {
    if (!isPanning) return;
    e.preventDefault();
    canvasArea.scrollLeft = panScrollLeft - (e.pageX - canvasArea.offsetLeft - panStartX);
    canvasArea.scrollTop  = panScrollTop  - (e.pageY - canvasArea.offsetTop  - panStartY);
});

window.addEventListener('mouseup', () => {
    if (!isPanning) return;
    isPanning = false;
    canvasArea.style.cursor = isSpaceDown ? 'grab' : 'default';
});
// ============================================================
// レスポンシブ (PC / スマホ表示切替)
// ============================================================

// 現在のキャンバス上の配置を、データ(bladeData.layouts)に保存する
// 現在のキャンバス上の配置を、データに保存する。
// PC編集中はノードの位置がそのまま transform(=PC正データ) になるので何もしない。
// スマホ編集中は layouts.mobile に保存する（mobileEditedフラグは個別操作で立てる）。
export function saveCurrentDeviceLayout() {
    if (currentDevice !== 'mobile') return;
    layer.getChildren().forEach(node => {
        if (!node.hasName('ui-element')) return;
        saveLayoutRecursive(node, 'mobile');
    });
}

function saveLayoutRecursive(node, device) {
    const bData = node.getAttr('bladeData');
    if (bData) {
        const geo = getNodeGeometry(node);
        if (!bData.layouts) bData.layouts = {};
        bData.layouts[device] = {
            x: geo.x, y: geo.y, w: geo.w, h: geo.h,
            fontsize: bData.fontsize || 16,
        };
        node.setAttr('bladeData', bData);
    }
    if (node.getAttr('uiType') === 'Group') {
        node.getChildren().forEach(child => {
            if (child.hasName('ui-element')) saveLayoutRecursive(child, device);
        });
    }
}

// ノード単体をスマホ配置として保存し、mobileEditedフラグを立てる。
// ユーザーが実際に動かしたタイミング(dragend/transformend/インスペクター更新)で呼ぶ。
export function markMobileEdited(node) {
    if (currentDevice !== 'mobile' || !node) return;
    const bData = node.getAttr('bladeData');
    if (!bData) return;
    const geo = getNodeGeometry(node);
    if (!bData.layouts) bData.layouts = {};
    bData.layouts.mobile = {
        x: geo.x, y: geo.y, w: geo.w, h: geo.h,
        fontsize: bData.fontsize || 16,
    };
    bData.mobileEdited = true;
    node.setAttr('bladeData', bData);
}

// PC編集時、ノードのPC配置を _pcGeom に同期する（出力時のPC復元用）。
// ユーザーがPC表示で動かした直後に呼ぶ。
export function updatePcGeom(node) {
    if (currentDevice !== 'pc' || !node) return;
    const bData = node.getAttr('bladeData');
    if (!bData) return;
    const geo = getNodeGeometry(node);
    bData._pcGeom = { x: geo.x, y: geo.y, w: geo.w, h: geo.h };
    node.setAttr('bladeData', bData);
}

// ノードの「左上座標・実サイズ」を、タイプ差（中心座標）を吸収して返す
function getNodeGeometry(node) {
    const type = node.getAttr('uiType');
    if (type === 'Circle') {
        const rx = node.radiusX(), ry = node.radiusY();
        return { x: node.x() - rx, y: node.y() - ry, w: rx * 2, h: ry * 2 };
    }
    return { x: node.x(), y: node.y(), w: node.width(), h: node.height() };
}

// 💻 / 📱 ボタンを押したときの切り替え処理
export function switchDevice(device) {
    if (currentDevice === device) return;

    if (currentDevice === 'pc' && device === 'mobile') {
        // PC→スマホ: PC配置を _pcGeom にバックアップ
        layer.getChildren().forEach(node => {
            if (!node.hasName('ui-element')) return;
            backupPcGeomRecursive(node);
        });
    } else if (currentDevice === 'mobile' && device === 'pc') {
        // スマホ→PC: スマホで動かした分を layouts.mobile に確定保存しておく
        saveCurrentDeviceLayout();
    }

    setCurrentDevice(device);
    updateCanvasSize();

    // ノードの配置を新しいデバイスに合わせて復元する
    layer.getChildren().forEach(node => {
        if (!node.hasName('ui-element')) return;
        if (device === 'pc') {
            // PCに戻す: _pcGeom から復元（無い要素は今の位置のままでOK＝もともとPC位置）
            restoreFromPcGeomRecursive(node);
        } else {
            // スマホに切替: layouts.mobile があれば復元（無ければPC位置のまま見せる）
            syncNodeToLayout(node, 'mobile');
        }
    });

    document.getElementById('btn-device-pc').style.opacity = device === 'pc' ? '1' : '0.4';
    document.getElementById('btn-device-mobile').style.opacity = device === 'mobile' ? '1' : '0.4';

    layer.batchDraw();
    updateInspectorFromNode();
}

// _pcGeom からPC配置に戻す（スマホ→PC切替時）
function restoreFromPcGeomRecursive(node) {
    const bData = node.getAttr('bladeData');
    const pc = bData?._pcGeom;
    if (pc) {
        const type = node.getAttr('uiType');
        if (type === 'Circle') {
            node.radiusX(pc.w / 2); node.radiusY(pc.h / 2);
            node.x(pc.x + pc.w / 2); node.y(pc.y + pc.h / 2);
        } else {
            node.x(pc.x); node.y(pc.y); node.width(pc.w); node.height(pc.h);
        }
    }
    if (node.getAttr('uiType') === 'Group') {
        node.getChildren().forEach(c => {
            if (c.hasName('ui-element')) restoreFromPcGeomRecursive(c);
        });
    }
}

// PC配置をノードのbladeDataに退避する（出力時のPC復元のため）
function backupPcGeomRecursive(node) {
    const bData = node.getAttr('bladeData');
    if (bData) {
        const geo = getNodeGeometry(node);
        bData._pcGeom = { x: geo.x, y: geo.y, w: geo.w, h: geo.h };
        node.setAttr('bladeData', bData);
    }
    if (node.getAttr('uiType') === 'Group') {
        node.getChildren().forEach(c => {
            if (c.hasName('ui-element')) backupPcGeomRecursive(c);
        });
    }
}