import type { Rng } from '../../logic/random';
import type { DeviceTarget, Question, Response } from '../../types';

export interface RenderApi {
  rng: Rng;
  device: Exclude<DeviceTarget, 'all'>;
  showRuby: boolean;
  /** 「こたえる」ボタンを押せるかどうかを知らせる */
  setReady(ready: boolean): void;
  /** 操作そのものが答えになる問題（ドラッグ、ショートカットなど）は、ここで即座に解答する */
  submit(r: Response): void;
}

export interface Rendered {
  el: HTMLElement;
  /** 「こたえる」を押したときの解答。まだ答えられないなら null */
  getResponse(): Response | null;
  /** 「こたえる」ボタンを使わない形式（操作で即答）なら true */
  autoSubmit?: boolean;
  /** 画面を離れるときの後片付け（イベント解除など） */
  dispose?(): void;
}

export type Renderer<Q extends Question = Question> = (q: Q, api: RenderApi) => Rendered;
