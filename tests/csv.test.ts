import { createHash } from 'node:crypto';
import { describe, expect, it } from 'vitest';
import {
  buildRecords,
  checkCode,
  csvFileName,
  csvToRecords,
  parseCsv,
  recordsToCsv,
  toCsv,
  verifyRecord,
  type RunData,
} from '../src/logic/csv';
import { judgeGrade } from '../src/logic/judge';
import { sha256Hex } from '../src/logic/sha256';
import type { Field, QuestionResult } from '../src/types';

function qr(grade: number, scores: Record<Field, number>): QuestionResult[] {
  const out: QuestionResult[] = [];
  for (const f of ['operation', 'information', 'safety'] as Field[]) {
    for (let i = 0; i < 3; i++) out.push({ id: `g${grade}-${f}-${i}`, field: f, correct: i < scores[f] });
  }
  return out;
}

const run: RunData = {
  profile: { cls: '5-1', number: 7, device: 'ipad', startGrade: 6, mode: 'production' },
  startedAt: new Date(2026, 10, 11, 9, 5, 3),
  results: [
    judgeGrade(6, qr(6, { operation: 3, information: 3, safety: 2 }), 200),
    judgeGrade(5, qr(5, { operation: 3, information: 3, safety: 1 }), 250),
  ],
  endReason: 'failed',
};

describe('SHA-256', () => {
  it('既知の値と一致する', () => {
    expect(sha256Hex('')).toBe('e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855');
    expect(sha256Hex('abc')).toBe('ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
    // 日本語（UTF-8）は Node 標準の crypto と突き合わせる
    for (const s of ['あいう', '5-1|合格|モラル・安全', '🐶'.repeat(40)]) {
      expect(sha256Hex(s)).toBe(createHash('sha256').update(s, 'utf8').digest('hex'));
    }
    // 64バイトをまたぐ長さ
    expect(sha256Hex('a'.repeat(1000))).toBe('41edece42d63e8d9bf515a9ba6932e1c20cbc9f5a5d134645adb5db1b9737ea3');
  });
});

describe('CSV 出力', () => {
  const recs = buildRecords(run);

  it('受けた級ごとに1行', () => {
    expect(recs).toHaveLength(2);
    expect(recs[0]).toMatchObject({
      class: '5-1',
      number: '7',
      startedAt: '2026-11-11 09:05:03',
      device: 'iPad',
      startGrade: '6',
      highestPassed: '6',
      grade: '6',
      result: '合格',
      total: '8',
      operation: '3',
      information: '3',
      safety: '2',
      elapsedSec: '200',
      mode: '本番',
      endReason: '不合格',
    });
    expect(recs[1].result).toBe('不合格');
    expect(recs[1].questions).toBe(
      'g5-operation-0:1;g5-operation-1:1;g5-operation-2:1;g5-information-0:1;g5-information-1:1;g5-information-2:1;g5-safety-0:1;g5-safety-1:0;g5-safety-2:0',
    );
  });

  it('合格がなければ highestPassed は「なし」', () => {
    const r = buildRecords({ ...run, results: [run.results[1]] });
    expect(r[0].highestPassed).toBe('なし');
  });

  it('UTF-8 BOM 付き・CRLF・見出し行', () => {
    const csv = recordsToCsv(recs);
    expect(csv.startsWith('﻿class,number,startedAt,')).toBe(true);
    expect(csv.split('\r\n')).toHaveLength(4); // 見出し＋2行＋最後の空
  });

  it('ファイル名', () => {
    expect(csvFileName(run.profile, run.startedAt)).toBe('ICT_5-1_07_20261111-0905.csv');
  });

  it('書き出して読み込むと元に戻る', () => {
    expect(csvToRecords(recordsToCsv(recs))).toEqual(recs);
  });

  it('カンマ・引用符・改行を含むセルも正しく扱う', () => {
    const csv = toCsv(['a', 'b'], [['x,y', 'he said "hi"\nok']]);
    expect(parseCsv(csv)).toEqual([
      ['a', 'b'],
      ['x,y', 'he said "hi"\nok'],
    ]);
  });

  it('見出しに必要な列がなければ null', () => {
    expect(csvToRecords('foo,bar\r\n1,2\r\n')).toBeNull();
  });
});

describe('確認コード', () => {
  const recs = buildRecords(run);

  it('8文字の16進数で、そのままなら一致する', () => {
    expect(recs[0].checkCode).toMatch(/^[0-9a-f]{8}$/);
    expect(recs.every(verifyRecord)).toBe(true);
  });

  it('点数を書き換えると一致しなくなる', () => {
    expect(verifyRecord({ ...recs[1], total: '9' })).toBe(false);
    expect(verifyRecord({ ...recs[1], result: '合格' })).toBe(false);
    expect(verifyRecord({ ...recs[1], highestPassed: '1' })).toBe(false);
    expect(verifyRecord({ ...recs[1], questions: recs[1].questions.replace(':0', ':1') })).toBe(false);
  });

  it('CSV のテキストを書き換えても検出できる', () => {
    const csv = recordsToCsv(recs).replace(',不合格,7,', ',合格,9,');
    const back = csvToRecords(csv)!;
    expect(back[1].total).toBe('9');
    expect(verifyRecord(back[1])).toBe(false);
    expect(verifyRecord(back[0])).toBe(true);
  });

  it('同じ内容なら同じコード', () => {
    expect(checkCode(recs[0])).toBe(recs[0].checkCode);
  });
});
