import { formatClock, remainingMs } from '../logic/session';
import { GRADES, gradeCode, type Grade, type Mode } from '../types';
import { clear, h } from './dom';

/** 級のステッパーの状態 */
export interface LadderState {
  startGrade: Grade;
  passed: Grade[];
  failed: Grade[];
  current: Grade | null;
}

/** 画面の枠（ヘッダー・級のステッパー・本体）。#app の中に作る */
export interface Layout {
  main: HTMLElement;
  setTimer(elapsedMs: number | null): void;
  setLadder(state: LadderState | null): void;
  show(...nodes: Node[]): void;
}

/** ヘッダーのロゴとシステム名（受験アプリ・先生用ツール・レビューで共通） */
export function brand(title: string, sys = 'ICT SKILL ASSESSMENT SYSTEM'): HTMLElement {
  return h(
    'div',
    { class: 'brand' },
    h('span', { class: 'brand-mark', 'aria-hidden': 'true' }, 'ICT'),
    h('div', {}, h('div', { class: 'brand-sys' }, sys), h('div', { class: 'app-title' }, title)),
  );
}

export function createLayout(root: HTMLElement, mode: Mode, title: string): Layout {
  clear(root);
  const timer = h('div', { class: 'timer', role: 'timer', 'aria-live': 'off', hidden: true });
  const header = h('header', { class: 'app-header' }, brand(title), timer);
  const banner =
    mode === 'trial'
      ? h('div', { class: 'trial-banner', role: 'note' }, 'TRIAL MODE ／ 試作モード（承認前の問題もふくみます。本番の結果ではありません）')
      : null;
  const ladderList = h('ol', {});
  const ladder = h(
    'nav',
    { class: 'ladder', 'aria-label': '級の進み具合', hidden: true },
    h('div', { class: 'ladder-inner' }, h('div', { class: 'ladder-label' }, 'GRADE PROGRESS ／ 級の進み具合'), ladderList),
  );
  const main = h('main', { class: 'app-main', tabindex: '-1' });
  if (banner) root.appendChild(banner);
  root.append(header, ladder, main);

  return {
    main,
    setTimer(elapsedMs) {
      if (elapsedMs === null) {
        timer.hidden = true;
        return;
      }
      timer.hidden = false;
      const rest = remainingMs(elapsedMs);
      timer.textContent = rest > 0 ? formatClock(rest) : '00:00';
      timer.setAttribute('aria-label', rest > 0 ? `のこり ${formatClock(rest)}` : '時間です');
      timer.classList.toggle('warn', rest > 0 && rest <= 5 * 60 * 1000);
      timer.classList.toggle('over', rest === 0);
    },
    setLadder(state) {
      if (!state) {
        ladder.hidden = true;
        return;
      }
      ladder.hidden = false;
      const startIdx = GRADES.indexOf(state.startGrade);
      ladderList.replaceChildren(
        ...GRADES.map((g, i) => {
          const cls = state.passed.includes(g)
            ? 'passed'
            : state.failed.includes(g)
              ? 'failed'
              : state.current === g
                ? 'current'
                : i < startIdx
                  ? 'skipped'
                  : '';
          const status = { passed: '合格', failed: '不合格', current: '受験中', skipped: '受けない', '': '未受験' }[cls];
          return h('li', { class: cls, title: `${gradeCode(g)}級：${status}`, 'aria-label': `${gradeCode(g)}級 ${status}` }, gradeCode(g));
        }),
      );
    },
    show(...nodes) {
      clear(main);
      nodes.forEach((n) => main.appendChild(n));
      window.scrollTo(0, 0);
      main.focus({ preventScroll: true });
    },
  };
}
