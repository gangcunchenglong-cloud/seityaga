// node --test browser-extension/tests/
"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const { normalize, linkText } = require("../src/shared/linkfilter.js");

test("http/https のみ許可し、フラグメントを除去する", () => {
  assert.equal(normalize("https://example.com/a#x"), "https://example.com/a");
  assert.equal(normalize("http://example.com/"), "http://example.com/");
});

test("危険・対象外スキームを拒否する", () => {
  for (const href of [
    "javascript:alert(1)",
    "data:text/html,hi",
    "file:///C:/x",
    "chrome://settings",
    "chrome-extension://abc/x.html",
    "mailto:a@example.com",
    "",
    "not a url",
  ]) {
    assert.equal(normalize(href), null, href);
  }
});

test("資格情報付きURLを拒否する", () => {
  assert.equal(normalize("https://user:pass@example.com/"), null);
});

test("長すぎるURLを拒否する", () => {
  assert.equal(normalize("https://example.com/" + "a".repeat(5000)), null);
});

test("同一ページ内アンカーを除外する", () => {
  assert.equal(normalize("https://example.com/page#section", "https://example.com/page"), null);
  assert.equal(normalize("https://example.com/other", "https://example.com/page"), "https://example.com/other");
});

test("リンク文字列を短く整形する", () => {
  const a = { innerText: "  長い\n  リンク  ", getAttribute: () => null };
  assert.equal(linkText(a), "長い リンク");
  const long = { innerText: "x".repeat(500), getAttribute: () => null };
  assert.equal(linkText(long).length, 200);
});
