import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const html = readFileSync(new URL('../../index.html', import.meta.url), 'utf8');
const anchors = [...html.matchAll(/<a\b[^>]*href="([^"]+)"[^>]*>/gi)].map(match => match[1]);
const ids = [...html.matchAll(/\bid="([^"]+)"/g)].map(match => match[1]);

assert.ok(html.includes('name="viewport"'), 'mobile viewport is declared');
assert.ok(html.includes('rel="noopener"') || html.includes('target="_blank"') === false, 'external links are not opened unsafely');
assert.ok(anchors.includes('https://github.com/doorsfan14/WinOptimizer'), 'source link targets the supplied repository');
assert.ok(anchors.includes('https://github.com/doorsfan14/WinOptimizer/releases'), 'release link targets the real releases page');
assert.ok(html.includes('No installer has been released yet'), 'the site does not invent a download asset');
assert.ok(html.includes('Illustrative interface preview'), 'decorative dashboard values are clearly identified as illustrative');
for (const href of anchors.filter(value => value.startsWith('#') && value.length > 1)) {
  assert.ok(ids.includes(href.slice(1)), `in-page link ${href} has a target`);
}

console.log(`Website checks passed (${anchors.length} links, ${ids.length} anchor targets).`);
