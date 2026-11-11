import type { ChoiceQuestion, MultiQuestion } from '../../types';
import { permutation } from '../../logic/random';
import { h } from '../dom';
import { rich } from '../text';
import type { Renderer } from './common';

/** choice（1つ選ぶ）と multi（すべて選ぶ）。選択肢の順番はシャッフルする */
export const renderChoice: Renderer<ChoiceQuestion | MultiQuestion> = (q, api) => {
  const multi = q.type === 'multi';
  const order = permutation(q.choices.length, api.rng);
  const selected = new Set<number>();
  const buttons: HTMLButtonElement[] = [];

  const refresh = () => {
    buttons.forEach((b) => {
      const on = selected.has(Number(b.dataset.index));
      b.classList.toggle('selected', on);
      b.setAttribute('aria-pressed', String(on));
    });
    api.setReady(selected.size > 0);
  };

  const list = h(
    'div',
    { class: 'choices', role: multi ? 'group' : 'radiogroup' },
    order.map((orig, i) => {
      const b = h(
        'button',
        { class: `choice ${multi ? 'is-multi' : ''}`, type: 'button', 'data-index': orig },
        h('span', { class: 'choice-mark', 'aria-hidden': 'true' }, multi ? '' : String(i + 1)),
        h('span', { class: 'choice-text' }, rich(q.choices[orig], api.showRuby)),
      );
      b.addEventListener('click', () => {
        if (multi) {
          if (selected.has(orig)) selected.delete(orig);
          else selected.add(orig);
        } else {
          selected.clear();
          selected.add(orig);
        }
        refresh();
      });
      buttons.push(b);
      return b;
    }),
  );
  refresh();

  return {
    el: h(
      'div',
      {},
      multi ? h('p', { class: 'hint' }, rich('{当|あ}てはまるものを ぜんぶ えらんでね', api.showRuby)) : null,
      list,
    ),
    getResponse: () => (selected.size ? { type: q.type, selected: [...selected] } : null),
  };
};
