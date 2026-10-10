#!/usr/bin/env bash
# URL Insight — Mac 版のビルド(Linux / macOS から、MacBook 用の URLInsight.app を作る)
#   必要: .NET 8 SDK、Python 3(Pillow があればアイコンを高画質で作る)、zip、
#         rcodesign(Linux で署名する場合。cargo install apple-codesign --bin rcodesign)または macOS の codesign
#   出力: dist-mac/URLInsight-<ver>-mac-arm64.zip(Apple シリコン: M1 以降)
#         dist-mac/URLInsight-<ver>-mac-x64.zip(Intel の Mac)
#   署名: Apple の開発者証明書は使わず「アドホック署名」をする(Apple シリコンの Mac は署名の無いアプリを起動できないため)。
#         公証(notarization)はしていないので、初回は「システム設定 → プライバシーとセキュリティ → このまま開く」が必要。
set -euo pipefail
cd "$(dirname "$0")"
[ -f env.sh ] && source env.sh

VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)
DIST=dist-mac
echo "== URL Insight（Mac 版）$VERSION =="
rm -rf "$DIST"
mkdir -p "$DIST"

echo "== 単体テスト =="
dotnet test tests/UrlInsight.Core.Tests -c Release --nologo
dotnet test tests/UrlInsight.Mac.Tests -c Release --nologo

# アプリのアイコン(.icns)を PNG から作る(PNG をそのまま入れる形式)
make_icns() {
  python3 - "$1" "$2" <<'PY'
import struct, sys, io
src, out = sys.argv[1], sys.argv[2]
try:
    from PIL import Image
    im = Image.open(src).convert("RGBA")
    def png(size):
        b = io.BytesIO(); im.resize((size, size), Image.LANCZOS).save(b, "PNG"); return b.getvalue()
    chunks = [(b"ic07", png(128)), (b"ic08", png(256)), (b"ic09", png(512))]
except ImportError:
    chunks = [(b"ic08", open(src, "rb").read())]
body = b"".join(t + struct.pack(">I", len(d) + 8) + d for t, d in chunks)
open(out, "wb").write(b"icns" + struct.pack(">I", len(body) + 8) + body)
PY
}

for RID in osx-arm64 osx-x64; do
  ARCH=${RID#osx-}
  APP="$DIST/$ARCH/URLInsight.app"
  echo "== $RID =="
  dotnet publish src/UrlInsight.Mac -c Release -r "$RID" --self-contained true -p:DebugType=none -p:UseAppHost=true -o "$DIST/publish-$ARCH"
  mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
  cp -R "$DIST/publish-$ARCH/." "$APP/Contents/MacOS/"
  chmod +x "$APP/Contents/MacOS/URLInsight"
  make_icns src/UrlInsight.Mac/Assets/app256.png "$APP/Contents/Resources/URLInsight.icns"
  cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>URL Insight</string>
    <key>CFBundleDisplayName</key><string>URL Insight</string>
    <key>CFBundleIdentifier</key><string>com.urlinsight.app</string>
    <key>CFBundleExecutable</key><string>URLInsight</string>
    <key>CFBundleIconFile</key><string>URLInsight.icns</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>$VERSION</string>
    <key>CFBundleVersion</key><string>$VERSION</string>
    <key>CFBundleDevelopmentRegion</key><string>ja</string>
    <key>LSMinimumSystemVersion</key><string>12.0</string>
    <key>LSApplicationCategoryType</key><string>public.app-category.productivity</string>
    <!-- Dock に出さず、メニューバーに常駐する -->
    <key>LSUIElement</key><true/>
    <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
  rm -rf "$DIST/publish-$ARCH"

  echo "== 署名（アドホック） =="
  if command -v codesign >/dev/null; then
    codesign --force --deep --sign - "$APP"
  elif command -v rcodesign >/dev/null; then
    # .NET の DLL(Mach-O ではないファイル)については rcodesign が警告を出すが、
    # 起動に必要な実行ファイル・dylib にはアドホック署名が付く
    rcodesign sign "$APP" 2>&1 | grep -v "nested rule\|do not know how to handle\|consider reporting" || true
    rcodesign print-signature-info "$APP/Contents/MacOS/URLInsight" | grep -qi "adhoc"
  else
    echo "codesign / rcodesign が無いため署名をスキップ（Apple シリコンの Mac では起動できません）"
  fi

  echo "== zip =="
  (cd "$DIST/$ARCH" && zip -qry "../URLInsight-$VERSION-mac-$ARCH.zip" URLInsight.app)
done

echo "== SHA256 =="
(cd "$DIST" && sha256sum *.zip | tee SHA256SUMS.txt)
echo "完了: $DIST"
