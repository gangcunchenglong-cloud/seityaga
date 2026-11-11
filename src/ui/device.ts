import type { DeviceKind } from '../types';

/**
 * 端末の種類を自動判定する（あくまで目安。スタート画面で手動変更できる）。
 * iPadOS の Safari は「Mac」と名乗るので、タッチ点の数で見分ける。
 */
export function detectDevice(
  ua: string = navigator.userAgent,
  maxTouchPoints: number = navigator.maxTouchPoints ?? 0,
): DeviceKind {
  if (/CrOS/.test(ua)) return 'chromebook';
  if (/iPhone|iPod/.test(ua)) return 'smartphone';
  if (/Android/.test(ua)) return /Mobile/.test(ua) ? 'smartphone' : 'ipad';
  if (/iPad/.test(ua)) return 'ipad';
  if (/Macintosh/.test(ua) && maxTouchPoints > 1) return 'ipad';
  return 'pc';
}
