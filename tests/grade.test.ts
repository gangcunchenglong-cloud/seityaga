import { describe, expect, it } from 'vitest';
import { GRADES, gradeCode, gradeKey, gradeLabel, nextGrade, parseGradeCode } from '../src/types';

describe('級の表記（準2級・準1級をふくむ10段階）', () => {
  it('受験の順番', () => {
    expect(GRADES.map(gradeLabel)).toEqual(['8級', '7級', '6級', '5級', '4級', '3級', '準2級', '2級', '準1級', '1級']);
  });
  it('CSVの表記と、読み戻し', () => {
    expect(gradeCode(5)).toBe('5');
    expect(gradeCode(2.5)).toBe('準2');
    expect(gradeCode(1.5)).toBe('準1');
    for (const g of GRADES) expect(parseGradeCode(gradeCode(g))).toBe(g);
    expect(parseGradeCode('準2級')).toBe(2.5);
    expect(parseGradeCode('9')).toBeNull();
    expect(parseGradeCode('なし')).toBeNull();
  });
  it('ID・ファイル名の表記', () => {
    expect(gradeKey(5)).toBe('5');
    expect(gradeKey(2.5)).toBe('p2');
    expect(gradeKey(1.5)).toBe('p1');
  });
  it('次の級', () => {
    expect(nextGrade(3)).toBe(2.5);
    expect(nextGrade(1.5)).toBe(1);
    expect(nextGrade(1)).toBeNull();
  });
});
