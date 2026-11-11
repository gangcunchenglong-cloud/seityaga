/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import { viteSingleFile } from 'vite-plugin-singlefile';
import type { Plugin } from 'vite';

/**
 * ビルドしたHTMLに Content-Security-Policy を入れて、
 * 外部への通信（fetch・外部スクリプト・外部画像など）をブラウザ側でも禁止する。
 * 開発サーバーでは自動再読み込みの通信が必要なので入れない。
 */
function noNetworkCsp(): Plugin {
  const csp = [
    "default-src 'none'",
    "script-src 'unsafe-inline'",
    "style-src 'unsafe-inline'",
    'img-src data: blob:',
    "connect-src 'none'",
    "form-action 'none'",
    "base-uri 'none'",
  ].join('; ');
  return {
    name: 'no-network-csp',
    apply: 'build',
    transformIndexHtml: (html) =>
      html.replace('<head>', `<head>\n    <meta http-equiv="Content-Security-Policy" content="${csp}" />`),
  };
}

/**
 * ビルドは「1ページ = 1つのHTMLファイル」にまとめます（JS・CSS・問題データを埋め込み）。
 * こうしておくと、GitHub Pages だけでなく、学校のファイル共有から
 * HTML をダブルクリックして file:// で開いても動きます。
 *
 *   vite build                 → dist/index.html   （受験アプリ）
 *   vite build --mode teacher  → dist/teacher.html （先生用ツール）
 *   vite build --mode review   → dist-tools/scripts/review.html（問題レビュー用・開発者向け）
 */
export default defineConfig(({ mode }) => {
  const page =
    mode === 'teacher' ? 'teacher.html' : mode === 'review' ? 'scripts/review.html' : 'index.html';
  return {
    base: './',
    plugins: [noNetworkCsp(), viteSingleFile({ removeViteModuleLoader: true })],
    build: {
      outDir: mode === 'review' ? 'dist-tools' : 'dist',
      // index を先にビルドし、teacher は同じ dist に追加する
      emptyOutDir: mode !== 'teacher',
      target: 'es2019',
      rollupOptions: { input: page },
    },
    test: {
      include: ['tests/**/*.test.ts'],
    },
  };
});
