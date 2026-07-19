// ============================================================
// transform-normalize.js - トランスフォーム完了時の正規化
// scale を width/height + fontSize へ変換し、変形後にグラデ再計算・履歴保存する。
// リスナー登録は events.js 側、ここは処理本体（finalizeAfterTransform）を提供する。
// ============================================================
import { layer, tr } from '../canvas/canvas.js';
import { applyImageCover, applyGradient } from '../nodes/node-style.js';
import { updateInspectorFromNode } from '../inspector/inspector.js';
import { renderExplorer } from '../explorer/explorer.js';
import { markMobileEdited, updatePcGeom } from '../canvas/display.js';
import { saveHistory } from '../history/history.js';

function normalizeResize(node) {
    const type = node.getAttr('uiType');
    const sx = node.scaleX(), sy = node.scaleY();
    if (sx === 1 && sy === 1) return;

    // フォント倍率: どの方向のドラッグでも文字サイズが変わるよう、縦横の大きい方を使う
    const fontFactor = Math.max(sx, sy);

    if (type === 'Label') {
        node.width(Math.max(5, node.width()  * sx));
        node.height(Math.max(5, node.height() * sy));
        node.scaleX(1); node.scaleY(1);
        const newFont = Math.max(8, node.fontSize() * fontFactor);
        node.fontSize(newFont);
        const b = node.getAttr('bladeData'); if (b) b.fontsize = newFont;
    } else if (type === 'Button') {
        node.width(Math.max(5, node.width()  * sx));
        node.height(Math.max(5, node.height() * sy));
        node.scaleX(1); node.scaleY(1);
        const bg  = node.findOne('.btn-bg');
        const txt = node.findOne('.btn-text');
        if (bg) { bg.width(node.width()); bg.height(node.height()); }
        if (txt) {
            txt.width(node.width()); txt.height(node.height());
            const newFont = Math.max(8, txt.fontSize() * fontFactor);
            txt.fontSize(newFont);
            const b = node.getAttr('bladeData'); if (b) b.fontsize = newFont;
        }
    }
}

// ※ ドラッグ中のリアルタイム正規化は、Transformer の内部計算と干渉して
//    フォント倍率が累積・巨大化するため行わない。整形は完了時(transformend)に
//    一度だけ行う（下の tr.on('transformend')）。

// 1ノードのスケールを width/height(+font) に正規化する（Image はカバー再計算）
function normalizeNode(node) {
    const type = node.getAttr('uiType');
    if (type === 'Image') {
        // スケールを width/height に確定してから、アスペクト維持で表示を整える
        const sx = node.scaleX(), sy = node.scaleY();
        if (sx !== 1 || sy !== 1) {
            node.width(node.width() * sx);
            node.height(node.height() * sy);
            node.scaleX(1); node.scaleY(1);
        }
        applyImageCover(node);
        return;
    }
    if (type === 'Label' || type === 'Button') { normalizeResize(node); return; }
    if (type === 'Triangle') {
        // ハンドルでの拡大縮小(scale)を width/height に確定する（幅・高さが正しく反映される）
        const sx = node.scaleX(), sy = node.scaleY();
        if (sx !== 1 || sy !== 1) {
            node.width(Math.max(5, node.width() * sx));
            node.height(Math.max(5, node.height() * sy));
            node.scaleX(1); node.scaleY(1);
        }
        return;
    }
    if (type !== 'Group') return;

    const scaleX = node.scaleX();
    const scaleY = node.scaleY();
    if (scaleX === 1 && scaleY === 1) return;
    node.getChildren().forEach(child => {
        if (!child.hasName('ui-element')) return;
        child.x(child.x() * scaleX);
        child.y(child.y() * scaleY);
        child.width(child.width()  * scaleX);
        child.height(child.height() * scaleY);
        const childType = child.getAttr('uiType');
        if (childType === 'Label') {
            child.fontSize(child.fontSize() * Math.max(scaleX, scaleY));
            child.getAttr('bladeData').fontsize = child.fontSize();
        } else if (childType === 'Button') {
            const bg = child.findOne('.btn-bg');
            const txt = child.findOne('.btn-text');
            if (bg) { bg.width(child.width()); bg.height(child.height()); }
            if (txt) { txt.width(child.width()); txt.height(child.height()); }
        }
    });
    node.width(node.width()   * scaleX);
    node.height(node.height() * scaleY);
    node.scaleX(1);
    node.scaleY(1);
}

// 変形/移動の完了処理（正規化→インスペクタ更新→履歴保存）
export function finalizeAfterTransform(nodes) {
    nodes.forEach(normalizeNode);
    // サイズが変わったのでグラデーションの起点/終点を再計算
    nodes.forEach(n => applyGradient(n, n.getAttr('bladeData')));
    updateInspectorFromNode();
    renderExplorer();
    layer.batchDraw();
    nodes.forEach(node => { updatePcGeom(node); markMobileEdited(node); });
    saveHistory();
}
