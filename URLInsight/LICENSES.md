# ライセンス一覧 / Third-party licenses

URL Insight / LinkLens 本体のソースコードの著作権は作成者に帰属します。
本ソフトウェアは以下のオープンソースソフトウェアを利用しています（バージョンは `packages.lock.json` で固定）。

| コンポーネント | バージョン | ライセンス | 用途 |
| --- | --- | --- | --- |
| .NET Runtime / WPF / Windows Forms | 8.0.x | MIT | 実行環境・UI（自己完結型として exe に同梱） |
| AngleSharp | 1.8.3 | MIT | HTML の安全な解析（スクリプトは実行しない） |
| SmartReader | 0.11.1 | Apache-2.0 | 本文抽出（Mozilla Readability の .NET 移植） |
| PdfPig (UglyToad.PdfPig) | 0.1.16 | Apache-2.0 | PDF からのテキスト抽出（マネージド実装） |
| Microsoft.Data.Sqlite / .Core | 8.0.31 | MIT | キャッシュ・履歴の保存 |
| SQLitePCLRaw (bundle_e_sqlite3 ほか) | 2.1.12 | Apache-2.0 | SQLite ネイティブ連携 |
| SQLite | (SQLitePCLRaw 同梱) | Public Domain | データベースエンジン |
| Interop.UIAutomationClient | 10.19041.0 | MIT | Windows UI オートメーション COM 版(UIA3)の .NET 用定義（拡張なしのホバー検出） |
| System.Security.Cryptography.ProtectedData | 8.0.0 | MIT | Windows DPAPI による API キーの暗号化 |
| System.Text.Json / Encodings.Web / Encoding.CodePages / IO.Pipelines / Memory | 10.0.x / 4.5.x | MIT | JSON・文字コード（Shift_JIS 等） |

開発・テストのみで使用（配布物には含まれません）: xUnit (Apache-2.0)、Microsoft.NET.Test.Sdk (MIT)、NSIS (zlib/libpng ライセンス)、Pillow (HPND, アイコン生成)。

各ライセンスの全文:
- MIT: https://licenses.nuget.org/MIT
- Apache-2.0: https://licenses.nuget.org/Apache-2.0
- SQLite: https://www.sqlite.org/copyright.html

## 選定理由（仕様書 10章「候補比較」）

- **本文抽出**: SmartReader は 2026年7月にも更新されている保守中の Readability 移植で、Apache-2.0。AngleSharp ベースでスクリプトを実行しない。極端に深い/巨大な DOM では処理が遅くなるため、本アプリでは DOM の深さ・要素数を検査し、超える場合は自前の簡易抽出に切り替える。
- **PDF**: PDFium はネイティブバイナリの同梱と再配布条件の管理が必要なため、マネージド実装で Apache-2.0 の PdfPig を採用（テキスト抽出のみ。OCR は行わない）。
- **キー保管**: Windows 資格情報マネージャーではなく DPAPI(CurrentUser) を採用。追加のネイティブ依存が不要で、暗号化データのみをユーザープロファイル配下に置ける。
