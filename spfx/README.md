# Hello Copilot SPFx Web Part

Pinned compatibility:
- SPFx: 1.23.2
- Node.js: 22.x
- React: 17.0.1
- TypeScript: 5.8.3

The exact generated scaffold was partially blocked by the environment approval layer. Compatibility is pinned and the files follow the current SPFx Heft layout.

```powershell
nvm install 22
nvm use 22
npm install
npm run build
npm run start
npm run build:prod
npm run package
```

The local development and production API/app-registration values are configured in the API and web-part manifest. Before deployment, set the real SharePoint tenant origin and API URL, and approve `access_as_user` under SharePoint Admin Center > Advanced > API access.

## Teams publishing

`teams/<web-part-id>_color.png` and `teams/<web-part-id>_outline.png` are required by SharePoint when synchronizing the solution package to Teams. Keep both files under `teams/` and rebuild with:

Stop any running `npm run start` / `heft start` watcher before building a release; it shares the output folders and can introduce development assets during packaging.

```powershell
npm run clean
npm run build:prod
npm run package
```

Upload `sharepoint/solution/hello-copilot.sppkg` to the tenant app catalog, deploy it, and use **Sync to Teams** from the app catalog. Tenant settings must allow custom Teams app deployment/sync.

## UI

The web part is a Fluent 2 chat surface laid out like Copilot in Teams, built with `@fluentui/react-components` v9 and `@fluentui/react-icons`:

- A collapsible sidebar with **New chat** and the chat history. Below 900px of available width it uses a Fluent modal drawer with contained keyboard focus. Escape, the close button, and the backdrop dismiss it and restore focus to the sidebar opener.
- A centered Fluent Title 3 greeting and a pill-shaped composer with a 56px minimum height and higher-contrast placeholder. In a conversation, the composer docks at the bottom of a centered reading column and grows for multiline drafts.
- Streamed replies with stop, copy, and regenerate actions. Enter sends and Shift+Enter adds a new line. Copy failures show recovery instructions; successful copies are announced to assistive technology.
- Waiting states: "Thinking…" until the first event, then an Activity section lists the progress steps the API reports (open while waiting, collapsed to "Worked through N steps" afterwards), with "Still working…" after 8 seconds. Phase changes are announced politely, not per chunk. Stopped or failed replies keep their partial text and offer Try again. If a reply is generating in another chat, a notice offers Go to chat or Stop, and the sidebar shows a spinner on that chat.
- Stream events are SSE `data:` JSON: `{ "text": "…" }` appends answer content, `{ "activity": "…" }` adds a short progress step (summaries only, not raw reasoning), `{ "error": "…" }` fails the reply, and `[DONE]` ends it. Plain `{ text }` streams keep working.
- The send/stop button is a 40px circle aligned to the composer's single-line centre and to the bottom edge when the draft grows.
- Assistant replies render Markdown headings, paragraphs, emphasis, lists, quotes, links, and code. Code blocks scroll independently and are keyboard-focusable. Raw HTML is skipped and images are shown as alt text without loading remote content. Only absolute HTTP(S), `mailto:`, and `tel:` links are clickable; links open separately with `noopener noreferrer`. Copy preserves the original response, including Markdown.
- Deleting a chat requires confirmation, with initial focus on **Cancel**. Cancellation preserves the chat and returns focus to its delete button; confirmed deletion moves focus to **New chat**.
- Chat history is stored in the browser's `localStorage`, per web part instance, up to 50 chats. It is not synced across devices. The API still receives one message per request.

In Teams the web part fills the tab without a frame. On SharePoint pages it renders as a bordered panel with a fixed, viewport-relative height. It uses Fluent design tokens, so it picks up the SharePoint site theme (`ThemeProvider.tryGetThemeV2()`) or the Teams light, dark, or high-contrast theme.
