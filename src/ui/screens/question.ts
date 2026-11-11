import { FIELD_LABELS, isYoungGrade } from '../../config';
import type { Rng } from '../../logic/random';
import type { DeviceTarget, Question, Response } from '../../types';
import { h } from '../dom';
import { renderFigure } from '../figure';
import { speakButton, stopSpeaking } from '../speech';
import { rich } from '../text';
import { renderQuestionBody } from '../types';

export interface QuestionScreenOpts {
  q: Question;
  index: number;
  total: number;
  device: Exclude<DeviceTarget, 'all'>;
  rng: Rng;
  overTime: boolean;
  onAnswer(r: Response): void;
}

/** 問題画面。1問を表示し、解答されたら onAnswer を1回だけ呼ぶ */
export function questionScreen(o: QuestionScreenOpts): { el: HTMLElement; dispose(): void } {
  const { q } = o;
  const young = isYoungGrade(q.grade);
  const showRuby = young || q.ruby;
  let done = false;

  const answerBtn = h('button', { class: 'btn btn-primary', type: 'button', disabled: true }, rich('こたえる', showRuby));
  const skipBtn = h('button', { class: 'btn btn-ghost', type: 'button' }, rich('わからない', showRuby));

  const finish = (r: Response) => {
    if (done) return;
    done = true;
    stopSpeaking();
    body.dispose?.();
    o.onAnswer(r);
  };

  const body = renderQuestionBody(q, {
    rng: o.rng,
    device: o.device,
    showRuby,
    setReady: (ready) => answerBtn.toggleAttribute('disabled', !ready),
    submit: finish,
  });

  answerBtn.addEventListener('click', () => {
    const r = body.getResponse();
    if (r) finish(r);
  });
  skipBtn.addEventListener('click', () => finish({ type: 'skip' }));

  /** 読み上げる文：問題文と選択肢 */
  const speakText = () => {
    const parts = [q.question];
    if ('choices' in q && q.type !== 'order') parts.push(...q.choices.map((c, i) => `${i + 1}、${c}`));
    return parts.join('。');
  };

  const el = h(
    'section',
    { class: `question q-${q.type}`, 'aria-label': `${o.index + 1}問目` },
    h(
      'div',
      { class: 'q-meta' },
      h('span', { class: 'chip chip-grade' }, `${q.grade}級`),
      h('span', { class: `chip chip-${q.field}` }, FIELD_LABELS[q.field]),
      h('span', { class: 'q-count' }, `${o.index + 1} / ${o.total}`),
      h('progress', { max: o.total, value: o.index, 'aria-hidden': 'true' }),
    ),
    o.overTime
      ? h('p', { class: 'overtime', role: 'status' }, '時間になりました。この級をさいごまで解いたら終わりです。')
      : null,
    h('h2', { class: 'q-text', lang: 'ja' }, rich(q.question, showRuby)),
    young ? speakButton(speakText) : null,
    q.figure ? renderFigure(q.figure, showRuby) : null,
    h('div', { class: 'q-body' }, body.el),
    h('div', { class: 'q-actions' }, skipBtn, body.autoSubmit ? null : answerBtn),
  );
  el.setAttribute('data-qid', q.id);

  return {
    el,
    dispose: () => {
      done = true;
      body.dispose?.();
      stopSpeaking();
    },
  };
}
