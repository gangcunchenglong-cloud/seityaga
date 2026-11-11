import { FIELDS, GRADES, type Question } from '../types';

const TYPES = ['choice', 'multi', 'order', 'match', 'input', 'ui-task'];
const DEVICES = ['all', 'pc', 'touch'];
const STATUSES = ['draft', 'approved', 'rejected'];

function isStrArray(v: unknown, min = 1): v is string[] {
  return Array.isArray(v) && v.length >= min && v.every((x) => typeof x === 'string' && x.length > 0);
}

function inRange(arr: unknown, n: number): boolean {
  return Array.isArray(arr) && arr.every((i) => Number.isInteger(i) && i >= 0 && i < n);
}

/**
 * 問題1つの形式をチェックし、問題点の一覧を返す（空なら OK）。
 * 問題バンクの読み込み・レビュー用ツール・生成スクリプトで共通に使う。
 */
export function validateQuestion(raw: unknown): string[] {
  const e: string[] = [];
  if (typeof raw !== 'object' || raw === null) return ['オブジェクトではありません'];
  const q = raw as Record<string, unknown>;
  const req = (cond: boolean, msg: string) => {
    if (!cond) e.push(msg);
  };
  req(typeof q.id === 'string' && /^[a-z0-9-]+$/.test(q.id as string), 'id は英小文字・数字・ハイフンで書いてください');
  req(GRADES.includes(q.grade as never), 'grade は 1〜8');
  req(FIELDS.includes(q.field as never), 'field は operation / information / safety');
  req(DEVICES.includes(q.device as string), 'device は all / pc / touch');
  req(TYPES.includes(q.type as string), 'type が不正です');
  req(typeof q.question === 'string' && (q.question as string).length > 0, 'question が空です');
  req(typeof q.ruby === 'boolean', 'ruby は true / false');
  req(typeof q.explanation === 'string', 'explanation がありません');
  req(STATUSES.includes(q.status as string), 'status は draft / approved / rejected');
  for (const k of ['source', 'reviewedBy', 'reviewedAt']) req(typeof q[k] === 'string', `${k} は文字列`);
  if (q.field !== 'operation') req(q.device === 'all', '基本操作以外の分野は device を all にしてください');

  switch (q.type) {
    case 'choice':
    case 'multi':
    case 'order': {
      req(isStrArray(q.choices, 2), 'choices は2つ以上の文字列');
      const n = Array.isArray(q.choices) ? q.choices.length : 0;
      req(Array.isArray(q.answer) && (q.answer as unknown[]).length > 0 && inRange(q.answer, n), 'answer の番号が choices の範囲外です');
      const ans = (q.answer as number[]) ?? [];
      if (q.type === 'choice') req(ans.length === 1, 'choice の answer は1つだけ');
      if (q.type === 'multi') req(new Set(ans).size === ans.length, 'answer に重複があります');
      if (q.type === 'order') {
        req(ans.length === n && new Set(ans).size === n, 'order の answer は choices 全部の並び');
      }
      break;
    }
    case 'match': {
      const pairs = q.pairs as { left?: unknown; right?: unknown }[] | undefined;
      req(
        Array.isArray(pairs) &&
          pairs.length >= 2 &&
          pairs.every((p) => typeof p.left === 'string' && typeof p.right === 'string'),
        'pairs は {left, right} の2組以上',
      );
      if (Array.isArray(pairs)) {
        const rights = pairs.map((p) => p.right);
        req(new Set(rights).size === rights.length, 'pairs の right が重複しています');
      }
      break;
    }
    case 'input':
      req(q.field === 'operation', 'input は基本操作の問題で使います');
      req(typeof q.target === 'string' && (q.target as string).length > 0, 'target が空です');
      req(typeof q.timeLimitSec === 'number' && (q.timeLimitSec as number) > 0, 'timeLimitSec は正の数');
      break;
    case 'ui-task':
      req(q.field === 'operation', 'ui-task は基本操作の問題で使います');
      e.push(...validateTask(q.task, q.device as string));
      break;
  }
  return e;
}

function validateTask(task: unknown, device: string): string[] {
  const e: string[] = [];
  if (typeof task !== 'object' || task === null) return ['task がありません'];
  const t = task as Record<string, unknown>;
  const opts = (key: string) => {
    if (!isStrArray(t[key], 2)) e.push(`task.${key} は2つ以上の文字列`);
    else if (!(Number.isInteger(t.answer) && (t.answer as number) >= 0 && (t.answer as number) < (t[key] as string[]).length))
      e.push('task.answer が範囲外です');
  };
  switch (t.kind) {
    case 'button':
      opts('buttons');
      if (typeof t.title !== 'string') e.push('task.title がありません');
      break;
    case 'context-menu':
      opts('menu');
      if (t.trigger === 'right-click' && device !== 'pc') e.push('右クリックは device: pc で使います');
      if (t.trigger === 'long-press' && device !== 'touch') e.push('長押しは device: touch で使います');
      break;
    case 'drag-to-folder':
      opts('folders');
      break;
    case 'open-item':
      opts('items');
      if (t.action === 'double-click' && device !== 'pc') e.push('ダブルクリックは device: pc で使います');
      break;
    case 'copy-paste':
      if (typeof t.text !== 'string' || !t.text) e.push('task.text が空です');
      break;
    case 'shortcut':
      if (device !== 'pc') e.push('shortcut は device: pc で使います');
      if (typeof t.key !== 'string' || (t.key as string).length !== 1) e.push('task.key は1文字');
      break;
    default:
      e.push('task.kind が不正です');
  }
  return e;
}

/** 問題バンク全体のチェック（id の重複など） */
export function validateBank(bank: readonly Question[]): string[] {
  const errors: string[] = [];
  const seen = new Set<string>();
  for (const q of bank) {
    for (const msg of validateQuestion(q)) errors.push(`${q.id ?? '(idなし)'}: ${msg}`);
    if (seen.has(q.id)) errors.push(`${q.id}: id が重複しています`);
    seen.add(q.id);
  }
  return errors;
}
