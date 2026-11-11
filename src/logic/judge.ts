import { FIELD_LABELS, RULES } from '../config';
import { FIELDS, type FieldScores, type Grade, type GradeResult, type QuestionResult } from '../types';

export function fieldScores(results: readonly QuestionResult[]): FieldScores {
  const s: FieldScores = { operation: 0, information: 0, safety: 0 };
  for (const r of results) if (r.correct) s[r.field]++;
  return s;
}

/**
 * 不合格の理由を文章にする。合格なら空文字。
 * 例：「合計は7問以上でしたが、モラル・安全が1問でした。」
 */
export function failReason(total: number, byField: FieldScores): string {
  const weak = FIELDS.filter((f) => byField[f] < RULES.passPerField);
  const weakText = weak.map((f) => `${FIELD_LABELS[f]}が${byField[f]}問`).join('、');
  const totalOk = total >= RULES.passTotal;
  if (totalOk && weak.length === 0) return '';
  if (totalOk) {
    return `合計は${RULES.passTotal}問以上でしたが、${weakText}でした（各分野${RULES.passPerField}問以上が必要です）。`;
  }
  const short = RULES.passTotal - total;
  const head = `合計が${total}問で、合格に必要な${RULES.passTotal}問に${short}問足りませんでした。`;
  if (weak.length === 0) return head;
  return `${head}また、${weakText}でした（各分野${RULES.passPerField}問以上が必要です）。`;
}

/** 合格条件：合計7問以上 かつ 各分野2問以上 */
export function isPass(total: number, byField: FieldScores): boolean {
  return total >= RULES.passTotal && FIELDS.every((f) => byField[f] >= RULES.passPerField);
}

export function judgeGrade(
  grade: Grade,
  results: readonly QuestionResult[],
  elapsedSec: number,
): GradeResult {
  const byField = fieldScores(results);
  const total = results.filter((r) => r.correct).length;
  const passed = isPass(total, byField);
  return {
    grade,
    passed,
    total,
    byField,
    questions: results.slice(),
    elapsedSec,
    reason: passed ? '' : failReason(total, byField),
  };
}

/** 最高合格級（数字が小さいほど上の級）。合格なしなら null */
export function highestPassed(results: readonly GradeResult[]): Grade | null {
  const passed = results.filter((r) => r.passed).map((r) => r.grade);
  if (passed.length === 0) return null;
  return Math.min(...passed) as Grade;
}
