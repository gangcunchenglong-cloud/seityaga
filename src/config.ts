import type { DeviceKind, DeviceTarget, Field, Grade } from './types';

/** 合格条件・時間などの設定 */
export const RULES = {
  /** 1つの級の問題数（分野ごと） */
  questionsPerField: 3,
  /** 合格に必要な合計正解数 */
  passTotal: 7,
  /** 合格に必要な各分野の正解数 */
  passPerField: 2,
  /** 全体の制限時間（ミリ秒） */
  timeLimitMs: 30 * 60 * 1000,
} as const;

/**
 * クラスの選択肢。学校に合わせて書き換えてください。
 * （ファイル名やCSVに使うので、カンマや空白は入れないでください）
 */
export const CLASSES: string[] = (() => {
  const list: string[] = [];
  for (let year = 1; year <= 6; year++) {
    for (let room = 1; room <= 4; room++) list.push(`${year}-${room}`);
  }
  list.push('その他');
  return list;
})();

/** 出席番号の最大値 */
export const MAX_NUMBER = 45;

export const FIELD_LABELS: Record<Field, string> = {
  operation: '基本操作',
  information: '情報収集・判断',
  safety: 'モラル・安全',
};

export const GRADE_GUIDE: Record<Grade, string> = {
  8: '小学1年生',
  7: '小学2年生',
  6: '小学3年生',
  5: '小学4年生',
  4: '小学5年生',
  3: '小学6年生',
  2: '中学生',
  1: '高校生〜大人',
};

export const DEVICE_LABELS: Record<DeviceKind, string> = {
  pc: 'パソコン（Windows など）',
  chromebook: 'Chromebook',
  ipad: 'iPad・タブレット',
  smartphone: 'スマホ',
};

/** CSVに書く端末名 */
export const DEVICE_CSV: Record<DeviceKind, string> = {
  pc: 'PC',
  chromebook: 'Chromebook',
  ipad: 'iPad',
  smartphone: 'スマホ',
};

export function deviceTarget(kind: DeviceKind): Exclude<DeviceTarget, 'all'> {
  return kind === 'pc' || kind === 'chromebook' ? 'pc' : 'touch';
}

/** ふりがな・読み上げボタンを付ける級（8級〜6級） */
export function isYoungGrade(grade: Grade): boolean {
  return grade >= 6;
}

/**
 * 確認コード計算用の埋め込み文字列。
 * 受験アプリと先生用ツールで同じ値を使います。
 * ※ アプリの中に入っているので、調べれば読めます。完全な不正防止ではなく「書き換えの抑止」です。
 */
export const CHECK_SALT = 'ict-skill-test/v1/7f3c9a51';
