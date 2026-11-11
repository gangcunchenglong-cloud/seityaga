/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import { viteSingleFile } from 'vite-plugin-singlefile';

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
    plugins: [viteSingleFile({ removeViteModuleLoader: true })],
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
