import type { InputQuestion } from '../../types';
import { h } from '../dom';
import { rich } from '../text';
import type { Renderer } from './common';

/**
 * 文字入力。制限秒数以内に、表示された文字を正確に入力できたら正解。
 * 表示された文字はコピーできないようにし、入力らんへの貼り付けも禁止する（タイピングの力を測るため）。
 */
export const renderInput: Renderer<InputQuestion> = (q, api) => {
  const start = performance.now();
  const limit = q.timeLimitSec * 1000;
  const box = h('input', {
    type: 'text',
    class: 'input-box',
    autocomplete: 'off',
    autocapitalize: 'off',
    autocorrect: 'off',
    spellcheck: 'false',
    enterkeyhint: 'done',
    'aria-label': '入力らん',
  });
  const bar = h('div', {});
  const barWrap = h('div', { class: 'time-bar', role: 'progressbar', 'aria-label': 'のこり時間' }, bar);
  const left = h('p', { class: 'task-status', 'aria-live': 'polite' });

  box.addEventListener('paste', (e) => e.preventDefault());
  box.addEventListener('drop', (e) => e.preventDefault());
  box.addEventListener('input', () => api.setReady(box.value.trim().length > 0));
  box.addEventListener('keydown', (e) => {
    // 日本語変換中の Enter は確定なので答えにしない
    if (e.key === 'Enter' && !e.isComposing && e.keyCode !== 229 && box.value.trim()) {
      e.preventDefault();
      api.submit(response());
    }
  });

  const response = () => ({ type: 'input' as const, text: box.value, elapsedMs: performance.now() - start });

  const tick = () => {
    const rest = Math.max(0, limit - (performance.now() - start));
    bar.style.width = `${(rest / limit) * 100}%`;
    barWrap.classList.toggle('low', rest < limit * 0.25);
    left.textContent = rest > 0 ? `のこり ${Math.ceil(rest / 1000)} びょう` : '時間になりました。「こたえる」をおしてください。';
    if (rest === 0) api.setReady(true);
  };
  tick();
  const id = window.setInterval(tick, 250);
  setTimeout(() => box.focus(), 50);

  return {
    el: h(
      'div',
      {},
      h('div', { class: 'input-target', 'aria-label': `入力する文字：${q.target}` }, q.target),
      box,
      barWrap,
      left,
      h('p', { class: 'hint' }, rich(`{制限|せいげん}{時間|じかん}は ${q.timeLimitSec}びょうです。`, api.showRuby)),
    ),
    getResponse: response,
    dispose: () => clearInterval(id),
  };
};
