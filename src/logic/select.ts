import { RULES } from '../config';
import { FIELDS, type DeviceTarget, type Field, type Grade, type Mode, type Question } from '../types';
import { shuffle, type Rng } from './random';

/** モードに応じて出題してよい問題か */
export function isUsable(q: Question, mode: Mode): boolean {
  if (q.status === 'approved') return true;
  return mode === 'trial' && q.status === 'draft';
}

/** 端末に合う問題か（基本操作は端末別、他の分野は端末共通） */
export function fitsDevice(q: Question, device: Exclude<DeviceTarget, 'all'>): boolean {
  return q.device === 'all' || q.device === device;
}

/** その級・分野で出題できる問題の一覧 */
export function pool(
  bank: readonly Question[],
  grade: Grade,
  field: Field,
  device: Exclude<DeviceTarget, 'all'>,
  mode: Mode,
): Question[] {
  return bank.filter(
    (q) => q.grade === grade && q.field === field && isUsable(q, mode) && fitsDevice(q, device),
  );
}

/** 問題が足りない分野（空なら受験できる） */
export function missingFields(
  bank: readonly Question[],
  grade: Grade,
  device: Exclude<DeviceTarget, 'all'>,
  mode: Mode,
): Field[] {
  return FIELDS.filter(
    (f) => pool(bank, grade, f, device, mode).length < RULES.questionsPerField,
  );
}

/**
 * 1つの級の問題を選ぶ。各分野からランダムに3問ずつ、合計9問。
 * usedIds に入っている問題（同じ受験中にすでに出した問題）は選ばない。
 * 並び順は分野がまざるようにシャッフルする。
 */
export function selectQuestions(
  bank: readonly Question[],
  grade: Grade,
  device: Exclude<DeviceTarget, 'all'>,
  mode: Mode,
  rng: Rng,
  usedIds: ReadonlySet<string> = new Set(),
): Question[] {
  const picked: Question[] = [];
  for (const field of FIELDS) {
    const candidates = pool(bank, grade, field, device, mode).filter((q) => !usedIds.has(q.id));
    if (candidates.length < RULES.questionsPerField) {
      throw new Error(`${grade}級の「${field}」の問題が足りません（${candidates.length}問）`);
    }
    picked.push(...shuffle(candidates, rng).slice(0, RULES.questionsPerField));
  }
  return shuffle(picked, rng);
}
