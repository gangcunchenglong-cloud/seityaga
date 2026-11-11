import { FIELD_LABELS, RULES } from '../../config';
import type { NextStep } from '../../logic/session';
import { FIELDS, gradeLabel, type GradeResult, type Question } from '../../types';
import { h } from '../dom';
import { rich } from '../text';

/** 級の合否画面 */
export function gradeResultScreen(
  r: GradeResult,
  questions: readonly Question[],
  step: NextStep,
  showRuby: boolean,
  onNext: () => void,
): HTMLElement {
  const nextLabel = step.kind === 'next' ? `${gradeLabel(step.grade)}へすすむ` : '結果を見る';
  let headline: string;
  if (r.passed && step.kind === 'next') headline = `${gradeLabel(r.grade)} 合格！ つぎは ${gradeLabel(step.grade)}`;
  else if (r.passed && step.kind === 'end' && step.reason === 'completed') headline = '1級 合格！ すべての級に合格しました';
  else if (r.passed) headline = `${gradeLabel(r.grade)} 合格！`;
  else headline = `${gradeLabel(r.grade)}は 不合格でした`;

  let note = '';
  if (step.kind === 'end' && step.reason === 'timeout') note = '時間になったので、ここで終わりです。';
  if (step.kind === 'end' && step.reason === 'no-questions') note = 'つぎの級の問題がまだ準備できていないので、ここで終わりです。';

  const byId = new Map(questions.map((q) => [q.id, q]));
  return h(
    'section',
    { class: `card grade-result ${r.passed ? 'passed' : 'failed'}` },
    h('p', { class: 'result-icon', 'aria-hidden': 'true' }, r.passed ? '🎉' : '📘'),
    h('h1', { class: 'result-headline', 'data-sec': r.passed ? 'GRADE CLEAR' : 'GRADE NOT CLEARED' }, headline),
    h(
      'p',
      { class: 'result-score' },
      `${RULES.questionsPerField * 3}問中 ${r.total}問 正解`,
    ),
    h(
      'ul',
      { class: 'field-scores' },
      FIELDS.map((f) =>
        h(
          'li',
          { class: r.byField[f] < RULES.passPerField ? 'low' : '' },
          h('span', {}, FIELD_LABELS[f]),
          h('strong', {}, `${r.byField[f]} / ${RULES.questionsPerField}`),
        ),
      ),
    ),
    r.reason ? h('p', { class: 'fail-reason' }, r.reason) : null,
    note ? h('p', { class: 'end-note' }, note) : null,
    h(
      'details',
      { class: 'review' },
      h('summary', {}, '問題ごとの答えあわせを見る'),
      h(
        'ol',
        {},
        r.questions.map((qr) => {
          const q = byId.get(qr.id);
          return h(
            'li',
            { class: qr.correct ? 'ok' : 'ng' },
            h('span', { class: 'mark', 'aria-label': qr.correct ? '正解' : '不正解' }, qr.correct ? '○' : '×'),
            h(
              'div',
              {},
              h('p', { class: 'rq' }, q ? rich(q.question, showRuby) : qr.id),
              q?.explanation ? h('p', { class: 'rx' }, rich(q.explanation, showRuby)) : null,
            ),
          );
        }),
      ),
    ),
    h('button', { class: 'btn btn-primary btn-large', type: 'button', onclick: onNext }, nextLabel),
  );
}
