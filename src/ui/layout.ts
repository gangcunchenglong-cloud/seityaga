import { formatClock, remainingMs } from '../logic/session';
import type { Mode } from '../types';
import { clear, h } from './dom';

/** 画面の枠（ヘッダー・本体）。#app の中に作る */
export interface Layout {
  main: HTMLElement;
  setTimer(elapsedMs: number | null): void;
  show(...nodes: Node[]): void;
}

export function createLayout(root: HTMLElement, mode: Mode, title: string): Layout {
  clear(root);
  const timer = h('div', { class: 'timer', role: 'timer', 'aria-live': 'off', hidden: true });
  const header = h(
    'header',
    { class: 'app-header' },
    h('div', { class: 'app-title' }, title),
    timer,
  );
  const banner =
    mode === 'trial'
      ? h('div', { class: 'trial-banner', role: 'note' }, '試作モード（承認前の問題もふくみます。本番の結果ではありません）')
      : null;
  const main = h('main', { class: 'app-main', tabindex: '-1' });
  if (banner) root.appendChild(banner);
  root.append(header, main);

  return {
    main,
    setTimer(elapsedMs) {
      if (elapsedMs === null) {
        timer.hidden = true;
        return;
      }
      timer.hidden = false;
      const rest = remainingMs(elapsedMs);
      timer.textContent = rest > 0 ? `のこり ${formatClock(rest)}` : '時間です';
      timer.classList.toggle('warn', rest > 0 && rest <= 5 * 60 * 1000);
      timer.classList.toggle('over', rest === 0);
    },
    show(...nodes) {
      clear(main);
      nodes.forEach((n) => main.appendChild(n));
      window.scrollTo(0, 0);
      main.focus({ preventScroll: true });
    },
  };
}
