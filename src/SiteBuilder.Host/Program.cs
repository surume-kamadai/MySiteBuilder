using Photino.NET;
using SiteBuilder.Host.Bridge;
using Velopack;

namespace SiteBuilder.Host;

// ============================================================
// SiteBuilder.Host — Electron の殻を置き換える純.NET ホスト（計画書 §3）。
// SiteBuilder.Host — pure-.NET host replacing the Electron shell (plan §3).
// OS の WebView に src/renderer 一式を無改変でロードし、
// ブリッジ 7API + メニュー + ファイル I/O だけを C# で提供する。
// Loads the verbatim src/renderer bundle in the OS WebView and provides only
// the 7 bridge APIs + menu + file I/O in C#.
// ============================================================
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Velopack: インストール/更新/アンインストールのフックを最優先で処理する。
        // Velopack: handle install/update/uninstall hooks first, before anything else.
        VelopackApp.Build().Run();

        var isDev = args.Contains("--dev");
        var engine = EngineModeParser.Resolve(args); // 出力エンジン選択（既定 JS）/ output engine (default JS)

        var window = new PhotinoWindow()
            .SetTitle("Site Builder")
            // ウィンドウ: 1400×900・最小 1000×700（Electron 版と同値）/ same window sizes as Electron.
            .SetUseOsDefaultSize(false)
            .SetSize(1400, 900)
            .SetMinSize(1000, 700)
            .Center()
            .SetResizable(true)
            // renderer 独自の右クリックUIを使うため、OS 標準のコンテキストメニューは無効化。
            // The renderer has its own right-click UI, so disable the OS context menu.
            .SetContextMenuEnabled(false)
            .SetDevToolsEnabled(isDev);

        // ブリッジ 7API のルーティング（export/pick-image/save-scene/load-scene ...）。
        var dispatcher = new BridgeDispatcher(window, engine);
        window.RegisterWebMessageReceivedHandler(dispatcher.Handle);

        // renderer 一式は wwwroot に無改変配置。相対パス/ESモジュール/vendor もそのまま解決される。
        // The renderer sits verbatim under wwwroot; relative paths / ES modules / vendor all resolve as-is.
        window.Load("wwwroot/index.html");

        window.WaitForClose();
    }
}
