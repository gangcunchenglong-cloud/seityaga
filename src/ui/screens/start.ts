import { CLASSES, DEVICE_LABELS, GRADE_GUIDE, MAX_NUMBER, RULES, deviceTarget } from '../../config';
import { missingFields } from '../../logic/select';
import type { Profile } from '../../state';
import { GRADES, gradeLabel, type DeviceKind, type Grade, type Mode, type Question } from '../../types';
import { h } from '../dom';

/** スタート画面：クラス・出席番号・開始する級・端末を選ぶ */
export function startScreen(
  bank: readonly Question[],
  mode: Mode,
  detected: DeviceKind,
  onStart: (p: Profile) => void,
): HTMLElement {
  const clsSel = h(
    'select',
    { id: 'cls', required: true },
    h('option', { value: '' }, 'えらんでください'),
    CLASSES.map((c) => h('option', { value: c }, c)),
  );
  const numSel = h(
    'select',
    { id: 'num', required: true },
    h('option', { value: '' }, 'えらんでください'),
    Array.from({ length: MAX_NUMBER }, (_, i) => h('option', { value: i + 1 }, `${i + 1}番`)),
  );
  const devSel = h(
    'select',
    { id: 'dev' },
    (Object.keys(DEVICE_LABELS) as DeviceKind[]).map((d) =>
      h('option', { value: d, selected: d === detected }, DEVICE_LABELS[d]),
    ),
  );
  const gradeSel = h('select', { id: 'grade', required: true });
  const gradeNote = h('p', { class: 'field-note', 'aria-live': 'polite' });
  const error = h('p', { class: 'form-error', role: 'alert' });

  /** 端末によって基本操作の問題が変わるので、端末を変えたら選べる級を作り直す */
  const fillGrades = () => {
    const keep = gradeSel.value;
    const target = deviceTarget(devSel.value as DeviceKind);
    gradeSel.replaceChildren(h('option', { value: '' }, 'えらんでください'));
    let unavailable = 0;
    for (const g of GRADES) {
      const ok = missingFields(bank, g, target, mode).length === 0;
      if (!ok) unavailable++;
      gradeSel.appendChild(
        h(
          'option',
          { value: g, disabled: !ok, selected: ok && String(g) === keep },
          `${gradeLabel(g)}（めやす：${GRADE_GUIDE[g]}）${ok ? '' : ' ― 準備中'}`,
        ),
      );
    }
    gradeNote.textContent =
      unavailable === 0
        ? '合格すると、つぎの級にすすみます。'
        : mode === 'production'
          ? `「準備中」の級は、先生が確認した問題がまだそろっていないため受けられません。`
          : `「準備中」の級は、問題が足りないため受けられません。`;
  };
  devSel.addEventListener('change', fillGrades);
  fillGrades();

  const start = h('button', { class: 'btn btn-primary btn-large', type: 'submit' }, 'スタート');
  const form = h(
    'form',
    { class: 'card start-form', novalidate: true },
    h('h1', { 'data-sec': 'SETUP' }, 'ICT活用能力テスト'),
    h(
      'div',
      { class: 'status-row' },
      h('span', { class: 'status-dot' }, h('b', {}, 'READY')),
      h('span', {}, 'GRADES ', h('b', {}, String(GRADES.length))),
      h('span', {}, 'Q/GRADE ', h('b', {}, String(RULES.questionsPerField * 3))),
      h('span', {}, 'LIMIT ', h('b', {}, `${RULES.timeLimitMs / 60000}:00`)),
      h('span', {}, 'MODE ', h('b', {}, mode === 'trial' ? 'TRIAL' : 'PRODUCTION')),
    ),
    h('p', { class: 'lead' }, 'いまの自分の「ICTを使う力」をたしかめるテストです。名前は入力しません。'),
    h('div', { class: 'form-row' }, h('label', { for: 'cls' }, 'クラス'), clsSel),
    h('div', { class: 'form-row' }, h('label', { for: 'num' }, '出席番号'), numSel),
    h(
      'div',
      { class: 'form-row' },
      h('label', { for: 'dev' }, 'つかっている端末'),
      devSel,
      h('p', { class: 'field-note' }, `自動で「${DEVICE_LABELS[detected]}」と判定しました。ちがうときは変えてください。`),
    ),
    h('div', { class: 'form-row' }, h('label', { for: 'grade' }, 'はじめる級'), gradeSel, gradeNote),
    h(
      'div',
      { class: 'rules' },
      h('h2', { 'data-sec': 'RULES' }, 'テストのきまり'),
      h(
        'ul',
        {},
        h('li', {}, `1つの級は${RULES.questionsPerField * 3}問（基本操作・情報収集・モラル安全から3問ずつ）。`),
        h('li', {}, `合計${RULES.passTotal}問以上 正解、かつ 各分野${RULES.passPerField}問以上 正解で合格。`),
        h('li', {}, '合格すると、つぎの級へ。不合格になったら、そこで終わりです。'),
        h('li', {}, `ぜんぶで${RULES.timeLimitMs / 60000}分。時間になったら、いま解いている級をさいごまで解いて終わりです。`),
        h('li', {}, 'わからない問題は「わからない」をおしてください。'),
      ),
    ),
    error,
    start,
  );

  form.addEventListener('submit', (e) => {
    e.preventDefault();
    const missing: string[] = [];
    if (!clsSel.value) missing.push('クラス');
    if (!numSel.value) missing.push('出席番号');
    if (!gradeSel.value) missing.push('はじめる級');
    if (missing.length) {
      error.textContent = `${missing.join('・')}をえらんでください。`;
      return;
    }
    onStart({
      cls: clsSel.value,
      number: Number(numSel.value),
      device: devSel.value as DeviceKind,
      startGrade: Number(gradeSel.value) as Grade,
      mode,
    });
  });
  return form;
}
