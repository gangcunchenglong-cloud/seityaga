# URL Insight / LinkLens

**リンクの先を、開く前に。** — Chrome でリンクにカーソルを合わせると、画面右側に日本語の要約カード（タイトル・約5行の要約・重要ポイント・ページ種別）を表示する Windows 常駐アプリです。

- 対象: Windows 10 / 11（x64）、Google Chrome（116 以降）
- 構成: **Windows 常駐アプリ（WPF / .NET 8）＋ Chrome 拡張（Manifest V3）＋ Native Messaging**
- **アプリを起動しておくだけで動きます**: リンクやURLの文字列にカーソルを重ねて少し止めると要約カードが出ます（Windows の UI オートメーションで検出。Chrome拡張は任意）
- URL を貼り付けて要約することもできます（手動モード）
- 初期状態では AI へ送信しません。プロバイダと API キーを設定し、送信前の確認を経たときだけ送信します

> 仕様書: `URL_Insight_LinkLens_Claude向け実装仕様書` v1.0 に基づく実装です。UI は同梱の UI サンプル SVG を参考にしています。

---

## 目次

1. [使い方（インストール版）](#1-使い方インストール版)
2. [AI の設定](#2-ai-の設定)
3. [アンインストールとデータ削除](#3-アンインストールとデータ削除)
4. [開発者向け: ビルドと実行](#4-開発者向け-ビルドと実行)
5. [構成と処理の流れ](#5-構成と処理の流れ)
6. [セキュリティとプライバシー](#6-セキュリティとプライバシー)
7. [テスト](#7-テスト)
8. [既知の制限・未確認事項](#8-既知の制限未確認事項)

---

## 1. 使い方（インストール版）

### 1-1. インストール

1. `URLInsight-Setup-1.3.1.exe` を実行します（管理者権限は不要。`%LOCALAPPDATA%\Programs\URLInsight` に入ります）。
   - コード署名をしていないため、SmartScreen の警告が出た場合は「詳細情報」→「実行」を選んでください。
2. インストーラーが Chrome 用のネイティブメッセージングホストを自動で登録します。
3. 完了画面で「URL Insight を起動する」にチェックを入れて完了します。

ポータブル版（zip）の場合は、任意のフォルダに展開して `URLInsight.exe` を起動し、設定 → ブラウザ拡張 →「ホストを登録」を押してください。

### 1-2. 初回起動

1. ようこそ画面で「Windows起動時に自動で開始するか」を選びます（既定では有効にしません）。
2. 設定画面の「ブラウザ拡張」が開きます。

### 1-3. Chrome 拡張の読み込み（任意）

拡張がなくても、アプリを起動しておけばホバーで要約できます（設定 → 一般「拡張機能なしでもホバーを検出する」、既定でオン）。拡張を入れると、ページの構造から直接リンクを検出できるため、より確実になります。

1. Chrome で `chrome://extensions` を開き、右上の **デベロッパーモード** をオンにします。
2. **「パッケージ化されていない拡張機能を読み込む」** を押し、インストール先の `browser-extension` フォルダ（スタートメニュー「URL Insight → Chrome拡張フォルダ」、または設定の「拡張フォルダを開く」）を選びます。
3. 拡張機能 ID が `jmmabchpljfpflppkhkhbfejbhjaadii` になっていることを確認します（同梱の公開鍵で固定されています）。
4. ツールバーの URL Insight アイコン → **「このサイトで有効にする」** → Chrome の権限確認で「許可」。
   - サイトごとに許可する方式です（全サイトへの権限は要求しません）。
   - 「このタブだけ一時的に有効にする」なら権限を保存せずに試せます。
5. ポップアップに「ネイティブホストと接続しています」「URL Insight アプリ: 稼働中」と出れば準備完了です。アプリのメイン画面でも「Chrome拡張: 接続済み」になります。

### 1-4. 使う

- アプリを起動した状態で、ブラウザのリンク・メモ帳などに書かれたURLにカーソルを重ねると、画面右側にカードが出ます（止めると約0.6秒で反応、それ以外も1秒ごとに検出。折り返された長いURLも全体を取得）（待ち時間は 300〜1500ms で変更可）。拡張を入れた場合は、拡張を有効にしたサイトでも同様に動きます。
- リンクから離れると少し後に閉じます。カードの上にカーソルを移せば残ります。Esc / × で閉じます。
- **すべての要約カードが、表示した時点で自動で固定されます。** URL にカーソルを重ねたとき・メイン画面に URL を貼り付けたとき・「最近の要約」から開いたとき・再要約のどれでも、その時点でカードを固定し、要約はそのカードの中で最後まで作られて表示されます。要約ができる前にカーソルが離れても、別のリンクにカーソルを重ねても消えず、× で閉じるまで残ります（AI 未設定でページ情報だけのカードも残ります）。エラーになったカードは5秒後に自動で閉じます（カードの上にカーソルがあるあいだは待ちます）。設定 → 一般 →「要約のカードを自動で固定する」でオフにできます。トレイアイコンの「固定したカードをすべて閉じる」でまとめて閉じられます。
- **カードの「Claude in Chrome で要約」ボタン**（「ページを開く」の隣）を押すと、起動中の Chrome を前面に出してサイドバーの Claude（Anthropic の拡張機能「Claude in Chrome」）を開き（閉じていればツールバーの Claude ボタンを押し、ボタンが無ければ Claude in Chrome のショートカット Ctrl+E を送ります）、「次のリンク先のページを開いて内容を読み、日本語で3〜5文に要約してください: URL」と入力して送信します。ボタンを押したときだけ動き、キーボードの入力先が Claude のパネルの入力欄だと確かめられたときだけ入力します。クリップボードは使いません。結果はボタンの下に表示します。**事前に、Chrome ウェブストアで拡張機能「Claude」（Claude in Chrome）を入れ、Anthropic のアカウントでログインし、拡張機能のボタンをツールバーに固定（拡張機能メニューのピン）しておいてください。** ページを開く・読む操作の許可を Claude in Chrome が求めた場合は、Chrome のサイドバーで許可してください。
- **カードの「Chrome の Gemini で要約」ボタン**（「ページを開く」の隣）を押すと、起動中の Chrome を前面に出してサイドバーの Gemini（Gemini in Chrome）を開き（閉じていれば Alt+G を送り、開かなければツールバーの Gemini ボタンを押します）、「次のリンク先のページの内容を、日本語で3〜5文に要約してください: URL」と入力して送信します。ボタンを押したときだけ動きます。キーボードの入力先が Gemini の入力欄だと確かめられたときだけ入力し、クリップボードは使いません。結果はボタンの下に表示します。Chrome の「Gemini in Chrome」と Alt+G のショートカットが有効である必要があります（Chrome の設定 → AI → Gemini in Chrome）。
- **固定したカードは、アプリやパソコンを再起動しても残ります。** 内容と画面上の位置を `%LOCALAPPDATA%\URLInsight\pinned-cards.json` に保存し、次に起動したときに同じ位置へ表示し直します（最大50枚。モニター構成が変わって画面外になる場合は、いちばん近い画面の中に収めます）。カードを閉じると保存からも消えます。
- **「固定」を押したカードも、別のリンクにカーソルを重ねても消えません。** 以後のホバーは新しいカードに表示し、固定したカードと重ならない位置に出します。固定したカードは何枚でも残せ、上部（種別・ドメインの行）をドラッグして移動できます。そのカードの「開く」「コピー」「再要約」はそのカードの内容に対して動き、× または固定を外すと閉じます。要約の途中で固定した場合も、結果は固定したカードに表示されます。
- カードの操作: **ページを開く / 再要約 / コピー / このURLを無視 / 閉じる**。自動でページへ移動することはありません。
- メイン画面の「URLを貼り付けて要約」から手動でも要約できます。トレイアイコンのメニューからも呼び出せます。
- 一時停止・再開はメイン画面またはトレイメニューから。ウィンドウの ✕ はトレイに格納するだけで、終了はトレイメニューの「終了」です。

## 2. AI の設定

設定 → **AIプロバイダ** で次から選びます。モデル名・料金・無料枠は変動するため、アプリには固定で持たせていません。提供元の公式情報を確認して入力してください。

| プロバイダ | エンドポイント | 備考 |
| --- | --- | --- |
| 未設定（既定） | — | AI へ送信しない。タイトル・説明などページ情報のみ表示 |
| OpenAI | `https://api.openai.com/v1` | Chat Completions 互換 |
| Anthropic (Claude) | `https://api.anthropic.com/v1` | Messages API。モデル例: `claude-opus-5-5`, `claude-haiku-4-5` |
| Google Gemini | `https://generativelanguage.googleapis.com/v1beta/openai` | OpenAI 互換エンドポイント |
| Groq | `https://api.groq.com/openai/v1` | OpenAI 互換 |
| OpenRouter | `https://openrouter.ai/api/v1` | OpenAI 互換 |
| カスタム OpenAI 互換（高度な設定） | 任意（HTTPS のみ） | 警告を表示。平文 HTTP は拒否 |
| テスト用（AIではありません） | — | 外部送信なし。本文の冒頭を抜粋してカードの動作を確認する用途。カードに「テスト用」と明示 |

手順: プロバイダ → モデル名 → API キー入力 →「キーを保存」→（任意）「接続テスト」→「保存して閉じる」。

- キーは **Windows DPAPI（現在のユーザー）** で暗号化して `%LOCALAPPDATA%\URLInsight\secrets` に保存します。設定ファイル・ログ・診断情報には書きません。暗号化に失敗した場合は平文で保存せず、「このセッションのみ使う」かどうかを確認します。
- **送信前の確認**: 各プロバイダの初回は必ず確認カードが出ます（送信先・送信内容・文字数を表示）。「毎回確認する」をオフにすると、2回目以降は確認を省略できます。
- 1回に送る本文は初期 12,000 文字まで（1,000〜50,000 で変更可）。
- **YouTube Data API キー（任意）**: 設定すると公式 API で動画の説明欄を取得して要約できます。

## 3. アンインストールとデータ削除

- 「設定 → アプリ → インストールされているアプリ」から **URL Insight** をアンインストール、またはスタートメニュー「URL Insight → アンインストール」。
  - ネイティブホスト登録と自動起動の設定を解除します。
  - 「ユーザーデータも削除しますか？」で **はい** を選ぶと、設定・キャッシュ・API キー・ログ（`%LOCALAPPDATA%\URLInsight`）も削除します。
- アプリを残したままデータだけ消す: 設定 → 詳細 →「すべてのユーザーデータを削除して終了」。
- Chrome 拡張は `chrome://extensions` から「削除」してください。

## 4. 開発者向け: ビルドと実行

### 4-1. 必要なもの

| ツール | バージョン | 用途 |
| --- | --- | --- |
| .NET SDK | 8.0（8.0.4xx で確認） | ビルド・テスト。**Linux でクロスビルドする場合は Microsoft 版 SDK**（`Microsoft.NET.Sdk.WindowsDesktop` を含むもの）が必要 |
| Node.js | 18 以降（22 で確認） | 拡張の単体テスト（`node --test`） |
| NSIS | 3.x | インストーラー生成（任意） |
| Google Chrome | 116 以降 | 動作確認 |
| Python 3 + Pillow | 任意 | アイコン再生成（`tools/make_icons.py`） |

### 4-2. 取得・復元・テスト

```powershell
git clone <このリポジトリ>
cd URLInsight
dotnet restore --locked-mode
dotnet test tests/UrlInsight.Core.Tests -c Release
node --test browser-extension/tests/linkfilter.test.js
```

### 4-3. Debug 実行（Windows）

```powershell
dotnet build src/UrlInsight.NativeHost -c Debug
dotnet build src/UrlInsight.App -c Debug
# ネイティブホストの exe をアプリと同じフォルダへ
copy src\UrlInsight.NativeHost\bin\Debug\net8.0\win-x64\URLInsight.NativeHost.exe src\UrlInsight.App\bin\Debug\net8.0-windows\win-x64\
src\UrlInsight.App\bin\Debug\net8.0-windows\win-x64\URLInsight.exe
```

### 4-4. Native Messaging ホストの開発用登録・解除

アプリの「設定 → ブラウザ拡張 → ホストを登録 / 登録を解除」、またはコマンドで:

```powershell
URLInsight.exe --register-native-host     # HKCU\Software\Google\Chrome\NativeMessagingHosts\com.urlinsight.linklens を作成
URLInsight.exe --unregister-native-host   # 登録と自動起動を解除
```

登録すると `%LOCALAPPDATA%\URLInsight\native-host\com.urlinsight.linklens.json`（`path` = アプリと同じフォルダの `URLInsight.NativeHost.exe`、`allowed_origins` = `chrome-extension://<拡張ID>/`）を作成し、レジストリからこのファイルを指します。拡張 ID を変えた場合は設定で ID を入力して登録し直してください。

拡張は `browser-extension` フォルダを「パッケージ化されていない拡張機能」として読み込みます（ビルド不要の素の JavaScript）。

### 4-5. Release ビルドとインストーラー

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1   # Windows
./build.sh                                             # Linux / WSL（Microsoft 版 .NET 8 SDK + NSIS）
```

出力（`dist/`）: `URLInsight-Setup-1.3.1.exe`（インストーラー）、`URLInsight-1.3.1-win-x64-portable.zip`、`URLInsight/URLInsight.exe`（自己完結型・単一ファイル）、`URLInsight/URLInsight.NativeHost.exe`、`URLInsight/browser-extension/`。詳細は [docs/RELEASE.md](docs/RELEASE.md)。署名はしていません（同ドキュメント参照）。

### 4-6. プロジェクト構成

```text
URLInsight/
  src/
    UrlInsight.Core/          # OS 非依存のロジック（テスト対象）
      Net/                    #   URL 検証・正規化、SSRF 対策付き HTTP 取得
      Content/                #   HTML / PDF / YouTube の本文抽出
      AI/                     #   プロバイダ共通IF・OpenAI互換・Anthropic・テスト用、プロンプト、JSON 検証、再試行
      Storage/                #   設定、DPAPI キー保管、SQLite キャッシュ
      Bridge/                 #   Native Messaging フレーミング、メッセージ検証、ホスト中継
      Pipeline/               #   1リンクの要約処理の流れ
      Diagnostics/            #   ログ（伏せ字処理）、診断情報
    UrlInsight.App/           # WPF 常駐アプリ（トレイ、メイン画面、カード、設定、パイプサーバー）
    UrlInsight.NativeHost/    # Chrome が起動するホスト（標準入出力 ⇔ 名前付きパイプ）
  browser-extension/          # Chrome 拡張（MV3）
  tests/UrlInsight.Core.Tests # xUnit
  installer/URLInsight.nsi    # NSIS スクリプト
  docs/                       # PRIVACY / TROUBLESHOOTING / RELEASE
  tools/make_icons.py
```

仕様書 14章の推奨構成（BrowserBridge / Content / AI / Storage を別プロジェクト）は、`UrlInsight.Core` 内の名前空間として分離しました。依存方向は同じで、配布物とビルドを簡潔にするためです。拡張は TypeScript ではなく素の JavaScript にしました（ビルド工程なしで読み込めるようにするため）。

## 5. 構成と処理の流れ

```text
[Chrome タブ] content script ──runtime.sendMessage──▶ [拡張 service worker]
                                                         │ connectNative (Native Messaging, stdio, 4バイト長+JSON)
                                                         ▼
                                    [URLInsight.NativeHost.exe]  ← Chrome が起動
                                                         │ 名前付きパイプ（同一ユーザーのみ）
                                                         ▼
[URLInsight.exe 常駐アプリ] ── 取得(HTTPS) ──▶ リンク先サイト
            │                ── 要約(HTTPS, 確認後) ──▶ AI 提供元
            └─ 要約カード（画面右側）/ キャッシュ(SQLite) / DPAPI
```

1. 拡張は「有効にしたサイト」でだけ動き、`<a href>` へのホバーを検出。初期 600ms 待ってから URL・リンク文字列・ページタイトルを送る。短い通過では送らない。`javascript:` / `data:` / `file:` / `chrome:` 等や資格情報付き URL は送らない。
2. ネイティブホストはメッセージのサイズ（64KB）・スキーマ版・形式を検証し、アプリへ中継する。アプリが起動していなければ拡張へ「未起動」を返す（勝手に起動しない）。
3. アプリは再度 URL を検証・正規化（追跡パラメータ `utm_*` 等のみ除去）し、カードに「ページ情報を確認中…」を表示。
4. キャッシュ（URL + プロンプト版 + プロバイダ/モデル + 言語 + 処理版）を検索。ヒットすれば即表示（AI 通信なし）。
5. ミスなら取得: localhost / プライベート / リンクローカル / 予約 IP を DNS 解決後に拒否し、その IP へ直接接続（DNS rebinding 対策）。リダイレクトは最大5回で毎回再検査。20MB・20秒の上限。Cookie / 認証情報は送らない。
6. HTML（SmartReader + 簡易抽出、5万字まで）/ PDF（PdfPig、50ページまで、画像のみ・暗号化は対象外）/ YouTube（公式 oEmbed・任意で Data API）を抽出し、根拠量を評価。本文が無ければ AI に送らず、メタ情報のみ表示。
7. AI 未設定ならページ情報のみ表示。設定済みなら送信前確認 → 要約。要約の精度を上げるため、(a) メニュー・Cookie 表示・著作権表示・共有ボタン・関連記事などの行を除いた本文を渡す、(b) 上限を超える本文は冒頭（約3/4）と末尾（約1/4）を渡して結論を取りこぼさない、(c)「結論を1文目に」「数値・固有名詞は本文の表記どおり」などページの種類に応じた指示を与える、(d) 要約に本文に無い数値（2桁以上）があれば、その数値を伝えて1回だけ作り直させ、それでも残ればカードに注意書きを出す。Google 検索の URL（`google.*/search?q=…`）は、Google が JavaScript なしでは結果を返さないため、検索キーワードを取り出して DuckDuckGo の HTML 版（取得できなければ Bing）で同じキーワードを検索し、上位の結果（題名・サイト・抜粋）をカードに一覧表示する（AI 設定済みなら結果の要約も表示。結果は時間で変わるため保存しない）。Google の転送用リンク（`google.*/url?q=…`）は転送先のページを要約する。出力は厳格な JSON 検証（要約1〜5文、重要ポイント最大4、長さ制限）。不正なら1回再試行。429 は Retry-After を尊重、5xx は指数バックオフ＋ジッターで最大2回、401/403 は再試行しない。
8. 別リンクへ移ったら古い処理をキャンセルし、古い結果は画面に出さない（requestId で判定）。

## 6. セキュリティとプライバシー

詳細は [docs/PRIVACY.md](docs/PRIVACY.md)。要点:

- テレメトリなし。初期状態で AI 送信なし。送信前に送信先・内容・文字数を確認。
- 拡張なしのホバー検出は、カーソルが止まったとき、および1秒ごとに、カーソル位置の要素と前後約1000文字だけを UI オートメーションで読み取ります（画面全体は読み取らない、自分のウィンドウ・パスワード欄は対象外、内容はログに残さない）。設定でオフにできます。
- ブラウザ（Chrome・Edge・Firefox など）のアドレスバーにカーソルを重ねると、表示中のページの URL を読み取って要約します。Chrome や Edge は「https://」を省いて表示するため、「example.com/path」の形でも https:// を補って扱います（入力途中の検索語は対象外。ブラウザ以外の入力欄では、ファイル名などとの取り違えを防ぐため補いません）。
- API キーは DPAPI で暗号化保存。平文フォールバックなし。ログ・診断情報は伏せ字処理し、さらに自動検査。
- ページ本文は AI への入力時に「引用データであり命令ではない」と明示（プロンプトインジェクション対策）。区切りタグの偽装も無害化。
- 要約・タイトル・URL は WPF のテキストとして表示（HTML として描画しない）。「ページを開く」は http/https として検証できた URL のみ。
- ローカル HTTP サーバーやネットワークポートは開かない。アプリ ⇔ ホストは同一 Windows ユーザー限定の名前付きパイプ。
- 悪意ある巨大・深い HTML への対策（DOM 深さ/要素数の上限、解析タイムアウト15秒）。

## 7. テスト

### 自動テスト（このリポジトリで実行済み）

| 対象 | 件数 | 結果 | 内容 |
| --- | --- | --- | --- |
| `UrlInsight.Core.Tests`（xUnit, Linux 上で実行） | 210 | 210 成功 / 0 失敗 | URL スキーム/資格情報/Unicode ドメイン/IPv4・IPv6・ローカル IP の拒否、接続時 IP 検査（DNS rebinding 想定）、リダイレクト上限・スキーム変更、サイズ上限、タイムアウト、HTTP ステータス分類、Cookie/認証ヘッダー非送信、HTML 抽出（メタ/JSON-LD/Shift_JIS/EUC-JP/巨大本文/悪意ある深い DOM）、PDF（抽出/画像のみ/破損）、YouTube ID、AI 出力 JSON 検証、プロンプトの区切り無害化、再試行（不正出力1回/Retry-After/5xx バックオフ/401・400 非再試行）、OpenAI 互換・Anthropic アダプター（ヘッダー/本文/エラー分類/エラー文からのキー除去/refusal）、キャッシュ TTL・LRU・キー分離・モデル変更での無効化、設定の破損時復旧、DPAPI の平文フォールバック禁止、ログ・診断情報の伏せ字、Native Messaging フレーミング/サイズ上限/不正メッセージ拒否、ホスト中継（アプリ未起動→起動→双方向中継→終了）、パイプライン通し（AI 未設定/同意拒否/要約→キャッシュヒット→再要約→モデル変更/確認省略/空ページ/403/テスト用プロバイダ/キャンセル）、要約の精度向上（本文のノイズ除去・冒頭と末尾の送信・数値の裏付け確認と作り直し）、カーソル下の URL 全体取得、アドレスバーの「https://」省略表示の URL 化、Google 検索（キーワード抽出・転送リンクの解除・DuckDuckGo/Bing の結果の読み取り・Bing への切り替え・AI 要約・保存しないこと） |
| 拡張 `linkfilter.test.js`（node:test） | 6 | 6 成功 / 0 失敗 | 許可スキーム、危険スキーム拒否、資格情報付き URL、長さ上限、同一ページ内アンカー除外、リンク文字列整形 |

### Windows 用 exe の動作確認（Linux 上の Wine 9.0 で実施）

ビルドした Windows 用バイナリそのものを Wine で実行して確認した項目です。

| 項目 | 結果 |
| --- | --- |
| `URLInsight.NativeHost.exe` を Chrome と同じ起動方法（引数 `chrome-extension://…/`、標準入出力に 4バイト長+JSON）で実行 | アプリ未起動時に `status(appRunning=false)` を返す、不正なスキーマ版を `unsupported_version` で拒否、入力終了で終了コード0 |
| ホスト ⇔ アプリ間の名前付きパイプ（`CurrentUserOnly`、アプリと同じ設定のサーバー） | `hello` → `config` 返信、`hoverLink`（日本語のリンク文字列を含む）・`hoverEnd` が検証を通ってアプリ側へ届くことを確認 |
| `URLInsight.exe --register-native-host` | HKCU のレジストリ値とホストマニフェスト（`path`・`allowed_origins`）が正しく作成される |
| アプリ移動時の自動再登録 | 別フォルダから起動するとホストのパスが自動で更新される |
| インストーラー（`/S` サイレント） | `%LOCALAPPDATA%\Programs\URLInsight` へ配置、ホスト登録、スタートメニュー（3項目）、「アプリと機能」登録 |
| アンインストーラー（`/S`） | ファイル・ホスト登録・アンインストール情報を削除、ユーザーデータは既定で保持 |
| GUI（`URLInsight.exe` 通常起動） | 起動処理（設定読込・キャッシュ・パイプサーバー開始）までログで確認。**画面描画は Wine の WPF 文字描画の制限で確認できず**（フォントを明示指定した WPF ウィンドウは、最小構成のサンプルでも Wine 上では同じ箇所で停止することを確認済み。実機 Windows では発生しない種類の問題） |

### 手動 E2E 手順（Windows 実機で確認すること）

[docs/RELEASE.md](docs/RELEASE.md) の「公開前チェック」を参照。

## 8. 既知の制限・未確認事項

**正直な状況報告**: このバージョンは Linux 上でクロスビルドしました。OS に依存しないロジック（取得・抽出・AI 接続・キャッシュ・中継など）は自動テストで、ネイティブホスト・パイプ通信・ホスト登録・インストーラー/アンインストーラーは Wine 上で確認しましたが、**Windows 実機と実際の Chrome での動作（画面表示・トレイ・カード位置・DPI・Chrome からの実接続・DPAPI 暗号化・実際の AI プロバイダとの通信）はまだ確認していません**。初回は [docs/RELEASE.md](docs/RELEASE.md) の「公開前チェック」に沿って確認してください。

- Windows 10/11 x64 のみ（ARM64 は対象外）。Chrome 以外の Chromium 系は未検証（Edge 登録は試験的オプション）。
- コード署名なし（SmartScreen 警告が出る）。Chrome ウェブストア未掲載（デベロッパーモードで読み込む）。自動更新なし。
- ログイン壁・CAPTCHA・有料記事・DRM・スキャン画像 PDF（OCR なし）・暗号化 PDF は要約不可。
- YouTube は字幕本文を取得しない（公式 API の制約）。説明欄の利用には YouTube Data API キーが必要。
- ページ取得はシステムのプロキシを使わない（接続先 IP 検証のため）。直接外部へ出られない社内ネットワークでは取得不可。
- テーマはライトのみ（ハイコントラスト時はシステム配色）。Windows の「テキストを大きくする」設定には自動追従しない。
- JavaScript で描画される SPA ページは、サーバーが返す HTML に本文が無い場合、メタ情報のみになる（スクリプトは実行しないため）。
- 拡張なしのホバー検出は、アプリが UI オートメーションに情報を公開している場合だけ動きます（画像内の URL、独自描画のアプリ、一部のゲーム等では反応しない）。初めて読み取るときに Chrome のアクセシビリティ機能が有効になり、Chrome の動作がわずかに重くなる場合があります。
- 拡張なしのホバー検出は Windows 実機で未確認です（Wine では UI オートメーションの「カーソル位置の要素の取得」が未実装のため）。うまく動かない場合は、メイン画面の「最後の検出」欄に表示される内容（例: 「chrome: リンクではない場所（ペイン）」）を確認してください。ログにも `uia hover:` で記録されます（URL は記録しません）。
