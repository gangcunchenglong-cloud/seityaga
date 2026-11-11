import type { Question, Response } from '../types';

/** 入力の比較用に文字をそろえる（全角英数→半角、全角スペース→半角、前後の空白を除く） */
export function normalizeText(s: string): string {
  return s.normalize('NFKC').replace(/\s+/g, ' ').trim();
}

function sameSet(a: readonly number[], b: readonly number[]): boolean {
  if (a.length !== b.length) return false;
  const s = new Set(a);
  return s.size === b.length && b.every((x) => s.has(x));
}

/**
 * 1問の正誤を判定する。
 * multi / order / match は、すべて正しいときだけ正解（部分点なし）。
 */
export function isCorrect(q: Question, r: Response): boolean {
  if (r.type === 'skip') return false;
  switch (q.type) {
    case 'choice':
    case 'multi':
      return (r.type === 'choice' || r.type === 'multi') && sameSet(r.selected, q.answer);
    case 'order':
      return (
        r.type === 'order' &&
        r.order.length === q.answer.length &&
        r.order.every((v, i) => v === q.answer[i])
      );
    case 'match':
      return (
        r.type === 'match' &&
        r.pairs.length === q.pairs.length &&
        r.pairs.every((v, i) => v === i)
      );
    case 'input':
      return (
        r.type === 'input' &&
        r.elapsedMs <= q.timeLimitSec * 1000 &&
        normalizeText(r.text) === normalizeText(q.target)
      );
    case 'ui-task':
      return r.type === 'ui-task' && r.success;
  }
}
