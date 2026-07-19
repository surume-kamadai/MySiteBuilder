// ============================================================
// 要素の生成・グループ化・解除
// ============================================================
import { layer, tr } from '../canvas/canvas.js';
import { selectedNodes, setSelectedNodes } from '../app/state.js';
import { saveHistory } from '../history/history.js';
import { updateInspectorFromNode } from '../inspector/inspector.js';
import { renderExplorer } from '../explorer/explorer.js';
import { applyImageCover, applyNodeShadow, applyGradient, applyStroke, applyDropShadow, applyTextStyle } from './node-style.js';

const DEFAULT_PROPS = {
    Button:    { bgcolor: '#007acc', event: 'submit'   },
    Rect:      { bgcolor: '#cccccc', event: 'none'     },
    Circle:    { bgcolor: '#e74c3c', event: 'none'     },
    Triangle:  { bgcolor: '#2ecc71', event: 'none'     },
    Group:     { bgcolor: 'transparent', event: 'none' },
    TextInput: { bgcolor: '#ffffff', event: 'none'     },
    Label:     { bgcolor: '#ffffff', event: 'none'     },
    Image:     { bgcolor: '#ffffff', event: 'none'     },
};

// --- 選択状態の更新 ---
export function applySelectedNodes(nodes) {
    setSelectedNodes(nodes);
    tr.nodes(nodes);
    tr.moveToTop();
    updateInspectorFromNode();
    renderExplorer();

    // 【Group内子要素のdraggable制御】
    // 子要素は基本ロック (draggable:false)。選択された子だけ一時的に動かせるようにする。
    // これで「Cardをクリック→Cardが動く」「中のLabelをクリック→Labelだけ動く」が両立する。
    // Space押下中(パンモード)は一切ドラッグ不可にする。
    // これがないと、Space中にオブジェクトを選択した瞬間ここでdraggableが復活してしまう。
    const spaceDown = typeof window !== 'undefined' && window.__spaceDown;
    layer.find('.ui-element').forEach(n => {
        const isInGroup = n.parent && n.parent.nodeType === 'Group' && n.parent.hasName('ui-element');
        if (!isInGroup) {
            // ルート直下の要素はロック設定（bData.lock）に従う
            const bData = n.getAttr('bladeData');
            n.draggable(!(bData && bData.lock) && !spaceDown);
        } else {
            // Group内の子: 選択中の子はドラッグ可、それ以外はロック
            const selected = nodes.includes(n);
            const bData = n.getAttr('bladeData');
            const userLocked = !!(bData && bData.lock);
            n.draggable(selected && !userLocked && !spaceDown);
        }
    });

    layer.batchDraw();
}

// 指定タイプの既存要素(グループ内も含む)を走査し、次に使う番号(=最大+1)を返す。
// これにより「タイプごとに採番」「削除後は残っている同タイプの最大番号の次から」が成立する。
export function nextNumberForType(type) {
    let max = 0;
    const prefix = String(type).toLowerCase() + '_';
    const scan = (container) => {
        container.getChildren().forEach(n => {
            if (!n.hasName || !n.hasName('ui-element')) return;
            if (n.getAttr('uiType') === type) {
                const s = String(n.id() || '');
                if (s.startsWith(prefix)) {
                    const num = parseInt(s.slice(prefix.length), 10);
                    if (Number.isFinite(num)) max = Math.max(max, num);
                }
            }
            if (n.getAttr('uiType') === 'Group') scan(n);
        });
    };
    scan(layer);
    return max + 1;
}

// バッチ生成(コンポーネント/貼り付け)用: 同一タイプを衝突なく連番で払い出す。
// レイヤーにまだ無いノードを一括生成するため、ローカルに加算していく。
export function makeTypeCounter() {
    const local = {};
    return (type) => {
        if (local[type] === undefined) local[type] = nextNumberForType(type) - 1;
        return ++local[type];
    };
}

// --- 要素の生成 ---
export function spawnElement(type, loadData = null, parentGroup = layer, isHistoryLoad = false, noHistorySave = false) {
    const count = loadData ? null : nextNumberForType(type);
    const id    = loadData ? loadData.id : type.toLowerCase() + '_' + count;

    const bData = loadData ? loadData.properties : {
        name:     type + ' ' + count,
        text:     type === 'Image' ? 'https://placehold.co/150x150/png' : 'テキスト',
        bgcolor:  DEFAULT_PROPS[type]?.bgcolor  ?? '#ffffff',
        color:    '#000000',
        fontsize: 16,
        align:    'left',
        fontfamily:'sans-serif',
        fontWeight: type === 'Button' ? 'bold' : 'normal',  // normal | bold
        italic:    false,
        underline: false,
        letterSpacing: 0,   // 字間(px)
        lineHeight: 1.2,    // 行間(倍率)
        lock:     false,
        route:    '#',
        method:   'POST',
        event:    DEFAULT_PROPS[type]?.event ?? 'none',
        shadow:   'none',
        animation:'none',
        opacity:  1,
        // 角の丸み(px)。ボタンは既定で少し丸く、それ以外は角ばった状態から。
        cornerRadius: type === 'Button' ? 8 : 0,
        // グラデーション（off時は単色 bgcolor）。type: linear|radial, dir: v|h|d1|d2
        gradient: { on: false, type: 'linear', c1: '#4facfe', c2: '#00f2fe', dir: 'v' },
        // 境界線（レイヤースタイルの Stroke）。図形/ボタン/画像/テキストに適用
        stroke:   { on: false, width: 2, color: '#000000' },
        // ドロップシャドウ(自由値)。on時はプリセット shadow より優先。
        dropShadow: { on: false, x: 4, y: 4, blur: 10, spread: 0, color: '#000000', opacity: 0.35 },
        bgimage:  '',
        // フォーム用: Button の役割（'link' | 'submit'）
        role:     type === 'Button' ? 'link' : 'none',
        // フォーム送信ボタンの送信完了メッセージ（空なら遷移のみ）
        successMessage: '送信ありがとうございました。',
        // フォーム用: TextInput の入力欄設定
        inputName: '',
        inputType: 'text',   // text | email | tel | number | textarea
        required:  false,
        layouts: loadData ? (loadData.properties.layouts || {}) : {},
        mobileEdited: loadData ? !!loadData.properties.mobileEdited : false,
        events:   loadData && loadData.properties.events ? loadData.properties.events : []
    };

    // 丸・三角は初期から正方形にして、潰れた楕円/平たい三角にならないようにする
    const squareDefault = (type === 'Circle' || type === 'Triangle');
    const tData = loadData ? loadData.transform
        : { x: 50, y: 50, width: squareDefault ? 120 : 150, height: squareDefault ? 120 : 50 };
    const base  = { x: tData.x, y: tData.y, width: tData.width, height: tData.height, draggable: true, name: 'ui-element', id };

    // 【重要】_pcGeom を必ず初期化する。これでスマホ表示中の出力時にPC位置が壊れない。
    // loadDataがあれば properties._pcGeom があればそれを使い、無ければtransformから作る。
    if (loadData) {
        if (!bData._pcGeom) {
            bData._pcGeom = { x: tData.x, y: tData.y, w: tData.width, h: tData.height };
        }
    } else {
        bData._pcGeom = { x: base.x, y: base.y, w: base.width, h: base.height };
    }

    let newNode;
    switch (type) {
        case 'Group':     newNode = new Konva.Group(base); break;
        case 'Button': {
            newNode = new Konva.Group({ ...base });
            
            // 背景用の四角形
            const bg = new Konva.Rect({
                x: 0, y: 0,
                width: base.width, height: base.height,
                fill: bData.bgimage ? null : bData.bgcolor, // 画像があれば色は透明に
                cornerRadius: bData.cornerRadius ?? 8,
                name: 'btn-bg'
            });

            // ボタン用背景画像ノード（初期は非表示または画像ロード）
            const bgImgNode = new Konva.Image({
                x: 0, y: 0,
                width: base.width, height: base.height,
                cornerRadius: bData.cornerRadius ?? 8,
                name: 'btn-bgimage',
                visible: !!bData.bgimage
            });
            
            // 画像URLがある場合はロード
            if (bData.bgimage) {
                const img = new Image();
                img.crossOrigin = 'Anonymous';
                img.onload = () => {
                    bgImgNode.image(img);
                    layer.batchDraw();
                };
                img.src = bData.bgimage;
            }

            const txt = new Konva.Text({
                x: 0, y: 0,
                width: base.width, height: base.height,
                text: bData.text, fill: bData.color, fontSize: bData.fontsize,
                align: bData.align || 'center',
                fontFamily: bData.fontfamily || 'sans-serif',
                verticalAlign: 'middle',
                name: 'btn-text'
            });
            
            newNode.add(bg, bgImgNode, txt);
            break;
        }
        case 'TextInput': newNode = new Konva.Rect({ ...base, fill: '#fff', stroke: '#ccc', strokeWidth: 1 }); break;
        case 'Label':     newNode = new Konva.Text({ ...base, text: bData.text, fill: bData.color, fontSize: bData.fontsize, align: bData.align || 'left', fontFamily: bData.fontfamily || 'sans-serif' }); break;
        case 'Rect':      newNode = new Konva.Rect({ ...base, fill: bData.bgcolor, cornerRadius: bData.cornerRadius || 0 }); break;
        case 'Warp': {
            const pts = bData.warpPoints || [
                { x: base.x, y: base.y },
                { x: base.x + base.width, y: base.y },
                { x: base.x + base.width, y: base.y + base.height },
                { x: base.x, y: base.y + base.height },
            ];
            const flat = pts.flatMap(p => [p.x, p.y]);
            newNode = new Konva.Line({ points: flat, closed: true, fill: bData.bgcolor, draggable: true, name: 'ui-element', id });
            break;
        }
        case 'Circle':    newNode = new Konva.Ellipse({ ...base, radiusX: base.width / 2, radiusY: base.height / 2, x: base.x + base.width / 2, y: base.y + base.height / 2, fill: bData.bgcolor }); break;
        case 'Triangle':  newNode = new Konva.Shape({ ...base, fill: bData.bgcolor, sceneFunc(ctx, shape) {
            const w = shape.width(), h = shape.height();
            ctx.beginPath(); ctx.moveTo(w / 2, 0); ctx.lineTo(w, h); ctx.lineTo(0, h); ctx.closePath();
            ctx.fillStrokeShape(shape);
        } }); break;
        case 'Image': {
            const img = new Image();
            img.crossOrigin = 'Anonymous';
            newNode = new Konva.Image({ ...base, image: img, cornerRadius: bData.cornerRadius || 0 });
            // 画像読込後にアスペクト調整。万一失敗しても描画は止めない（画像が出なくならないように）
            img.onload = () => {
                try { applyImageCover(newNode); } catch (err) { console.error('[applyImageCover]', err); }
                layer.batchDraw();
            };
            img.onerror = () => {
                console.warn('画像の読み込みに失敗しました。ダミーに差し替えます:', bData.text);
                img.src = 'data:image/gif;base64,R0lGODlhAQABAIAAAMLCwgAAACH5BAAAAAAALAAAAAABAAEAAAICRAEAOw==';
            };
            img.src = bData.text;
            break;
        }
        case 'Slider': {
            newNode = new Konva.Group({ ...base });
            // 背景
            const bg = new Konva.Rect({
                x: 0, y: 0, width: base.width, height: base.height,
                fill: bData.bgcolor || '#2d3436', cornerRadius: 5
            });
            // スライド数を計算（新スキーマ slides[] 優先、旧 text からの後方互換あり）
            const slideCount = Array.isArray(bData.slider?.slides)
                ? bData.slider.slides.length
                : (bData.text || '').split(',').filter(s => s.trim()).length;
            const txt = new Konva.Text({
                x: 0, y: 0, width: base.width, height: base.height,
                text: `🖼️ スライダー\n(現在 ${slideCount} 枚のスライド)\n📋「スライド一覧を編集」で詳細設定`,
                fill: '#ffffff', fontSize: 14, align: 'center', verticalAlign: 'middle',
                name: 'slider-placeholder',
            });
            newNode.add(bg, txt);
            break;
        }
        case 'ArticleGrid': {
            newNode = new Konva.Group({ ...base });
            const bg = new Konva.Rect({
                x: 0, y: 0, width: base.width, height: base.height,
                fill: bData.bgcolor || '#f8f9fa', cornerRadius: 5
            });
            const itemCount = Array.isArray(bData.grid?.items)
                ? bData.grid.items.length
                : 3;
            const cols = bData.grid?.columns ?? 3;
            const txt = new Konva.Text({
                x: 0, y: 0, width: base.width, height: base.height,
                text: `📰 記事グリッド\n(${itemCount} 件 / ${cols} カラム)\n📋「アイテム一覧を編集」で詳細設定`,
                fill: '#666', fontSize: 14, align: 'center', verticalAlign: 'middle',
                name: 'grid-placeholder',
            });
            newNode.add(bg, txt);
            break;
        }
        case 'Accordion': {
            newNode = new Konva.Group({ ...base });
            const bg = new Konva.Rect({
                x: 0, y: 0, width: base.width, height: base.height,
                fill: bData.bgcolor || '#ffffff', cornerRadius: 5,
                stroke: '#ddd', strokeWidth: 1,
            });
            const itemCount = Array.isArray(bData.accordion?.items)
                ? bData.accordion.items.length
                : 3;
            const txt = new Konva.Text({
                x: 0, y: 0, width: base.width, height: base.height,
                text: `🪗 アコーディオン\n(${itemCount} 項目)\n📋「項目一覧を編集」で詳細設定`,
                fill: '#555', fontSize: 14, align: 'center', verticalAlign: 'middle',
                name: 'accordion-placeholder',
            });
            newNode.add(bg, txt);
            break;
        }
        default: return null;
    }

    if (type === 'Group' && loadData?.children) {
        loadData.children.forEach(child => spawnElement(child.type, child, newNode, isHistoryLoad, true));
    }

    newNode.setAttr('uiType', type);
    newNode.setAttr('bladeData', bData);

    if (loadData) {
        if (bData.lock) {
            newNode.draggable(false);
            newNode.listening(false);
        }
        if (bData.visible === false) {
            newNode.visible(false);
        }
    } else {
        newNode.draggable(true);
        newNode.listening(true);
        bData.lock = false;
        bData.visible = true;
    }

    applyNodeShadow(newNode, bData.shadow);

    // 保存済みの不透明度を読み込み時にKonvaノードへ反映（既定は1=不透明）
    if (typeof bData.opacity === 'number' && Number.isFinite(bData.opacity)) {
        newNode.opacity(Math.min(1, Math.max(0, bData.opacity)));
    }
    applyGradient(newNode, bData);
    applyStroke(newNode, bData);
    applyDropShadow(newNode, bData);
    applyTextStyle(newNode, bData);   // 太さ/斜体/下線/字間/行間（Label/Button）

    newNode.on('dragend transformend', () => {
        updateInspectorFromNode();
        renderExplorer();
        saveHistory();
    });

    parentGroup.add(newNode);

    if (!loadData && !isHistoryLoad) {
        // 新規作成時はPC/スマホ用の layouts は作らない。
        // PC配置は node の x/y/width/height + transform が正データ。
        // スマホ配置はユーザーが📱で動かしたときだけ生成する。
        applySelectedNodes([newNode]);
        if (!noHistorySave) saveHistory();
    }
    return newNode;
}

// --- グループ化 / 解除 ---
export function groupNodes() {
    if (selectedNodes.length < 2) return;
    const count = nextNumberForType('Group');

    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    selectedNodes.forEach(node => {
        const box    = node.getClientRect({ skipTransform: true });
        const absPos = node.getAbsolutePosition();
        minX = Math.min(minX, absPos.x);
        minY = Math.min(minY, absPos.y);
        maxX = Math.max(maxX, absPos.x + box.width);
        maxY = Math.max(maxY, absPos.y + box.height);
    });

    const zoom  = tr.getStage().scaleX();
    const gx = minX / zoom, gy = minY / zoom;
    const gw = (maxX - minX) / zoom, gh = (maxY - minY) / zoom;

    const group = new Konva.Group({
        x: gx, y: gy, width: gw, height: gh,
        draggable: true, name: 'ui-element', id: 'group_' + count,
    });
    group.setAttr('uiType', 'Group');
    // 新グループの _pcGeom も初期化（レスポンシブの正データ）
    group.setAttr('bladeData', {
        name: 'フォルダ ' + count, bgcolor: 'transparent', color: '#000', fontsize: 16,
        _pcGeom: { x: gx, y: gy, w: gw, h: gh },
        layouts: {}, mobileEdited: false,
    });
    layer.add(group);

    selectedNodes.forEach(node => {
        const absPos = node.getAbsolutePosition();
        node.moveTo(group);
        node.absolutePosition(absPos);
        // 親が変わったので _pcGeom を新しい相対座標で更新
        const bData = node.getAttr('bladeData');
        if (bData) {
            const type = node.getAttr('uiType');
            let nx = node.x(), ny = node.y(), nw = node.width(), nh = node.height();
            if (type === 'Circle') {
                nx = node.x() - node.radiusX(); ny = node.y() - node.radiusY();
                nw = node.radiusX() * 2; nh = node.radiusY() * 2;
            }
            bData._pcGeom = { x: nx, y: ny, w: nw, h: nh };
            // グループ化前のスマホ配置は無効になるのでクリア
            if (bData.layouts) delete bData.layouts.mobile;
            bData.mobileEdited = false;
            node.setAttr('bladeData', bData);
        }
    });

    applySelectedNodes([group]);
    saveHistory();
}

export function createEmptyFolder() {
    // 現在選択中のレイヤー（同じ親階層のもの）を基準に、その「上」に作る
    const ref = selectedNodes.find(n => n.parent === layer) || null;
    const node = spawnElement('Group');
    node.width(300);
    node.height(200);
    // エクスプローラーは z 順の逆表示（上=高z）。選択レイヤーの1つ上に差し込む
    if (ref && ref.parent === node.parent) {
        node.setZIndex(Math.min(node.parent.getChildren().length - 1, ref.getZIndex() + 1));
    }
    applySelectedNodes([node]);
    renderExplorer();
    saveHistory();
    return node;
}

export function ungroupNodes() {
    if (selectedNodes.length !== 1 || selectedNodes[0].getAttr('uiType') !== 'Group') return;
    const group      = selectedNodes[0];
    const children   = group.getChildren().slice();
    const newSelected = [];

    children.forEach(child => {
        if (!child.hasName('ui-element')) return;
        const absPos = child.getAbsolutePosition();
        child.moveTo(layer);
        child.absolutePosition(absPos);
        // 親(group)から layer 直下に移動したので _pcGeom を新しい絶対座標で更新
        const bData = child.getAttr('bladeData');
        if (bData) {
            const type = child.getAttr('uiType');
            let nx = child.x(), ny = child.y(), nw = child.width(), nh = child.height();
            if (type === 'Circle') {
                nx = child.x() - child.radiusX(); ny = child.y() - child.radiusY();
                nw = child.radiusX() * 2; nh = child.radiusY() * 2;
            }
            bData._pcGeom = { x: nx, y: ny, w: nw, h: nh };
            if (bData.layouts) delete bData.layouts.mobile;
            bData.mobileEdited = false;
            child.setAttr('bladeData', bData);
        }
        newSelected.push(child);
    });

    group.destroy();
    applySelectedNodes(newSelected);
    saveHistory();
}


// ============================================================
// レスポンシブ用：デバイスレイアウトの適用
// ============================================================
export function syncNodeToLayout(node, device) {
    const bData = node.getAttr('bladeData');
    if (!bData) return;
    if (!bData.layouts) bData.layouts = {};

    let l = bData.layouts[device];

    // 対象デバイスのレイアウトが無いとき：
    //   device=mobile → 何もしない（PC位置のまま表示。ユーザーが動かしたら初めて記録）
    //   device=pc     → PC配置はノードの現状（transformと一致）なので何もしない
    if (!l || l.x === undefined) return;

    if (l.fontsize) bData.fontsize = l.fontsize;

    const type = node.getAttr('uiType');

    // l.x/l.y は左上座標。タイプごとに正しい座標系へ変換して適用する
    if (type === 'Circle') {
        node.radiusX(l.w / 2);
        node.radiusY(l.h / 2);
        node.x(l.x + l.w / 2);
        node.y(l.y + l.h / 2);
    } else {
        node.x(l.x);
        node.y(l.y);
        node.width(l.w);
        node.height(l.h);
    }

    if (type === 'Label') {
        node.fontSize(bData.fontsize);
    } else if (type === 'Button') {
        const bg = node.findOne('.btn-bg');
        const txt = node.findOne('.btn-text');
        const bgImgNode = node.findOne('.btn-bgimage');
        if (bg) { bg.width(l.w); bg.height(l.h); }
        if (bgImgNode) { bgImgNode.width(l.w); bgImgNode.height(l.h); }
        if (txt) { txt.width(l.w); txt.height(l.h); txt.fontSize(bData.fontsize); }
    } else if (type === 'Image') {
        applyImageCover(node);
    }

    if (type === 'Group') {
        node.getChildren().forEach(child => {
            if (child.hasName('ui-element')) syncNodeToLayout(child, device);
        });
    }
}