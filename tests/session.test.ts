import { describe, expect, it } from 'vitest';
import { formatClock, isOverTime, nextStep, remainingMs } from '../src/logic/session';

const MIN = 60 * 1000;
const always = () => true;

describe('階段型の進行', () => {
  it('合格すると次の級（数字が1つ小さい級）へ進む', () => {
    expect(nextStep(5, true, 5 * MIN, always)).toEqual({ kind: 'next', grade: 4 });
    expect(nextStep(8, true, 1 * MIN, always)).toEqual({ kind: 'next', grade: 7 });
  });
  it('不合格ならそこで終了', () => {
    expect(nextStep(5, false, 5 * MIN, always)).toEqual({ kind: 'end', reason: 'failed' });
  });
  it('1級に合格したら終了', () => {
    expect(nextStep(1, true, 5 * MIN, always)).toEqual({ kind: 'end', reason: 'completed' });
  });
  it('次の級の問題が足りなければ終了', () => {
    expect(nextStep(5, true, 5 * MIN, (g) => g !== 4)).toEqual({
      kind: 'end',
      reason: 'no-questions',
    });
  });
});

describe('30分の制限時間', () => {
  it('30分未満なら次へ進める', () => {
    expect(nextStep(5, true, 30 * MIN - 1, always)).toEqual({ kind: 'next', grade: 4 });
  });
  it('30分を過ぎて級を解き終えたら、合格でも終了（時間切れ）', () => {
    expect(nextStep(5, true, 30 * MIN, always)).toEqual({ kind: 'end', reason: 'timeout' });
    expect(nextStep(5, true, 41 * MIN, always)).toEqual({ kind: 'end', reason: 'timeout' });
  });
  it('30分を過ぎても不合格なら理由は「不合格」', () => {
    expect(nextStep(5, false, 35 * MIN, always)).toEqual({ kind: 'end', reason: 'failed' });
  });
  it('残り時間と表示', () => {
    expect(remainingMs(10 * MIN)).toBe(20 * MIN);
    expect(remainingMs(40 * MIN)).toBe(0);
    expect(isOverTime(29 * MIN)).toBe(false);
    expect(isOverTime(30 * MIN)).toBe(true);
    expect(formatClock(20 * MIN)).toBe('20:00');
    expect(formatClock(59_001)).toBe('01:00');
    expect(formatClock(0)).toBe('00:00');
  });
});
