import { describe, expect, it } from 'vitest';
import { ADVICE, pickAdvice } from '../src/content/advice';
import { FIELDS, GRADES, type GradeResult } from '../src/types';

const result = (grade: GradeResult['grade'], operation: number, information: number, safety: number) =>
  ({ grade, byField: { operation, information, safety } }) as GradeResult;

describe('アドバイス', () => {
  it('分野 × 級のすべてに文章がある', () => {
    for (const f of FIELDS) for (const g of GRADES) expect(ADVICE[f][g].length).toBeGreaterThan(10);
  });
  it('満点でない分野を、点数の低い順に出す', () => {
    const a = pickAdvice(result(4, 2, 3, 1));
    expect(a.map((x) => x.field)).toEqual(['safety', 'operation']);
    expect(a[0].text).toBe(ADVICE.safety[4]);
  });
  it('全分野満点なら出さない', () => {
    expect(pickAdvice(result(1, 3, 3, 3))).toEqual([]);
    expect(pickAdvice(undefined)).toEqual([]);
  });
});
