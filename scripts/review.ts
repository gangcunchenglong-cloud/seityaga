/**
 * 問題レビュー用ツール（開発者・先生用）。
 * draft の問題を1問ずつ表示し、「承認」「却下」「編集」して、結果を JSON で書き出す。
 * ファイルはブラウザの中だけで処理し、どこにも送信しない。
 */
import '../src/styles.css';
import '../src/theme.css';
import '../src/teacher/teacher.css';
import './review.css';
import { FIELD_LABELS } from '../src/config';
import { seededRng } from '../src/logic/random';
import { isCorrect } from '../src/logic/score';
import { validateQuestion } from '../src/logic/validate';
import { gradeLabel, type Question, type QuestionStatus } from '../src/types';
import { clear, h } from '../src/ui/dom';
import { downloadText } from '../src/ui/save';
import { brand } from '../src/ui/layout';
import { questionScreen } from '../src/ui/screens/question';
import { plainText } from '../src/ui/text';

const root = document.getElementById('app')!;
let items: Question[] = [];
let index = 0;
let onlyDraft = true;
let disposePreview: (() => void) | null = null;

const store = {
  get(k: string) {
    try {
      return localStorage.getItem(k) ?? '';
    } catch {
      return '';
    }
  },
  set(k: string, v: string) {
    try {
      localStorage.setItem(k, v);
    } catch {
      /* 保存できない環境では何もしない */
    }
  },
};

const reviewer = h('input', { type: 'text', id: 'reviewer', placeholder: '例：山田', value: store.get('reviewer') });
reviewer.addEventListener('input', () => store.set('reviewer', reviewer.value));
const fileInput = h('input', { type: 'file', accept: '.json,application/json', multiple: true, id: 'files', class: 'visually-hidden' });
const drop = h(
  'label',
  { class: 'drop', for: 'files', tabindex: '0' },
  h('strong', {}, '問題の JSON ファイルをドラッグ＆ドロップ'),
  h('span', {}, '（questions/drafts/ の生成結果や src/questions/grade-N.json。クリックで選択）'),
);
const view = h('div', { class: 'rv-view' });

root.append(
  h('header', { class: 'app-header' }, brand('ICT活用能力テスト 問題レビュー', 'ICT SKILL ASSESSMENT ／ ITEM REVIEW')),
  h(
    'main',
    { class: 'app-main t-main' },
    h('p', { class: 'lead' }, 'AIが作った問題（draft）を1問ずつ確認し、承認・却下・編集します。ファイルはこのブラウザの中だけで処理します。'),
    h('div', { class: 'form-row' }, h('label', { for: 'reviewer' }, '確認者の名前（reviewedBy に記録）'), reviewer),
    drop,
    fileInput,
    view,
  ),
);

async function addFiles(list: FileList | File[]) {
  for (const f of [...list]) {
    try {
      const data: unknown = JSON.parse(await f.text());
      const arr = (Array.isArray(data) ? data : [data]) as Question[];
      for (const q of arr) {
        const i = items.findIndex((x) => x.id === q.id);
        if (i >= 0) items[i] = q;
        else items.push(q);
      }
    } catch {
      alert(`${f.name} は JSON として読み込めませんでした。`);
    }
  }
  index = 0;
  render();
}
fileInput.addEventListener('change', () => fileInput.files && addFiles(fileInput.files));
drop.addEventListener('dragover', (e) => e.preventDefault());
drop.addEventListener('drop', (e) => {
  e.preventDefault();
  if (e.dataTransfer?.files) addFiles(e.dataTransfer.files);
});
window.addEventListener('dragover', (e) => e.preventDefault());
window.addEventListener('drop', (e) => e.preventDefault());

const visible = () => items.filter((q) => !onlyDraft || q.status === 'draft');

/** 正解を文章で表す（確認しやすいように） */
function answerText(q: Question): string {
  switch (q.type) {
    case 'choice':
    case 'multi':
      return q.answer.map((i) => plainText(q.choices[i])).join(' ／ ');
    case 'order':
      return q.answer.map((i, n) => `${n + 1}. ${plainText(q.choices[i])}`).join('　');
    case 'match':
      return q.pairs.map((p) => `${plainText(p.left)} → ${plainText(p.right)}`).join('　');
    case 'input':
      return `「${q.target}」を ${q.timeLimitSec} 秒以内`;
    case 'ui-task': {
      const t = q.task;
      switch (t.kind) {
        case 'button':
          return `ボタン「${plainText(t.buttons[t.answer])}」`;
        case 'context-menu':
          return `${t.trigger === 'right-click' ? '右クリック' : '長押し'} →「${plainText(t.menu[t.answer])}」`;
        case 'drag-to-folder':
          return `「${t.file}」→「${t.folders[t.answer]}」`;
        case 'copy-paste':
          return `「${t.text}」をコピー＆貼り付け`;
        case 'shortcut':
          return `${t.ctrl ? 'Ctrl + ' : ''}${t.shift ? 'Shift + ' : ''}${t.key.toUpperCase()}`;
        case 'open-item':
          return `${t.action === 'tap' ? 'タップ' : 'ダブルクリック'}で「${t.items[t.answer]}」`;
      }
    }
  }
}

function setStatus(status: QuestionStatus) {
  const list = visible();
  const q = list[index];
  if (!q) return;
  if (status !== 'draft' && !reviewer.value.trim()) {
    alert('確認者の名前を入力してください。');
    reviewer.focus();
    return;
  }
  q.status = status;
  q.reviewedBy = status === 'draft' ? '' : reviewer.value.trim();
  q.reviewedAt = status === 'draft' ? '' : new Date().toISOString().slice(0, 10);
  // draft だけ表示中なら、同じ位置に次の問題が来る
  if (!onlyDraft) index = Math.min(index + 1, list.length - 1);
  render();
}

function exportJson(approvedOnly: boolean) {
  const out = approvedOnly ? items.filter((q) => q.status === 'approved') : items;
  const day = new Date().toISOString().slice(0, 10).replace(/-/g, '');
  downloadText(JSON.stringify(out, null, 2) + '\n', `reviewed-${approvedOnly ? 'approved-' : ''}${day}.json`, 'application/json');
}

function render() {
  disposePreview?.();
  disposePreview = null;
  clear(view);
  if (items.length === 0) return;

  const counts = { draft: 0, approved: 0, rejected: 0 } as Record<QuestionStatus, number>;
  items.forEach((q) => counts[q.status]++);
  const onlyBox = h('input', { type: 'checkbox', checked: onlyDraft });
  onlyBox.addEventListener('change', () => {
    onlyDraft = onlyBox.checked;
    index = 0;
    render();
  });

  view.append(
    h(
      'div',
      { class: 't-summary' },
      h('span', {}, `全 ${items.length}問`),
      h('span', {}, `未確認 ${counts.draft}`),
      h('span', { class: 'ok' }, `承認 ${counts.approved}`),
      h('span', { class: 'ng' }, `却下 ${counts.rejected}`),
      h('label', { class: 't-check' }, onlyBox, ' 未確認（draft）だけ表示'),
    ),
    h(
      'div',
      { class: 'final-actions rv-export' },
      h('button', { class: 'btn btn-primary', type: 'button', onclick: () => exportJson(false) }, '結果をJSONで書き出す（全問）'),
      h('button', { class: 'btn', type: 'button', onclick: () => exportJson(true) }, '承認した問題だけ書き出す'),
    ),
  );

  const list = visible();
  if (list.length === 0) {
    view.append(h('p', { class: 'end-note' }, '未確認の問題はありません。結果を書き出してください。'));
    return;
  }
  index = Math.max(0, Math.min(index, list.length - 1));
  const q = list[index];
  const errors = validateQuestion(q);

  const result = h('p', { class: 'rv-result', 'aria-live': 'polite' });
  const mountPreview = () => {
    disposePreview?.();
    const scr = questionScreen({
      q,
      index: 0,
      total: 1,
      device: q.device === 'touch' ? 'touch' : 'pc',
      rng: seededRng(Date.now() % 100000),
      overTime: false,
      onAnswer: (r) => {
        const ok = isCorrect(q, r);
        result.textContent = r.type === 'skip' ? '「わからない」が選ばれました。' : ok ? '○ 正解と判定されました' : '× 不正解と判定されました';
        result.className = `rv-result ${ok ? 'ok' : 'ng'}`;
        mountPreview();
      },
    });
    disposePreview = scr.dispose;
    preview.replaceChildren(scr.el);
  };
  const preview = h('div', { class: 'rv-preview' });

  const editor = h('textarea', { class: 'rv-editor', rows: 18, spellcheck: 'false', hidden: true });
  const editErr = h('p', { class: 'form-error' });
  const saveEdit = h('button', { class: 'btn btn-primary', type: 'button', hidden: true }, '編集を保存');
  const editBtn = h('button', { class: 'btn', type: 'button' }, '編集');
  editBtn.addEventListener('click', () => {
    editor.value = JSON.stringify(q, null, 2);
    editor.hidden = false;
    saveEdit.hidden = false;
    editor.focus();
  });
  saveEdit.addEventListener('click', () => {
    try {
      const nq = JSON.parse(editor.value) as Question;
      const errs = validateQuestion(nq);
      if (errs.length) {
        editErr.textContent = `形式に問題があります：${errs.join(' / ')}`;
        return;
      }
      if (nq.id !== q.id && items.some((x) => x.id === nq.id)) {
        editErr.textContent = 'その id はほかの問題で使われています。';
        return;
      }
      items[items.indexOf(q)] = nq;
      render();
    } catch (e) {
      editErr.textContent = `JSON として読み込めません：${(e as Error).message}`;
    }
  });

  view.append(
    h(
      'section',
      { class: 'card rv-card' },
      h(
        'div',
        { class: 'rv-nav' },
        h('button', { class: 'btn btn-small', type: 'button', disabled: index === 0, onclick: () => { index--; render(); } }, '← 前へ'),
        h('strong', {}, `${index + 1} / ${list.length}`),
        h('button', { class: 'btn btn-small', type: 'button', disabled: index >= list.length - 1, onclick: () => { index++; render(); } }, '次へ →'),
      ),
      h(
        'dl',
        { class: 'rv-meta' },
        h('dt', {}, 'id'), h('dd', { class: 'mono' }, q.id),
        h('dt', {}, '級・分野'), h('dd', {}, `${gradeLabel(q.grade)}・${FIELD_LABELS[q.field] ?? q.field}`),
        h('dt', {}, '端末・形式'), h('dd', {}, `${q.device}・${q.type}${q.type === 'ui-task' ? `（${q.task.kind}）` : ''}`),
        h('dt', {}, '状態'), h('dd', {}, `${q.status}${q.reviewedBy ? `（${q.reviewedBy} ${q.reviewedAt}）` : ''}　出典：${q.source}`),
        h('dt', {}, '正解'), h('dd', { class: 'strong' }, answerText(q)),
        h('dt', {}, '解説'), h('dd', {}, plainText(q.explanation)),
      ),
      errors.length ? h('div', { class: 'fail-box' }, h('strong', {}, '形式の問題：'), h('ul', {}, errors.map((e) => h('li', {}, e)))) : null,
      h(
        'details',
        { class: 'rv-check' },
        h('summary', {}, '確認のポイント'),
        h(
          'ul',
          {},
          h('li', {}, '正解が1つに決まるか（ほかの選択肢も正しいと言えないか）'),
          h('li', {}, '事実として正確か（数字・用語・操作手順）'),
          h('li', {}, '対象学年に合った言葉と内容か（8〜6級はふりがな）'),
          h('li', {}, '人を傷つける表現や、特定の家庭環境を前提にした表現がないか'),
        ),
      ),
      h('h2', {}, 'プレビュー（実際に解いて判定を確かめられます）'),
      preview,
      result,
      h(
        'div',
        { class: 'rv-actions' },
        h('button', { class: 'btn btn-primary', type: 'button', disabled: errors.length > 0, onclick: () => setStatus('approved') }, '✓ 承認'),
        h('button', { class: 'btn rv-reject', type: 'button', onclick: () => setStatus('rejected') }, '✕ 却下'),
        editBtn,
        q.status !== 'draft' ? h('button', { class: 'btn btn-ghost', type: 'button', onclick: () => setStatus('draft') }, '未確認にもどす') : null,
      ),
      editor,
      editErr,
      saveEdit,
    ),
  );
  mountPreview();
}
