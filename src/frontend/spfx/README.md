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

## Endpoint configuration and validation

`apiUrl` is the exact streaming destination: its path, trailing slash and query
are preserved. Conventional URLs ending in `/api/messages/stream` derive the
companion endpoint root automatically, including APIM prefixes such as `/chat`.
For a custom stream path, set optional `companionBaseUrl` in the property pane or
personal-app Settings tab (for example `https://host/chat`). It must use the same
origin and Entra audience as the stream and must not contain a query string.
Companion paths such as `/api/chats` and `/api/documents` are appended to that root.

Without a derivable/configured companion root, the UI explicitly uses streaming-only
mode: conversations stay in the current tab, and Library, attachments, skills and
saved-chat requests are disabled. It never guesses an origin-root endpoint.
Invalid URL configuration is shown as an error and blocks requests.
Changing endpoint/resource settings resets the view and aborts its active stream.

Chat-list refreshes reconcile against local mutation revisions, so a delayed list
cannot remove a newly promoted server chat, restore a deleted chat, or overwrite
changes made while it was loading. Loaded/streamed messages are retained.

Run `npm test` for endpoint, settings, reconciliation and citation regression tests,
and `npm run build` for TypeScript/webpack validation.

## Teams publishing

The repo provides its own Teams app manifest in `teams/manifest.json`: a personal app with a **Chat** tab and a **Settings** tab, plus a configurable channel/group-chat tab that uses the property pane. `npm run package` first runs `npm run package:teams`, which zips the manifest and the two icons (`teams/<web-part-id>_color.png` and `_outline.png`) into `teams/TeamsSPFxApp.zip`; SharePoint deploys that package on **Sync to Teams**. Bump the manifest `version` whenever you change it. See section 6.4 of the root README.

The **Settings** tab is the `chatSettings` web part (`supportedHosts: []`, so it only appears there). It saves the API URL, resource and scope to `localStorage` (per user and device), and the chat web part applies them only when it runs in the personal app.

Stop any running `npm run start` / `heft start` watcher (including the Aspire AppHost) before building a release; it shares the output folders and can introduce development assets during packaging.

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
- Optional `followUpQuestions` events contain up to three suggested next messages. The latest completed answer shows
  clickable questions; selecting one sends it in the current conversation and preserves any unsent composer draft.
  Questions are hidden on partial/failed replies, disabled while attachments are being saved or sending is blocked,
  and restored when reopening a saved chat. Older servers without this field remain compatible.
- `citations` and `retrieval` events provide structured passage evidence and the executed query/scope. Inline citation buttons reveal an excerpt, section/page when available, Local/SPO origin and a safe HTTPS source link. Code samples are not converted into citation links. Unknown reference numbers are marked unverified.
- Retrieval `method` distinguishes new searches, reused validated passages, and no retrieval. Reuse is labeled as reused
  evidence, not a new search; no-retrieval replies have no search-details panel. Chats without attachments go straight
  to answering without document-search progress steps.
- **Library.** The sidebar (Copilot style) has **New chat** and **Library** at the top, then the chats under **Recent**; the active item is highlighted. The Library is a Fluent table of all documents: file-type icon and name (linked for SharePoint files), Local/SPO badge, status, size, date added and **Used in** (the chats that include it). Columns adapt to the space beside the sidebar: Size and Added are hidden below 1100px, and below 760px Status and Used in are hidden too, status moves under the name, and Upload/From SharePoint become icon buttons. Select rows (or all) to **Attach to chat** (New chat, or an existing chat; chats that already have every selected file are disabled), **Delete** (with confirmation), or clear the selection. Failed documents can be deleted but not attached. Uploads started in the Library only add to the library.
- **One conversation per document set.** A new chat shows only the greeting and the message box. Files attached with the paperclip, or attached from the Library with **New chat**, appear as removable chips inside the message box until the first message is sent. If that set already belongs to a conversation, an info bar offers **Open it** and Send stays disabled. The chat list under **Recent** shows only conversation titles; a conversation's documents appear in its header pill and in the Library's **Used in** column. In a conversation, the header pill ("2 documents", or "Add documents" when empty) opens a popover listing each file with origin and status, a remove button and **Add from Library**, which opens the Library with that chat as the target ("Add N to chat"). Changes appear in the thread as a dated divider. A change that would duplicate another conversation's set is rejected with an **Open it** action. At most 20 documents per conversation; editing is locked while saving or generating.
- **Skills.** The wand button next to the paperclip opens a Skills menu (name plus a two-line description). Typing `/` in an empty message box opens the same menu. The chosen skill becomes a removable chip in the message box and changes the placeholder; it applies to the next message only and is shown above that message in the thread. Regenerate reuses the message's skill. Without skills from `GET /api/skills`, the button is hidden.
- The send/stop button is a 40px circle aligned to the composer's single-line centre and to the bottom edge when the draft grows.
- Assistant replies render Markdown headings, paragraphs, emphasis, lists, quotes, links, and code. Code blocks scroll independently and are keyboard-focusable. Raw HTML is skipped and images are shown as alt text without loading remote content. Only absolute HTTP(S), `mailto:`, and `tel:` links are clickable; links open separately with `noopener noreferrer`. Copy preserves the original response, including Markdown.
- Deleting a chat requires confirmation, with initial focus on **Cancel**. Cancellation preserves the chat and returns focus to its delete button; confirmed deletion moves focus to **New chat**.
- Chat history, citation evidence, document sets and change events are stored by the API (Cosmos DB when configured). A new chat's document choice stays local until the first message is sent. Removing a document from a conversation doesn't remove historical quoted evidence.

In Teams the web part fills the tab without a frame. On SharePoint pages it renders as a bordered panel with a fixed, viewport-relative height. It uses Fluent design tokens, so it picks up the SharePoint site theme (`ThemeProvider.tryGetThemeV2()`) or the Teams light, dark, or high-contrast theme.
