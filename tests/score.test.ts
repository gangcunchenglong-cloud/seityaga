import { describe, expect, it } from 'vitest';
import { isCorrect, normalizeText } from '../src/logic/score';
import { makeQ } from './helpers';

describe('1問の正誤判定', () => {
  it('choice', () => {
    const q = makeQ('a', 5, 'safety', { type: 'choice', answer: [2] } as never);
    expect(isCorrect(q, { type: 'choice', selected: [2] })).toBe(true);
    expect(isCorrect(q, { type: 'choice', selected: [1] })).toBe(false);
    expect(isCorrect(q, { type: 'skip' })).toBe(false);
  });
  it('multi は全部そろったときだけ正解（部分点なし）', () => {
    const q = makeQ('a', 5, 'safety', { type: 'multi', answer: [0, 2] } as never);
    expect(isCorrect(q, { type: 'multi', selected: [2, 0] })).toBe(true);
    expect(isCorrect(q, { type: 'multi', selected: [0] })).toBe(false);
    expect(isCorrect(q, { type: 'multi', selected: [0, 1, 2] })).toBe(false);
  });
  it('order は順番が完全に一致したら正解', () => {
    const q = makeQ('a', 5, 'operation', { type: 'order', answer: [2, 0, 1] } as never);
    expect(isCorrect(q, { type: 'order', order: [2, 0, 1] })).toBe(true);
    expect(isCorrect(q, { type: 'order', order: [0, 2, 1] })).toBe(false);
  });
  it('match はすべての組が正しいときだけ正解', () => {
    const q = makeQ('a', 5, 'safety', {
      type: 'match',
      pairs: [
        { left: 'a', right: '1' },
        { left: 'b', right: '2' },
      ],
    } as never);
    expect(isCorrect(q, { type: 'match', pairs: [0, 1] })).toBe(true);
    expect(isCorrect(q, { type: 'match', pairs: [1, 0] })).toBe(false);
    expect(isCorrect(q, { type: 'match', pairs: [0, null] })).toBe(false);
  });
  it('input は時間内に正確に入力できたら正解（全角・半角の違いは無視）', () => {
    const q = makeQ('a', 6, 'operation', {
      type: 'input',
      target: 'Tokyo 2026',
      timeLimitSec: 30,
    } as never);
    expect(isCorrect(q, { type: 'input', text: 'Ｔｏｋｙｏ　２０２６', elapsedMs: 10_000 })).toBe(true);
    expect(isCorrect(q, { type: 'input', text: ' Tokyo 2026 ', elapsedMs: 30_000 })).toBe(true);
    expect(isCorrect(q, { type: 'input', text: 'Tokyo 2026', elapsedMs: 30_001 })).toBe(false);
    expect(isCorrect(q, { type: 'input', text: 'tokyo 2026', elapsedMs: 1000 })).toBe(false);
  });
  it('ui-task', () => {
    const q = makeQ('a', 6, 'operation', {
      type: 'ui-task',
      task: { kind: 'copy-paste', text: 'x' },
    } as never);
    expect(isCorrect(q, { type: 'ui-task', success: true })).toBe(true);
    expect(isCorrect(q, { type: 'ui-task', success: false })).toBe(false);
  });
  it('normalizeText', () => {
    expect(normalizeText('　ＡＢＣ　１２３ ')).toBe('ABC 123');
  });
});
