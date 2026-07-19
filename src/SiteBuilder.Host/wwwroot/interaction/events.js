// ============================================================
// イベントハンドラ
// ステージ操作 / キーボード / パネルドラッグ / 画像D&D / 右クリック
// ============================================================
import { stage, layer, tr, selectionRect } from '../canvas/canvas.js';
import { selectedNodes, setSelectedNodes, lastClickedNode, setLastClickedNode, currentCanvasWidth, currentCanvasHeight } from '../app/state.js';
import { applySelectedNodes, spawnElement, groupNodes, ungroupNodes, nextNumberForType } from '../nodes/elements.js';
import { saveHistory, undo, redo } from '../history/history.js';
import { deleteSelectedNode } from '../inspector/inspector.js';
import { renderExplorer } from '../explorer/explorer.js';
import { saveAndExport, importJSON, uploadImage } from '../project/api.js';
import { showToast } from '../ui/toast.js';
import { exitWarpMode, isWarpMode, getWarpTarget } from '../nodes/warp.js';
import { finalizeAfterTransform } from './transform-normalize.js';
import { startInlineTextEdit } from './text-edit.js';
import { copySelected, pasteClipboard } from './clipboard.js';

// ============================================================
// ステージ: トランスフォーム完了時のグループスケール正規化
// ============================================================

// Label / Button のリサイズを「scale」から「width/height + fontSize」へ正規化する。
// ドラッグ中(transform)に毎フレーム呼ぶことで、スケール操作中もリアルタイムに
// 文字サイズが変わる。フォント倍率は縦横の大きい方に追従（どの方向でも変化）。

// リサイズ完了（Transformer のイベントを直接使う＝stageに届かない環境でも確実）
tr.on('transformend', () => {
    if (vGuide) vGuide.style.display = 'none';
    if (hGuide) hGuide.style.display = 'none';
    finalizeAfterTransform(tr.nodes());
});

// 移動完了（ドラッグ）。対象は操作ノード＋選択中ノード（複数選択移動対応）
stage.on('dragend', (e) => {
    const touched = new Set();
    if (e.target && e.target.hasName && e.target.hasName('ui-element')) touched.add(e.target);
    selectedNodes.forEach(n => touched.add(n));
    finalizeAfterTransform([...touched]);
});

// ============================================================
// 【完全版】スマートスナップ（端・中央合わせ ＋ 等間隔配置）
// ============================================================
const SNAP_THRESHOLD = 6;

// Alt キーを押している間はスナップを一時無効化（端や他要素に吸着させず微調整したい時用）
let altHeld = false;
window.addEventListener('keydown', e => { if (e.key === 'Alt') altHeld = true; });
window.addEventListener('keyup',   e => { if (e.key === 'Alt') altHeld = false; });
window.addEventListener('blur',    () => { altHeld = false; });

let vGuide = document.getElementById('snap-v');
if (!vGuide) {
    vGuide = document.createElement('div');
    vGuide.id = 'snap-v';
    vGuide.className = 'snap-guideline vertical';
    document.getElementById('canvas-wrapper').appendChild(vGuide);
}
let hGuide = document.getElementById('snap-h');
if (!hGuide) {
    hGuide = document.createElement('div');
    hGuide.id = 'snap-h';
    hGuide.className = 'snap-guideline horizontal';
    document.getElementById('canvas-wrapper').appendChild(hGuide);
}

// ============================================================
// リサイズ時のスナップ（boundBoxFunc）
// ドラッグしている辺を、他要素の端・キャンバス端・ガイドに吸着させる。
// 変形パイプライン内で完結するため、変形ループと干渉しない（安定）。
// ============================================================
tr.boundBoxFunc((oldBox, newBox) => {
    // 最小サイズガード
    if (newBox.width < 12 || newBox.height < 12) return oldBox;
    // Alt 押下中はスナップせず自由にリサイズ（微調整）
    if (altHeld) { vGuide.style.display = 'none'; hGuide.style.display = 'none'; return newBox; }
    // 回転中はスナップしない
    if (newBox.rotation) { vGuide.style.display = 'none'; hGuide.style.display = 'none'; return newBox; }

    const zoom = stage.scaleX() || 1;
    const active = (typeof tr.getActiveAnchor === 'function') ? (tr.getActiveAnchor() || '') : '';

    // 吸着候補（要素座標）: キャンバス端 + 他要素の端 + ガイド
    const xs = [0, currentCanvasWidth];
    const ys = [0, currentCanvasHeight];
    const sel = tr.nodes();
    layer.getChildren().forEach(n => {
        if (!n.hasName('ui-element') || sel.includes(n) || n.visible() === false) return;
        const b = n.getClientRect({ relativeTo: layer });
        xs.push(b.x, b.x + b.width);
        ys.push(b.y, b.y + b.height);
    });
    if (typeof window.__getGuideXs === 'function') window.__getGuideXs().forEach(v => xs.push(v));
    if (typeof window.__getGuideYs === 'function') window.__getGuideYs().forEach(v => ys.push(v));

    // newBox は stage(スケール済み)座標 → 要素座標へ
    let left = newBox.x / zoom, top = newBox.y / zoom;
    let right = (newBox.x + newBox.width) / zoom, bottom = (newBox.y + newBox.height) / zoom;

    const nearest = (val, cands) => {
        let best = null, bd = SNAP_THRESHOLD;
        for (const c of cands) { const d = Math.abs(val - c); if (d < bd) { bd = d; best = c; } }
        return best;
    };

    let snapX = null, snapY = null;
    // 動いている辺だけ吸着する
    if (active.includes('left'))   { const s = nearest(left,   xs); if (s !== null) { left = s;   snapX = s; } }
    if (active.includes('right'))  { const s = nearest(right,  xs); if (s !== null) { right = s;  snapX = s; } }
    if (active.includes('top'))    { const s = nearest(top,    ys); if (s !== null) { top = s;    snapY = s; } }
    if (active.includes('bottom')) { const s = nearest(bottom, ys); if (s !== null) { bottom = s; snapY = s; } }

    // ガイド線の表示
    if (snapX !== null) { vGuide.style.display = 'block'; vGuide.style.left = Math.min(snapX * zoom, currentCanvasWidth * zoom - 1) + 'px'; } else vGuide.style.display = 'none';
    if (snapY !== null) { hGuide.style.display = 'block'; hGuide.style.top  = Math.min(snapY * zoom, currentCanvasHeight * zoom - 1) + 'px'; } else hGuide.style.display = 'none';

    const nb = { ...newBox, x: left * zoom, y: top * zoom, width: (right - left) * zoom, height: (bottom - top) * zoom };
    if (nb.width < 12 || nb.height < 12) return oldBox;
    return nb;
});

stage.on('dragmove', (e) => {
    const draggingNode = e.target;
    if (!draggingNode.hasName('ui-element') || selectedNodes.length > 1) return;
    // Alt 押下中はスナップせず自由に移動（微調整）
    if (altHeld || e.evt?.altKey) {
        if (vGuide) vGuide.style.display = 'none';
        if (hGuide) hGuide.style.display = 'none';
        return;
    }

    const zoom = stage.scaleX();
    let absMinX = draggingNode.x();
    let absMinY = draggingNode.y();
    let absMaxX = absMinX + draggingNode.width();
    let absMaxY = absMinY + draggingNode.height();
    let absMidX = absMinX + draggingNode.width() / 2;
    let absMidY = absMinY + draggingNode.height() / 2;

    let snapX = null, snapY = null;
    let showVLine = false, showHLine = false;
    let guideX = 0, guideY = 0;

    const targets = [];
    layer.getChildren().forEach(n => {
        if (!n.hasName('ui-element') || n === draggingNode || n.className === 'Transformer') return;
        const tData = n.getAttr('bladeData');
        if (n.visible() === false || (tData && tData.lock)) return; 
        
        targets.push({
            minX: n.x(), maxX: n.x() + n.width(),
            minY: n.y(), maxY: n.y() + n.height(),
            midX: n.x() + n.width() / 2, midY: n.y() + n.height() / 2,
        });
    });

    targets.forEach(t => {
        if (Math.abs(absMinX - t.minX) < SNAP_THRESHOLD) { snapX = t.minX; guideX = t.minX; showVLine = true; }
        else if (Math.abs(absMinX - t.maxX) < SNAP_THRESHOLD) { snapX = t.maxX; guideX = t.maxX; showVLine = true; }
        else if (Math.abs(absMaxX - t.minX) < SNAP_THRESHOLD) { snapX = t.minX - draggingNode.width(); guideX = t.minX; showVLine = true; }
        else if (Math.abs(absMaxX - t.maxX) < SNAP_THRESHOLD) { snapX = t.maxX - draggingNode.width(); guideX = t.maxX; showVLine = true; }
        else if (Math.abs(absMidX - t.midX) < SNAP_THRESHOLD) { snapX = t.midX - draggingNode.width() / 2; guideX = t.midX; showVLine = true; }

        if (Math.abs(absMinY - t.minY) < SNAP_THRESHOLD) { snapY = t.minY; guideY = t.minY; showHLine = true; }
        else if (Math.abs(absMinY - t.maxY) < SNAP_THRESHOLD) { snapY = t.maxY; guideY = t.maxY; showHLine = true; }
        else if (Math.abs(absMaxY - t.minY) < SNAP_THRESHOLD) { snapY = t.minY - draggingNode.height(); guideY = t.minY; showHLine = true; }
        else if (Math.abs(absMaxY - t.maxY) < SNAP_THRESHOLD) { snapY = t.maxY - draggingNode.height(); guideY = t.maxY; showHLine = true; }
        else if (Math.abs(absMidY - t.midY) < SNAP_THRESHOLD) { snapY = t.midY - draggingNode.height() / 2; guideY = t.midY; showHLine = true; }
    });

    // --- キャンバスの端・中央へスナップ ---
    const cw = currentCanvasWidth, ch = currentCanvasHeight;
    const dw = draggingNode.width(), dh = draggingNode.height();
    [[absMinX, 0, 0], [absMaxX, cw, -dw], [absMidX, cw / 2, -dw / 2]].forEach(([val, edge, off]) => {
        if (Math.abs(val - edge) < SNAP_THRESHOLD) { snapX = edge + off; guideX = edge; showVLine = true; }
    });
    [[absMinY, 0, 0], [absMaxY, ch, -dh], [absMidY, ch / 2, -dh / 2]].forEach(([val, edge, off]) => {
        if (Math.abs(val - edge) < SNAP_THRESHOLD) { snapY = edge + off; guideY = edge; showHLine = true; }
    });

    // --- ユーザーが定規から引いたガイドへスナップ ---
    const gxs = (typeof window.__getGuideXs === 'function') ? window.__getGuideXs() : [];
    const gys = (typeof window.__getGuideYs === 'function') ? window.__getGuideYs() : [];
    gxs.forEach(gx => {
        if      (Math.abs(absMinX - gx) < SNAP_THRESHOLD) { snapX = gx;          guideX = gx; showVLine = true; }
        else if (Math.abs(absMaxX - gx) < SNAP_THRESHOLD) { snapX = gx - dw;     guideX = gx; showVLine = true; }
        else if (Math.abs(absMidX - gx) < SNAP_THRESHOLD) { snapX = gx - dw / 2; guideX = gx; showVLine = true; }
    });
    gys.forEach(gy => {
        if      (Math.abs(absMinY - gy) < SNAP_THRESHOLD) { snapY = gy;          guideY = gy; showHLine = true; }
        else if (Math.abs(absMaxY - gy) < SNAP_THRESHOLD) { snapY = gy - dh;     guideY = gy; showHLine = true; }
        else if (Math.abs(absMidY - gy) < SNAP_THRESHOLD) { snapY = gy - dh / 2; guideY = gy; showHLine = true; }
    });

    for (let i = 0; i < targets.length; i++) {
        for (let j = 0; j < targets.length; j++) {
            if (i === j) continue;
            let t1 = targets[i];
            let t2 = targets[j];

            let overlapY = Math.max(0, Math.min(t1.maxY, t2.maxY) - Math.max(t1.minY, t2.minY));
            if (overlapY > 0 && t1.minX >= t2.maxX) {
                let gap = t1.minX - t2.maxX; 
                if (Math.abs(absMinX - (t1.maxX + gap)) < SNAP_THRESHOLD) {
                    snapX = t1.maxX + gap; showVLine = true; guideX = snapX;
                }
                else if (Math.abs(absMaxX - (t2.minX - gap)) < SNAP_THRESHOLD) {
                    snapX = t2.minX - gap - draggingNode.width(); showVLine = true; guideX = snapX + draggingNode.width();
                }
            }

            let overlapX = Math.max(0, Math.min(t1.maxX, t2.maxX) - Math.max(t1.minX, t2.minX));
            if (overlapX > 0 && t1.minY >= t2.maxY) {
                let gap = t1.minY - t2.maxY;
                if (Math.abs(absMinY - (t1.maxY + gap)) < SNAP_THRESHOLD) {
                    snapY = t1.maxY + gap; showHLine = true; guideY = snapY;
                }
                else if (Math.abs(absMaxY - (t2.minY - gap)) < SNAP_THRESHOLD) {
                    snapY = t2.minY - gap - draggingNode.height(); showHLine = true; guideY = snapY + draggingNode.height();
                }
            }
        }
    }

    if (snapX !== null) draggingNode.x(snapX);
    if (snapY !== null) draggingNode.y(snapY);

    if (showVLine) {
        vGuide.style.display = 'block';
        // 右端(x=幅)はキャンバス枠の外に出てクリップされるので1px内側に寄せて見せる
        vGuide.style.left = Math.min(guideX * zoom, currentCanvasWidth * zoom - 1) + 'px';
    } else {
        vGuide.style.display = 'none';
    }

    if (showHLine) {
        hGuide.style.display = 'block';
        hGuide.style.top = Math.min(guideY * zoom, currentCanvasHeight * zoom - 1) + 'px';
    } else {
        hGuide.style.display = 'none';
    }

    layer.batchDraw();
});

stage.on('dragend', () => {
    if (vGuide) vGuide.style.display = 'none';
    if (hGuide) hGuide.style.display = 'none';
});

// ============================================================
// ラバーバンド選択
// ============================================================
let x1, y1, x2, y2;

stage.on('mousedown touchstart', e => {
    if (e.target !== stage) return;
    e.evt.preventDefault();
    const pos  = stage.getPointerPosition();
    const zoom = stage.scaleX();
    x1 = pos.x / zoom; y1 = pos.y / zoom;
    x2 = x1; y2 = y1;
    selectionRect.width(0).height(0).visible(true);
});

stage.on('mousemove touchmove', e => {
    if (!selectionRect.visible()) return;
    e.evt.preventDefault();
    const pos  = stage.getPointerPosition();
    const zoom = stage.scaleX();
    x2 = pos.x / zoom; y2 = pos.y / zoom;
    selectionRect.setAttrs({
        x: Math.min(x1, x2), y: Math.min(y1, y2),
        width: Math.abs(x2 - x1), height: Math.abs(y2 - y1),
    });
});

stage.on('mouseup touchend', e => {
    if (!selectionRect.visible()) return;
    e.evt.preventDefault();
    setTimeout(() => selectionRect.visible(false));
    const box         = selectionRect.getClientRect();
    const newSelected = layer.getChildren().filter(
        node => node.hasName('ui-element') && Konva.Util.haveIntersection(box, node.getClientRect())
    );
    // バンドで要素を掴めた→選択、何も掴めなかった（＝空白クリック/空バンド）→単体含め選択解除
    if (newSelected.length > 0) applySelectedNodes(newSelected);
    else applySelectedNodes([]);
});

// クリックで要素選択
stage.on('click', e => {
    if (selectionRect.visible()) return;
    let node = e.target;

    // Warp ハンドルをクリックした場合は通常選択処理をスキップ（ハンドル自体のdragが効く）
    if (node.name && node.name() === 'warp-handle') return;

    if (node.getParent?.()?.className === 'Transformer') return;
    while (node.parent?.nodeType === 'Group' && node.parent.hasName('ui-element')) {
        node = node.parent;
    }

    // Warpモード中に対象以外の要素／空白をクリックしたら Warp を抜ける
    if (isWarpMode()) {
        const warpTarget = getWarpTarget();
        const clickedSelf = node === warpTarget;
        if (!clickedSelf) {
            exitWarpMode();
        }
    }

    if (node.hasName?.('ui-element')) {
        let newSelection = [...selectedNodes];
        const ctrlKey = e.evt.ctrlKey || e.evt.metaKey;
        const shiftKey = e.evt.shiftKey;

        if (ctrlKey) {
            // Ctrl+クリック: 個別トグル（追加/除外）
            const index = newSelection.indexOf(node);
            if (index >= 0) newSelection.splice(index, 1);
            else newSelection.push(node);
        } else if (shiftKey && lastClickedNode) {
            // Shift+クリック: 同じ親階層内で、直前選択ノード〜今クリックノードの間を範囲選択
            const parent = node.getParent();
            const lastParent = lastClickedNode.getParent();
            if (parent === lastParent) {
                const siblings = parent.getChildren().filter(c => c.hasName('ui-element'));
                const idxA = siblings.indexOf(lastClickedNode);
                const idxB = siblings.indexOf(node);
                if (idxA >= 0 && idxB >= 0) {
                    const [from, to] = idxA < idxB ? [idxA, idxB] : [idxB, idxA];
                    const range = siblings.slice(from, to + 1);
                    // 既存選択にrangeをマージ
                    const set = new Set(newSelection);
                    range.forEach(r => set.add(r));
                    newSelection = Array.from(set);
                } else {
                    newSelection = [node];
                }
            } else {
                // 親が違う場合は単一選択にフォールバック
                newSelection = [node];
            }
        } else {
            newSelection = [node];
        }
        setLastClickedNode(node);
        applySelectedNodes(newSelection);
    } else {
        setLastClickedNode(null);
        applySelectedNodes([]);
    }
});

// ============================================================
// ダブルクリックで Label/Button のテキスト内容をインライン編集
// ============================================================
stage.on('dblclick dbltap', e => {
    let node = e.target;
    if (node.getParent?.()?.className === 'Transformer') return;
    // 子要素(btn-bg/btn-text/内部Rect/Text)から ui-element まで親に登る
    while (node.parent?.nodeType === 'Group' && node.parent.hasName?.('ui-element')) {
        node = node.parent;
    }
    if (!node.hasName?.('ui-element')) return;

    const type = node.getAttr('uiType');
    if (type !== 'Label' && type !== 'Button') return;

    startInlineTextEdit(node);
});


document.getElementById('workspace').addEventListener('mousedown', e => {
    // 余白（ワークスペース／ペインやキャンバスの背景）を直接クリックした時だけ選択解除する。
    // パネル・入力欄・golden-layout のスプリッタ/ヘッダ等を触った時は選択を維持
    // （複数選択中にペイン境界やウィンドウをリサイズしても選択が外れないようにする）。
    const bgIds = ['workspace', 'gl-root', 'canvas-area', 'canvas-wrapper'];
    if (!bgIds.includes(e.target.id)) return;
    if (selectedNodes.length > 0) applySelectedNodes([]);
});

// ============================================================
// コピー＆ペースト
// ============================================================

// ============================================================
// キーボードショートカット
// ============================================================
window.addEventListener('keydown', e => {
    if (e.ctrlKey && (e.key === 's' || e.key === 'S')) {
        e.preventDefault();
        saveAndExport();
        return;
    }
    if (document.activeElement.tagName === 'INPUT' || document.activeElement.tagName === 'TEXTAREA') return;

    if      (e.key === 'Delete' || e.key === 'Backspace')         { deleteSelectedNode(); }
    else if (e.ctrlKey && (e.key === 'c' || e.key === 'C'))       { copySelected(); }
    else if (e.ctrlKey && (e.key === 'v' || e.key === 'V'))       { pasteClipboard(); }
    else if (e.ctrlKey && (e.key === 'y' || e.key === 'Y'))       { redo(); }
    else if (e.ctrlKey && (e.key === 'z' || e.key === 'Z'))       { e.shiftKey ? redo() : undo(); }
});

// ============================================================
// 画像のドラッグ＆ドロップ追加
// ============================================================
document.getElementById('workspace').addEventListener('dragover', e => e.preventDefault());
document.getElementById('workspace').addEventListener('drop', async e => {
    e.preventDefault();
    const file = e.dataTransfer.files?.[0];
    if (!file?.type.startsWith('image/')) return;

    const imageUrl = await uploadImage(file);
    if (!imageUrl) return;

    const img = new Image();
    img.onload = () => {
        const stageBox = stage.container().getBoundingClientRect();
        const zoom = stage.scaleX();
        let x = (e.clientX - stageBox.left) / zoom;
        let y = (e.clientY - stageBox.top)  / zoom;
        let w = img.width;
        let h = img.height;
        if (w > 300) { h = h * (300 / w); w = 300; }

        // spawnElement('Image', loadData) を再利用し、ツールボタン経由と同一の
        // bladeData 構造（_pcGeom / layouts / events 等を含む）で生成する。
        // これでレスポンシブ出力やシリアライズの不整合を防ぐ。
        const count = nextNumberForType('Image');
        const loadData = {
            id: 'image_' + count,
            transform: { x: x > 0 ? x : 50, y: y > 0 ? y : 50, width: w, height: h },
            properties: {
                name: '画像 ' + count, text: imageUrl,
                bgcolor: '#ffffff', color: '#000000', fontsize: 16,
                align: 'left', fontfamily: 'sans-serif', lock: false,
                route: '#', method: 'POST', event: 'none',
                shadow: 'none', animation: 'none', bgimage: '',
                layouts: {}, mobileEdited: false, visible: true, events: [],
            },
        };
        // loadData 指定時は spawnElement が自動選択/履歴保存をしないので、後で明示的に行う
        const newNode = spawnElement('Image', loadData, layer, false, true);
        applySelectedNodes([newNode]);
        saveHistory();
        showToast('画像を追加しました。');
    };
    img.src = imageUrl;
});

// （旧フローティングパネルのドラッグ/スナップ/ドッキング処理は
//   golden-layout 導入で不要になったため削除）

// ============================================================
// 右クリック（コンテキストメニュー）の制御
// ============================================================
const contextMenu = document.getElementById('context-menu');
if (contextMenu) {
    stage.on('contextmenu', (e) => {
        e.evt.preventDefault();
        let node = e.target;
        if (node === stage) {
            applySelectedNodes([]);
        } else {
            if (node.getParent?.()?.className === 'Transformer') return;
            while (node.parent?.nodeType === 'Group' && node.parent.hasName('ui-element')) {
                node = node.parent;
            }
            if (node.hasName?.('ui-element') && !selectedNodes.includes(node)) {
                applySelectedNodes([node]);
            }
        }
        contextMenu.style.display = 'block';
        contextMenu.style.left = e.evt.clientX + 'px';
        contextMenu.style.top  = e.evt.clientY + 'px';
    });

    window.addEventListener('click', () => {
        contextMenu.style.display = 'none';
    });

    document.getElementById('menu-bring-front').onclick = () => {
        if (selectedNodes.length === 0) return;
        selectedNodes.forEach(node => node.moveToTop());
        tr.moveToTop();
        layer.batchDraw();
        renderExplorer();
        saveHistory();
    };

    document.getElementById('menu-send-back').onclick = () => {
        if (selectedNodes.length === 0) return;
        selectedNodes.forEach(node => node.moveToBottom());
        layer.batchDraw();
        renderExplorer();
        saveHistory();
    };

    document.getElementById('menu-copy').onclick    = copySelected;
    document.getElementById('menu-paste').onclick   = pasteClipboard;
    document.getElementById('menu-delete').onclick  = deleteSelectedNode;
    document.getElementById('menu-group').onclick   = groupNodes;
    document.getElementById('menu-ungroup').onclick = ungroupNodes;

    // 🎨 レイヤースタイル: フローティングダイアログを開く
    const menuLayerStyle = document.getElementById('menu-layerstyle');
    if (menuLayerStyle) menuLayerStyle.onclick = () => window.openLayerStyleDialog?.();
}

// ============================================================
// Bladeテンプレートから呼ばれるグローバル関数をエクスポート
// ============================================================
export { groupNodes, ungroupNodes, deleteSelectedNode, saveAndExport, importJSON };

// ============================================================
// 【追加】ページエクスプローラーからリンク先URLへのD&D流し込み制御
// ============================================================
setTimeout(() => {
    const routeInput = document.getElementById('ins-route');
    if (routeInput) {
        // ドラッグが乗っかった時
        routeInput.addEventListener('dragover', (e) => {
            e.preventDefault();
            routeInput.style.borderColor = '#00a8ff';
            routeInput.style.backgroundColor = 'rgba(0, 168, 255, 0.1)';
        });

        // 離れた時
        routeInput.addEventListener('dragleave', () => {
            routeInput.style.borderColor = '#555';
            routeInput.style.backgroundColor = '#1e1e1e';
        });

        // ドロップされた時
        routeInput.addEventListener('drop', (e) => {
            e.preventDefault();
            routeInput.style.borderColor = '#555';
            routeInput.style.backgroundColor = '#1e1e1e';
            
            const pagePath = e.dataTransfer.getData('text/plain');
            if (pagePath && !pagePath.startsWith('data:image')) {
                // ドロップされたテキストをインプットに適用
                routeInput.value = pagePath;
                // インスペクターのデータ同期を呼び出して確定（履歴保存を走らせる）
                if (typeof onInspectorUpdate === 'function') {
                    onInspectorUpdate(true);
                }
                showToast(`リンク先を 「${pagePath}」 に設定しました。`);
            }
        });
    }
}, 500);
