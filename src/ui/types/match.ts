import { permutation } from '../../logic/random';
import type { MatchQuestion } from '../../types';
import { h } from '../dom';
import { rich } from '../text';
import type { Renderer } from './common';

const COLORS = ['#1f5fbf', '#0f7d74', '#a2482a', '#6b3fa0', '#8a6d00', '#34405a'];

/**
 * 組み合わせ。左の項目をタップ → 右の項目をタップ で結ぶ（どちらから選んでもよい）。
 * 結んだ組は同じ番号・同じ色のしるしで表示する。
 */
export const renderMatch: Renderer<MatchQuestion> = (q, api) => {
  const n = q.pairs.length;
  const leftOrder = permutation(n, api.rng);
  const rightOrder = permutation(n, api.rng);
  /** pairs[左の元の添字] = 右の元の添字 */
  const pairs: (number | null)[] = Array(n).fill(null);
  let active: { side: 'L' | 'R'; index: number } | null = null;

  const leftBtns = new Map<number, HTMLButtonElement>();
  const rightBtns = new Map<number, HTMLButtonElement>();

  const mk = (side: 'L' | 'R', orig: number) => {
    const text = side === 'L' ? q.pairs[orig].left : q.pairs[orig].right;
    const b = h(
      'button',
      { class: 'choice match-item', type: 'button' },
      h('span', { class: 'choice-text' }, rich(text, api.showRuby)),
      h('span', { class: 'badge', hidden: true }),
    );
    b.addEventListener('click', () => pick(side, orig));
    (side === 'L' ? leftBtns : rightBtns).set(orig, b);
    return b;
  };

  const pick = (side: 'L' | 'R', index: number) => {
    if (!active || active.side === side) {
      active = active && active.side === side && active.index === index ? null : { side, index };
    } else {
      const l = side === 'L' ? index : active.index;
      const r = side === 'R' ? index : active.index;
      // 右の項目がすでに別の左と結ばれていたら外す
      pairs.forEach((v, i) => {
        if (v === r) pairs[i] = null;
      });
      pairs[l] = r;
      active = null;
    }
    refresh();
  };

  const refresh = () => {
    leftOrder.forEach((l, pos) => {
      const b = leftBtns.get(l)!;
      const badge = b.querySelector('.badge') as HTMLElement;
      const paired = pairs[l] !== null;
      badge.hidden = !paired;
      badge.textContent = String(pos + 1);
      badge.style.background = COLORS[pos % COLORS.length];
      b.classList.toggle('selected', paired);
      b.classList.toggle('active', active?.side === 'L' && active.index === l);
    });
    rightOrder.forEach((r) => {
      const b = rightBtns.get(r)!;
      const badge = b.querySelector('.badge') as HTMLElement;
      const l = pairs.indexOf(r);
      const pos = l >= 0 ? leftOrder.indexOf(l) : -1;
      badge.hidden = pos < 0;
      badge.textContent = String(pos + 1);
      if (pos >= 0) badge.style.background = COLORS[pos % COLORS.length];
      b.classList.toggle('selected', pos >= 0);
      b.classList.toggle('active', active?.side === 'R' && active.index === r);
    });
    api.setReady(pairs.every((v) => v !== null));
  };

  const el = h(
    'div',
    {},
    h('p', { class: 'hint' }, rich('{左|ひだり}を えらんでから、{右|みぎ}の あうものを えらびましょう', api.showRuby)),
    h(
      'div',
      { class: 'match' },
      h('div', { class: 'match-col' }, leftOrder.map((l) => mk('L', l))),
      h('div', { class: 'match-col' }, rightOrder.map((r) => mk('R', r))),
    ),
  );
  refresh();
  return { el, getResponse: () => ({ type: 'match', pairs: pairs.slice() }) };
};
