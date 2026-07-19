// ============================================================
// 共有ステート / Shared state
// モジュール間で参照・更新する変数をここで一元管理する。
// Central store for variables read and updated across modules.
// ============================================================

export let currentCanvasWidth  = 800;
export let currentCanvasHeight = 600;
export let selectedNodes       = [];
// 範囲選択 (Shift+クリック) の起点となる「最後にクリックされたノード」
// The "last clicked node" that anchors range selection (Shift+click).
export let lastClickedNode     = null;
export function setLastClickedNode(node) { lastClickedNode = node; }

export function setCurrentCanvasWidth(v)  { currentCanvasWidth  = v; }
export function setCurrentCanvasHeight(v) { currentCanvasHeight = v; }
export function setSelectedNodes(nodes)   { selectedNodes = nodes; }
// --- デバイス（レスポンシブ）状態 / Device (responsive) state ---
export let currentDevice = 'pc'; // 'pc' または 'mobile' / 'pc' or 'mobile'
export function setCurrentDevice(v) { currentDevice = v; }