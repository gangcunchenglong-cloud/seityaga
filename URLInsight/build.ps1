# URL Insight — Release ビルド (Windows PowerShell / PowerShell 7)
#   必要: .NET 8 SDK、Node.js(拡張テスト・任意)、NSIS(makensis・任意。インストーラー生成時)
#   使い方:  powershell -ExecutionPolicy Bypass -File .\build.ps1
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$dist = Join-Path $PSScriptRoot "dist"
$out = Join-Path $dist "URLInsight"
Write-Host "== URL Insight $version =="
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

Write-Host "== 単体テスト =="
dotnet test tests/UrlInsight.Core.Tests -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "テスト失敗" }
if (Get-Command node -ErrorAction SilentlyContinue) {
  node --test (Get-ChildItem browser-extension/tests/*.test.js).FullName
  if ($LASTEXITCODE -ne 0) { throw "拡張テスト失敗" }
}

# 自己完結型・単一ファイルの設定は各 csproj に記述
$common = @("-c","Release","-p:DebugType=none")

Write-Host "== アプリ本体 =="
dotnet publish src/UrlInsight.App @common -o $out
if ($LASTEXITCODE -ne 0) { throw "publish 失敗" }

Write-Host "== ネイティブホスト =="
$hostOut = Join-Path $dist "host"
dotnet publish src/UrlInsight.NativeHost @common -o $hostOut
if ($LASTEXITCODE -ne 0) { throw "publish 失敗" }
Copy-Item (Join-Path $hostOut "URLInsight.NativeHost.exe") $out
Remove-Item $hostOut -Recurse -Force

Write-Host "== 拡張・文書 =="
$ext = Join-Path $out "browser-extension"
New-Item -ItemType Directory -Force $ext | Out-Null
Copy-Item browser-extension/manifest.json $ext
Copy-Item browser-extension/src $ext -Recurse
Copy-Item browser-extension/icons $ext -Recurse
Copy-Item README.md, LICENSES.md $out
New-Item -ItemType Directory -Force (Join-Path $out "docs") | Out-Null
Copy-Item docs/*.md (Join-Path $out "docs")

Compress-Archive -Path $out -DestinationPath (Join-Path $dist "URLInsight-$version-win-x64-portable.zip")

$makensis = Get-Command makensis -ErrorAction SilentlyContinue
if (-not $makensis -and (Test-Path "${env:ProgramFiles(x86)}\NSIS\makensis.exe")) { $makensis = "${env:ProgramFiles(x86)}\NSIS\makensis.exe" }
if ($makensis) {
  Write-Host "== インストーラー =="
  & $makensis "-DVERSION=$version" "-DSRCDIR=$out" "-DOUTFILE=$dist\URLInsight-Setup-$version.exe" installer\URLInsight.nsi
} else {
  Write-Host "NSIS が見つからないためインストーラー生成をスキップしました"
}
Get-FileHash (Get-ChildItem $dist -Recurse -Include *.exe, *.zip) -Algorithm SHA256 | Format-Table -AutoSize
Write-Host "完了: $dist"
