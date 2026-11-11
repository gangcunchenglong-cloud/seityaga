import { csvFileName, recordsToCsv, buildRecords } from '../logic/csv';
import type { Run } from '../state';
import { h, s } from './dom';

/** CSV をファイルとして保存（ダウンロード）する。通信はせず、ブラウザ内でファイルを作る */
export function downloadText(text: string, fileName: string, type = 'text/csv;charset=utf-8'): void {
  const blob = new Blob([text], { type });
  const url = URL.createObjectURL(blob);
  const a = h('a', { href: url, download: fileName, style: 'display:none' });
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/** 結果画面の「保存」部分（ボタン＋端末に合わせた提出までの手順） */
export function saveArea(run: Run): HTMLElement {
  const csv = recordsToCsv(buildRecords(run));
  const name = csvFileName(run.profile, run.startedAt);
  const touch = run.profile.device === 'ipad' || run.profile.device === 'smartphone';
  const status = h('p', { class: 'save-status', 'aria-live': 'polite' });

  const saveBtn = h('button', { class: 'btn btn-primary btn-large', type: 'button' }, '結果を保存（CSV）');
  saveBtn.addEventListener('click', () => {
    downloadText(csv, name);
    status.textContent = `「${name}」を保存しました。下の手順で提出してください。`;
  });

  // iPad・スマホでは「共有」メニューから直接「ファイル」アプリや Teams に送れる（使える端末だけ表示）
  let shareBtn: HTMLElement | null = null;
  const file = typeof File !== 'undefined' ? new File([csv], name, { type: 'text/csv' }) : null;
  if (touch && file && navigator.canShare?.({ files: [file] })) {
    shareBtn = h('button', { class: 'btn btn-large', type: 'button' }, '共有メニューで保存・提出');
    shareBtn.addEventListener('click', async () => {
      try {
        await navigator.share({ files: [file] });
        status.textContent = '共有メニューから保存しました。';
      } catch {
        /* キャンセルされたときは何もしない */
      }
    });
  }

  return h(
    'div',
    { class: 'save' },
    h('h2', { 'data-sec': 'EXPORT' }, '結果を提出しよう'),
    saveBtn,
    shareBtn,
    status,
    touch ? touchGuide() : pcGuide(),
  );
}

function pcGuide(): HTMLElement {
  return h(
    'div',
    { class: 'guide' },
    h('h2', {}, '提出のしかた（パソコン・Chromebook）'),
    h(
      'ol',
      { class: 'guide-text' },
      h('li', {}, h('span', { class: 'step-num' }, '1'), '「結果を保存（CSV）」をおすと、「ダウンロード」フォルダーに保存されます。'),
      h('li', {}, h('span', { class: 'step-num' }, '2'), 'Teams で課題を開き、「作業を追加」→「このデバイスからアップロード」でファイルを選びます。'),
      h('li', {}, h('span', { class: 'step-num' }, '3'), '「提出」をおします。'),
    ),
  );
}

/** iPad・スマホ向けの図付き手順 */
function touchGuide(): HTMLElement {
  const steps: [string, SVGSVGElement][] = [
    ['「結果を保存（CSV）」をおし、「ダウンロード」を選びます。（「共有メニューで保存・提出」をおして「“ファイル”に保存」を選んでもOK）', figDownload()],
    ['「ファイル」アプリの「ダウンロード」フォルダーに、ICT_ で始まるファイルが保存されます。', figFiles()],
    ['Teams で課題を開き、「＋作業を追加」→「ファイル」（参照）から、保存したファイルを選びます。', figTeams()],
    ['ファイルが追加されたことを確かめて、「提出」をおします。', figSubmit()],
  ];
  return h(
    'div',
    { class: 'guide' },
    h('h2', {}, '提出のしかた（iPad・スマホ）'),
    h(
      'ol',
      {},
      steps.map(([text, fig], i) => h('li', {}, fig, h('p', {}, h('span', { class: 'step-num' }, String(i + 1)), text))),
    ),
  );
}

/* ---- 手順の図（SVG で描いた簡単な画面のイラスト） ---- */
function frame(...children: SVGElement[]): SVGSVGElement {
  const svg = s('svg', { viewBox: '0 0 140 100', 'aria-hidden': 'true' });
  svg.append(s('rect', { x: 2, y: 2, width: 136, height: 96, rx: 10, fill: '#fff', stroke: '#8a93a6', 'stroke-width': 2 }), ...children);
  return svg;
}
const txt = (x: number, y: number, t: string, size = 9, fill = '#1d2433', weight = 'normal') =>
  s('text', { x, y, 'font-size': size, fill, 'font-weight': weight, 'text-anchor': 'middle' }, t);

function figDownload() {
  return frame(
    s('rect', { x: 18, y: 26, width: 104, height: 50, rx: 8, fill: '#eef1f6' }),
    txt(70, 42, 'ダウンロードしますか？', 8),
    s('rect', { x: 26, y: 52, width: 40, height: 16, rx: 4, fill: '#fff', stroke: '#c9ced8' }),
    txt(46, 63, '表示', 8),
    s('rect', { x: 74, y: 52, width: 40, height: 16, rx: 4, fill: '#1f5fbf' }),
    txt(94, 63, 'ダウンロード', 7, '#fff', 'bold'),
    s('circle', { cx: 104, cy: 66, r: 9, fill: 'none', stroke: '#e0a000', 'stroke-width': 3 }),
  );
}
function figFiles() {
  return frame(
    txt(70, 18, 'ファイル', 9, '#1d2433', 'bold'),
    s('rect', { x: 12, y: 26, width: 116, height: 22, rx: 4, fill: '#e3edfc' }),
    txt(52, 41, '📁 ダウンロード', 9),
    s('rect', { x: 12, y: 54, width: 116, height: 22, rx: 4, fill: '#fff', stroke: '#c9ced8' }),
    txt(70, 69, '📄 ICT_5-1_07_…csv', 8),
  );
}
function figTeams() {
  return frame(
    s('rect', { x: 2, y: 2, width: 136, height: 18, rx: 10, fill: '#5b5fc7' }),
    txt(70, 15, 'Teams 課題', 9, '#fff', 'bold'),
    s('rect', { x: 20, y: 34, width: 100, height: 22, rx: 6, fill: '#fff', stroke: '#5b5fc7', 'stroke-width': 2 }),
    txt(70, 49, '＋ 作業を追加', 10, '#5b5fc7', 'bold'),
    txt(70, 78, '→ ファイル（参照）', 9),
  );
}
function figSubmit() {
  return frame(
    txt(70, 24, '📄 ICT_5-1_07_…csv', 8),
    s('path', { d: 'M54 40 l8 8 l16 -16', fill: 'none', stroke: '#1b7f3b', 'stroke-width': 4, 'stroke-linecap': 'round' }),
    s('rect', { x: 40, y: 62, width: 60, height: 22, rx: 6, fill: '#5b5fc7' }),
    txt(70, 77, '提出', 11, '#fff', 'bold'),
  );
}
