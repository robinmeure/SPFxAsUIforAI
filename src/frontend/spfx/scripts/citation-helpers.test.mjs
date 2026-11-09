import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';

const source = readFileSync(new URL('../src/webparts/helloCopilot/components/citationHelpers.ts', import.meta.url), 'utf8');
const { outputText } = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext } });
const { citationLinks, safeSourceUrl } = await import(`data:text/javascript;base64,${Buffer.from(outputText).toString('base64')}`);

test('citation markers inside prose become individual evidence links without changing text', () => {
  const tree = { type: 'root', children: [{ type: 'paragraph', children: [
    { type: 'text', value: 'First [1], second [2].' },
    { type: 'strong', children: [{ type: 'text', value: 'Repeated [1]' }] }
  ] }] };
  citationLinks()(tree);
  const children = tree.children[0].children;
  assert.deepEqual(children.filter(node => node.type === 'link').map(node => node.url), ['#citation-1', '#citation-2']);
  assert.equal(children.at(-1).children.find(node => node.type === 'link').url, '#citation-1');
  const text = node => node.value ?? (node.children ?? []).map(text).join('');
  assert.equal(text(tree), 'First [1], second [2].Repeated [1]');
});

test('code samples, images and existing links are not rewritten as citations', () => {
  const tree = { type: 'root', children: [
    { type: 'code', value: 'array[1]' },
    { type: 'inlineCode', value: '[2]' },
    { type: 'link', url: 'https://example.org', children: [{ type: 'text', value: '[3]' }] },
    { type: 'linkReference', children: [{ type: 'text', value: '[4]' }] },
    { type: 'image', url: 'https://example.org/image', value: '[5]' }
  ] };
  const before = structuredClone(tree);
  citationLinks()(tree);
  assert.deepEqual(tree, before);
});

test('source URLs cannot execute script or navigate to local/relative resources', () => {
  for (const url of ['javascript:alert(1)', 'data:text/html,test', 'file:///secret', '/relative', 'http://example.org', 'not a url']) {
    assert.equal(safeSourceUrl(url), undefined);
  }
  assert.equal(safeSourceUrl('https://example.sharepoint.com/a%20b.docx'), 'https://example.sharepoint.com/a%20b.docx');
});
