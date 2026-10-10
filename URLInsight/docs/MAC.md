# URL Insight Mac 版（MacBook 用）

Windows 版と同じように、リンクや URL にカーソルを重ねて少し止めると、リンク先の要約カードが画面の右側に出ます。
要約・ページ取得・キャッシュ・固定カードの仕組みは Windows 版と共通です（同じ `UrlInsight.Core` を使っています）。

> **注意**: Mac 版は Linux 上でビルドしており、**本物の Mac ではまだ一度も動かしていません**。
> 画面の表示は Linux 上で実際に描いて確認しましたが、カーソル下のリンクの読み取り（macOS のアクセシビリティ機能）、
> キーチェーンへのキーの保存、メニューバーのアイコンは未確認です。うまく動かない場合は、メイン画面の
> 「最後の検出」の表示と、設定 → 詳細 →「診断情報を書き出す」の内容を教えてください。

## 1. ダウンロード

お使いの Mac に合ったほうを選んでください（左上の Apple メニュー →「この Mac について」の「チップ」で確認できます）。

| チップ | ファイル |
| --- | --- |
| Apple M1 / M2 / M3 / M4 など | `release/URLInsight-<バージョン>-mac-arm64.zip` |
| Intel | `release/URLInsight-<バージョン>-mac-x64.zip` |

## 2. インストール（最初に1回だけ）

1. zip をダブルクリックして展開し、出てきた **URLInsight**（URL Insight.app）を「アプリケーション」フォルダへ移します。
2. **ターミナル**（アプリケーション → ユーティリティ → ターミナル）を開き、次の1行を貼り付けて Enter を押します。

   ```sh
   xattr -dr com.apple.quarantine /Applications/URLInsight.app
   ```

   - **理由**: このアプリは Apple の開発者証明書による署名と公証（notarization）をしていないため、そのままだと macOS が
     「開発元を検証できないため開けません」「壊れているため開けません」と表示して起動を止めます。
     このコマンドは、インターネットからダウンロードした印（隔離属性）を外し、この確認を省くものです。
   - 代わりに「システム設定 → プライバシーとセキュリティ」の下の方にある「このまま開く」を押す方法もありますが、
     署名の形式によっては「壊れている」と表示されることがあるため、コマンドの方法をおすすめします。
3. アプリケーションフォルダの URLInsight をダブルクリックして起動します。

## 3. 初回起動

1. 「URL Insight へようこそ」が出ます。ログイン時に自動で開始するかを選びます。
2. macOS の「アクセシビリティ」の案内が出たら「システム設定を開く」を押し、一覧の **URLInsight をオン**にします。
   - カーソルの下にあるリンクを読み取るために必要です（VoiceOver などと同じ仕組みです）。
   - オンにしてから反映まで数秒かかります。メイン画面の「ホバー検出」が緑になれば使えます。
   - **アプリを更新（入れ直し）したときは、もう一度オンにし直す必要があります**（macOS が別のアプリとして扱うため）。
     一覧に古い URLInsight が残っていたら「−」で消してから、新しいものをオンにしてください。
3. AI の設定画面が開きます。プロバイダ・モデル・API キーを入れて「キーを保存」を押します（Windows 版と同じです）。
   - キーは **macOS のキーチェーン**に保存します（平文のファイルには保存しません）。
   - 「キーチェーンの使用を許可しますか」と聞かれたら「常に許可」を選んでください。

## 4. 使い方

- メニューバー（画面右上）の URL Insight のアイコンから、メイン画面・URL の貼り付け・一時停止・固定したカードをすべて閉じる・設定・終了 を選べます。
  Dock にはアイコンを出しません。
- Safari・Chrome・Edge などのリンク、アドレスバーの URL、テキストエディットやメモに書かれた URL にカーソルを重ねて少し止めると、カードが出ます。
- Chrome・Edge などは、最初にカーソルを重ねたときに、アプリから「支援技術を使います」という合図（`AXManualAccessibility`）を送って、ページの中のリンクを読めるようにします。
- 要約カードは表示した時点で自動で固定されます（設定 → 一般 でオフにできます）。固定したカードは上部をドラッグして動かせ、再起動後も同じ位置に戻ります。

## 5. Windows 版との違い

| 項目 | Mac 版 |
| --- | --- |
| カーソル下のリンクの読み取り | macOS のアクセシビリティ機能（Windows 版は UI オートメーション） |
| API キーの保存先 | キーチェーン（Windows 版は DPAPI） |
| データの保存先 | `~/Library/Application Support/URLInsight`（設定 → プライバシー に表示） |
| 自動起動 | `~/Library/LaunchAgents/com.urlinsight.app.plist`（ログイン時） |
| 常駐の場所 | メニューバー（Windows 版は通知領域） |
| 「Claude in Chrome で要約」「Chrome の Gemini で要約」ボタン | **まだありません**（Windows 版のみ） |
| Chrome 拡張（ネイティブメッセージング） | ありません（拡張なしのホバー検出で動きます） |

## 6. アンインストール

1. メニューバーのアイコン →「URL Insight を終了」。
2. 設定 → 詳細 →「すべてのユーザーデータを削除して終了」を使うと、設定・キャッシュ・ログ・キーチェーンのキー・自動起動をまとめて消せます。
   使わなかった場合は、次を手で削除します。
   - `~/Library/Application Support/URLInsight`
   - `~/Library/LaunchAgents/com.urlinsight.app.plist`
   - キーチェーンアクセスで「URL Insight」という名前の項目
3. アプリケーションフォルダの URLInsight をゴミ箱へ入れます。
4. システム設定 → プライバシーとセキュリティ → アクセシビリティ の一覧から URLInsight を「−」で外します。

## 7. 開発者向け: ビルド

```sh
./build-mac.sh   # Linux / macOS。dist-mac/ に arm64 と x64 の zip を作る
```

- 必要なもの: .NET 8 SDK、Python 3、zip、署名用に macOS の `codesign` または `rcodesign`（`cargo install apple-codesign --bin rcodesign`）。
- Apple シリコンの Mac は署名の無い実行ファイルを起動できないため、アドホック署名（証明書なしの署名）をします。
- テスト: `dotnet test tests/UrlInsight.Mac.Tests`（配置の計算と、画面なしモードでの画面の描画）。
  `URLINSIGHT_SNAPSHOT_DIR=<フォルダ>` を付けると、描いた画面を PNG で保存します。
- 構成: `src/UrlInsight.Mac/`
  - `Platform/MacNative.cs`: アクセシビリティ（AX）・CoreFoundation・カーソル位置の呼び出し
  - `Platform/KeychainSecretStore.cs`: キーチェーンへの API キーの保存
  - `Platform/LoginItem.cs`: ログイン時の自動起動
  - `Services/AxHoverWatcher.cs`: カーソル下のリンク・URL の検出（Windows 版の UiaHoverWatcher に相当）
  - `Services/HoverCoordinator.cs`: カードの表示・固定・保存（Windows 版から移したもの）
  - `UI/`: カード・メイン画面・設定画面（Avalonia）
