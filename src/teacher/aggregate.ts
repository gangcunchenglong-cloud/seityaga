import { parseGradeCode } from '../types';
import { CSV_COLUMNS, csvToRecords, toCsv, verifyRecord, type CsvRecord } from '../logic/csv';

export interface LoadedFile {
  name: string;
  text: string;
}

export interface Row extends CsvRecord {
  fileName: string;
  /** 確認コードが一致したか */
  valid: boolean;
}

export interface StudentSummary {
  fileName: string;
  class: string;
  number: string;
  startedAt: string;
  device: string;
  startGrade: string;
  highestPassed: string;
  lastGrade: string;
  lastResult: string;
  operation: string;
  information: string;
  safety: string;
  mode: string;
  endReason: string;
  /** すべての行の確認コードが一致したか */
  valid: boolean;
}

export interface QuestionStat {
  id: string;
  attempts: number;
  correct: number;
  rate: number;
}

export interface Aggregate {
  rows: Row[];
  students: StudentSummary[];
  /** 読み込めなかったファイル */
  errors: { name: string; message: string }[];
}

const num = (s: string) => (Number.isFinite(Number(s)) ? Number(s) : Number.MAX_SAFE_INTEGER);
/** 級の表記（"5"・"準2"）を数に（読めなければいちばん下） */
const gnum = (s: string) => parseGradeCode(s) ?? Number.MAX_SAFE_INTEGER;

/** クラス → 出席番号 → 開始日時 の順に並べる */
function compareStudent(a: { class: string; number: string; startedAt: string }, b: typeof a): number {
  return (
    a.class.localeCompare(b.class, 'ja', { numeric: true }) ||
    num(a.number) - num(b.number) ||
    a.startedAt.localeCompare(b.startedAt)
  );
}

/** 複数の CSV をまとめて読み込む。同じ内容のファイルが2回入っても1回分として扱う */
export function aggregate(files: readonly LoadedFile[]): Aggregate {
  const rows: Row[] = [];
  const errors: Aggregate['errors'] = [];
  const seen = new Set<string>();
  for (const f of files) {
    const key = `${f.name}\n${f.text}`;
    if (seen.has(key)) continue;
    seen.add(key);
    const recs = csvToRecords(f.text);
    if (!recs) {
      errors.push({ name: f.name, message: 'このテストの結果のCSVではないようです（見出しの列が足りません）。' });
      continue;
    }
    if (recs.length === 0) {
      errors.push({ name: f.name, message: '結果の行がありません。' });
      continue;
    }
    for (const r of recs) rows.push({ ...r, fileName: f.name, valid: verifyRecord(r) });
  }

  const byFile = new Map<string, Row[]>();
  for (const r of rows) {
    const k = `${r.fileName}|${r.class}|${r.number}|${r.startedAt}`;
    byFile.set(k, [...(byFile.get(k) ?? []), r]);
  }
  const students: StudentSummary[] = [...byFile.values()].map((rs) => {
    // 最後に受けた級 = 数字がいちばん小さい級
    const last = rs.reduce((a, b) => (gnum(b.grade) < gnum(a.grade) ? b : a));
    return {
      fileName: last.fileName,
      class: last.class,
      number: last.number,
      startedAt: last.startedAt,
      device: last.device,
      startGrade: last.startGrade,
      highestPassed: last.highestPassed,
      lastGrade: last.grade,
      lastResult: last.result,
      operation: last.operation,
      information: last.information,
      safety: last.safety,
      mode: last.mode,
      endReason: last.endReason,
      valid: rs.every((r) => r.valid),
    };
  });
  students.sort(compareStudent);
  rows.sort((a, b) => compareStudent(a, b) || gnum(b.grade) - gnum(a.grade));
  return { rows, students, errors };
}

/**
 * 問題ごとの正答率。確認コードが一致しない行は数えない。
 * includeTrial=false なら試作モードの結果も除く。
 */
export function questionStats(rows: readonly Row[], includeTrial = true): QuestionStat[] {
  const map = new Map<string, { attempts: number; correct: number }>();
  for (const r of rows) {
    if (!r.valid) continue;
    if (!includeTrial && r.mode === '試作') continue;
    for (const item of r.questions.split(';')) {
      const m = item.match(/^(.+):([01])$/);
      if (!m) continue;
      const s = map.get(m[1]) ?? { attempts: 0, correct: 0 };
      s.attempts++;
      if (m[2] === '1') s.correct++;
      map.set(m[1], s);
    }
  }
  return [...map.entries()]
    .map(([id, s]) => ({ id, ...s, rate: s.correct / s.attempts }))
    .sort((a, b) => a.rate - b.rate || a.id.localeCompare(b.id));
}

/** 判定の目安：受験者が少ないうちは判定しない */
export function difficultyLabel(s: QuestionStat, minAttempts = 5): string {
  if (s.attempts < minAttempts) return '';
  if (s.rate < 0.3) return '難しすぎ？';
  if (s.rate > 0.95) return '易しすぎ？';
  return '';
}

/** 読み込んだ全行を1つの CSV にまとめる（先頭にファイル名と確認結果の列を追加） */
export function mergedCsv(rows: readonly Row[]): string {
  return toCsv(
    ['fileName', 'checkOK', ...CSV_COLUMNS],
    rows.map((r) => [r.fileName, r.valid ? 'OK' : 'NG', ...CSV_COLUMNS.map((c) => r[c])]),
  );
}
