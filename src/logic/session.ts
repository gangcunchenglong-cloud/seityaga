import { RULES } from '../config';
import { nextGrade, type EndReason, type Grade } from '../types';

export type NextStep = { kind: 'next'; grade: Grade } | { kind: 'end'; reason: EndReason };

/**
 * 1つの級を解き終えたあと、次に何をするかを決める。
 * - 不合格 → 終了
 * - 1級に合格 → 終了（全級合格）
 * - 制限時間（30分）を過ぎていた → 終了（その級の結果は記録する）
 * - 次の級の問題が足りない → 終了
 * - それ以外 → 次の級へ
 */
export function nextStep(
  grade: Grade,
  passed: boolean,
  elapsedMs: number,
  hasQuestions: (g: Grade) => boolean,
  limitMs: number = RULES.timeLimitMs,
): NextStep {
  if (!passed) return { kind: 'end', reason: 'failed' };
  const next = nextGrade(grade);
  if (next === null) return { kind: 'end', reason: 'completed' };
  if (elapsedMs >= limitMs) return { kind: 'end', reason: 'timeout' };
  if (!hasQuestions(next)) return { kind: 'end', reason: 'no-questions' };
  return { kind: 'next', grade: next };
}

/** 残り時間（ミリ秒、0未満にはしない） */
export function remainingMs(elapsedMs: number, limitMs: number = RULES.timeLimitMs): number {
  return Math.max(0, limitMs - elapsedMs);
}

export function isOverTime(elapsedMs: number, limitMs: number = RULES.timeLimitMs): boolean {
  return elapsedMs >= limitMs;
}

/** mm:ss 形式 */
export function formatClock(ms: number): string {
  const total = Math.ceil(ms / 1000);
  const m = Math.floor(total / 60);
  const s = total % 60;
  return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
}

export const END_REASON_LABELS: Record<EndReason, string> = {
  failed: '不合格',
  completed: '1級合格',
  timeout: '時間切れ',
  'no-questions': '問題不足',
};
