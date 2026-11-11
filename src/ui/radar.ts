import { FIELD_LABELS, RULES } from '../config';
import { FIELDS, type FieldScores } from '../types';
import { s } from './dom';

/** 3分野の正答率のレーダーチャート（SVG で自作。外部ライブラリなし） */
export function radarChart(scores: FieldScores, max: number = RULES.questionsPerField): SVGSVGElement {
  const W = 360;
  const H = 280;
  const cx = W / 2;
  const cy = 170;
  const R = 110;
  const angle = (i: number) => -Math.PI / 2 + (2 * Math.PI * i) / FIELDS.length;
  const pt = (i: number, r: number) => [cx + r * Math.cos(angle(i)), cy + r * Math.sin(angle(i))];
  const rates = FIELDS.map((f) => scores[f] / max);

  const svg = s('svg', { viewBox: `0 0 ${W} ${H}`, class: 'radar', role: 'img' });
  svg.appendChild(
    s('title', {}, FIELDS.map((f, i) => `${FIELD_LABELS[f]} ${Math.round(rates[i] * 100)}%`).join('、')),
  );
  for (const k of [0.25, 0.5, 0.75, 1]) {
    svg.appendChild(s('polygon', { class: 'ring', points: FIELDS.map((_, i) => pt(i, R * k).join(',')).join(' ') }));
  }
  FIELDS.forEach((f, i) => {
    const [x, y] = pt(i, R);
    svg.appendChild(s('line', { class: 'spoke', x1: cx, y1: cy, x2: x, y2: y }));
    const [lx, ly] = pt(i, R + 30);
    // 左右の軸のラベルは、画面からはみ出さないよう少し内側へ寄せる
    const dx = Math.abs(lx - cx) < 5 ? 0 : lx > cx ? -40 : 40;
    svg.appendChild(s('text', { x: lx + dx, y: ly, 'text-anchor': 'middle' }, FIELD_LABELS[f]));
    svg.appendChild(s('text', { x: lx + dx, y: ly + 18, 'text-anchor': 'middle', class: 'pct' }, `${Math.round(rates[i] * 100)}%`));
  });
  // 0% でも点が見えるように、ほんの少しだけ外側に出す
  svg.appendChild(
    s('polygon', { class: 'area', points: FIELDS.map((_, i) => pt(i, Math.max(R * rates[i], 2)).join(',')).join(' ') }),
  );
  FIELDS.forEach((_, i) => {
    const [x, y] = pt(i, Math.max(R * rates[i], 2));
    svg.appendChild(s('circle', { class: 'pt', cx: x, cy: y, r: 5 }));
  });
  return svg;
}
