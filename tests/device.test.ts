import { describe, expect, it } from 'vitest';
import { deviceTarget } from '../src/config';
import { detectDevice } from '../src/ui/device';

describe('端末の自動判定', () => {
  it.each([
    ['Mozilla/5.0 (X11; CrOS x86_64 14541.0.0) AppleWebKit/537.36 Chrome/120 Safari/537.36', 0, 'chromebook'],
    ['Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36', 0, 'pc'],
    ['Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Version/17.0 Safari/605.1.15', 5, 'ipad'],
    ['Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Version/17.0 Safari/605.1.15', 0, 'pc'],
    ['Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148', 5, 'smartphone'],
    ['Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 Chrome/120 Mobile Safari/537.36', 5, 'smartphone'],
    ['Mozilla/5.0 (Linux; Android 13; SM-X200) AppleWebKit/537.36 Chrome/120 Safari/537.36', 5, 'ipad'],
  ] as const)('%s → %s', (ua, touch, expected) => {
    expect(detectDevice(ua, touch)).toBe(expected);
  });

  it('基本操作の問題：PC・Chromebook は pc、iPad・スマホは touch', () => {
    expect(deviceTarget('pc')).toBe('pc');
    expect(deviceTarget('chromebook')).toBe('pc');
    expect(deviceTarget('ipad')).toBe('touch');
    expect(deviceTarget('smartphone')).toBe('touch');
  });
});
