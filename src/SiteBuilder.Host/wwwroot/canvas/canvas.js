// ============================================================
// Konva キャンバス初期化
// stage / layer / transformer / selectionRect を生成してエクスポートする
// ============================================================
import { currentCanvasWidth, currentCanvasHeight } from '../app/state.js';

export const stage = new Konva.Stage({
    container: 'canvas-container',
    width:  currentCanvasWidth,
    height: currentCanvasHeight,
});

export const layer = new Konva.Layer();
stage.add(layer);

export const tr = new Konva.Transformer({
    keepRatio: false,
    enabledAnchors: [
        'top-left', 'top-center', 'top-right',
        'middle-left', 'middle-right',
        'bottom-left', 'bottom-center', 'bottom-right',
    ],
    rotateEnabled: true,
    // 最小サイズガード: 小さくし過ぎてトランスフォーマーが壊れる/文字が消えるのを防ぐ
    boundBoxFunc: (oldBox, newBox) => {
        if (newBox.width < 12 || newBox.height < 12) return oldBox;
        return newBox;
    },
});
layer.add(tr);

export const selectionRect = new Konva.Rect({
    fill: 'rgba(0, 168, 255, 0.3)',
    visible: false,
    listening: false,
});
layer.add(selectionRect);