# MySiteBuilder (C# / Photino ホスト)

`docs/csharp-migration-plan.md`（v2）に基づき、**WebSitebuilder-Laravel（Electron 版）の
見た目・機能・操作感を完全に維持したまま C# 化**したデスクトップアプリです。

> 方針: 既存の Web UI（`src/renderer`）を **一切変更せずそのまま動かし**、
> Electron の殻（ウィンドウ・メニュー・ファイル I/O）だけを .NET（Photino.NET）に置き換える。
> 見た目・操作感は「似せる」のではなく「**同一のコードが動く**」ことで保証される。

これは移行計画の **Step 1（Electron 殻の C# 置換）** に相当します。

---

## アーキテクチャ

```
┌──────────────────────────────────────────────┐
│ SiteBuilder.Host (C# / Photino.NET)           │
│  ・ウィンドウ生成 (1400×900 / 最小 1000×700)   │
│  ・HTML メニューバー相当を提供 (host-bridge.js) │
│  ・ブリッジ 7API + ファイル I/O + SafeResolve   │
│  ┌────────────────────────────────────────┐   │
│  │ OS WebView (Win: WebView2 / Mac: WKWebView)│ │
│  │  wwwroot/ = src/renderer を無改変ロード     │ │
│  │  (Konva / GoldenLayout / inspector / …)    │ │
│  └────────────────────────────────────────┘   │
└──────────────────────────────────────────────┘
```

### ディレクトリ

```
MySiteBuilder/
├─ SiteBuilder.slnx
└─ src/SiteBuilder.Host/
   ├─ SiteBuilder.Host.csproj      # Photino.NET / net8.0
   ├─ Program.cs                   # ウィンドウ生成・ブリッジ配線
   ├─ Bridge/
   │  ├─ BridgeMessage.cs          # メッセージ/ペイロードのモデル
   │  ├─ BridgeDispatcher.cs       # メッセージルータ（計画書 §4.2）
   │  ├─ DialogService.cs          # フォルダ/ファイル/画像ダイアログ
   │  └─ ExportWriter.cs           # export-project 相当 + SafeResolve（§4.2）
   └─ wwwroot/                     # WebSitebuilder-Laravel/src/renderer の無改変コピー
      └─ host-bridge.js            # ★唯一の追加ファイル（preload.js 代替 shim + メニュー）
```

**renderer 本体のコードには手を入れていません。** 追加は `host-bridge.js` 1 ファイルと、
`index.html` にそれを読み込む `<script>` 1 行のみです
（`diff` で `src/renderer` と照合可能）。

---

## ブリッジ（preload.js の 7API を Photino メッセージング上に再現）

`host-bridge.js` が `window.electronAPI` を preload.js と**完全に同じ形**で定義するため、
`project/api.js` 以下の既存コードは 1 文字も変わりません。

| API | 方向 | C# 側の実装 |
|---|---|---|
| `exportProject(payload)` | JS→C# | `ExportWriter.Export`（初回はフォルダ選択 → `選択フォルダ/プロジェクト名/`、SafeResolve でパストラバーサル遮断） |
| `pickImage()` | JS→C# | `DialogService.PickImage` → `{dataUrl, name}` |
| `saveScene(json)` | JS→C# | `DialogService.SaveScene` → `{success, path}` |
| `loadScene()` | JS→C# | `DialogService.LoadScene` → `{content, dirPath}` |
| `onMenuAction(cb)` | C#→JS | メニュー操作（new-project / open-project / save-export / undo / reset-layout） |
| `onTogglePanel(cb)` | C#→JS | 「表示」メニューのパネル開閉 `{id, show}` |
| `notifyPanelState(id, open)` | JS内 | メニューのチェック同期 |

メニュー（ファイル / 編集 / 表示 / ヘルプ）は計画書 §4.3 の許容案どおり **HTML メニューバー**で
同項目・同順序を再現しています（外部リンクは既定ブラウザで開き、`終了`/`バージョン情報` は
C# 側でネイティブに処理）。

---

## ビルドと実行

必要: [.NET 8 SDK](https://dotnet.microsoft.com/download) と、Photino が要求する OS の WebView
（Windows は Evergreen WebView2、macOS は標準の WKWebView）。

```bash
# ビルド
dotnet build src/SiteBuilder.Host/SiteBuilder.Host.csproj

# 実行（開発時は --dev で開発者ツール有効）
dotnet run --project src/SiteBuilder.Host -- --dev

# 配布用の自己完結パブリッシュ（例: Windows）
dotnet publish src/SiteBuilder.Host -c Release -r win-x64
```

> 注: 本環境（CI/コンテナ）では GUI（WebView）を起動できないため、`dotnet build` による
> コンパイル確認まで実施済みです。実機での起動・全機能の受け入れ確認は計画書 §6-1 の
> チェックリストに従ってください。

---

## 移行計画上の位置づけ

- **Step 1（本コミット）: 完了** — Electron 殻を C#（Photino.NET）へ置換。UI は無改変で動作し、
  Electron/Node への依存が消えた状態。出力エンジンは引き続き renderer 内の JS 版が動くため、
  出力結果は従来と完全に同一。
- **Step 2（今後）** — 出力エンジン（`export/` 純ロジック）を `SiteBuilder.Core` へ移植し、
  ゴールデンテストでバイト一致を確認のうえ既定を C# 化。
- **Step 3（今後）** — Velopack（Win）/ dmg（Mac）での配布。

元 UI・出力エンジンの実装は `WebSitebuilder-Laravel`（`src/renderer`）を正とします。
