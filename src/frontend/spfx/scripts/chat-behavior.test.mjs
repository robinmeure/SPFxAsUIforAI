import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';

const compile = path => ts.transpileModule(readFileSync(new URL(path, import.meta.url), 'utf8'),
  { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2020 } }).outputText;
const moduleUrl = code => `data:text/javascript;base64,${Buffer.from(code).toString('base64')}`;
const endpointsUrl = moduleUrl(compile('../src/common/apiEndpoints.ts'));
const { ChatApi } = await import(moduleUrl(compile('../src/webparts/helloCopilot/components/api.ts')
  .replace('../../../common/apiEndpoints', endpointsUrl)));
const { ChatListState } = await import(moduleUrl(compile('../src/webparts/helloCopilot/components/chatListState.ts')));
const { applyApiSettings, saveApiSettings } = await import(moduleUrl(compile('../src/common/apiSettings.ts')));
const context = { aadTokenProviderFactory: { getTokenProvider: async () => ({ getToken: async () => 'test-token' }) } };
const chat = (id, text = '') => ({ id, title: id, messages: [text] });
const metadata = (local, remote) => ({ ...local, title: remote.title });

test('a late initial list preserves a promoted conversation and subsequent stream updates', () => {
  const state = new ChatListState();
  const initial = state.begin();
  state.touch('local-draft');
  let chats = [chat('local-draft', 'first chunk')];
  state.promote('local-draft', 'stored-id');
  chats = chats.map(c => ({ ...c, id: 'stored-id' }));
  chats = state.merge(initial, chats, [], metadata);
  state.touch('stored-id');
  chats = chats.map(c => ({ ...c, messages: [...c.messages, 'second chunk'] }));
  assert.equal(chats[0].id, 'stored-id');
  assert.deepEqual(chats[0].messages, ['first chunk', 'second chunk']);
});

test('stale snapshots cannot restore deleted chats or override attachment changes', () => {
  const state = new ChatListState();
  const request = state.begin();
  const current = [{ ...chat('keep'), documentIds: ['new'] }];
  state.remove('deleted');
  state.touch('keep');
  const result = state.merge(request, current, [
    chat('deleted'), { ...chat('keep'), documentIds: ['old'] }
  ], metadata);
  assert.equal(result.length, 1);
  assert.deepEqual(result[0].documentIds, ['new']);
  const later = state.begin();
  assert.deepEqual(state.merge(later, result, [], metadata), []);
});

test('only the current list owns state and invalidation ignores old configuration responses', () => {
  const state = new ChatListState();
  const older = state.begin();
  const newer = state.begin();
  const current = [chat('current', 'loaded history')];
  assert.equal(state.current(older), false);
  assert.equal(state.merge(older, current, [chat('obsolete')], metadata), current);
  assert.deepEqual(state.merge(newer, current, [chat('current')], metadata)[0].messages, ['loaded history']);
  state.invalidate();
  assert.equal(state.current(newer), false);
  assert.equal(state.merge(newer, current, [], metadata), current);
});

test('stream destinations remain exact and companion endpoints retain their prefix', async t => {
  const originalFetch = globalThis.fetch;
  t.after(() => { globalThis.fetch = originalFetch; });
  const requests = [];
  globalThis.fetch = async (url, init) => {
    requests.push({ url, init });
    return new Response(init?.headers?.Accept === 'text/event-stream' ? 'data: [DONE]\n\n' : '[]');
  };
  for (const [stream, base, expectedRoot] of [
    ['https://host/api/messages/stream', undefined, 'https://host'],
    ['https://host/chat/api/messages/stream?version=1', undefined, 'https://host/chat'],
    ['https://host/chat/api/messages/stream/', undefined, 'https://host/chat'],
    ['https://host/custom/stream/?version=1', 'https://host/custom-root/', 'https://host/custom-root']
  ]) {
    const api = new ChatApi(context, stream, 'api://test', base);
    await api.streamReply('Hello', undefined, new AbortController().signal, () => {});
    await api.listChats();
    assert.equal(requests.at(-2).url, stream);
    assert.equal(requests.at(-1).url, `${expectedRoot}/api/chats`);
    assert.equal(requests.at(-2).init.headers.Authorization, 'Bearer test-token');
    assert.equal(requests.at(-1).init.headers.Authorization, 'Bearer test-token');
  }
});

test('stream-only mode never guesses companion endpoints and invalid configuration fails explicitly', async t => {
  const originalFetch = globalThis.fetch;
  t.after(() => { globalThis.fetch = originalFetch; });
  const requests = [];
  globalThis.fetch = async url => { requests.push(url); return new Response('data: [DONE]\n\n'); };
  const api = new ChatApi(context, 'https://host/custom?mode=chat', 'api://test');
  assert.equal(api.hasCompanions, false);
  await api.streamReply('Hi', undefined, new AbortController().signal, () => {});
  await assert.rejects(api.listChats(), /companion base URL/);
  await assert.rejects(api.listDocuments(), /companion base URL/);
  await assert.rejects(api.listSkills(), /companion base URL/);
  assert.deepEqual(requests, ['https://host/custom?mode=chat']);
  for (const [stream, base] of [
    ['javascript:alert(1)', undefined],
    ['https://host/custom', 'https://other-host'],
    ['https://host/custom', 'https://host/base?secret=value'],
    ['https://user:password@host/custom', undefined]
  ]) {
    const invalid = new ChatApi(context, stream, 'api://test', base);
    assert.ok(invalid.configurationError);
    await assert.rejects(invalid.streamReply('Hi', undefined, new AbortController().signal, () => {}));
  }
  assert.equal(requests.length, 1);
});

test('old saved settings still load and an optional companion root round-trips', t => {
  const originalWindow = globalThis.window;
  t.after(() => { globalThis.window = originalWindow; });
  let stored;
  globalThis.window = { localStorage: {
    getItem: () => stored, setItem: (_, value) => { stored = value; }, removeItem: () => { stored = undefined; }
  } };
  const old = { apiUrl: 'https://host/api/messages/stream', apiResource: 'api://test', apiScope: 'scope' };
  saveApiSettings(old);
  assert.deepEqual(applyApiSettings(old), old);
  saveApiSettings({ ...old, companionBaseUrl: 'https://host/base' });
  assert.equal(applyApiSettings(old).companionBaseUrl, 'https://host/base');
  saveApiSettings({});
  assert.deepEqual(applyApiSettings(old), old);
});

test('suggested questions and reused evidence survive fragmented SSE and history reload', async t => {
  const originalFetch = globalThis.fetch;
  t.after(() => { globalThis.fetch = originalFetch; });
  const suggestions = ['Compare the exceptions.', 'Summarize this as a checklist.'];
  const retrieval = { query: 'Explain that.', scope: 'reuse', method: 'reuse', documentIds: ['doc1'], passageCount: 1 };
  const message = { id: 'answer', role: 'assistant', content: 'Answer [1]', followUpQuestions: suggestions, retrieval };
  const stream = `data: ${JSON.stringify({ text: 'Answer [1]' })}\n\ndata: ${JSON.stringify({ followUpQuestions: suggestions, retrieval })}\n\ndata: [DONE]\n\n`;
  globalThis.fetch = async (_, init) => init?.headers?.Accept === 'text/event-stream'
    ? new Response(new ReadableStream({
      start(controller) {
        const bytes = new TextEncoder().encode(stream);
        for (let i = 0; i < bytes.length; i += 7) controller.enqueue(bytes.slice(i, i + 7));
        controller.close();
      }
    }))
    : new Response(JSON.stringify({ id: 'chat', messages: [message] }));
  const api = new ChatApi(context, 'https://host/api/messages/stream', 'api://test');
  const events = [];
  await api.streamReply('Explain that.', 'chat', new AbortController().signal, event => events.push(event));
  assert.deepEqual(events[1].followUpQuestions, suggestions);
  assert.equal(events[1].retrieval.method, 'reuse');
  assert.deepEqual((await api.getChat('chat')).messages[0], message);
});
