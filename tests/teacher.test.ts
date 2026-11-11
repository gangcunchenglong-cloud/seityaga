import { describe, expect, it } from 'vitest';
import { buildRecords, parseCsv, recordsToCsv, type RunData } from '../src/logic/csv';
import { judgeGrade } from '../src/logic/judge';
import { aggregate, difficultyLabel, mergedCsv, questionStats } from '../src/teacher/aggregate';
import type { Field, QuestionResult } from '../src/types';

function qr(grade: number, scores: Record<Field, number>): QuestionResult[] {
  const out: QuestionResult[] = [];
  for (const f of ['operation', 'information', 'safety'] as Field[]) {
    for (let i = 0; i < 3; i++) out.push({ id: `g${grade}-${f}-${i}`, field: f, correct: i < scores[f] });
  }
  return out;
}

function csvFor(cls: string, number: number, mode: 'production' | 'trial' = 'production'): string {
  const run: RunData = {
    profile: { cls, number, device: 'pc', startGrade: 5, mode },
    startedAt: new Date(2026, 10, 11, 9, 0, 0),
    results: [
      judgeGrade(5, qr(5, { operation: 3, information: 3, safety: 3 }), 100),
      judgeGrade(4, qr(4, { operation: 1, information: 3, safety: 2 }), 120),
    ],
    endReason: 'failed',
  };
  return recordsToCsv(buildRecords(run));
}

describe('先生用ツールの集計', () => {
  const a = aggregate([
    { name: 'b.csv', text: csvFor('5-2', 3) },
    { name: 'a.csv', text: csvFor('5-1', 10) },
    { name: 'c.csv', text: csvFor('5-1', 2) },
  ]);

  it('生徒一覧はクラス・番号順で、最後に受けた級の分野別点数を出す', () => {
    expect(a.students.map((s) => `${s.class}-${s.number}`)).toEqual(['5-1-2', '5-1-10', '5-2-3']);
    expect(a.students[0]).toMatchObject({ startGrade: '5', highestPassed: '5', lastGrade: '4', lastResult: '不合格', operation: '1', valid: true });
  });

  it('書き換えた行を検出する', () => {
    const tampered = csvFor('5-1', 5).replace(',不合格,6,1,', ',合格,8,3,');
    const t = aggregate([{ name: 'x.csv', text: tampered }]);
    expect(t.rows.map((r) => r.valid)).toEqual([true, false]);
    expect(t.students[0].valid).toBe(false);
  });

  it('関係ないCSVはエラーとして報告し、同じファイルの重複は1回分', () => {
    const t = aggregate([
      { name: 'other.csv', text: 'a,b\r\n1,2\r\n' },
      { name: 'a.csv', text: csvFor('5-1', 1) },
      { name: 'a.csv', text: csvFor('5-1', 1) },
    ]);
    expect(t.errors).toHaveLength(1);
    expect(t.students).toHaveLength(1);
  });

  it('問題ごとの正答率（書き換えた行と、指定すれば試作も除外）', () => {
    const t = aggregate([
      { name: 'a.csv', text: csvFor('5-1', 1) },
      { name: 'b.csv', text: csvFor('5-1', 2, 'trial') },
      { name: 'c.csv', text: csvFor('5-1', 3).replace(',不合格,6,1,', ',合格,8,3,') },
    ]);
    const all = questionStats(t.rows);
    const op = all.find((s) => s.id === 'g4-operation-1')!;
    // a と b の2人（c の4級の行は書き換えで除外）
    expect(op).toMatchObject({ attempts: 2, correct: 0, rate: 0 });
    const prod = questionStats(t.rows, false).find((s) => s.id === 'g4-operation-1')!;
    expect(prod.attempts).toBe(1);
    expect(all[0].rate).toBeLessThanOrEqual(all[all.length - 1].rate);
  });

  it('難しすぎ・易しすぎの目安', () => {
    expect(difficultyLabel({ id: 'x', attempts: 10, correct: 2, rate: 0.2 })).toBe('難しすぎ？');
    expect(difficultyLabel({ id: 'x', attempts: 10, correct: 10, rate: 1 })).toBe('易しすぎ？');
    expect(difficultyLabel({ id: 'x', attempts: 3, correct: 0, rate: 0 })).toBe('');
  });

  it('まとめCSVにファイル名と確認結果の列が付く', () => {
    const rows = parseCsv(mergedCsv(a.rows));
    expect(rows[0].slice(0, 4)).toEqual(['fileName', 'checkOK', 'class', 'number']);
    expect(rows).toHaveLength(1 + 6);
    expect(rows[1][1]).toBe('OK');
  });
});
