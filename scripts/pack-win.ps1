# ============================================================
# pack-win.ps1 — Windows 配布物（Velopack インストーラ + 自動更新）を作る。
# pack-win.ps1 — build the Windows distributable (Velopack installer + auto-update).
#
# 前提 / Prereqs:
#   - Windows + .NET SDK
#   - Velopack CLI:  dotnet tool install -g vpk
#   実行 / Run:      pwsh scripts/pack-win.ps1 -Version 1.0.0
#
# Velopack のブートストラップが Evergreen WebView2 の導入も面倒を見る（計画書 §8）。
# Velopack's bootstrapper also handles installing Evergreen WebView2 (plan §8).
# ============================================================
param(
    [string]$Version = "1.0.0",
    [string]$Rid = "win-x64"
)
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "publish/$Rid"

dotnet publish (Join-Path $root "src/SiteBuilder.Host/SiteBuilder.Host.csproj") `
    -c Release -r $Rid --self-contained -o $publishDir

# vpk pack: publish フォルダ一式から NSIS 相当のインストーラと更新パッケージを生成。
vpk pack `
    --packId SiteBuilder `
    --packTitle "Site Builder" `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe "SiteBuilder.exe"

Write-Host "Windows package created under ./Releases (installer + delta updates)."
