/** 0以上1未満の乱数を返す関数（テストでは固定の乱数に差し替える） */
export type Rng = () => number;

export const defaultRng: Rng = () => Math.random();

/** 再現可能な乱数（テスト用）。mulberry32 */
export function seededRng(seed: number): Rng {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** Fisher–Yates シャッフル（元の配列は変えない） */
export function shuffle<T>(items: readonly T[], rng: Rng = defaultRng): T[] {
  const a = items.slice();
  for (let i = a.length - 1; i > 0; i--) {
    const j = Math.floor(rng() * (i + 1));
    [a[i], a[j]] = [a[j], a[i]];
  }
  return a;
}

/** 0..n-1 をシャッフルした並び */
export function permutation(n: number, rng: Rng = defaultRng): number[] {
  return shuffle(
    Array.from({ length: n }, (_, i) => i),
    rng,
  );
}

/**
 * 並べ替え問題の初期表示用。正解の並びと同じにならないようにシャッフルする。
 */
export function derangedOrder(answer: readonly number[], rng: Rng = defaultRng): number[] {
  if (answer.length < 2) return answer.slice();
  for (let tries = 0; tries < 20; tries++) {
    const p = shuffle(answer, rng);
    if (p.some((v, i) => v !== answer[i])) return p;
  }
  return answer.slice().reverse();
}
