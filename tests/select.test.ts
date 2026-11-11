import { describe, expect, it } from 'vitest';
import { derangedOrder, seededRng, shuffle } from '../src/logic/random';
import { missingFields, selectQuestions } from '../src/logic/select';
import { makeBank, makeQ } from './helpers';

describe('出題', () => {
  it('各分野から3問ずつ、合計9問。その級の問題だけ', () => {
    const qs = selectQuestions(makeBank(5), 5, 'pc', 'production', seededRng(1));
    expect(qs).toHaveLength(9);
    expect(qs.every((q) => q.grade === 5)).toBe(true);
    for (const f of ['operation', 'information', 'safety']) {
      expect(qs.filter((q) => q.field === f)).toHaveLength(3);
    }
    expect(new Set(qs.map((q) => q.id)).size).toBe(9);
  });

  it('ランダムに選ばれる（乱数が違えば組み合わせも変わる）', () => {
    const bank = makeBank(10);
    const a = selectQuestions(bank, 5, 'pc', 'production', seededRng(1)).map((q) => q.id).sort();
    const b = selectQuestions(bank, 5, 'pc', 'production', seededRng(2)).map((q) => q.id).sort();
    expect(a).not.toEqual(b);
  });

  it('同じ受験中にすでに出した問題は選ばない', () => {
    const bank = makeBank(6);
    const used = new Set(['g5-safety-0', 'g5-safety-1', 'g5-safety-2']);
    const qs = selectQuestions(bank, 5, 'pc', 'production', seededRng(3), used);
    expect(qs.some((q) => used.has(q.id))).toBe(false);
  });

  it('本番モードでは approved だけ、試作モードでは draft も出る', () => {
    const drafts = makeBank(3, 'draft');
    expect(() => selectQuestions(drafts, 5, 'pc', 'production', seededRng(1))).toThrow();
    expect(selectQuestions(drafts, 5, 'pc', 'trial', seededRng(1))).toHaveLength(9);
    expect(missingFields(drafts, 5, 'pc', 'production')).toEqual([
      'operation',
      'information',
      'safety',
    ]);
    expect(missingFields(drafts, 5, 'pc', 'trial')).toEqual([]);
  });

  it('rejected は試作モードでも出ない', () => {
    const bank = makeBank(3, 'rejected');
    expect(missingFields(bank, 5, 'pc', 'trial')).toHaveLength(3);
  });

  it('基本操作は端末に合う問題だけ（pc / touch / all）', () => {
    const bank = makeBank(3).filter((q) => q.field !== 'operation');
    for (let i = 0; i < 3; i++) {
      bank.push(makeQ(`pc-${i}`, 5, 'operation', { device: 'pc' }));
      bank.push(makeQ(`touch-${i}`, 5, 'operation', { device: 'touch' }));
    }
    const pc = selectQuestions(bank, 5, 'pc', 'production', seededRng(1));
    expect(pc.filter((q) => q.field === 'operation').every((q) => q.device === 'pc')).toBe(true);
    const touch = selectQuestions(bank, 5, 'touch', 'production', seededRng(1));
    expect(touch.filter((q) => q.field === 'operation').every((q) => q.device === 'touch')).toBe(
      true,
    );
  });
});

describe('シャッフル', () => {
  it('要素は変わらず、元の配列も変えない', () => {
    const a = [1, 2, 3, 4, 5];
    const b = shuffle(a, seededRng(9));
    expect(b.slice().sort()).toEqual(a);
    expect(a).toEqual([1, 2, 3, 4, 5]);
  });
  it('並べ替え問題の初期表示は正解と同じ並びにならない', () => {
    for (let s = 0; s < 50; s++) {
      expect(derangedOrder([0, 1, 2, 3], seededRng(s))).not.toEqual([0, 1, 2, 3]);
    }
  });
});
