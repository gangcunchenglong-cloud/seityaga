import { h } from './dom';
import { speechText } from './text';

/**
 * 読み上げ（Web Speech API の speechSynthesis）。
 * 外部通信をしないため、端末の中で音声を作る日本語の声（localService: true）だけを使う。
 * （Chrome の「Google 日本語」などはネット経由なので使わない）
 */
let voice: SpeechSynthesisVoice | null = null;
let listeners: (() => void)[] = [];

function pickVoice(): void {
  if (!('speechSynthesis' in window)) return;
  const voices = speechSynthesis.getVoices();
  voice =
    voices.find((v) => v.lang.replace('_', '-').startsWith('ja') && v.localService) ?? null;
  listeners.forEach((f) => f());
}

export function initSpeech(): void {
  if (!('speechSynthesis' in window)) return;
  pickVoice();
  speechSynthesis.addEventListener?.('voiceschanged', pickVoice);
}

export function canSpeak(): boolean {
  return voice !== null;
}

export function speak(text: string): void {
  if (!voice) return;
  speechSynthesis.cancel();
  const u = new SpeechSynthesisUtterance(speechText(text));
  u.voice = voice;
  u.lang = voice.lang;
  u.rate = 0.9;
  speechSynthesis.speak(u);
}

export function stopSpeaking(): void {
  if ('speechSynthesis' in window) speechSynthesis.cancel();
}

/** 読み上げボタン。使える声がなければ無効にして理由を表示 */
export function speakButton(getText: () => string): HTMLElement {
  const btn = h('button', { class: 'btn btn-speak', type: 'button' }, '🔊 よみあげる');
  const note = h('span', { class: 'speak-note' });
  const update = () => {
    btn.toggleAttribute('disabled', !canSpeak());
    note.textContent = canSpeak() ? '' : '（この たんまつでは よみあげが つかえません）';
  };
  btn.addEventListener('click', () => speak(getText()));
  listeners.push(update);
  update();
  return h('span', { class: 'speak' }, btn, note);
}

export function resetSpeechListeners(): void {
  listeners = [];
}
