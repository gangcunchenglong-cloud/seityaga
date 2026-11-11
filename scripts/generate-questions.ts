/**
 * 問題の候補を Claude API で生成する（開発者用）。
 *
 *   npm run generate -- --grade 5 --field safety --type choice --count 5
 *   npm run generate -- --grade 4 --field operation --device touch --type ui-task --count 3
 *   npm run generate -- --grade 3 --field information --type match --dry-run   （APIを呼ばずにプロンプトを表示）
 *
 * - APIキーは環境変数 ANTHROPIC_API_KEY から読む（.env があれば読み込む）。キーをコードに書かないこと。
 * - 生成した問題は status: "draft" として questions/drafts/ に保存する。
 *   人が scripts/review.html で確認・承認してから、npm run merge で問題バンクに入れる。
 */
import fs from 'node:fs';
import path from 'node:path';
import { parseArgs } from 'node:util';
import Anthropic from '@anthropic-ai/sdk';
import { betaZodOutputFormat } from '@anthropic-ai/sdk/helpers/beta/zod';
import { z } from 'zod';
import { FIELD_LABELS, GRADE_GUIDE } from '../src/config';
import { validateQuestion } from '../src/logic/validate';
import { BANK } from '../src/questions';
import type { DeviceTarget, Field, Grade, Question, QuestionType } from '../src/types';

const MODEL_DEFAULT = 'claude-opus-5-5';

// ---------------------------------------------------------------- 引数
const { values: args } = parseArgs({
  options: {
    grade: { type: 'string' },
    field: { type: 'string' },
    type: { type: 'string' },
    count: { type: 'string', default: '5' },
    device: { type: 'string' },
    model: { type: 'string', default: MODEL_DEFAULT },
    'dry-run': { type: 'boolean', default: false },
    help: { type: 'boolean', default: false },
  },
});

function usage(msg?: string): never {
  if (msg) console.error(`エラー: ${msg}\n`);
  console.error(`使い方:
  npm run generate -- --grade <1-8> --field <operation|information|safety> --type <形式> [--count 5] [--device pc|touch] [--dry-run]

  形式: choice / multi / order / match / input / ui-task
  基本操作（operation）では --device pc か --device touch が必要です。
  input と ui-task は基本操作の問題だけで使えます。`);
  process.exit(msg ? 1 : 0);
}

if (args.help) usage();
const grade = Number(args.grade) as Grade;
const field = args.field as Field;
const qtype = args.type as QuestionType;
const count = Number(args.count);
if (!(grade >= 1 && grade <= 8 && Number.isInteger(grade))) usage('--grade は 1〜8');
if (!['operation', 'information', 'safety'].includes(field)) usage('--field が不正です');
if (!['choice', 'multi', 'order', 'match', 'input', 'ui-task'].includes(qtype)) usage('--type が不正です');
if (!(count >= 1 && count <= 20)) usage('--count は 1〜20');
if ((qtype === 'input' || qtype === 'ui-task') && field !== 'operation') usage(`${qtype} は基本操作（operation）だけで使えます`);
let device: DeviceTarget = 'all';
if (field === 'operation') {
  if (args.device !== 'pc' && args.device !== 'touch') usage('基本操作では --device pc か --device touch を指定してください');
  device = args.device;
}

// ---------------------------------------------------------------- 級・分野の説明（プロンプトに入れる）
const GRADE_DETAIL: Record<Grade, string> = {
  8: 'タップ・ドラッグ、パスワードを人に教えない',
  7: '簡単な文字入力、知らない人からのメッセージへの対応',
  6: 'ローマ字入力、キーワード検索の基本',
  5: 'コピー＆ペースト、著作権の入口、写真の扱い',
  4: '情報の比べ方、表やスライドの基本、SNSの危険',
  3: '複数の情報源の比較、グラフの読み取り、個人情報の範囲',
  2: 'ネット情報の真偽、セキュリティの基本、生成AIの使い方',
  1: '出典の確認、フィッシング詐欺の見分け方、AIの出力の批判的な読み方',
};
const MEXT_STEP: Record<Grade, string> = { 8: '1', 7: '1', 6: '2', 5: '2', 4: '3', 3: '3', 2: '4', 1: '5' };

const FIELD_DETAIL: Record<Field, string> = {
  operation: 'タップ・ドラッグ、文字入力、コピー＆ペースト、ファイルの保存、画面の読み取り',
  information: '検索の仕方、情報の比較、真偽の見極め、出典の確認、グラフや表の読み取り',
  safety: 'パスワード、個人情報、著作権、SNSの危険、AIの適切な使い方',
};

const DEVICE_DETAIL: Record<DeviceTarget, string> = {
  all: '端末を問わない内容にする。',
  pc: 'PC・Chromebook（キーボード・マウス）を使う場面にする：キーボード入力、ショートカットキー（Ctrl＋キー）、右クリック、ダブルクリック、ファイル操作。',
  touch: 'iPad・スマホ（タッチ操作）を使う場面にする：タップ・長押し・ドラッグ、ソフトウェアキーボードでの入力、共有メニュー。',
};

const TYPE_DETAIL: Record<QuestionType, string> = {
  choice: '4択（choices は4つ）。answer は正解の choices の番号（0始まり）を1つだけ入れた配列。',
  multi: '当てはまるものをすべて選ぶ（choices は4つ、正解は2〜3個）。answer は正解の番号の配列。',
  order: '正しい順番に並べる（choices は3〜5個）。choices は正しい順番のとおりに書き、answer は [0,1,2,...] とする。順番が一通りに決まる内容にすること。',
  match: '左右を組み合わせる（pairs は3組）。pairs[i].left と pairs[i].right が正しい組。どの右も、ほかの左とは組み合わさらないようにする。',
  input: '指定された文字を入力する。target は入力してもらう文字、timeLimitSec はその学年の子どもが余裕を持って入力できる秒数（20〜90）。',
  'ui-task':
    '擬似的な画面での操作。task.kind は次のどれか：' +
    'button（正しいボタンを押す。style は dialog / share / toolbar）、' +
    'context-menu（PCは trigger: right-click、タッチは trigger: long-press。target を右クリック/長押ししてメニュー menu から選ぶ）、' +
    'drag-to-folder（file を folders のうち正しいものへドラッグ）、' +
    'copy-paste（text をコピーして貼り付ける）、' +
    'shortcut（PCのみ。key は英字1文字、ctrl は true。Ctrl+W/T/N/Q のようにブラウザが止められないキーは使わない）、' +
    'open-item（PCは action: double-click、タッチは action: tap。items から正しいアイコンを開く）。' +
    'file・folders・items・target の先頭には内容に合う絵文字アイコンを1つ付けてよい（例：「📁 理科」）。answer は正しい選択肢の番号（0始まり）。',
};

// ---------------------------------------------------------------- 出力の形式（Zod スキーマ）
const text = (d: string) => z.string().describe(d);
const figureSchema = z
  .union([
    z.object({ kind: z.literal('table'), caption: z.string(), headers: z.array(z.string()), rows: z.array(z.array(z.string())) }),
    z.object({ kind: z.enum(['bar', 'line']), caption: z.string(), labels: z.array(z.string()), values: z.array(z.number()), unit: z.string() }),
    z.object({ kind: z.literal('screen'), title: z.string(), lines: z.array(z.string()) }),
  ])
  .nullable()
  .describe('表・グラフ・擬似画面が必要なときだけ入れる。不要なら null');

const common = {
  question: text('問題文'),
  explanation: text('なぜその答えになるかの短い解説（受験者向け）'),
  figure: figureSchema,
};

const taskSchema = z.union([
  z.object({ kind: z.literal('button'), style: z.enum(['dialog', 'share', 'toolbar']), title: z.string(), message: z.string(), buttons: z.array(z.string()), answer: z.number().int() }),
  z.object({ kind: z.literal('context-menu'), trigger: z.enum(['right-click', 'long-press']), target: z.string(), menu: z.array(z.string()), answer: z.number().int() }),
  z.object({ kind: z.literal('drag-to-folder'), file: z.string(), folders: z.array(z.string()), answer: z.number().int() }),
  z.object({ kind: z.literal('copy-paste'), text: z.string() }),
  z.object({ kind: z.literal('shortcut'), key: z.string(), ctrl: z.boolean(), shift: z.boolean(), context: z.string() }),
  z.object({ kind: z.literal('open-item'), action: z.enum(['double-click', 'tap']), items: z.array(z.string()), answer: z.number().int() }),
]);

const itemSchemas = {
  choice: z.object({ ...common, choices: z.array(z.string()), answer: z.array(z.number().int()) }),
  multi: z.object({ ...common, choices: z.array(z.string()), answer: z.array(z.number().int()) }),
  order: z.object({ ...common, choices: z.array(z.string()), answer: z.array(z.number().int()) }),
  match: z.object({ ...common, pairs: z.array(z.object({ left: z.string(), right: z.string() })) }),
  input: z.object({ ...common, target: z.string(), timeLimitSec: z.number().int() }),
  'ui-task': z.object({ ...common, task: taskSchema }),
} as const;

const outputSchema = z.object({ questions: z.array(itemSchemas[qtype]) });

// ---------------------------------------------------------------- プロンプト
const young = grade >= 6;
const existing = BANK.filter((q) => q.grade === grade && q.field === field && (field !== 'operation' || q.device === device || q.device === 'all'))
  .map((q) => `- ${q.question.replace(/\{([^{}|]+)\|[^{}|]+\}/g, '$1')}`)
  .join('\n');

const system = `あなたは日本の学校で使う「ICT活用能力テスト」の問題作成者です。
受験者は小学生から大人までで、級ごとに対象学年が決まっています。問題は人間の先生が確認してから使われます。

必ず守ること：
1. 正解が1つに決まること（multi は正解の組み合わせが1通り、order は順番が1通り、match は組み合わせが1通り）。
   ほかの選択肢も「場合によっては正しい」と言えてしまう選択肢は作らない。
2. 事実として正確であること。操作手順・用語・数値・法律やルールは、一般に認められている内容だけにする。
   OSやアプリによって手順が違う内容は、どの端末でも成り立つ言い方にするか、避ける。
3. 対象学年の子どもが読める言葉と内容にする。${young ? 'この級は低学年向けなので、漢字にはふりがなを {漢字|かんじ} の形で付ける（例：{名前|なまえ}）。ひらがなを多めにし、文は短くする。' : '常用漢字の範囲で、分かりやすい文にする。'}
4. 実在の企業・サービス・人物をかたる内容（偽のメールの差出人など）には、架空の名前と、例示用に予約された実在しないドメイン（example.jp・example.com など。例：shopmart.example.jp）を使う。
5. 個人を傷つける表現、特定の家庭環境を前提にした表現、怖がらせすぎる表現は使わない。
6. 解説は1〜2文で、なぜそれが正しいかを受験者に伝える。
7. 表・グラフ・擬似画面（figure）は、問題を解くのに必要なときだけ付ける。グラフの数値と答えが一致していることを確かめる。`;

const prompt = `次の条件で、テストの問題を ${count} 問作ってください。

- 級：${grade}級（対象：${GRADE_GUIDE[grade]}。文部科学省「情報活用能力の体系表例」のステップ${MEXT_STEP[grade]}程度）
- この級の内容のイメージ：${GRADE_DETAIL[grade]}
- 分野：${FIELD_LABELS[field]}（${FIELD_DETAIL[field]}）
- 端末：${DEVICE_DETAIL[device]}
- 問題の形式：${qtype} … ${TYPE_DETAIL[qtype]}

${count} 問はそれぞれ違う内容にし、次の既存の問題とも重ならないようにしてください。
${existing || '（既存の問題はありません）'}`;

if (args['dry-run']) {
  console.log('===== system =====\n' + system + '\n\n===== user =====\n' + prompt);
  process.exit(0);
}

// ---------------------------------------------------------------- API 呼び出し
if (fs.existsSync('.env')) process.loadEnvFile('.env');
if (!process.env.ANTHROPIC_API_KEY) {
  usage('環境変数 ANTHROPIC_API_KEY が設定されていません（.env.example を .env にコピーして設定してください）');
}

const client = new Anthropic();
console.log(`${grade}級・${FIELD_LABELS[field]}・${qtype}${field === 'operation' ? `（${device}）` : ''} を ${count} 問生成しています…`);

let response;
try {
  response = await client.beta.messages.parse({
    model: args.model!,
    max_tokens: 16000,
    // 正確さが大事なので、考える深さ（effort）を上げる
    output_config: { effort: 'high', format: betaZodOutputFormat(outputSchema) },
    // 安全のための判定で断られたときは、サーバー側で別のモデルに切り替えて続ける
    betas: ['server-side-fallback-2026-07-01'],
    fallbacks: 'default',
    system,
    messages: [{ role: 'user', content: prompt }],
  });
} catch (e) {
  if (e instanceof Anthropic.AuthenticationError) console.error('APIキーが正しくありません。');
  else if (e instanceof Anthropic.RateLimitError) console.error('利用制限に達しました。しばらく待ってからやり直してください。');
  else if (e instanceof Anthropic.APIError) console.error(`APIエラー（${e.status}）：${e.message}`);
  else throw e;
  process.exit(1);
}

if (response.stop_reason === 'refusal') {
  console.error('生成が断られました：', response.stop_details?.explanation ?? '');
  process.exit(1);
}
if (response.stop_reason === 'max_tokens') {
  console.error('出力が長すぎて途中で切れました。--count を減らしてください。');
  process.exit(1);
}
const parsed = response.parsed_output;
if (!parsed) {
  console.error('出力を読み取れませんでした。もう一度実行してください。');
  process.exit(1);
}

// ---------------------------------------------------------------- 問題の形に整えて保存
const stamp = new Date().toISOString().replace(/[-:]/g, '').replace('T', '-').slice(0, 13); // 20261111-0905
const f = { operation: 'op', information: 'info', safety: 'safety' }[field];
const dev = field === 'operation' ? `-${device}` : '';
const ok: Question[] = [];
parsed.questions.forEach((item, i) => {
  const { figure, ...rest } = item as typeof item & { figure: unknown };
  const q = {
    id: `g${grade}-${f}${dev}-ai${stamp.toLowerCase()}-${String(i + 1).padStart(2, '0')}`,
    grade,
    field,
    device,
    type: qtype,
    ...rest,
    ruby: young,
    ...(figure ? { figure } : {}),
    status: 'draft',
    source: 'ai',
    reviewedBy: '',
    reviewedAt: '',
  };
  const errors = validateQuestion(q);
  if (errors.length) {
    console.warn(`⚠ ${i + 1}問目は形式に問題があるため保存しません：${errors.join(' / ')}`);
    return;
  }
  ok.push(q as unknown as Question);
});

if (ok.length === 0) {
  console.error('保存できる問題がありませんでした。');
  process.exit(1);
}
const outDir = path.join('questions', 'drafts');
fs.mkdirSync(outDir, { recursive: true });
const outFile = path.join(outDir, `grade-${grade}-${field}${dev}-${qtype}-${stamp}.json`);
fs.writeFileSync(outFile, JSON.stringify(ok, null, 2) + '\n');
console.log(`✓ ${ok.length}問を ${outFile} に保存しました（status: draft）。`);
console.log('  次は npm run dev → http://localhost:5173/scripts/review.html で確認してください。');
