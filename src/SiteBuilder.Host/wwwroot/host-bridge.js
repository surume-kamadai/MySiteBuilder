// ============================================================
// host-bridge.js — Photino ホスト用の橋渡し（preload.js の代替 shim）
// host-bridge.js — bridge shim for the Photino host (replaces preload.js).
//
// これは C# 化に伴い追加された「唯一のレンダラー側新規ファイル」です。
// This is the ONLY renderer-side file added for the C# migration.
// renderer 本体（canvas / inspector / export ...）のコードには一切手を入れていません。
// No renderer source (canvas / inspector / export ...) is modified at all.
//
// 役割 / Responsibilities:
//   1. Photino の window.external メッセージング上に window.electronAPI を再現する
//      Recreate window.electronAPI on top of Photino's window.external messaging.
//      → project/api.js 以下の既存コードは 1 文字も変わらない。
//        The existing project/api.js code stays byte-for-byte identical.
//   2. Electron のネイティブメニューを HTML メニューバーで同項目再現する（計画書 §4.3）
//      Reproduce the Electron native menu as an HTML menu bar (plan §4.3).
//   3. 外部リンクは既定ブラウザで開く（アプリ内遷移は起こさない）
//      Open external links in the default browser (never navigate the app itself).
// ============================================================
(function () {
    'use strict';

    // ---- Photino <-> renderer メッセージング / messaging (preload.js equivalent) ----
    let seq = 0;
    const pending = new Map();                       // 要求ID → resolve / request id → resolve
    const listeners = { 'menu-action': [], 'toggle-panel': [] };

    const ext = window.external;
    if (ext && typeof ext.receiveMessage === 'function') {
        ext.receiveMessage((raw) => {
            let msg;
            try { msg = JSON.parse(raw); } catch { return; }
            if (msg.replyTo != null) {                       // C# からの応答 / reply from C#
                const cb = pending.get(msg.replyTo);
                if (cb) { pending.delete(msg.replyTo); cb(msg.result); }
            } else if (msg.channel && listeners[msg.channel]) { // C# からのイベント / event from C#
                listeners[msg.channel].forEach((fn) => fn(msg.payload));
            }
        });
    }

    function invoke(channel, payload) {
        // Electron の ipcRenderer.invoke 相当（Promise で結果を待つ）
        // Equivalent to Electron's ipcRenderer.invoke (awaits a result via Promise).
        return new Promise((resolve) => {
            const id = ++seq;
            pending.set(id, resolve);
            window.external.sendMessage(JSON.stringify({ id, channel, payload }));
        });
    }
    function send(channel, payload) {
        // 応答不要の一方向通知 / fire-and-forget notification (no reply expected)
        window.external.sendMessage(JSON.stringify({ channel, payload }));
    }

    // preload.js が公開していた API と完全に同じ形 / same shape as preload.js exposed
    window.electronAPI = {
        exportProject: (payload) => invoke('export-project', payload),
        pickImage:     ()        => invoke('pick-image'),
        saveScene:     (json)    => invoke('save-scene', json),
        loadScene:     ()        => invoke('load-scene'),
        onMenuAction:  (cb) => listeners['menu-action'].push((a) => cb(a)),
        onTogglePanel: (cb) => listeners['toggle-panel'].push((p) => cb(p)),
        // Electron ではネイティブメニューのチェックを同期していた処理。
        // In Electron this synced the native menu checkbox; here it updates the HTML menu.
        notifyPanelState: (id, open) => syncPanelCheck(id, open),
    };

    // ---- HTML メニューバーからレンダラーのリスナーへ配送するヘルパー ----
    // ---- Helpers dispatching from the HTML menu bar into the renderer's listeners ----
    function emitMenuAction(action) {
        listeners['menu-action'].forEach((fn) => fn(action));
    }
    function emitTogglePanel(id, show) {
        listeners['toggle-panel'].forEach((fn) => fn({ id, show }));
    }

    // ============================================================
    // HTML メニューバー（計画書 §4.3 の許容案「HTML メニューバーで同位置・同項目を再現」）
    // HTML menu bar (plan §4.3's sanctioned option: reproduce items via an HTML menu bar).
    // main.js のメニュー定義（ファイル/編集/表示/ヘルプ）と項目・順序を一致させる。
    // Matches main.js's menu items and ordering (File / Edit / View / Help).
    // ============================================================
    const PANELS = [
        { id: 'pane-tools',     label: 'ツール' },
        { id: 'pane-pages',     label: 'ページ' },
        { id: 'pane-explorer',  label: 'エクスプローラー' },
        { id: 'pane-canvas',    label: 'キャンバス' },
        { id: 'pane-settings',  label: 'プロジェクト設定' },
        { id: 'pane-inspector', label: 'プロパティ' },
    ];
    const panelCheckEls = new Map();

    function syncPanelCheck(id, open) {
        const el = panelCheckEls.get(id);
        if (el) el.textContent = open ? '✓' : '';
    }

    function buildMenuBar() {
        // renderer の CSS ファイルには触れず、ここで最小のスタイルだけ注入する。
        // Inject a minimal style here so the renderer's own CSS files stay untouched.
        const style = document.createElement('style');
        style.textContent = `
            body.has-menubar .workspace { top: 30px; }
            #host-menubar { position: fixed; top:0; left:0; right:0; height:30px; z-index:5000;
                display:flex; align-items:stretch; background:#2a2a2a; border-bottom:1px solid #444;
                font-family:'Segoe UI', sans-serif; font-size:13px; color:#e0e0e0; user-select:none; }
            #host-menubar .hm-top { position:relative; display:flex; align-items:center; padding:0 12px; cursor:default; }
            #host-menubar .hm-top:hover, #host-menubar .hm-top.open { background:#3a3a3a; }
            #host-menubar .hm-drop { position:absolute; top:30px; left:0; min-width:230px; background:#2f2f2f;
                border:1px solid #555; box-shadow:0 6px 20px rgba(0,0,0,0.5); padding:4px 0; display:none; }
            #host-menubar .hm-top.open .hm-drop { display:block; }
            #host-menubar .hm-item { display:flex; align-items:center; gap:8px; padding:6px 16px; white-space:nowrap; cursor:pointer; }
            #host-menubar .hm-item:hover { background:#4a6cff; color:#fff; }
            #host-menubar .hm-accel { margin-left:auto; opacity:0.6; font-size:12px; padding-left:24px; }
            #host-menubar .hm-check { width:14px; display:inline-block; text-align:center; }
            #host-menubar .hm-sep { height:1px; margin:4px 0; background:#555; }
        `;
        document.head.appendChild(style);

        const bar = document.createElement('div');
        bar.id = 'host-menubar';

        const menus = [
            { label: 'ファイル', items: [
                { label: '新規プロジェクト', action: () => emitMenuAction('new-project') },
                { label: '開く...',          action: () => emitMenuAction('open-project') },
                { label: '保存して書き出し',  action: () => emitMenuAction('save-export') },
                { sep: true },
                { label: '終了', action: () => send('app-quit') },
            ] },
            { label: '編集', items: [
                { label: '元に戻す', accel: 'Ctrl+Z', action: () => emitMenuAction('undo') },
                { sep: true },
                { label: '切り取り', action: () => document.execCommand('cut') },
                { label: 'コピー',   action: () => document.execCommand('copy') },
                { label: '貼り付け', action: () => document.execCommand('paste') },
            ] },
            { label: '表示', items: [
                ...PANELS.map((p) => ({ panel: p })),
                { sep: true },
                { label: 'レイアウトを初期状態に戻す', action: () => emitMenuAction('reset-layout') },
                { sep: true },
                { label: '再読み込み',   action: () => window.location.reload() },
                { label: '開発者ツール', action: () => send('toggle-devtools') },
            ] },
            { label: 'ヘルプ', items: [
                { label: 'バージョン情報', action: () => send('show-about') },
            ] },
        ];

        function closeAll() {
            bar.querySelectorAll('.hm-top.open').forEach((el) => el.classList.remove('open'));
        }

        menus.forEach((m) => {
            const top = document.createElement('div');
            top.className = 'hm-top';
            const cap = document.createElement('span');
            cap.textContent = m.label;
            top.appendChild(cap);

            const drop = document.createElement('div');
            drop.className = 'hm-drop';

            m.items.forEach((it) => {
                if (it.sep) {
                    const s = document.createElement('div');
                    s.className = 'hm-sep';
                    drop.appendChild(s);
                    return;
                }
                const row = document.createElement('div');
                row.className = 'hm-item';

                if (it.panel) {
                    // 「表示」パネルのチェック項目 / checkbox item under the View menu
                    const chk = document.createElement('span');
                    chk.className = 'hm-check';
                    chk.textContent = '✓';                 // 初期状態は全パネル表示 / all panels shown initially
                    panelCheckEls.set(it.panel.id, chk);
                    const lab = document.createElement('span');
                    lab.textContent = it.panel.label;
                    row.appendChild(chk);
                    row.appendChild(lab);
                    row.addEventListener('click', () => {
                        const nowOpen = chk.textContent !== '✓';   // トグル / toggle
                        chk.textContent = nowOpen ? '✓' : '';
                        emitTogglePanel(it.panel.id, nowOpen);
                        closeAll();
                    });
                } else {
                    const spacer = document.createElement('span');
                    spacer.className = 'hm-check';
                    const lab = document.createElement('span');
                    lab.textContent = it.label;
                    row.appendChild(spacer);
                    row.appendChild(lab);
                    if (it.accel) {
                        const a = document.createElement('span');
                        a.className = 'hm-accel';
                        a.textContent = it.accel;
                        row.appendChild(a);
                    }
                    row.addEventListener('click', () => { it.action(); closeAll(); });
                }
                drop.appendChild(row);
            });

            top.appendChild(drop);
            top.addEventListener('click', (e) => {
                e.stopPropagation();
                const wasOpen = top.classList.contains('open');
                closeAll();
                if (!wasOpen) top.classList.add('open');
            });
            top.addEventListener('mouseenter', () => {
                // いずれかが開いている間はホバーで切り替え（ネイティブメニュー風）
                // While a menu is open, hovering switches to it (native-menu-like behavior).
                if (bar.querySelector('.hm-top.open')) { closeAll(); top.classList.add('open'); }
            });
            bar.appendChild(top);
        });

        document.addEventListener('click', closeAll);

        document.body.classList.add('has-menubar');
        document.body.appendChild(bar);
    }

    // ---- 外部リンクは既定ブラウザで開く / open external links in the default browser ----
    function installExternalLinkGuard() {
        document.addEventListener('click', (e) => {
            const a = e.target && e.target.closest ? e.target.closest('a[href]') : null;
            if (!a) return;
            const href = a.getAttribute('href') || '';
            if (/^https?:\/\//i.test(href)) {
                e.preventDefault();                     // アプリ内遷移を止める / stop in-app navigation
                send('open-external', { url: href });   // C# 側で既定ブラウザを開く / C# opens the browser
            }
        }, true);
    }

    function init() {
        buildMenuBar();
        installExternalLinkGuard();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
