import { permutation } from '../../logic/random';
import { normalizeText } from '../../logic/score';
import type { UiTask, UiTaskQuestion } from '../../types';
import { h } from '../dom';
import { rich } from '../text';
import type { RenderApi, Rendered, Renderer } from './common';

/** 「🐶 いぬの え」→ アイコンと文字に分ける */
function iconLabel(s: string, showRuby: boolean): HTMLElement[] {
  const m = s.match(/^(\p{Extended_Pictographic}️?)\s*(.*)$/u);
  if (!m) return [h('span', { class: 'label' }, rich(s, showRuby))];
  return [h('span', { class: 'icon', 'aria-hidden': 'true' }, m[1]), h('span', { class: 'label' }, rich(m[2], showRuby))];
}

export const renderUiTask: Renderer<UiTaskQuestion> = (q, api) => {
  const t = q.task;
  switch (t.kind) {
    case 'button':
      return buttonTask(t, api);
    case 'context-menu':
      return contextMenuTask(t, api);
    case 'drag-to-folder':
      return dragTask(t, api);
    case 'copy-paste':
      return copyPasteTask(t, api);
    case 'shortcut':
      return shortcutTask(t, api);
    case 'open-item':
      return openItemTask(t, api);
  }
};

type Of<K extends UiTask['kind']> = Extract<UiTask, { kind: K }>;

/** 正しいボタンを押す（ダイアログ・共有メニュー・ツールバー） */
function buttonTask(t: Of<'button'>, api: RenderApi): Rendered {
  // ダイアログはボタンの位置に意味があるので並びを変えない。共有メニュー・ツールバーはシャッフル
  const order = t.style === 'dialog' ? t.buttons.map((_, i) => i) : permutation(t.buttons.length, api.rng);
  const btns = order.map((i) =>
    h(
      'button',
      { class: 'btn', type: 'button', onclick: () => api.submit({ type: 'ui-task', success: i === t.answer }) },
      rich(t.buttons[i], api.showRuby),
    ),
  );
  let body: HTMLElement;
  if (t.style === 'share') {
    body = h(
      'div',
      { class: 'share-sheet' },
      t.message ? h('p', {}, rich(t.message, api.showRuby)) : null,
      h('div', { class: 'share-grid' }, btns),
    );
  } else if (t.style === 'toolbar') {
    body = h(
      'div',
      {},
      h('div', { class: 'mock-toolbar', role: 'toolbar' }, btns),
      h('div', { class: 'mock-content' }, t.message ? rich(t.message, api.showRuby) : ''),
    );
  } else {
    body = h(
      'div',
      {},
      h('div', { class: 'mock-content' }, t.message ? rich(t.message, api.showRuby) : ''),
      h('div', { class: 'mock-buttons' }, btns),
    );
  }
  return {
    el: h('div', { class: 'mock', role: 'dialog', 'aria-label': '画面の例' }, h('div', { class: 'mock-titlebar' }, rich(t.title, api.showRuby)), body),
    getResponse: () => null,
    autoSubmit: true,
  };
}

/** 右クリック（PC）／長押し（タッチ）でメニューを出して選ぶ */
function contextMenuTask(t: Of<'context-menu'>, api: RenderApi): Rendered {
  const target = h('div', { class: 'ctx-target', tabindex: '0', role: 'button' }, iconLabel(t.target, api.showRuby));
  const area = h('div', { class: 'ctx-area' }, target);
  const status = h('p', { class: 'task-status', 'aria-live': 'polite' });
  let menu: HTMLElement | null = null;

  const openMenu = (x: number, y: number) => {
    menu?.remove();
    const rect = area.getBoundingClientRect();
    menu = h(
      'div',
      { class: 'ctx-menu', role: 'menu' },
      t.menu.map((m, i) =>
        h(
          'button',
          { type: 'button', role: 'menuitem', onclick: () => api.submit({ type: 'ui-task', success: i === t.answer }) },
          rich(m, api.showRuby),
        ),
      ),
    );
    area.appendChild(menu);
    // 画面からはみ出さないように位置を調整
    const left = Math.min(Math.max(0, x - rect.left), Math.max(0, area.clientWidth - menu.offsetWidth - 4));
    const top = Math.min(Math.max(0, y - rect.top), Math.max(0, area.clientHeight - menu.offsetHeight - 4));
    menu.style.left = `${left}px`;
    menu.style.top = `${top}px`;
    (menu.querySelector('button') as HTMLElement).focus({ preventScroll: true });
  };

  // 右クリック（Android の長押しでも発生する）
  target.addEventListener('contextmenu', (e) => {
    e.preventDefault();
    openMenu(e.clientX || target.getBoundingClientRect().left + 20, e.clientY || target.getBoundingClientRect().bottom);
  });

  if (t.trigger === 'right-click') {
    target.addEventListener('click', () => {
      status.textContent = 'マウスの右ボタンをおしてみましょう。';
    });
  } else {
    // 長押しの判定（0.5秒）。iPad の Safari は contextmenu を出さないので自分で判定する
    let timer: number | undefined;
    let sx = 0;
    let sy = 0;
    const cancel = () => {
      clearTimeout(timer);
      target.classList.remove('pressing');
    };
    target.addEventListener('pointerdown', (e) => {
      sx = e.clientX;
      sy = e.clientY;
      target.classList.add('pressing');
      timer = window.setTimeout(() => {
        target.classList.remove('pressing');
        openMenu(sx, sy);
      }, 500);
    });
    target.addEventListener('pointermove', (e) => {
      if (Math.hypot(e.clientX - sx, e.clientY - sy) > 12) cancel();
    });
    target.addEventListener('pointerup', (e) => {
      if (!menu && timer !== undefined) status.textContent = 'しばらく おしたままに してみましょう。';
      cancel();
      void e;
    });
    target.addEventListener('pointercancel', cancel);
  }

  return { el: h('div', { class: 'mock' }, area, status), getResponse: () => null, autoSubmit: true };
}

/** ファイルを正しいフォルダーへドラッグする */
function dragTask(t: Of<'drag-to-folder'>, api: RenderApi): Rendered {
  const file = h('div', { class: 'desk-file', role: 'img', 'aria-label': t.file }, iconLabel(t.file, api.showRuby));
  const order = permutation(t.folders.length, api.rng);
  const folders = order.map((i) =>
    h('div', { class: 'folder', 'data-index': i }, iconLabel(t.folders[i], api.showRuby)),
  );
  const status = h('p', { class: 'task-status', 'aria-live': 'polite' });

  const folderAt = (x: number, y: number): HTMLElement | null => {
    for (const el of document.elementsFromPoint(x, y)) {
      const f = (el as HTMLElement).closest?.('.folder') as HTMLElement | null;
      if (f) return f;
    }
    return null;
  };

  file.addEventListener('pointerdown', (e) => {
    e.preventDefault();
    file.setPointerCapture(e.pointerId);
    file.classList.add('dragging');
    const sx = e.clientX;
    const sy = e.clientY;
    let over: HTMLElement | null = null;
    const onMove = (ev: PointerEvent) => {
      file.style.transform = `translate(${ev.clientX - sx}px, ${ev.clientY - sy}px)`;
      const f = folderAt(ev.clientX, ev.clientY);
      if (f !== over) {
        over?.classList.remove('over');
        f?.classList.add('over');
        over = f;
      }
    };
    const onUp = (ev: PointerEvent) => {
      file.removeEventListener('pointermove', onMove);
      file.removeEventListener('pointerup', onUp);
      file.removeEventListener('pointercancel', onUp);
      file.classList.remove('dragging');
      const f = ev.type === 'pointerup' ? folderAt(ev.clientX, ev.clientY) : null;
      over?.classList.remove('over');
      if (f) {
        api.submit({ type: 'ui-task', success: Number(f.dataset.index) === t.answer });
      } else {
        file.style.transform = '';
        if (Math.hypot(ev.clientX - sx, ev.clientY - sy) < 8) {
          status.textContent = 'おしたまま うごかして、フォルダーの上で はなしましょう。';
        }
      }
    };
    file.addEventListener('pointermove', onMove);
    file.addEventListener('pointerup', onUp);
    file.addEventListener('pointercancel', onUp);
  });

  return {
    el: h('div', { class: 'mock' }, h('div', { class: 'desk' }, file, h('div', { class: 'folders' }, folders)), status),
    getResponse: () => null,
    autoSubmit: true,
  };
}

/** 文字をコピーして貼り付ける（実際に「貼り付け」の操作をしたときだけ正解） */
function copyPasteTask(t: Of<'copy-paste'>, api: RenderApi): Rendered {
  let pasted = false;
  const area = h('textarea', {
    class: 'paste-box',
    rows: 2,
    autocomplete: 'off',
    autocapitalize: 'off',
    spellcheck: 'false',
    placeholder: 'ここに貼り付け',
    'aria-label': '貼り付けるらん',
  });
  area.addEventListener('paste', () => {
    pasted = true;
  });
  area.addEventListener('input', (e) => {
    if ((e as InputEvent).inputType === 'insertFromPaste') pasted = true;
    api.setReady(area.value.trim().length > 0);
  });
  return {
    el: h(
      'div',
      {},
      h('p', { class: 'hint' }, 'コピーする文：'),
      h('div', { class: 'copy-source' }, t.text),
      h('p', { class: 'hint', style: 'margin-top:12px' }, '貼り付けるらん：'),
      area,
    ),
    getResponse: () => ({
      type: 'ui-task',
      success: pasted && normalizeText(area.value) === normalizeText(t.text),
    }),
  };
}

/** キーボードのショートカット。最初に押した「Ctrl＋キー」の組み合わせで判定する */
function shortcutTask(t: Of<'shortcut'>, api: RenderApi): Rendered {
  const status = h('p', { class: 'task-status', 'aria-live': 'polite' }, 'キーボードで おしてください。');
  const onKey = (e: KeyboardEvent) => {
    if (['Control', 'Meta', 'Shift', 'Alt'].includes(e.key)) return;
    const mod = e.ctrlKey || e.metaKey;
    if (!mod) {
      status.textContent = 'ショートカットキーは、ほかのキーと組み合わせておします。';
      return;
    }
    e.preventDefault();
    const key = (e.code.startsWith('Key') ? e.code.slice(3) : e.key).toLowerCase();
    api.submit({
      type: 'ui-task',
      success: key === t.key.toLowerCase() && e.shiftKey === t.shift && t.ctrl,
    });
  };
  window.addEventListener('keydown', onKey, true);
  return {
    el: h(
      'div',
      { class: 'mock' },
      h('div', { class: 'mock-titlebar' }, '文書'),
      h('div', { class: 'kbd-area' }, h('div', { class: 'doc' }, h('mark', {}, t.context)), status),
    ),
    getResponse: () => null,
    autoSubmit: true,
    dispose: () => window.removeEventListener('keydown', onKey, true),
  };
}

/** アイコンを開く（PC：ダブルクリック、タッチ：タップ） */
function openItemTask(t: Of<'open-item'>, api: RenderApi): Rendered {
  const status = h('p', { class: 'task-status', 'aria-live': 'polite' });
  const order = permutation(t.items.length, api.rng);
  const open = (i: number) => api.submit({ type: 'ui-task', success: i === t.answer });
  const items = order.map((i) => {
    const b = h('button', { class: 'open-item', type: 'button' }, iconLabel(t.items[i], api.showRuby));
    if (t.action === 'tap') {
      b.addEventListener('click', () => open(i));
    } else {
      b.addEventListener('click', () => {
        items.forEach((x) => x.classList.remove('selected'));
        b.classList.add('selected');
        status.textContent = '2かい つづけて すばやく おしてみましょう。';
      });
      b.addEventListener('dblclick', () => open(i));
      b.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') open(i);
      });
    }
    return b;
  });
  return {
    el: h('div', { class: 'mock' }, h('div', { class: 'mock-titlebar' }, 'ホーム'), h('div', { class: 'open-items' }, items), status),
    getResponse: () => null,
    autoSubmit: true,
  };
}
