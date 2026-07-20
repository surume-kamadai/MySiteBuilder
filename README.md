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
├─ src/
│  ├─ SiteBuilder.Host/            # Photino ホスト（ウィンドウ・メニュー・ブリッジ・I/O）
│  │  ├─ SiteBuilder.Host.csproj   # Photino.NET / net8.0
│  │  ├─ Program.cs                # ウィンドウ生成・ブリッジ配線・エンジン選択
│  │  ├─ Bridge/
│  │  │  ├─ BridgeMessage.cs       # メッセージ/ペイロードのモデル
│  │  │  ├─ BridgeDispatcher.cs    # メッセージルータ（計画書 §4.2）
│  │  │  ├─ DialogService.cs       # フォルダ/ファイル/画像ダイアログ
│  │  │  ├─ EngineMode.cs          # JS / Shadow / CSharp の切替（§2.7）
│  │  │  └─ ExportWriter.cs        # export-project 相当 + SafeResolve + エンジン適用
│  │  └─ wwwroot/                  # WebSitebuilder-Laravel/src/renderer の無改変コピー
│  │     └─ host-bridge.js         # ★唯一の追加ファイル（preload.js 代替 shim + メニュー）
│  └─ SiteBuilder.Core/            # ★Step 2: 出力エンジン（UI依存ゼロの純ロジック）
│     ├─ Js.cs                     # JS 値セマンティクス互換層（?? と || / toFixed / String(number)）
│     └─ Export/
│        ├─ CssHelpers.cs          # ← css-generator.js
│        ├─ ComponentRenderers.cs  # ← render-components.js
│        ├─ HtmlRenderer.cs        # ← renderer.js
│        └─ Exporter.cs            # ← exporter.js（buildStatic / buildLaravel）
└─ tests/
   ├─ golden/                      # ゴールデン入力プロジェクト + JS版ダンプ器
   │  ├─ projects/*.json           # 代表プロジェクト（全要素/Group/Warp/フォーム/複数ページ…）
   │  └─ dump.mjs                  # wwwroot の JS 版で期待フィクスチャを生成
   └─ SiteBuilder.Core.Tests/      # xUnit: C# 出力が JS 版とバイト一致することを検証
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

```bash
# テスト（ゴールデン + JS互換層）
dotnet test tests/SiteBuilder.Core.Tests/SiteBuilder.Core.Tests.csproj
```

> 注: `SiteBuilder.slnx` は .NET 9 以降の `dotnet` が解釈します（.NET 8 SDK 単体で使う場合は
> 各 `.csproj` を直接ビルド/テストしてください）。本環境（CI/コンテナ）では GUI（WebView）を
> 起動できないため、`dotnet build` + `dotnet test` によるコンパイル/出力検証まで実施済みです。
> 実機での起動・全機能の受け入れ確認は計画書 §6-1 のチェックリストに従ってください。

---

## 出力エンジンの C# 化（Step 2）

`WebSitebuilder-Laravel/src/renderer/export`（純ロジック 1,328 行）を `SiteBuilder.Core` へ移植しました。
JS の値セマンティクス（`??` と `||` の違い・`toFixed`・`String(number)`・`Map`/`Set` の挿入順）まで
再現し、**JS 版とバイト一致**することをゴールデンテストで機械検証しています。

### エンジンの切替（`--engine=` / `SITEBUILDER_ENGINE`）

| モード | 挙動 |
|---|---|
| `js`（既定） | 従来どおり renderer(JS) が生成した files をそのまま書き出す（**挙動不変**） |
| `shadow` | JS の出力を書き出しつつ、C# エンジンの出力と毎回比較して不一致を `TEMP/sitebuilder-shadow.log` に記録（ユーザー影響ゼロで実データ検証） |
| `csharp` | ペイロード内の `project.json` から C# エンジンで再生成した files を書き出す |

```bash
dotnet run --project src/SiteBuilder.Host -- --engine=shadow
```

renderer は無改変のまま（`api.js` が保存時に `project.json` を同梱しているため、C# 側はそれを
入力に再生成/照合できます）。既定は `js` なので、一致確認が済むまで出力は従来と完全に同一です。

### ゴールデンテスト

`tests/golden/dump.mjs` が wwwroot の JS 版エンジンで期待フィクスチャを生成し、
`SiteBuilder.Core.Tests` が C# 版の出力と**パス集合・ファイル内容をバイト比較**します。
入力プロジェクトを増やしたら次で再生成します:

```bash
node tests/golden/dump.mjs   # フィクスチャ再生成 → その後 dotnet test
```

ゴールデンテストは CI（`.github/workflows/ci.yml`）で毎回実行され、フィクスチャの
鮮度チェック（JS 版から再生成して差分ゼロ）も行われます。

---

## 配布（Step 3）

Velopack のブートストラップを起動時に組み込み（`VelopackApp.Build().Run()`）、
Windows/macOS 向けのパッケージングスクリプトを用意しています。

```bash
# Windows: Velopack インストーラ + 自動更新（要 dotnet tool install -g vpk）
pwsh scripts/pack-win.ps1 -Version 1.0.0

# macOS: .app → dmg（要 brew install create-dmg）
RID=osx-arm64 bash scripts/pack-mac.sh 1.0.0
```

- Windows は Velopack が Evergreen WebView2 の導入も面倒を見ます（計画書 §8）。
- macOS の本番配布では Apple Developer 署名 + notarize が別途必要です。
- 実際のインストーラ生成には各 OS 実機（と WebView）が必要なため、本リポジトリでは
  スクリプトとブートストラップの用意までを行っています。

---

## 移行計画上の位置づけ

- **Step 1: 完了** — Electron 殻を C#（Photino.NET）へ置換。UI は無改変で動作し、
  Electron/Node への依存が消えた状態。
- **Step 2: 完了** — 出力エンジンを `SiteBuilder.Core` へ移植し、ゴールデンテストで
  JS 版とのバイト一致を確認。`--engine` フラグで JS/Shadow/C# を切替可能（既定は JS）。
- **Step 3（本 PR）: 配布の足場を用意** — Velopack ブートストラップ + Win/Mac パッケージング
  スクリプト + CI（ゴールデンテスト自動実行）。実機での配布物生成と署名は今後。
- **今後** — シャドウ実運用で不一致ゼロを確認後、既定エンジンを C# へ切替。実機での配布・署名。

元 UI・出力エンジンの実装は `WebSitebuilder-Laravel`（`src/renderer`）を正とします。
