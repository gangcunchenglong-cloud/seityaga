import '../styles.css';
import '../theme.css';
import './teacher.css';
import { FIELD_LABELS } from '../config';
import { gradeLabel } from '../types';
import { BANK } from '../questions';
import { h, clear } from '../ui/dom';
import { brand } from '../ui/layout';
import { plainText } from '../ui/text';
import { downloadText } from '../ui/save';
import { aggregate, difficultyLabel, mergedCsv, questionStats, type LoadedFile } from './aggregate';

/**
 * 先生用ツール。提出された CSV をまとめて読み込み、一覧・警告・問題ごとの正答率を表示する。
 * ファイルはブラウザの中だけで読み、どこにも送信しない。
 */
const root = document.getElementById('app')!;
const files: LoadedFile[] = [];
const qById = new Map(BANK.map((q) => [q.id, q]));

const fileInput = h('input', { type: 'file', accept: '.csv,text/csv', multiple: true, id: 'files', class: 'visually-hidden' });
const drop = h(
  'label',
  { class: 'drop', for: 'files', tabindex: '0' },
  h('strong', {}, 'ここに CSV ファイルをまとめてドラッグ＆ドロップ'),
  h('span', {}, 'またはクリックしてファイルを選ぶ（複数選択できます）'),
);
const out = h('div', { class: 't-out' });
const trialBox = h('input', { type: 'checkbox', id: 'trial', checked: true });

root.append(
  h(
    'header',
    { class: 'app-header' },
    brand('ICT活用能力テスト 先生用ツール', 'ICT SKILL ASSESSMENT ／ TEACHER CONSOLE'),
  ),
  h(
    'main',
    { class: 'app-main t-main' },
    h('p', { class: 'lead' }, 'ファイルはこのブラウザの中だけで読み込みます。インターネットには送信しません。'),
    drop,
    fileInput,
    out,
  ),
);

async function addFiles(list: FileList | File[]) {
  for (const f of [...list]) {
    if (!/\.csv$/i.test(f.name) && f.type !== 'text/csv') continue;
    files.push({ name: f.name, text: await f.text() });
  }
  render();
}

fileInput.addEventListener('change', () => fileInput.files && addFiles(fileInput.files));
drop.addEventListener('keydown', (e) => {
  if (e.key === 'Enter' || e.key === ' ') fileInput.click();
});
for (const ev of ['dragenter', 'dragover']) {
  drop.addEventListener(ev, (e) => {
    e.preventDefault();
    drop.classList.add('over');
  });
}
drop.addEventListener('dragleave', () => drop.classList.remove('over'));
drop.addEventListener('drop', (e) => {
  e.preventDefault();
  drop.classList.remove('over');
  if (e.dataTransfer?.files) addFiles(e.dataTransfer.files);
});
// ドロップ先を外したときに、ブラウザがファイルを開いてしまわないように
window.addEventListener('dragover', (e) => e.preventDefault());
window.addEventListener('drop', (e) => e.preventDefault());
trialBox.addEventListener('change', () => render());

const pct = (r: number) => `${Math.round(r * 100)}%`;

function render() {
  clear(out);
  if (files.length === 0) return;
  const a = aggregate(files);
  const bad = a.rows.filter((r) => !r.valid);

  out.append(
    h(
      'div',
      { class: 't-summary' },
      h('span', {}, `ファイル ${files.length}件`),
      h('span', {}, `受験者 ${a.students.length}人`),
      h('span', {}, `行数 ${a.rows.length}`),
      h('span', { class: bad.length ? 'ng' : 'ok' }, bad.length ? `⚠ 確認コード不一致 ${bad.length}行` : '✓ 確認コードはすべて一致'),
      h('button', { class: 'btn btn-small', type: 'button', onclick: () => { files.length = 0; render(); } }, '読み込みをやり直す'),
    ),
  );

  if (a.errors.length) {
    out.append(
      h('div', { class: 'fail-box' }, h('h2', {}, '読み込めなかったファイル'), h('ul', {}, a.errors.map((e) => h('li', {}, `${e.name}：${e.message}`)))),
    );
  }

  if (bad.length) {
    out.append(
      h(
        'div',
        { class: 'fail-box' },
        h('h2', {}, '⚠ 確認コードが一致しない行（書き換えられた可能性があります）'),
        h('ul', {}, bad.map((r) => h('li', {}, `${r.fileName}：${r.class} ${r.number}番 ${r.grade}級（${r.result}・${r.total}問）`))),
        h('p', { class: 'small' }, '確認コードは書き換えの「抑止」のためのしくみです。不一致の場合は、本人に確認するか、再受験を検討してください。'),
      ),
    );
  }

  // 全員の一覧
  out.append(
    h('h2', { 'data-sec': 'STUDENTS' }, '受験者の一覧'),
    h('p', { class: 'small muted' }, '分野別の点数は、最後に受けた級（3問中）のものです。'),
    h(
      'div',
      { class: 'table-wrap' },
      h(
        'table',
        { class: 'result-table t-table' },
        h(
          'thead',
          {},
          h('tr', {}, ['クラス', '番号', '受験日時', '端末', '開始級', '最高合格級', '最後の級', FIELD_LABELS.operation, FIELD_LABELS.information, FIELD_LABELS.safety, 'モード', '終了理由', '確認'].map((c) => h('th', {}, c))),
        ),
        h(
          'tbody',
          {},
          a.students.map((s) =>
            h(
              'tr',
              { class: s.valid ? '' : 'tampered', title: s.fileName },
              h('td', {}, s.class),
              h('td', {}, s.number),
              h('td', {}, s.startedAt),
              h('td', {}, s.device),
              h('td', {}, `${s.startGrade}級`),
              h('td', { class: 'strong' }, s.highestPassed === 'なし' ? '合格なし' : `${s.highestPassed}級`),
              h('td', {}, `${s.lastGrade}級 ${s.lastResult}`),
              h('td', {}, s.operation),
              h('td', {}, s.information),
              h('td', {}, s.safety),
              h('td', {}, s.mode),
              h('td', {}, s.endReason),
              h('td', {}, s.valid ? '✓' : '⚠ 不一致'),
            ),
          ),
        ),
      ),
    ),
    h(
      'div',
      { class: 'final-actions' },
      h(
        'button',
        { class: 'btn btn-primary', type: 'button', onclick: () => downloadText(mergedCsv(a.rows), `ICT_summary_${new Date().toISOString().slice(0, 10)}.csv`) },
        'まとめた CSV を保存（全行・確認結果つき）',
      ),
    ),
  );

  // 問題ごとの正答率
  const stats = questionStats(a.rows, trialBox.checked);
  out.append(
    h('h2', { 'data-sec': 'ITEM ANALYSIS' }, '問題ごとの正答率'),
    h(
      'p',
      { class: 'small' },
      h('label', { class: 't-check' }, trialBox, ' 試作モードの結果も集計に含める'),
      h('span', { class: 'muted' }, '　※確認コードが一致しない行は数えません。5人以上が解いた問題で、正答率30%未満を「難しすぎ？」、95%超を「易しすぎ？」と表示します。'),
    ),
    h(
      'div',
      { class: 'table-wrap' },
      h(
        'table',
        { class: 'result-table t-table' },
        h('thead', {}, h('tr', {}, ['問題ID', '級', '分野', '問題文', '解いた人数', '正解', '正答率', '目安'].map((c) => h('th', {}, c)))),
        h(
          'tbody',
          {},
          stats.map((s) => {
            const q = qById.get(s.id);
            const label = difficultyLabel(s);
            return h(
              'tr',
              { class: label ? 'flag' : '' },
              h('td', { class: 'mono' }, s.id),
              h('td', {}, q ? gradeLabel(q.grade) : ''),
              h('td', {}, q ? FIELD_LABELS[q.field] : ''),
              h('td', { class: 'qtext' }, q ? plainText(q.question) : '（この版の問題バンクにない問題）'),
              h('td', {}, s.attempts),
              h('td', {}, s.correct),
              h('td', {}, h('span', { class: 'bar', style: `--w:${pct(s.rate)}` }, pct(s.rate))),
              h('td', { class: 'strong' }, label),
            );
          }),
        ),
      ),
    ),
  );
}
