import type { Figure } from '../types';
import { h, s } from './dom';
import { rich } from './text';

/** 表・グラフ・擬似画面（問題の資料） */
export function renderFigure(f: Figure, showRuby: boolean): HTMLElement {
  switch (f.kind) {
    case 'table':
      return h(
        'figure',
        { class: 'fig fig-table' },
        h(
          'table',
          {},
          h('thead', {}, h('tr', {}, f.headers.map((c) => h('th', { scope: 'col' }, rich(c, showRuby))))),
          h('tbody', {}, f.rows.map((r) => h('tr', {}, r.map((c) => h('td', {}, rich(c, showRuby)))))),
        ),
        f.caption ? h('figcaption', {}, rich(f.caption, showRuby)) : null,
      );
    case 'screen':
      return h(
        'figure',
        { class: 'fig fig-screen', 'aria-label': '画面の例' },
        h('div', { class: 'mock-titlebar' }, rich(f.title, showRuby)),
        h('div', { class: 'mock-body' }, f.lines.map((l) => h('p', {}, rich(l, showRuby)))),
      );
    case 'bar':
    case 'line':
      return h(
        'figure',
        { class: 'fig fig-chart' },
        chartSvg(f.kind, f.labels, f.values, f.unit ?? ''),
        f.caption ? h('figcaption', {}, rich(f.caption, showRuby)) : null,
      );
  }
}

/** 棒グラフ・折れ線グラフを SVG で描く（外部ライブラリを使わない） */
function chartSvg(kind: 'bar' | 'line', labels: string[], values: number[], unit: string): SVGSVGElement {
  const W = 480;
  const H = 260;
  const pad = { l: 52, r: 16, t: 24, b: 44 };
  const max = niceMax(Math.max(...values, 1));
  const iw = W - pad.l - pad.r;
  const ih = H - pad.t - pad.b;
  const x = (i: number) => pad.l + (iw / labels.length) * (i + 0.5);
  const y = (v: number) => pad.t + ih - (v / max) * ih;
  const svg = s('svg', { viewBox: `0 0 ${W} ${H}`, class: 'chart', role: 'img' });
  svg.appendChild(
    s('title', {}, labels.map((l, i) => `${l} ${values[i]}${unit}`).join('、')),
  );
  for (let k = 0; k <= 5; k++) {
    const v = Math.round(((max / 5) * k) * 100) / 100;
    svg.appendChild(s('line', { x1: pad.l, x2: W - pad.r, y1: y(v), y2: y(v), class: 'grid' }));
    svg.appendChild(s('text', { x: pad.l - 6, y: y(v) + 5, 'text-anchor': 'end', class: 'axis' }, `${v}`));
  }
  if (unit) svg.appendChild(s('text', { x: pad.l - 6, y: 14, 'text-anchor': 'end', class: 'axis' }, `（${unit}）`));
  const bw = Math.min(48, (iw / labels.length) * 0.6);
  if (kind === 'line') {
    svg.appendChild(
      s('polyline', { points: values.map((v, i) => `${x(i)},${y(v)}`).join(' '), class: 'line' }),
    );
  }
  values.forEach((v, i) => {
    if (kind === 'bar') {
      svg.appendChild(s('rect', { x: x(i) - bw / 2, y: y(v), width: bw, height: pad.t + ih - y(v), class: 'bar', rx: 3 }));
    } else {
      svg.appendChild(s('circle', { cx: x(i), cy: y(v), r: 5, class: 'dot' }));
    }
    svg.appendChild(s('text', { x: x(i), y: y(v) - 8, 'text-anchor': 'middle', class: 'val' }, `${v}`));
    svg.appendChild(s('text', { x: x(i), y: H - pad.b + 22, 'text-anchor': 'middle', class: 'axis' }, labels[i]));
  });
  return svg;
}

function niceMax(v: number): number {
  const p = Math.pow(10, Math.floor(Math.log10(v)));
  for (const m of [1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10]) if (v <= m * p) return m * p;
  return 10 * p;
}
