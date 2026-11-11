import { describe, expect, it } from 'vitest';
import { RULES } from '../src/config';
import { missingFields, pool } from '../src/logic/select';
import { validateBank, validateQuestion } from '../src/logic/validate';
import { BANK } from '../src/questions';
import { FIELDS, GRADES } from '../src/types';
import { makeQ } from './helpers';

describe('問題バンク（src/questions/*.json）', () => {
  it('すべての問題が正しい形式で、id が重複していない', () => {
    expect(validateBank(BANK)).toEqual([]);
  });

  it('全8級 × 3分野 × 端末（pc / touch）で、試作モードなら3問以上ある', () => {
    for (const g of GRADES) {
      for (const d of ['pc', 'touch'] as const) {
        expect(missingFields(BANK, g, d, 'trial'), `${g}級 ${d}`).toEqual([]);
        for (const f of FIELDS) {
          expect(pool(BANK, g, f, d, 'trial').length).toBeGreaterThanOrEqual(RULES.questionsPerField);
        }
      }
    }
  });

  it('id は級・分野と一致し、ファイルの級と問題の級が一致する', () => {
    for (const q of BANK) {
      expect(q.id.startsWith(`g${q.grade}-`), q.id).toBe(true);
    }
  });

  it('同じ問題の中で選択肢が重複していない', () => {
    for (const q of BANK) {
      if ('choices' in q) expect(new Set(q.choices).size, q.id).toBe(q.choices.length);
    }
  });

  it('8級〜6級の問題は ruby: true', () => {
    for (const q of BANK.filter((q) => q.grade >= 6)) expect(q.ruby, q.id).toBe(true);
  });

  it('ふりがな記法 {漢字|かんじ} の括弧が閉じている', () => {
    for (const q of BANK) {
      const text = JSON.stringify(q);
      const opens = (text.match(/\{/g) ?? []).length;
      const closes = (text.match(/\}/g) ?? []).length;
      expect(opens, q.id).toBe(closes);
    }
  });
});

describe('形式チェック（validateQuestion）', () => {
  it('正しい問題は OK', () => {
    expect(validateQuestion(makeQ('g5-safety-01', 5, 'safety'))).toEqual([]);
  });
  it('answer が範囲外ならエラー', () => {
    expect(validateQuestion(makeQ('g5-safety-01', 5, 'safety', { answer: [4] } as never)).length).toBeGreaterThan(0);
  });
  it('choice で answer が2つならエラー', () => {
    expect(validateQuestion(makeQ('x', 5, 'safety', { answer: [0, 1] } as never))).toContain('choice の answer は1つだけ');
  });
  it('基本操作以外で device が pc ならエラー', () => {
    expect(validateQuestion(makeQ('x', 5, 'safety', { device: 'pc' }))).toContain(
      '基本操作以外の分野は device を all にしてください',
    );
  });
  it('右クリックの問題を touch にするとエラー', () => {
    const q = makeQ('x', 5, 'operation', {
      type: 'ui-task',
      device: 'touch',
      task: { kind: 'context-menu', trigger: 'right-click', target: 'a', menu: ['a', 'b'], answer: 0 },
    } as never);
    expect(validateQuestion(q)).toContain('右クリックは device: pc で使います');
  });
});
