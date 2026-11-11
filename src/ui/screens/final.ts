import { FIELD_LABELS, RULES } from '../../config';
import { highestPassed } from '../../logic/judge';
import { END_REASON_LABELS } from '../../logic/session';
import type { Run } from '../../state';
import { FIELDS } from '../../types';
import { h } from '../dom';

/** 結果画面 */
export function finalScreen(run: Run, extras: HTMLElement[] = []): HTMLElement {
  const best = highestPassed(run.results);
  const last = run.results[run.results.length - 1];
  return h(
    'section',
    { class: 'card final' },
    h('p', { class: 'final-label' }, 'あなたの最高合格級'),
    h('p', { class: `final-grade ${best ? '' : 'none'}` }, best ? `${best}級` : '合格なし'),
    run.endReason === 'timeout' ? h('p', { class: 'end-note' }, '時間切れのため終了しました。') : null,
    run.endReason === 'completed' ? h('p', { class: 'end-note' }, 'すべての級に合格しました。すばらしい！') : null,
    h('h2', {}, '受けた級の結果'),
    h(
      'div',
      { class: 'table-wrap' },
      h(
        'table',
        { class: 'result-table' },
        h(
          'thead',
          {},
          h(
            'tr',
            {},
            h('th', {}, '級'),
            h('th', {}, '合否'),
            h('th', {}, '合計'),
            FIELDS.map((f) => h('th', {}, FIELD_LABELS[f])),
          ),
        ),
        h(
          'tbody',
          {},
          run.results.map((r) =>
            h(
              'tr',
              { class: r.passed ? 'passed' : 'failed' },
              h('th', { scope: 'row' }, `${r.grade}級`),
              h('td', {}, r.passed ? '合格' : '不合格'),
              h('td', {}, `${r.total} / ${RULES.questionsPerField * 3}`),
              FIELDS.map((f) => h('td', {}, `${r.byField[f]} / ${RULES.questionsPerField}`)),
            ),
          ),
        ),
      ),
    ),
    last && !last.passed ? h('div', { class: 'fail-box' }, h('h2', {}, `${last.grade}級が不合格だった理由`), h('p', {}, last.reason)) : null,
    ...extras,
    h('p', { class: 'small muted' }, `終了の理由：${run.endReason ? END_REASON_LABELS[run.endReason] : ''}`),
  );
}
