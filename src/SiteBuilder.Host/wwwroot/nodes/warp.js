// ============================================================
// warp.js - 図形の頂点変形（多角形化して個別に動かす）
//
// 対象の図形を Konva.Line(closed) として扱い、
// 各頂点にドラッグハンドルを表示して自由に動かせるようにする。
// 四角形(4点)、三角形(3点)、円(12点の近似ポリゴン) に対応。
// 出力時は clip-path: polygon() で形を再現する。
// ============================================================
import { layer } from '../canvas/canvas.js';
import { saveHistory } from '../history/history.js';

let warpHandles = [];      // 現在表示中のハンドル群
let warpTargetNode = null; // 変形対象のノード

// ハンドルを全消去
function clearHandles() {
    warpHandles.forEach(h => h.destroy());
    warpHandles = [];
    warpTargetNode = null;
    layer.batchDraw();
}

// 自由変形モードに入る
export function enterWarpMode(node) {
    clearHandles();
    if (!node) return;

    const type = node.getAttr('uiType');
    // 対象は Rect / Circle / Triangle / Warp のみ
    if (!['Rect', 'Circle', 'Triangle', 'Warp'].includes(type)) return;

    const bData = node.getAttr('bladeData');
    let points = bData.warpPoints;

    // 初めてWarp化する場合は、元の図形に合わせて頂点を生成する
    if (!points) {
        const x = node.x();
        const y = node.y();
        const w = node.width();
        const h = node.height();
        
        if (type === 'Triangle') {
            // 三角形: 3つの頂点
            points = [
                { x: x + w / 2, y: y },           // 上
                { x: x + w,     y: y + h },       // 右下
                { x: x,         y: y + h }        // 左下
            ];
        } else if (type === 'Circle') {
            // 円: 12角形で近似（12個の頂点）
            points = [];
            const cx = x + w / 2;
            const cy = y + h / 2;
            const rx = w / 2;
            const ry = h / 2;
            
            for (let i = 0; i < 12; i++) {
                // 上(12時の方向)から時計回りに生成
                const angle = (i * Math.PI * 2) / 12 - (Math.PI / 2);
                points.push({
                    x: cx + rx * Math.cos(angle),
                    y: cy + ry * Math.sin(angle)
                });
            }
        } else {
            // Rect (四角形): 4つの頂点
            points = [
                { x: x,     y: y     },
                { x: x + w, y: y     },
                { x: x + w, y: y + h },
                { x: x,     y: y + h },
            ];
        }
    }

    // Warpノード（カスタム多角形）に変換
    let warpNode = node;
    if (type !== 'Warp') {
        warpNode = convertToWarp(node, points);
    }

    warpTargetNode = warpNode;
    createHandles(warpNode, points);
}

// 既存ノードを Konva.Line(closed) のWarpノードに変換
function convertToWarp(node, points) {
    const bData = { ...node.getAttr('bladeData') };
    bData.warpPoints = points;

    const flat = points.flatMap(p => [p.x, p.y]);
    const warpNode = new Konva.Line({
        points: flat,
        closed: true,
        fill: bData.bgcolor || '#cccccc',
        draggable: true,
        name: 'ui-element',
        id: node.id(),
    });
    warpNode.setAttr('uiType', 'Warp');
    warpNode.setAttr('bladeData', bData);

    node.destroy();
    layer.add(warpNode);
    layer.batchDraw();
    return warpNode;
}

// 各頂点にドラッグハンドルを作成
function createHandles(warpNode, points) {
    points.forEach((pt, idx) => {
        const handle = new Konva.Circle({
            x: pt.x,
            y: pt.y,
            radius: 6,
            fill: '#9b59b6', // プロパティパネルのボタンに合わせた紫色
            stroke: '#fff',
            strokeWidth: 2,
            draggable: true,
            name: 'warp-handle',
        });

        handle.on('dragmove', () => {
            points[idx] = { x: handle.x(), y: handle.y() };
            const flat = points.flatMap(p => [p.x, p.y]);
            warpNode.points(flat);
            
            const bData = warpNode.getAttr('bladeData');
            bData.warpPoints = points.map(p => ({ x: p.x, y: p.y }));
            warpNode.setAttr('bladeData', bData);
            layer.batchDraw();
        });

        handle.on('dragend', () => saveHistory());

        layer.add(handle);
        warpHandles.push(handle);
    });
    layer.batchDraw();
}

// 台形変形モードを抜ける
export function exitWarpMode() {
    clearHandles();
}

// 現在モード中か
export function isWarpMode() {
    return warpTargetNode !== null;
}

// 現在Warp編集中のノード（exitWarpMode判定に使う）
export function getWarpTarget() {
    return warpTargetNode;
}