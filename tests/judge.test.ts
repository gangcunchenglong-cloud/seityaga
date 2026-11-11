import { describe, expect, it } from 'vitest';
import { failReason, highestPassed, isPass, judgeGrade } from '../src/logic/judge';
import type { Field, FieldScores, GradeResult, QuestionResult } from '../src/types';

function results(scores: FieldScores): QuestionResult[] {
  const out: QuestionResult[] = [];
  for (const f of ['operation', 'information', 'safety'] as Field[]) {
    for (let i = 0; i < 3; i++) out.push({ id: `${f}-${i}`, field: f, correct: i < scores[f] });
  }
  return out;
}

describe('合格条件（合計7問以上 かつ 各分野2問以上）', () => {
  const cases: [FieldScores, boolean][] = [
    [{ operation: 3, information: 3, safety: 3 }, true],
    [{ operation: 3, information: 2, safety: 2 }, true], // 7問・各2以上 → ちょうど合格
    [{ operation: 2, information: 2, safety: 2 }, false], // 6問 → 合計不足
    [{ operation: 3, information: 3, safety: 1 }, false], // 7問だが1分野が1問
    [{ operation: 3, information: 3, safety: 0 }, false], // 6問・1分野0問
    [{ operation: 0, information: 0, safety: 0 }, false],
  ];
  it.each(cases)('%o → %s', (s, expected) => {
    const total = s.operation + s.information + s.safety;
    expect(isPass(total, s)).toBe(expected);
    const r = judgeGrade(5, results(s), 100);
    expect(r.passed).toBe(expected);
    expect(r.total).toBe(total);
    expect(r.byField).toEqual(s);
  });
});

describe('不合格の理由', () => {
  it('合格なら空', () => {
    expect(failReason(9, { operation: 3, information: 3, safety: 3 })).toBe('');
  });
  it('合計は足りているが分野が足りない', () => {
    expect(failReason(7, { operation: 3, information: 3, safety: 1 })).toBe(
      '合計は7問以上でしたが、モラル・安全が1問でした（各分野2問以上が必要です）。',
    );
  });
  it('合計が足りない', () => {
    expect(failReason(6, { operation: 2, information: 2, safety: 2 })).toBe(
      '合計が6問で、合格に必要な7問に1問足りませんでした。',
    );
  });
  it('合計も分野も足りない', () => {
    expect(failReason(4, { operation: 3, information: 1, safety: 0 })).toContain(
      'また、情報収集・判断が1問、モラル・安全が0問でした',
    );
  });
  it('judgeGrade に理由が入る', () => {
    const r = judgeGrade(3, results({ operation: 3, information: 3, safety: 1 }), 10);
    expect(r.reason).toContain('モラル・安全が1問');
  });
});

describe('最高合格級', () => {
  const r = (grade: GradeResult['grade'], passed: boolean) =>
    ({ grade, passed }) as GradeResult;
  it('合格なし → null', () => {
    expect(highestPassed([])).toBeNull();
    expect(highestPassed([r(5, false)])).toBeNull();
  });
  it('準級もふくめて、いちばん上の級を返す', () => {
    expect(highestPassed([r(3, true), r(2.5, true), r(2, false)])).toBe(2.5);
    expect(highestPassed([r(2, true), r(1.5, true), r(1, false)])).toBe(1.5);
  });
  it('いちばん上の級（数字が小さい）を返す', () => {
    expect(highestPassed([r(6, true), r(5, true), r(4, false)])).toBe(5);
  });
});
