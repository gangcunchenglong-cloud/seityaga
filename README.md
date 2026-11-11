# ICT活用能力テスト

受験者が「今の自分のICT活用能力」を知るための、級別・階段型のWebテストです。
ブラウザだけで動き（サーバー・ログインなし）、結果はCSVで保存して Teams／OneDrive で提出します。

- 8級（小1めやす）〜1級（高校〜大人めやす）、1つの級は9問（基本操作・情報収集・モラル安全から3問ずつ）
- 合格条件：**合計7問以上 かつ 各分野2問以上**。合格すると次の級へ、不合格でそこで終了。全体で30分
- 対応端末：Chromebook / iPad / Windows PC / スマホ（基本操作の問題は端末で切り替え）

先生向けの使い方は [docs/teacher-guide.md](docs/teacher-guide.md) を見てください。

## コマンド

| コマンド | 内容 |
|---|---|
| `npm install` | 準備（最初に1回） |
| `npm run dev` | 開発サーバーを起動（http://localhost:5173/） |
| `npm run build` | `dist/index.html`（受験アプリ）と `dist/teacher.html`（先生用ツール）を作る。どちらも1ファイルで完結し、`file://` でも開ける |
| `npm test` | 単体テスト（出題・採点・合否・30分制限・CSV・確認コード・問題バンクの形式など） |
| `npm run typecheck` | TypeScript の型チェック |
| `npm run generate -- …` | Claude API で問題の候補を生成（開発者用、`ANTHROPIC_API_KEY` が必要） |
| `npm run build:review` | 問題レビュー用ツールを `dist-tools/scripts/review.html` に作る |
| `npm run merge -- <file>` | レビューで承認した問題を問題バンクに反映 |

開発サーバーで開ける画面：

- 受験アプリ（本番モード）：http://localhost:5173/
- 受験アプリ（試作モード・draft の問題も出題）：http://localhost:5173/?mode=trial
- 先生用ツール：http://localhost:5173/teacher.html
- 問題レビュー：http://localhost:5173/scripts/review.html

> サンプル問題はすべて `draft`（未確認）です。本番モードでは問題が「準備中」になるので、動作確認は `?mode=trial` で行ってください。

## ファイル構成

```
index.html / teacher.html     各ページの入口
src/
  types.ts, config.ts         型定義・設定（合格条件、制限時間、クラス一覧、確認コード用の文字列）
  logic/                      画面から独立したロジック（単体テストの対象）
    select.ts                 出題（級・分野・端末・status で絞り込み、各分野ランダム3問）
    score.ts / judge.ts       正誤判定・合否判定・不合格理由
    session.ts                階段型の進行と30分制限
    csv.ts / sha256.ts        CSV の書き出し・読み込み、確認コード
    validate.ts               問題JSONの形式チェック
  questions/grade-1〜8.json   問題バンク
  content/advice.ts           分野×級のアドバイス文
  ui/                         画面（スタート・問題・級の合否・結果）と問題形式ごとの部品
  teacher/                    先生用ツール
scripts/                      問題生成・レビュー・反映（開発者用）
questions/drafts/             生成した問題の置き場
docs/teacher-guide.md         先生向けガイド
tests/                        単体テスト
```

## プライバシー・安全

- 名前・メールアドレスは扱いません（クラスと出席番号のみ）。
- テスト中・結果表示中に外部への通信はしません。ビルドしたHTMLには外部通信を禁止する Content-Security-Policy を入れています。解析ツール・トラッキングはありません。
- 受験中のデータはメモリ上のみで、ブラウザを閉じると消えます。
- APIキーはコードやリポジトリに書かず、環境変数 `ANTHROPIC_API_KEY`（`.env`、`.gitignore` 済み）で渡します。
