import { CHECK_SALT, DEVICE_CSV } from '../config';
import type { Profile } from '../state';
import { gradeCode, type EndReason, type GradeResult } from '../types';
import { highestPassed } from './judge';
import { END_REASON_LABELS } from './session';
import { sha256Hex } from './sha256';

/** CSV の列（この順番で出力する） */
export const CSV_COLUMNS = [
  'class',
  'number',
  'startedAt',
  'device',
  'startGrade',
  'highestPassed',
  'grade',
  'result',
  'total',
  'operation',
  'information',
  'safety',
  'questions',
  'elapsedSec',
  'mode',
  'endReason',
  'checkCode',
] as const;

export type CsvColumn = (typeof CSV_COLUMNS)[number];
export type CsvRecord = Record<CsvColumn, string>;

/** 確認コードの対象にする列（checkCode 以外すべて） */
const SIGNED = CSV_COLUMNS.filter((c) => c !== 'checkCode');

/**
 * 確認コード：行の内容＋アプリに埋め込んだ文字列から SHA-256 を計算し、先頭8文字。
 * 点数などを書き換えるとコードが一致しなくなる（書き換えの「抑止」であり、完全な不正防止ではない）。
 */
export function checkCode(rec: Omit<CsvRecord, 'checkCode'> | CsvRecord): string {
  return sha256Hex([CHECK_SALT, ...SIGNED.map((c) => rec[c])].join('|')).slice(0, 8);
}

export function verifyRecord(rec: CsvRecord): boolean {
  return rec.checkCode === checkCode(rec);
}

const pad = (n: number) => String(n).padStart(2, '0');

/** 2026-11-11 09:05:03 */
export function formatDateTime(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}

/** ICT_<クラス>_<出席番号>_<YYYYMMDD-HHMM>.csv */
export function csvFileName(p: Pick<Profile, 'cls' | 'number'>, startedAt: Date): string {
  const d = startedAt;
  const stamp = `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}-${pad(d.getHours())}${pad(d.getMinutes())}`;
  const safeCls = p.cls.replace(/[\\/:*?"<>|\s]/g, '_');
  return `ICT_${safeCls}_${pad(p.number)}_${stamp}.csv`;
}

export interface RunData {
  profile: Profile;
  startedAt: Date;
  results: GradeResult[];
  endReason?: EndReason;
}

/** 受けた級ごとに1行 */
export function buildRecords(run: RunData): CsvRecord[] {
  const best = highestPassed(run.results);
  return run.results.map((r) => {
    const rec: Omit<CsvRecord, 'checkCode'> = {
      class: run.profile.cls,
      number: String(run.profile.number),
      startedAt: formatDateTime(run.startedAt),
      device: DEVICE_CSV[run.profile.device],
      startGrade: gradeCode(run.profile.startGrade),
      highestPassed: best === null ? 'なし' : gradeCode(best),
      grade: gradeCode(r.grade),
      result: r.passed ? '合格' : '不合格',
      total: String(r.total),
      operation: String(r.byField.operation),
      information: String(r.byField.information),
      safety: String(r.byField.safety),
      questions: r.questions.map((q) => `${q.id}:${q.correct ? 1 : 0}`).join(';'),
      elapsedSec: String(r.elapsedSec),
      mode: run.profile.mode === 'trial' ? '試作' : '本番',
      endReason: run.endReason ? END_REASON_LABELS[run.endReason] : '',
    };
    return { ...rec, checkCode: checkCode(rec) };
  });
}

function escapeCell(v: string): string {
  return /[",\r\n]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v;
}

/** CSV 文字列（先頭に BOM を付けると Excel で文字化けしない） */
export function toCsv(header: readonly string[], rows: readonly (readonly string[])[], bom = true): string {
  const lines = [header, ...rows].map((r) => r.map(escapeCell).join(','));
  return (bom ? '﻿' : '') + lines.join('\r\n') + '\r\n';
}

export function recordsToCsv(records: readonly CsvRecord[]): string {
  return toCsv(CSV_COLUMNS, records.map((r) => CSV_COLUMNS.map((c) => r[c])));
}

/** CSV を読み込む（BOM、ダブルクォート、改行入りのセル、CRLF/LF に対応） */
export function parseCsv(text: string): string[][] {
  const src = text.replace(/^﻿/, '');
  const rows: string[][] = [];
  let row: string[] = [];
  let cell = '';
  let quoted = false;
  for (let i = 0; i < src.length; i++) {
    const ch = src[i];
    if (quoted) {
      if (ch === '"') {
        if (src[i + 1] === '"') {
          cell += '"';
          i++;
        } else quoted = false;
      } else cell += ch;
    } else if (ch === '"') quoted = true;
    else if (ch === ',') {
      row.push(cell);
      cell = '';
    } else if (ch === '\n' || ch === '\r') {
      if (ch === '\r' && src[i + 1] === '\n') i++;
      row.push(cell);
      rows.push(row);
      row = [];
      cell = '';
    } else cell += ch;
  }
  if (cell !== '' || row.length) {
    row.push(cell);
    rows.push(row);
  }
  return rows.filter((r) => r.some((c) => c.trim() !== ''));
}

/** 見出し行から列を対応づけてレコードにする。必要な列がなければ null */
export function csvToRecords(text: string): CsvRecord[] | null {
  const [head, ...body] = parseCsv(text);
  if (!head) return null;
  const idx = CSV_COLUMNS.map((c) => head.indexOf(c));
  if (idx.some((i) => i < 0)) return null;
  return body.map((r) => Object.fromEntries(CSV_COLUMNS.map((c, k) => [c, r[idx[k]] ?? ''])) as CsvRecord);
}
