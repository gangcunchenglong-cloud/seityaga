/** 分野 */
export type Field = 'operation' | 'information' | 'safety';
export const FIELDS: readonly Field[] = ['operation', 'information', 'safety'];

/** 級（8級がいちばんやさしく、1級がいちばん難しい） */
export type Grade = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8;
export const GRADES: readonly Grade[] = [8, 7, 6, 5, 4, 3, 2, 1];

/** 問題の対象端末 */
export type DeviceTarget = 'all' | 'pc' | 'touch';

/** 受験者が使っている端末の種類（CSVにも出力） */
export type DeviceKind = 'pc' | 'chromebook' | 'ipad' | 'smartphone';

export type QuestionStatus = 'draft' | 'approved' | 'rejected';
export type QuestionType = 'choice' | 'multi' | 'order' | 'match' | 'input' | 'ui-task';

/** 本番 / 試作 */
export type Mode = 'production' | 'trial';

/** 表（情報の読み取り問題用） */
export interface TableFigure {
  kind: 'table';
  caption?: string;
  headers: string[];
  rows: string[][];
}

/** 棒グラフ・折れ線グラフ（情報の読み取り問題用） */
export interface ChartFigure {
  kind: 'bar' | 'line';
  caption?: string;
  labels: string[];
  values: number[];
  unit?: string;
}

/** 擬似的な画面のメッセージ（「画面の読み取り」問題用） */
export interface ScreenFigure {
  kind: 'screen';
  title: string;
  lines: string[];
}

export type Figure = TableFigure | ChartFigure | ScreenFigure;

interface QuestionBase {
  id: string;
  grade: Grade;
  field: Field;
  device: DeviceTarget;
  type: QuestionType;
  /** 問題文。ふりがなは {漢字|かんじ} と書く */
  question: string;
  ruby: boolean;
  figure?: Figure;
  explanation: string;
  status: QuestionStatus;
  source: string;
  reviewedBy: string;
  reviewedAt: string;
}

/** 4択など、1つ選ぶ */
export interface ChoiceQuestion extends QuestionBase {
  type: 'choice';
  choices: string[];
  answer: number[];
}

/** 当てはまるものをすべて選ぶ */
export interface MultiQuestion extends QuestionBase {
  type: 'multi';
  choices: string[];
  answer: number[];
}

/** 正しい順番に並べる。answer は choices の添字を正しい順に並べたもの */
export interface OrderQuestion extends QuestionBase {
  type: 'order';
  choices: string[];
  answer: number[];
}

/** 左右を組み合わせる。pairs[i].left と pairs[i].right が正しい組 */
export interface MatchQuestion extends QuestionBase {
  type: 'match';
  pairs: { left: string; right: string }[];
}

/** 指定された文字を入力する */
export interface InputQuestion extends QuestionBase {
  type: 'input';
  /** 入力してほしい文字 */
  target: string;
  /** 制限秒数（この秒数以内に正しく入力できたら正解） */
  timeLimitSec: number;
}

/** 擬似画面での操作 */
export type UiTask =
  /** 擬似ダイアログやメニューで、正しいボタンを押す */
  | { kind: 'button'; style: 'dialog' | 'share' | 'toolbar'; title: string; message?: string; buttons: string[]; answer: number }
  /** 右クリック（PC）／長押し（タッチ）でメニューを出して、正しい項目を選ぶ */
  | { kind: 'context-menu'; trigger: 'right-click' | 'long-press'; target: string; menu: string[]; answer: number }
  /** ファイルを正しいフォルダーへドラッグする */
  | { kind: 'drag-to-folder'; file: string; folders: string[]; answer: number }
  /** 文字をコピーして、入力らんに貼り付ける */
  | { kind: 'copy-paste'; text: string }
  /** キーボードのショートカットを押す（PCのみ） */
  | { kind: 'shortcut'; key: string; ctrl: boolean; shift: boolean; context: string }
  /** アイコンを開く（PC：ダブルクリック、タッチ：タップ） */
  | { kind: 'open-item'; action: 'double-click' | 'tap'; items: string[]; answer: number };

export interface UiTaskQuestion extends QuestionBase {
  type: 'ui-task';
  task: UiTask;
}

export type Question =
  | ChoiceQuestion
  | MultiQuestion
  | OrderQuestion
  | MatchQuestion
  | InputQuestion
  | UiTaskQuestion;

/** 解答（元の添字で表す。表示時のシャッフルとは無関係） */
export type Response =
  | { type: 'choice' | 'multi'; selected: number[] }
  | { type: 'order'; order: number[] }
  | { type: 'match'; pairs: (number | null)[] }
  | { type: 'input'; text: string; elapsedMs: number }
  | { type: 'ui-task'; success: boolean }
  /** 「わからない」で飛ばした */
  | { type: 'skip' };

export interface QuestionResult {
  id: string;
  field: Field;
  correct: boolean;
}

export type FieldScores = Record<Field, number>;

export interface GradeResult {
  grade: Grade;
  passed: boolean;
  total: number;
  byField: FieldScores;
  questions: QuestionResult[];
  elapsedSec: number;
  /** 不合格の理由（合格なら空） */
  reason: string;
}

export type EndReason = 'failed' | 'completed' | 'timeout' | 'no-questions';
