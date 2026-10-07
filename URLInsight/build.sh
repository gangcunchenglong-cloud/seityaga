#!/usr/bin/env bash
# URL Insight — Release ビルド(Linux / macOS / WSL から Windows 用 exe をクロスビルド)
#   必要: .NET 8 SDK(Microsoft版。Microsoft.NET.Sdk.WindowsDesktop を含むもの)、Node.js(拡張テスト)、NSIS(makensis、任意)
#   出力: dist/URLInsight/ (ポータブル版), dist/URLInsight-<ver>-win-x64-portable.zip, dist/URLInsight-Setup-<ver>.exe
set -euo pipefail
cd "$(dirname "$0")"
[ -f env.sh ] && source env.sh

VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)
DIST=dist
OUT=$DIST/URLInsight

echo "== URL Insight $VERSION =="
rm -rf "$DIST"
mkdir -p "$OUT"

echo "== 単体テスト (.NET) =="
dotnet test tests/UrlInsight.Core.Tests -c Release --nologo

echo "== 単体テスト (拡張) =="
if command -v node >/dev/null; then node --test browser-extension/tests/*.test.js; else echo "node が無いためスキップ"; fi

# 自己完結型・単一ファイルの設定は各 csproj に記述(グローバル指定にすると Core のロックファイルが変わるため)
COMMON=(-c Release -p:DebugType=none)

echo "== アプリ本体 =="
dotnet publish src/UrlInsight.App "${COMMON[@]}" -o "$OUT"

echo "== ネイティブメッセージングホスト =="
dotnet publish src/UrlInsight.NativeHost "${COMMON[@]}" -o "$DIST/host"
cp "$DIST/host/URLInsight.NativeHost.exe" "$OUT/"
rm -rf "$DIST/host"

echo "== 拡張・文書 =="
mkdir -p "$OUT/browser-extension"
cp -r browser-extension/manifest.json browser-extension/src browser-extension/icons "$OUT/browser-extension/"
cp README.md LICENSES.md "$OUT/"
mkdir -p "$OUT/docs" && cp docs/*.md "$OUT/docs/"

echo "== ポータブル版 zip =="
(cd "$DIST" && python3 - "$VERSION" <<'EOF'
import sys, zipfile, os
ver = sys.argv[1]
name = f"URLInsight-{ver}-win-x64-portable.zip"
with zipfile.ZipFile(name, "w", zipfile.ZIP_DEFLATED) as z:
    for root, _, files in os.walk("URLInsight"):
        for f in files:
            p = os.path.join(root, f)
            z.write(p, p)
print(name)
EOF
)

echo "== インストーラー (NSIS) =="
if command -v makensis >/dev/null; then
  makensis -V2 -DVERSION="$VERSION" -DSRCDIR="$(pwd)/$OUT" -DOUTFILE="$(pwd)/$DIST/URLInsight-Setup-$VERSION.exe" installer/URLInsight.nsi
else
  echo "makensis が無いためインストーラーの生成をスキップ"
fi

echo "== SHA256 =="
(cd "$DIST" && sha256sum URLInsight/*.exe *.zip *.exe 2>/dev/null | tee SHA256SUMS.txt)
echo "完了: $DIST"
