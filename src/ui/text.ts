/**
 * ふりがな記法 {漢字|かんじ} を扱う。
 *  - 画面表示：<ruby>漢字<rt>かんじ</rt></ruby>
 *  - 読み上げ：かんじ
 *  - ふりがな不要の表示：漢字
 */
const RUBY_RE = /\{([^{}|]+)\|([^{}|]+)\}/g;

export interface TextPart {
  base: string;
  reading?: string;
}

export function parseRuby(text: string): TextPart[] {
  const parts: TextPart[] = [];
  let last = 0;
  for (const m of text.matchAll(RUBY_RE)) {
    const i = m.index ?? 0;
    if (i > last) parts.push({ base: text.slice(last, i) });
    parts.push({ base: m[1], reading: m[2] });
    last = i + m[0].length;
  }
  if (last < text.length) parts.push({ base: text.slice(last) });
  return parts;
}

/** 読み上げ用（ふりがなの読みを使う） */
export function speechText(text: string): string {
  return text.replace(RUBY_RE, (_, _b, r) => r);
}

/** ふりがなを外した文字 */
export function plainText(text: string): string {
  return text.replace(RUBY_RE, (_, b) => b);
}

/** ふりがな付きの表示用ノード。showRuby=false ならふりがなを外して表示 */
export function rich(text: string, showRuby = true): DocumentFragment {
  const frag = document.createDocumentFragment();
  for (const p of parseRuby(text)) {
    if (p.reading && showRuby) {
      const r = document.createElement('ruby');
      r.appendChild(document.createTextNode(p.base));
      const rt = document.createElement('rt');
      rt.textContent = p.reading;
      r.appendChild(rt);
      frag.appendChild(r);
    } else {
      frag.appendChild(document.createTextNode(p.base));
    }
  }
  return frag;
}
