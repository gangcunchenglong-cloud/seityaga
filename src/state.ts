import type { Rng } from './logic/random';
import type { DeviceKind, EndReason, Grade, GradeResult, Mode } from './types';

/** スタート画面で決める情報（名前は扱わない） */
export interface Profile {
  cls: string;
  number: number;
  device: DeviceKind;
  startGrade: Grade;
  mode: Mode;
}

/**
 * 受験中のデータ。メモリ上だけに置き、保存しない（ブラウザを閉じたら消える）。
 */
export interface Run {
  profile: Profile;
  startedAt: Date;
  /** performance.now() 基準の開始時刻 */
  startMs: number;
  results: GradeResult[];
  usedIds: Set<string>;
  rng: Rng;
  endReason?: EndReason;
}
