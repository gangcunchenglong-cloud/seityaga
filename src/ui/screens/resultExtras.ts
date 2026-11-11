import { FIELD_LABELS } from '../../config';
import { pickAdvice } from '../../content/advice';
import type { Run } from '../../state';
import { h } from '../dom';
import { radarChart } from '../radar';

/** 結果画面の追加部分：レーダーチャート・アドバイス・ボタン類 */
export function resultExtras(run: Run, onRestart: () => void, saveArea?: HTMLElement): HTMLElement[] {
  const last = run.results[run.results.length - 1];
  if (!last) return [];
  const advice = pickAdvice(last);
  return [
    h('h2', {}, `${last.grade}級の分野ごとの正答率`),
    radarChart(last.byField),
    h('h2', {}, 'つぎに学ぶとよいこと'),
    advice.length
      ? h(
          'div',
          { class: 'advice' },
          advice.map((a) =>
            h('div', { class: `advice-item ${a.field}` }, h('h3', {}, FIELD_LABELS[a.field]), h('p', {}, a.text)),
          ),
        )
      : h('p', { class: 'advice-item' }, `${last.grade}級は全分野で満点でした。この調子で、身につけた力を授業や生活で使ってみましょう。`),
    saveArea ?? null,
    h(
      'div',
      { class: 'final-actions' },
      h('button', { class: 'btn btn-ghost', type: 'button', onclick: () => confirm('結果を保存しましたか？ はじめの画面にもどると、この結果は消えます。') && onRestart() }, 'はじめの画面にもどる'),
    ),
  ].filter(Boolean) as HTMLElement[];
}
