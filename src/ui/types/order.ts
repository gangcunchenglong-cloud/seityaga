import { derangedOrder } from '../../logic/random';
import type { OrderQuestion } from '../../types';
import { h } from '../dom';
import { rich } from '../text';
import type { Renderer } from './common';

/**
 * 並べ替え。項目をドラッグ（指・マウス）するか、▲▼ボタン（キーボード操作にも対応）で動かす。
 */
export const renderOrder: Renderer<OrderQuestion> = (q, api) => {
  const list = h('ol', { class: 'order-list' });
  const items = derangedOrder(q.answer, api.rng).map((orig) => {
    const up = h('button', { class: 'btn btn-small', type: 'button', 'aria-label': '上へ' }, '▲');
    const down = h('button', { class: 'btn btn-small', type: 'button', 'aria-label': '下へ' }, '▼');
    const handle = h('span', { class: 'order-handle', 'aria-hidden': 'true' }, '☰');
    const li = h(
      'li',
      { class: 'order-item', 'data-index': orig },
      h('span', { class: 'order-num' }),
      h('span', { class: 'order-text' }, rich(q.choices[orig], api.showRuby)),
      h('span', { class: 'order-btns' }, up, down),
      handle,
    );
    up.addEventListener('click', () => move(li, -1));
    down.addEventListener('click', () => move(li, 1));
    li.addEventListener('pointerdown', (e) => {
      if ((e.target as HTMLElement).closest('button')) return;
      startDrag(li, e);
    });
    return li;
  });
  items.forEach((li) => list.appendChild(li));

  const renumber = () => {
    [...list.children].forEach((li, i) => {
      (li.querySelector('.order-num') as HTMLElement).textContent = String(i + 1);
      const [up, down] = li.querySelectorAll('.order-btns button');
      up.toggleAttribute('disabled', i === 0);
      down.toggleAttribute('disabled', i === list.children.length - 1);
    });
  };

  const move = (li: Element, dir: -1 | 1) => {
    if (dir < 0 && li.previousElementSibling) list.insertBefore(li, li.previousElementSibling);
    if (dir > 0 && li.nextElementSibling) list.insertBefore(li.nextElementSibling, li);
    renumber();
    (li.querySelector(dir < 0 ? '.order-btns button' : '.order-btns button:last-child') as HTMLElement)?.focus();
  };

  /** ドラッグ中、指の位置が隣の項目の中央を越えたら入れ替える */
  const startDrag = (li: HTMLElement, e: PointerEvent) => {
    e.preventDefault();
    li.setPointerCapture(e.pointerId);
    li.classList.add('dragging');
    const onMove = (ev: PointerEvent) => {
      const prev = li.previousElementSibling as HTMLElement | null;
      const next = li.nextElementSibling as HTMLElement | null;
      if (prev) {
        const r = prev.getBoundingClientRect();
        if (ev.clientY < r.top + r.height / 2) list.insertBefore(li, prev);
      }
      if (next) {
        const r = next.getBoundingClientRect();
        if (ev.clientY > r.top + r.height / 2) list.insertBefore(next, li);
      }
      renumber();
    };
    const onUp = () => {
      li.classList.remove('dragging');
      li.removeEventListener('pointermove', onMove);
      li.removeEventListener('pointerup', onUp);
      li.removeEventListener('pointercancel', onUp);
    };
    li.addEventListener('pointermove', onMove);
    li.addEventListener('pointerup', onUp);
    li.addEventListener('pointercancel', onUp);
  };

  renumber();
  api.setReady(true);
  return {
    el: h(
      'div',
      {},
      h('p', { class: 'hint' }, rich('{項目|こうもく}を ドラッグするか、▲▼で {正|ただ}しい {順番|じゅんばん}に ならべましょう', api.showRuby)),
      list,
    ),
    getResponse: () => ({
      type: 'order',
      order: [...list.children].map((li) => Number((li as HTMLElement).dataset.index)),
    }),
  };
};
