import type { Field, Grade, Question } from '../src/types';

/** テスト用の問題を作る */
export function makeQ(
  id: string,
  grade: Grade,
  field: Field,
  opts: Partial<Question> = {},
): Question {
  return {
    id,
    grade,
    field,
    device: 'all',
    type: 'choice',
    question: 'テスト',
    ruby: false,
    choices: ['A', 'B', 'C', 'D'],
    answer: [0],
    explanation: '',
    status: 'approved',
    source: 'test',
    reviewedBy: '',
    reviewedAt: '',
    ...opts,
  } as Question;
}

/** 各級・各分野に n 問ずつの問題バンク */
export function makeBank(n = 4, status: Question['status'] = 'approved'): Question[] {
  const bank: Question[] = [];
  for (const g of [1, 2, 3, 4, 5, 6, 7, 8] as Grade[]) {
    for (const f of ['operation', 'information', 'safety'] as Field[]) {
      for (let i = 0; i < n; i++) bank.push(makeQ(`g${g}-${f}-${i}`, g, f, { status }));
    }
  }
  return bank;
}
