/**
 * レビュー用ツールで書き出したJSONを、問題バンク（src/questions/grade-N.json）に反映する（開発者用）。
 *
 *   npm run merge -- reviewed.json [reviewed2.json ...]
 *   npm run merge -- reviewed.json --all     （approved 以外〔rejected・draft〕も反映する）
 *
 * - 同じ id の問題があれば置きかえ、なければその級のファイルの最後に追加する。
 * - 既定では status が approved の問題だけを反映する。
 * - 形式に問題がある問題は反映しない。
 */
import fs from 'node:fs';
import path from 'node:path';
import { parseArgs } from 'node:util';
import { validateQuestion } from '../src/logic/validate';
import { gradeKey, type Grade, type Question } from '../src/types';

const { values, positionals } = parseArgs({
  allowPositionals: true,
  options: { all: { type: 'boolean', default: false } },
});
if (positionals.length === 0) {
  console.error('使い方: npm run merge -- <レビュー済みのJSONファイル> [--all]');
  process.exit(1);
}

const dir = path.join('src', 'questions');
const banks = new Map<Grade, Question[]>();
const load = (g: Grade) => {
  if (!banks.has(g)) banks.set(g, JSON.parse(fs.readFileSync(path.join(dir, `grade-${gradeKey(g)}.json`), 'utf8')));
  return banks.get(g)!;
};

let added = 0;
let replaced = 0;
let skipped = 0;
for (const file of positionals) {
  const data: unknown = JSON.parse(fs.readFileSync(file, 'utf8'));
  const list = (Array.isArray(data) ? data : [data]) as Question[];
  for (const q of list) {
    if (!values.all && q.status !== 'approved') {
      skipped++;
      continue;
    }
    const errors = validateQuestion(q);
    if (errors.length) {
      console.warn(`⚠ ${q.id ?? '(idなし)'} は形式に問題があるため反映しません：${errors.join(' / ')}`);
      skipped++;
      continue;
    }
    const bank = load(q.grade);
    const i = bank.findIndex((x) => x.id === q.id);
    if (i >= 0) {
      bank[i] = q;
      replaced++;
    } else {
      bank.push(q);
      added++;
    }
  }
}

for (const [g, bank] of banks) {
  fs.writeFileSync(path.join(dir, `grade-${gradeKey(g)}.json`), JSON.stringify(bank, null, 2) + '\n');
}
console.log(`追加 ${added}問・置きかえ ${replaced}問・反映しなかった ${skipped}問`);
console.log('npm test で問題バンク全体をチェックしてから、npm run build してください。');
