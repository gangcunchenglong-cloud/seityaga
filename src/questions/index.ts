import type { Question } from '../types';
import g1 from './grade-1.json';
import g2 from './grade-2.json';
import g3 from './grade-3.json';
import g4 from './grade-4.json';
import g5 from './grade-5.json';
import g6 from './grade-6.json';
import g7 from './grade-7.json';
import g8 from './grade-8.json';
import gp1 from './grade-p1.json';
import gp2 from './grade-p2.json';

/** 問題バンク（ビルド時にアプリへ埋め込まれる。実行中に通信はしない） */
export const BANK: Question[] = [g8, g7, g6, g5, g4, g3, gp2, g2, gp1, g1].flat() as Question[];
