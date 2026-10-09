# リリース手順

## 1. 事前確認

- `Directory.Build.props` の `<Version>` と `browser-extension/manifest.json` の `version` を更新する。
- バージョン番号（`1.4.0` = 左.真ん中.右）の付け方:
  - **大型アップデート**（新しい機能の追加など）: 真ん中の数字を1つ上げ、右の数字を 0 に戻す（例: `1.4.0` → `1.5.0`）。
  - **小さな変更**（不具合の修正・調整など）: 右の数字を1つ上げる（例: `1.4.0` → `1.4.1`）。
  - 一度使った番号は使い回さない（取り消した版の番号も使用済み: `1.3.0`「APIなし版」、`1.4.0`「Chrome の Gemini 連携」、`1.5.0`「Edge の Copilot 連携・まとめて依頼」。次の大型アップデートは `1.6.0`）。
- 依存関係は `packages.lock.json` で固定されている。更新する場合は意図して `dotnet restore --force-evaluate` を実行し、ライセンス・保守状況を確認して `LICENSES.md` を更新する。

## 2. ビルド

Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Linux / WSL（クロスビルド。Microsoft 版の .NET 8 SDK が必要。Ubuntu 標準の `dotnet-sdk-8.0` パッケージには WPF ビルド用の `Microsoft.NET.Sdk.WindowsDesktop` が含まれないため使えない）:

```bash
./build.sh
```

どちらも次を行う: 単体テスト → アプリ/ネイティブホストの自己完結型 single-file publish（win-x64）→ 拡張・文書のコピー → ポータブル zip → NSIS インストーラー → SHA256 一覧。

## 3. 出力物（`dist/`）

| ファイル | 内容 |
| --- | --- |
| `URLInsight-Setup-<ver>.exe` | インストーラー（ユーザー単位、管理者権限不要） |
| `URLInsight-<ver>-win-x64-portable.zip` | ポータブル版 |
| `URLInsight/URLInsight.exe` | アプリ本体（.NET ランタイム同梱。別途インストール不要） |
| `URLInsight/URLInsight.NativeHost.exe` | Chrome から起動されるネイティブメッセージングホスト |
| `URLInsight/browser-extension/` | Chrome 拡張（デベロッパーモードで読み込む） |
| `SHA256SUMS.txt` | ハッシュ値 |

## 4. 署名（任意）

初期リリースではコード署名証明書を使用していないため、初回実行時に Microsoft Defender SmartScreen の警告（「WindowsによってPCが保護されました」）が表示される。「詳細情報」→「実行」で起動できる。配布する場合は、証明書を取得して `signtool sign /fd SHA256 /tr <timestamp> /td SHA256 <exe>` で `URLInsight.exe`・`URLInsight.NativeHost.exe`・インストーラーに署名すること。

## 5. 公開前チェック

- [ ] クリーンな Windows 10 / 11 (x64) でインストール → 初回起動 → ホスト登録 → 拡張読み込み → ホバーでカード表示
- [ ] AI 未設定 / テスト用プロバイダ / 実プロバイダでの表示
- [ ] PDF・YouTube・ログイン必須ページ・存在しないページのエラー表示
- [ ] マルチモニター・異なる DPI・画面右端でのカード位置
- [ ] アンインストール（データを残す／削除する）
- [ ] ログ・診断情報に URL のパス・キー・本文が含まれないこと
