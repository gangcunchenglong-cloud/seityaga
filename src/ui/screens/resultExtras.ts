import type { Run } from '../../state';
import { h } from '../dom';

/** 結果画面の追加部分（レーダーチャート・アドバイス・CSV保存は後の段階で追加） */
export function resultExtras(_run: Run, onRestart: () => void): HTMLElement[] {
  return [
    h('div', { class: 'final-actions' }, h('button', { class: 'btn btn-ghost', type: 'button', onclick: onRestart }, 'はじめの画面にもどる')),
  ];
}
