# Hello Copilot — SharePoint and Teams as the front end for your own RAG/AI applications

## Why this repo exists

Organizations build their own AI applications: RAG over company documents, agents over line-of-business data, domain-specific assistants. They then have to answer a second question: **where do users talk to them?** A standalone web app means another URL to find, another sign-in, another host to secure and operate, and a UI that looks nothing like the tools people already use.

This repo shows the alternative. **A SharePoint Framework (SPFx) web part becomes the chat interface, and SharePoint Online and Microsoft Teams become the front end**, while your RAG/AI logic stays in your own API. One package gives you:

- **A chat on SharePoint pages, as a Teams tab and as a Teams personal app**, all from the same `.sppkg`.
- **A Copilot-like experience** that users already recognize: streaming answers, progress steps while the backend retrieves and reasons, stop and retry, chat history, and Markdown answers.
- **Your backend, your choice**: the web part calls any HTTPS endpoint that streams Server-Sent Events. The [API](./src/backend/api) includes Azure AI Search retrieval and Foundry generation through the Responses API, plus a selectable mock. There is no bot runtime and no Copilot license dependency in the front end.

### How Fluent UI helps

- **It looks native.** The UI is built with [Fluent UI v9](https://react.fluentui.dev/), the React implementation of the [Fluent 2 design system](https://fluent2.microsoft.design/) used by Microsoft 365. Users see the same type, spacing, controls and motion as in Teams and SharePoint, so the AI app feels like part of the workplace, not a bolt-on.
- **It follows the host's theme.** SharePoint site themes and the Teams light, dark and high-contrast themes are mapped to Fluent tokens at runtime (section 4). There is no hard-coded colour to maintain.
- **It handles the hard UI parts.** Accessible components (focus management, keyboard support, screen-reader labels), responsive layout and dialogs come from the library, so the repo's code is mostly about the chat experience itself.

### How SPFx deployment helps

- **No front end to host.** The web part ships as a `.sppkg` to the tenant app catalog, and SharePoint serves its assets. You only host the API.
- **Single sign-on without a client app registration.** The web part gets a token for your API from SPFx (`AadTokenProvider`). Users are already signed in to Microsoft 365; you register only the API (section 5).
- **Governed by existing admin controls.** IT approves the API permission once in the SharePoint admin center and manages Teams availability in the Teams admin center (section 8). Rollout and rollback use the tools they already know.
- **One build, two front ends.** "Add to Teams" turns the same package into a Teams tab and personal app. Updates are a version bump and a re-upload.

The rest of this README is the runbook: build, theme, register, deploy and verify.

| Folder | Contents |
|---|---|
| [src/frontend/spfx/](./src/frontend/spfx) | The web part (SPFx 1.23.2, Heft, React 17, Fluent UI v9) |
| [src/backend/api/](./src/backend/api) | .NET 10 controllers, Azure AI Search + Foundry RAG, selectable mock, SSE streaming, CORS, Microsoft.Identity.Web |
| [docs/research/](./docs/research/spfx-teams-chat-webpart.md) | Sourced research behind these choices, including open questions. See also [Teams iframe tab vs. SPFx](./docs/research/teams-iframe-tab-vs-spfx.md) for the manifest-pointed tab alternative and Copilot reach, and [private backend access](./docs/research/private-backend-teams-sharepoint.md) for networking options from Teams and SharePoint |

## Contents

1. [Architecture](#1-architecture)
2. [Prerequisites](#2-prerequisites)
3. [Build and run locally](#3-build-and-run-locally)
4. [UX, UI and theming (SharePoint, Teams, Copilot look)](#4-ux-ui-and-theming)
5. [Create the Entra app registration](#5-create-the-entra-app-registration)
6. [Register the app registration in the manifests](#6-register-the-app-registration-in-the-manifests)
7. [Configure and deploy the API](#7-configure-and-deploy-the-api)
8. [Package and deploy the web part](#8-package-and-deploy-the-web-part)
9. [Verify](#9-verify)
10. [Troubleshooting](#10-troubleshooting)
11. [Known limits](#11-known-limits)
12. [Other hosts: Outlook, Microsoft 365 Copilot, Copilot chat and iframe tabs](#12-other-hosts-outlook-microsoft-365-copilot-copilot-chat-and-iframe-tabs)

---

## 1. Architecture

```
SharePoint page / Teams tab / Teams personal app
  └─ SPFx web part (React 17 + Fluent UI v9)
       1. context.aadTokenProviderFactory.getTokenProvider().getToken(<Application ID URI>)
       2. fetch(apiUrl, { Authorization: Bearer <token> })   ← native fetch, to read the SSE stream
              │
              ▼
       Azure API Management /chat  ── CORS, validates the JWT and scope, rate limit per user, unbuffered SSE
              │                        (forwards the same token)
              ▼
       Web API (.NET 10, Microsoft.Identity.Web)  ── validates the JWT again, scope access_as_user
              │
              ├─ Retrieval: structured intent → application-owned hybrid search → numbered passage evidence
              ├─ Agent loop (Foundry Responses API): list_my_documents, load_skill, read_skill_resource, create_document
              ├─ Azure AI Search: index my-documents; filtered by owner AND thread attachments, optionally section/page
              ├─ Ingestion worker: Document Intelligence → Markdown chunks → embeddings → Search
              ├─ Cosmos DB (serverless): chat history and the user's document registry
              └─ Microsoft Graph (on-behalf-of): download picked SharePoint files, save generated files
```

The web part has three user-facing features on top of chat:

- **Library and one conversation per document set, with citations.** The sidebar has **New chat** and **Library** at the top and your chats under **Recent**. The Library lists all of your documents (file type, Local/SPO origin, status, size, date added, the chats using them). Upload files or pick them from SharePoint (File Picker v8), delete them, or select several and **Attach to chat** (a new chat or an existing one). In a chat, the message box's paperclip uploads and attaches files directly. A conversation has at most 20 documents; without documents it's a general chat. Each set has exactly one conversation: choosing a set that's already in use offers to open that conversation instead. The chat list shows conversation titles only. In a conversation, a header pill ("3 documents") lists the set with origin and status, removes files, and opens the Library to add more. The thread marks each change with a divider. Only the conversation's ready documents are searched. Click inline citations to inspect the supporting passage, section/page when available, and source link.
- **Skills.** Instruction packages in [src/backend/api/skills](./src/backend/api/skills) (`SKILL.md` with front matter, plus optional reference files). Pick one for a message with the wand button in the message box, or type `/` in an empty message box; the chosen skill shows as a chip and its instructions are applied to that message. Without a choice, the model sees the skill catalog and loads a skill when the request matches. The first skill, `extract-and-generate`, pulls data from documents and saves a DOCX, XLSX or PDF to the user's OneDrive (or a configured drive). See [API skills](./src/backend/api/README.md#skills).
- **Server-side history.** Chats are stored per user in Cosmos DB, so history follows the user across SharePoint, Teams and devices.

Key decisions:

- **Auth: SPFx's own token provider, not MSAL.** Microsoft states that using MSAL.js directly in SPFx is not supported. The web part gets a delegated token for your API from SPFx. Delegated permissions are granted to the tenant-wide *SharePoint Online Client Extensibility Web Application Principal*, so **only the API needs an app registration**. There is no separate client registration.
- **Why native `fetch` and not `AadHttpClient`.** `AadHttpClient` is the documented way to call an Entra API, but its docs don't say whether the response body is streamable. This project takes the token from `AadTokenProvider` and calls `fetch()` itself so it can read `text/event-stream`. Confirm the method signature against the installed `@microsoft/sp-http-base` typings if you change it.
- **History lives on the server.** The web part sends the new message plus a `chatId`. The API loads the earlier turns from Cosmos DB, so the client can't rewrite history and every host sees the same chats. Without `Cosmos:Endpoint` (or in mock mode) the API keeps chats in memory until restart.
- **Application-owned retrieval, agentic actions.** A structured intent step resolves the question to the thread, named documents, or a section/page. Code validates the scope and runs hybrid Search before answer generation; the answer model cannot skip search or widen it. Even whole-document questions use top matching passages, not an exhaustive read. Skills and file creation remain agent tools. Iterations are capped (`Rag:MaxAgentIterations`, default 8).

**Existing documents/chats:** restart the API to add the new index fields, then re-add documents to populate section/page metadata. Older chunks still support document-wide search, but location filters require re-ingestion. Older chats have no documents (general chats) until you add some from the header pill. See [retrieval details](./src/backend/api/README.md#application-owned-retrieval-and-citations).

## 2. Prerequisites

| Need | Version / role |
|---|---|
| Node.js | **22.x** (SPFx 1.23 supports Node 22 only). Install with `nvm install 22 && nvm use 22` (nvm-windows) or from <https://nodejs.org> |
| npm | 11+ |
| .NET SDK | 10 (<https://dotnet.microsoft.com/download>) |
| Aspire CLI | 13.6+, for section 3.4 (`dotnet tool install -g Aspire.Cli`, or see <https://aspire.dev>) |
| Azure CLI | Section 7: Bicep deployment (`az login`); Bicep CLI via `az bicep install` |
| Dev tunnels CLI | Section 7.2: exposes the local API to APIM (`winget install Microsoft.devtunnel`) |
| SPFx / React / TypeScript | 1.23.2 / **17.0.1 (exact)** / 5.8.3 |
| SharePoint Online | A tenant with a **tenant app catalog**. Heft-era SPFx is SharePoint Online only. |
| Entra roles | Create the app registration (Application Developer or higher); approve API permissions (Application Administrator is enough for your own API) |
| Teams | Custom app upload/sync allowed in the Teams admin center |

SPFx pins React exactly. After any dependency change, check that `react` and `react-dom` are still `17.0.1`. The repo has no lockfile and uses caret ranges for Fluent UI and `react-markdown`, so installs are not byte-reproducible. Commit a lockfile and use `npm ci` if you need that.

## 3. Build and run locally

### 3.1 Web part

```powershell
cd src/frontend/spfx
npm install --package-lock=false      # the repo has no lockfile
npm run build                         # heft build (dev)
npm run start                         # heft start: serves https://localhost:4321
```

Scripts: `build`, `build:prod`, `start`, `package`, `clean`, `devbuild` (all Heft; the old `gulp` commands don't apply). This guide uses the first five.

Trust the SPFx dev certificate once: `npx heft trust-dev-cert`.

The hosted workbench is deprecated and retires on **Dec 1, 2026**. Debug on a real page instead: open any modern page on your tenant and add the query string

```
?debug=true&noredir=true&debugManifestsFile=https%3A%2F%2Flocalhost%3A4321%2Ftemp%2Fbuild%2Fmanifests.js
```

Then add **Hello Copilot** from the web part picker.

### 3.2 API

```powershell
cd src/backend/api
dotnet dev-certs https --trust
dotnet run --launch-profile https     # https://localhost:7198
```

Development defaults to real authentication and real RAG responses. Run `az login` and grant the backend identity access to Search and Foundry as described in the [API setup](./src/backend/api/README.md). To use the resources from [infra/main.bicep](./infra/main.bicep), and to call the API through APIM, see section 7.2. For local UI-only work, set both `UseMockAuthentication=true` and `UseMockResponses=true` before starting the API. Never enable mock authentication in hosting configuration.

Both switches can be set directly as **top-level properties** in
[appsettings.Development.json](./src/backend/api/appsettings.Development.json) or
[appsettings.json](./src/backend/api/appsettings.json):

```json
{
  "UseMockAuthentication": false,
  "UseMockResponses": true
}
```

Merge these properties into the existing file. This example tests real user
authentication with canned responses, without calling Search or Foundry.

| `UseMockAuthentication` | `UseMockResponses` | Mode |
|---|---|---|
| `false` | `false` | Authenticated RAG (checked-in default) |
| `false` | `true` | Authenticated canned responses |
| `true` | `false` | Anonymous RAG; local testing only |
| `true` | `true` | Anonymous canned responses; local UI testing only |

In Development, the Development file overrides the base file; environment
variables override both, and command-line arguments override environment variables.
Restart the API after changing these switches or authentication settings.
See the [API configuration and auth checklist](./src/backend/api/README.md#authentication)
for the exact current tenant settings, Azure RBAC requirements and troubleshooting.

**Test real auth locally** (do this before deploying to find Entra problems early). After section 5, keep mock authentication off, select canned responses to isolate user auth, and use the web part from a tenant page via the debug query string:

```powershell
$env:UseMockAuthentication = "false"
$env:UseMockResponses = "true"
$env:AzureAd__TenantId = "<tenant-id>"; $env:AzureAd__ClientId = "<client-id>"; $env:AzureAd__Audience = "<client-id>"
dotnet run --launch-profile https
```

Expect 401 without a token, 403 with a token that lacks `access_as_user`, and 200 with a valid one. For v2 access tokens the API audience is the **bare client ID**; the frontend still requests the `api://<client-id>` resource. Once auth works, set `UseMockResponses=false` and restart to test real RAG. Remove temporary environment overrides when returning to JSON configuration.

Smoke-test the SSE stream (only when `UseMockAuthentication=true` locally;
otherwise use the authenticated web part or supply an access-token header):

```powershell
'{"message":"hi"}' | Set-Content $env:TEMP\body.json
curl.exe -N -H "Content-Type: application/json" --data-binary "@$env:TEMP\body.json" https://localhost:7198/api/messages/stream
```

### 3.3 Stream contract

`POST /api/messages/stream` with body `{ "message": "...", "chatId": "<guid>" }` returns `text/event-stream`. Omit `chatId` to start a new chat; an unknown `chatId` returns 404. Each event is `data: <json>` followed by a blank line:

| Event | Meaning in the UI |
|---|---|
| `{ "chatId": "<guid>" }` | Always the first event: the chat the turn is stored in. The web part swaps its temporary id for this one. |
| `{ "activity": "Searching: …" }` | A short, user-facing progress step shown in the collapsible **Activity** section. Send summaries, not raw model reasoning. |
| `{ "text": "…" }` | Appends answer content (Markdown) |
| `{ "error": "…" }` | Fails the reply; any partial text is kept |
| `[DONE]` | Ends the stream |

Plain `{ text }` streams work without any activity events. The user message is stored before streaming and the assistant reply only when it completes.

Other endpoints (all require the same token and scope; data is always scoped to the caller's `oid`):

| Endpoint | Purpose |
|---|---|
| `GET /api/chats` | The caller's chats, newest first |
| `GET /api/chats/{id}` / `DELETE /api/chats/{id}` | One chat with its messages / delete it (204, or 404 if unknown) |
| `GET /api/skills` | Skills a user can pick for a message (`name`, `description`) |
| `POST /api/chats/{id}/documents` | `{ "documentIds": [...] }` replaces the conversation's document set. 409 when another conversation already uses that exact set (see [one conversation per document set](./src/backend/api/README.md#one-conversation-per-document-set)) |
| `GET /api/documents` | The caller's documents with status `processing`, `ready` or `failed` |
| `POST /api/documents` | Multipart upload (`file`). 202 with the document; 415 unsupported type; 413 over 10 MB; 503 when the ingestion queue is full |
| `POST /api/documents/sharepoint` | `{ "driveId", "itemId" }` from the picker. The API downloads the file through Graph on behalf of the user |
| `DELETE /api/documents/{id}` | Removes the document and its chunks from the index |

Supported file types: `.pdf`, `.docx`, `.pptx`, `.xlsx`, `.txt`, `.md`, `.html`/`.htm`, up to 10 MB.

### 3.4 Run everything with Aspire

[src/apphost](./src/apphost/AppHost.cs) is an Aspire AppHost that starts the whole local loop in one command, with logs, traces and health in the Aspire dashboard:

| Resource | What it runs |
|---|---|
| `api` | The API with the `http` launch profile on `http://localhost:5198` (not proxied, so the port stays fixed); health at `/health` |
| `tunnel` | Hosts the persistent dev tunnel `chatbot-api` (anonymous access) to port 5198: the APIM backend from section 7.1 |
| `spfx` | `npm run start -- --nobrowser` in `src/frontend/spfx` on `https://localhost:4321`; the dashboard links to a SharePoint page with the debug query string |

```powershell
cd src/frontend/spfx; npm install --package-lock=false; cd ../../..   # once; Aspire doesn't install packages
aspire start                     # from the repo root; prints the dashboard URL
aspire describe                  # resource state and URLs
aspire logs api                  # or open the dashboard
aspire stop
```

The API still reads its settings from [appsettings.json](./src/backend/api/appsettings.json) and its user secrets (7.2). Change the tunnel ID and region (`DevTunnel:TunnelId`, `DevTunnel:Region`; keep them in line with `apiBackendUrl`) or the debug page (`SharePoint:DebugPageUrl`) in [the AppHost's appsettings.json](./src/apphost/appsettings.json). Aspire creates the tunnel if it doesn't exist and asks you to sign in to dev tunnels when needed. Leave the region pinned: without it the service can create a second tunnel with the same ID in another cluster, and APIM's backend URL then hits an empty tunnel. The API uses [ServiceDefaults](./src/backend/servicedefaults/Extensions.cs) for OpenTelemetry, health checks and HTTP resilience; telemetry goes to the dashboard only when Aspire sets `OTEL_EXPORTER_OTLP_ENDPOINT`.

## 4. UX, UI and theming

### 4.1 What to use

| Concern | Use | Notes |
|---|---|---|
| Components and tokens | `@fluentui/react-components` (Fluent UI **v9**) | Peer range covers React 17. Not an officially documented SPFx integration path (the SPFx docs still describe v8), so test in both hosts. |
| Styling | Griffel `makeStyles` + design tokens (`tokens.*`, `typographyStyles`) | Theme-dependent colours and typography come from tokens (a few geometry values, such as the 28px pill radius, are literals). Tokens give you SharePoint, Teams, dark and high-contrast for free. |
| Icons | `@fluentui/react-icons` | Regular for UI chrome, Filled for emphasis. |
| Markdown | `react-markdown` ^8.0.7 (React 17 compatible) | `skipHtml`, links limited to http(s)/mailto/tel with `rel="noopener noreferrer"`, images shown as alt text. |
| Theme source | `@microsoft/sp-component-base` `ThemeProvider` and the Teams JS SDK bundled with SPFx | See 4.2 |
| Do **not** use | `@fluentui-copilot/*` | Its README says external use is not supported. This project reproduces the Copilot look with stable v9 parts instead. |

Manifest requirement: `"supportsThemeVariants": true` in the web part manifest.

### 4.2 Theming in SharePoint and Teams

[HelloCopilotWebPart.ts](./src/frontend/spfx/src/webparts/helloCopilot/HelloCopilotWebPart.ts) picks the Fluent theme once and re-renders on change:

1. **In Teams** (`context.sdks.microsoftTeams.teamsJs` exists): read `app.getContext()` → `app.theme`, map `dark` → `teamsDarkTheme`, `contrast` → `teamsHighContrastTheme`, anything else → `teamsLightTheme`, and subscribe with `registerOnThemeChangeHandler`.
2. **Otherwise / fallback**: `ThemeProvider.tryGetThemeV2()` (a Fluent v9 `Theme` built from the site theme), with `webLightTheme` / `webDarkTheme` as a last resort. Subscribe to `themeChangedEvent`.
3. The theme goes to `<FluentProvider theme={…}>`; every style reads tokens.

Never install your own copy of the Teams JS SDK. SPFx supplies it, and custom versions are unsupported.

### 4.3 Copilot-style layout (what makes it feel like Copilot)

- **Surface**: neutral background; brand colour only for the primary action. In Teams the chat fills the tab with no frame. On SharePoint pages it is a bordered panel with a viewport-relative height.
- **Empty state**: a centred greeting (Title 3, semibold) with a pill composer (56px minimum, 28px radius). Once a conversation starts, the composer docks to the bottom of a ≤760px centred column.
- **Messages**: user messages in a bubble; assistant replies as plain, left-aligned Markdown with a small avatar and name, which is the Copilot pattern. No card in a card.
- **Composer**: Enter sends, Shift+Enter adds a line. The Send/Stop button is a fixed 40×40 circle (up-arrow icon for Send, square for Stop), centred on the composer's single line and bottom-aligned as the textarea grows.
- **Sidebar**: New chat plus history. Below **900px** of available width it becomes a modal Fluent `OverlayDrawer` with focus containment and focus restoration. History is per instance in `localStorage`. Delete asks for confirmation.
- **Wait UX**: "Thinking…" until the first event, then the Activity section (open while waiting, collapsed to "Worked through N steps" afterwards) and a "Generating…" indicator. After 8 seconds without text it says "Still working…". Stop is always available. Stopped or failed replies keep their partial text and offer **Try again**. Finished replies offer **Copy** and **Regenerate**. If a reply is running in another chat, a notice offers **Go to chat** or **Stop**.
- **Accessibility**: polite live region for phase changes (not per chunk), keyboard-focusable code blocks, visible focus rings, reduced-motion support, forced-colors support, and layout checked down to 320px.

Fluent 2 references: <https://fluent2.microsoft.design/>.

## 5. Create the Entra app registration

Create **one** registration for the **API**. The SPFx web part itself needs none.

### 5.1 Portal steps (Microsoft Entra admin center → App registrations → New registration)

1. **Name**: `Hello Copilot Mock API` (the value currently in `package-solution.json`; you may pick another name, but see the warning below). **Supported account types**: *Single tenant*. **Redirect URI**: none.
2. Copy **Application (client) ID** and **Directory (tenant) ID**.
3. **Expose an API**:
   - Next to *Application ID URI* choose **Add**. Keep the default `api://<client-id>`.
   - **Add a scope**: name `access_as_user`; *Who can consent*: **Admins and users** (or admins only if you prefer admin consent); fill in the consent display names and descriptions, for example "Access Hello Copilot API as the signed-in user"; state **Enabled**.
4. **Do not** add authorized client applications or SPA/web redirect URIs for this flow. Consent for the web part is granted by approving the SharePoint API access request (section 8).
5. **Microsoft Graph on behalf of the user** (required for SharePoint documents and generated files):
   - **API permissions** → Add → Microsoft Graph → *Delegated* → `Files.ReadWrite.All` → **Grant admin consent**. Without admin consent the on-behalf-of exchange fails, because the user never sees a consent prompt for the API.
   - **Certificates & secrets** → New client secret. Store it in the Key Vault created in section 7 (`az keyvault secret set --vault-name <kv> -n AzureAd--ClientSecret --value <secret>`) and never in source. A certificate or federated credential also works with Microsoft.Identity.Web if you prefer.
   - Uploads from the device and chat without documents work without this step; picking from SharePoint and `create_document` don't.

> **Warning — the display name is the lookup key.** SharePoint resolves `webApiPermissionRequests[].resource` by the app's **display name**, not its ID. It must match character for character and be unique in the tenant. If you choose or change the name, update `package-solution.json`, bump the version and redeploy. Using the object ID there makes the approval fail.

### 5.2 The Entra app manifest (App registration → Manifest)

The relevant parts of the **Microsoft Graph app manifest** after step 5.1:

```json
{
  "signInAudience": "AzureADMyOrg",
  "identifierUris": [ "api://<client-id>" ],
  "api": {
    "requestedAccessTokenVersion": 2,
    "oauth2PermissionScopes": [
      {
        "id": "<new-guid>",
        "value": "access_as_user",
        "type": "User",
        "isEnabled": true,
        "adminConsentDisplayName": "Access Hello Copilot API",
        "adminConsentDescription": "Allows the web part to call the API on behalf of the signed-in user.",
        "userConsentDisplayName": "Access Hello Copilot API",
        "userConsentDescription": "Allow this app to call the API on your behalf."
      }
    ],
    "preAuthorizedApplications": []
  }
}
```

Use `requestedAccessTokenVersion=2` for the current configuration. A v2 access
token's `aud` is the **bare API client ID**, so configure `AzureAd:Audience` as
`<client-id>`, not `api://<client-id>`. The SPFx `apiResource` remains
`api://<client-id>`; these values have different purposes. No dual-audience
override is required for this v2 setup. If migrating an existing v1 registration,
update the manifest and API settings together, restart, and acquire a fresh token.
Inspect `ver`, `aud`, `iss`, `tid` and `scp` locally; do not share or upload live
tokens. See Microsoft's [access-token claims reference](https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference).

## 6. Register the app registration in the manifests

Four places must agree. Everything is keyed off the same three values: the **display name**, the **Application ID URI** and the **scope**. The checked-in files contain sample values from the original tenant, so **replace them before you package** (section 8):

- [ ] `package-solution.json` → `resource` equals your Entra app's display name exactly
- [ ] web part manifest → `apiUrl` is your deployed API URL (not `https://localhost:7198`), `apiResource` is `api://<client-id>`
- [ ] `src/backend/api/appsettings.json` (or hosting settings) → your `TenantId`, `ClientId`, `Audience`
- [ ] `Cors:AllowedOrigins` → your tenant origin
- [ ] `solution.version` bumped

Property-pane overrides only affect SharePoint page and Teams channel/group-chat tab instances. The Teams personal app has no property pane; users change its settings in its **Settings** tab (6.4).

| # | File | Setting | Value |
|---|---|---|---|
| 1 | [src/frontend/spfx/config/package-solution.json](./src/frontend/spfx/config/package-solution.json) | `solution.webApiPermissionRequests[]` | `{ "resource": "<Entra app display name>", "scope": "access_as_user" }` |
| 2 | [src/frontend/spfx/src/webparts/helloCopilot/HelloCopilotWebPart.manifest.json](./src/frontend/spfx/src/webparts/helloCopilot/HelloCopilotWebPart.manifest.json) | `preconfiguredEntries[0].properties` | `apiUrl`, `apiResource` = `api://<client-id>`, `apiScope` = `api://<client-id>/access_as_user` |
| 3 | [src/backend/api/appsettings.json](./src/backend/api/appsettings.json) | `AzureAd` | `TenantId`, `ClientId`, `Audience` = `<client-id>` (bare ID for v2 tokens), `Scopes` = `access_as_user`; check Development and hosting overrides too |
| 4 | [src/frontend/spfx/teams/manifest.json](./src/frontend/spfx/teams/manifest.json) | `webApplicationInfo`, `version`, `developer` URLs | See 6.4 |

### 6.1 `package-solution.json`

```json
"solution": {
  "name": "hello-copilot-client-side-solution",
  "id": "<solution-guid>",
  "version": "1.0.0.0",
  "includeClientSideAssets": true,
  "skipFeatureDeployment": true,
  "isDomainIsolated": false,
  "webApiPermissionRequests": [
    { "resource": "Hello Copilot Mock API", "scope": "access_as_user" }
  ]
}
```

On upload to the app catalog this creates a **pending request** that an admin approves (section 8). Bump `version` for every redeploy, or the catalog keeps the old build.

For an API in **another tenant** (SPFx ≥ 1.15.2) add both `appId` and `replyUrl` to the request so that a service principal is created in the consuming tenant. Either both or neither.

Domain-isolated web parts are retired. Keep `isDomainIsolated` as `false`.

### 6.2 Web part manifest

`supportedHosts` decides where the web part can run, and `supportsThemeVariants` enables theming:

```json
"supportedHosts": ["SharePointWebPart", "TeamsTab", "TeamsPersonalApp"],
"supportsThemeVariants": true,
"preconfiguredEntries": [{
  "title": { "default": "Hello Copilot" },
  "properties": {
    "apiUrl": "https://<your-api-host>/api/messages/stream",
    "apiResource": "api://<client-id>",
    "apiScope": "api://<client-id>/access_as_user"
  }
}]
```

`apiResource` is what the code passes to `getToken(...)`. `apiScope` is informational in this sample. These `preconfiguredEntries` defaults are what every new instance starts with, so set the real values **before** packaging. On SharePoint pages and in Teams channel/group-chat tabs they can be overridden per instance in the property pane; in the Teams personal app, each user can override them in the **Settings** tab (6.4).

### 6.3 API settings

```json
"AzureAd": {
  "Instance": "https://login.microsoftonline.com/",
  "TenantId": "<tenant-id>",
  "ClientId": "<client-id>",
  "Audience": "<client-id>",
  "Scopes": "access_as_user"
},
"UseMockAuthentication": false,
"UseMockResponses": false
```

The endpoint requires the policy `AccessAsUser` (authenticated user **and** `scp` contains `access_as_user`).

The implementation uses Microsoft.Identity.Web's `RequireScope("access_as_user")`
to handle mapped/unmapped scope claims and space-separated lists. Do not replace
it with an exact-match check on the raw `scp` claim. For the current registration,
`TenantId` is `63645c73-a00c-4659-b911-eb6c4c2d4a8f` and both `ClientId` and
`Audience` are `1c389841-5b43-4575-8acb-8135d21ca809`. Full working configuration
and the consent checklist are in the [API README](./src/backend/api/README.md#authentication).

`Logging:EnablePiiLogging` currently defaults to `true` in the
[Development settings](./src/backend/api/appsettings.Development.json) for authentication
troubleshooting. It is honored only in Development and logs a startup warning.
Set it to `false` after troubleshooting; do not share unredacted authentication logs.

### 6.4 Teams app manifest

The repo ships its own Teams manifest, [src/frontend/spfx/teams/manifest.json](./src/frontend/spfx/teams/manifest.json), because the generated one has a single personal tab and therefore no way to configure the personal app ([Configure SPFx web parts in Teams](https://learn.microsoft.com/sharepoint/dev/spfx/build-for-teams-configure-in-teams)). It defines:

| Teams surface | Web part | How it's configured |
|---|---|---|
| Personal app, **Chat** tab | Hello Copilot (`f4c41a4f-…`) | Uses the saved Settings values, otherwise the `preconfiguredEntries` defaults |
| Personal app, **Settings** tab | Hello Copilot settings (`4a51e7ae-…`, `supportedHosts: []` so it never shows in SharePoint) | API URL, API resource and scope, saved in the browser's `localStorage`. Per user **and per device**; the Chat tab picks up changes immediately. **Reset to defaults** removes them |
| Channel or group-chat tab | Hello Copilot | The web part's property pane, when the tab is added and later from the tab's **Settings**; SharePoint stores the values per tab |

`npm run package` runs `npm run package:teams` first, which zips `manifest.json` and both icons (at the zip root) into `teams/TeamsSPFxApp.zip` (git-ignored). SPFx packs the `teams/` folder into the `.sppkg`, and **Sync to Teams** then deploys that package instead of generating one ([deployment options](https://learn.microsoft.com/sharepoint/dev/spfx/deployment-spfx-teams-solutions)). The app `id` is the chat web part's ID, the same one SharePoint's generated manifest uses, so an existing synced app is updated rather than duplicated.

When you change the manifest:

- Bump `version` with every change, or Teams keeps the old package.
- Replace the `developer` URLs with your own privacy and terms pages.
- Keep `webApplicationInfo` exactly as is:

```json
"webApplicationInfo": {
  "resource": "https://{teamSiteDomain}",
  "id": "00000003-0000-0ff1-ce00-000000000000"
}
```

  That is SharePoint's own application ID, used for SharePoint-brokered token acquisition. **It is not your API's client ID.** Your API is reached through the `webApiPermissionRequests` grant above.
- `src/frontend/spfx/teams/<web-part-id>_color.png` and `_outline.png` must exist (they are scaffolded). Replace the placeholders with real 192×192 and 32×32 icons.

## 7. Configure and deploy the API

The API uses controllers and an injectable chat service. By default it runs an agent loop on Foundry's Responses API that searches Azure AI Search (the user's own documents), can load skills and can create files. See the [API setup](./src/backend/api/README.md) for configuration, identity permissions and the independent `UseMockResponses` switch. The SSE contract is in section 3.3.

[infra/main.bicep](./infra/main.bicep) provisions everything around the API (7.1). During development the API runs on your machine behind a dev tunnel (7.2); for production host it on App Service (7.3).

```
SPFx web part ──HTTPS + Bearer──► APIM /chat ──► chat API (dev tunnel → localhost:5198, or App Service)
                                                   ├─ Azure AI Search: index my-documents
                                                   ├─ Foundry: gpt-6.1-sol, text-embedding-3-large, Document Intelligence
                                                   ├─ Cosmos DB: chats, documents
                                                   └─ Key Vault: AzureAd--ClientSecret
```

### 7.1 Provision Azure

| Resource | Configuration |
|---|---|
| API Management (`apim-…`, Developer tier) | API `chat` at path `/chat`; `GET`, `POST` and `DELETE /*` are forwarded unchanged to `apiBackendUrl`. The [policy](./infra/modules/chat-api-policy.xml) answers CORS preflights for `allowedOrigins`, validates the Entra token (tenant, audience = API client ID) with `validate-azure-ad-token`, returns 403 when `scp` lacks `access_as_user`, rate-limits per user `oid` and streams SSE unbuffered. No subscription key. Application Insights logging without headers or bodies |
| Azure AI Search (`srch-…`, Basic) | Entra-only auth, semantic ranker (free). The API creates its index at startup |
| Foundry (`aif-…`, AIServices) | Entra-only auth. Deployments `gpt-6.1-sol` and `text-embedding-3-large` (parameters `chatModel`, `embeddingModel`). The same resource serves Document Intelligence |
| Cosmos DB for NoSQL (`cosmos-…`) | Serverless, Entra-only auth, database `chatbot` with containers `chats` and `documents`. Public network access is `SecuredByPerimeter` |
| Network Security Perimeter (`nsp-…`) | Public mode only. Profile `cosmos-profile`, associated with Cosmos DB in `Enforced` mode. Inbound rules allow the IPs in `cosmosAllowedIps` (`allow-client-ips`) and, when `apiPrincipalId` is set, managed identities in this subscription (`allow-subscriptions`) |
| Key Vault (`kv-…`) | RBAC; holds the Graph on-behalf-of client secret `AzureAd--ClientSecret` |
| Log Analytics, Application Insights | APIM request logs |
| VNet, private endpoints, private DNS zones | Only with `privateNetworking=true` (see below) |

`apiPrincipalId` (the hosted API's managed identity) and `developerPrincipalId` (you, for local runs) get *Search Service Contributor*, *Search Index Data Contributor*, *Cognitive Services OpenAI User*, *Cognitive Services User*, *Cosmos DB Built-in Data Contributor* and *Key Vault Secrets User* (*Secrets Officer* for the developer, who stores the secret).

```powershell
az login
devtunnel user login

# A persistent dev tunnel to the API's HTTP port. Its URL is the APIM backend.
devtunnel create chatbot-api --allow-anonymous
devtunnel port create chatbot-api -p 5198 --protocol http
devtunnel show chatbot-api        # tunnel ID chatbot-api.<region> → https://chatbot-api-5198.<region>.devtunnels.ms

az group create -n rg-chatbot-dev -l swedencentral
az deployment group create -n main -g rg-chatbot-dev -f infra/main.bicep -p infra/main.bicepparam `
  -p apiBackendUrl=https://chatbot-api-5198.<region>.devtunnels.ms `
  --query properties.outputs
```

[main.bicepparam](./infra/main.bicepparam) holds this tenant's values: the API client ID, the developer's object ID (`developerPrincipalId`), the allowed origins, the APIM publisher email and `cosmosAllowedIps` (the public IPs that may reach Cosmos DB; update it and redeploy when your IP changes). Change them for another tenant or developer (`az ad signed-in-user show --query id -o tsv` returns your object ID). A new Developer-tier APIM takes 30–45 minutes; later runs are incremental. Check the model quota in the region first (`az cognitiveservices usage list -l swedencentral`) and lower `chatModel.capacity` or `embeddingModel.capacity` if needed.

The tunnel allows anonymous connections so APIM can reach it, which also means the API is reachable without APIM. The API still validates every token itself. Use it for development only, and delete the tunnel when you're done (`devtunnel delete chatbot-api`).

**Private networking.** `-p privateNetworking=true` privatizes the data dependencies: a VNet with private endpoints and private DNS zones for Search, Foundry, Cosmos DB and Key Vault, public network access disabled on all four, and APIM in External VNet mode. It **does not close App Service inbound access**. Host the API (7.3), configure outbound integration with `appIntegrationSubnetId`, and separately complete the [private API ingress cutover](./docs/design/private-api-cutover.md) using [api-private-ingress.bicep](./infra/api-private-ingress.bicep). Until that cutover, the public API can bypass APIM. The Cosmos Direct-mode/private-endpoint port issue remains deferred, so the complete private deployment is not yet certified. Switching an existing APIM into a VNet takes another 30–45 minutes.

### 7.2 Run the API locally behind APIM

Point the local API at the new resources with user secrets (Development only; they override [appsettings.json](./src/backend/api/appsettings.json)):

```powershell
$o = az deployment group show -g rg-chatbot-dev -n main --query properties.outputs -o json | ConvertFrom-Json
cd src/backend/api
dotnet user-secrets set Rag:SearchEndpoint $o.searchEndpoint.value
dotnet user-secrets set Rag:FoundryEndpoint $o.foundryEndpoint.value
dotnet user-secrets set Cosmos:Endpoint $o.cosmosEndpoint.value

# For the SharePoint picker and saved files (Graph on-behalf-of), using the secret from 5.1 step 5:
az keyvault secret set --vault-name $o.keyVaultName.value -n AzureAd--ClientSecret --value <client-secret>
dotnet user-secrets set AzureAd:ClientSecret <client-secret>

cd ../../..
aspire start                       # API on http://localhost:5198, the chatbot-api tunnel and the SPFx dev server (3.4)
```

Without Aspire, run `dotnet run --launch-profile http` in `src/backend/api` and `devtunnel host chatbot-api` in a second terminal.

Then set the web part's `apiUrl` to output `webPartApiUrl` (`https://<apim>.azure-api.net/chat/api/messages/stream`) in the property pane, or in the manifest (6.2) before packaging. `apiResource` stays the same: APIM checks the same token the API does. A quick check of the chain:

The configured stream URL is used exactly. Custom stream paths can supply
`companionBaseUrl` for saved chats/documents/skills; without it they use an explicitly
labeled streaming-only mode. See [endpoint configuration](./src/frontend/spfx/README.md#endpoint-configuration-and-validation).

```powershell
curl.exe -i "$($o.apimGatewayUrl.value)/chat/api/chats"   # 401 from APIM: no token
```

New role assignments can take a few minutes to apply. Until then the API stops at startup because it cannot create the search index; the log says why.

### 7.3 Host the API in Azure

1. **Host it** on Azure App Service. Example (adjust names; check supported runtimes with `az webapp list-runtimes`):

```powershell
az appservice plan create -g rg-chatbot-dev -n plan-chatbot --sku B1 --is-linux
az webapp create -g rg-chatbot-dev -p plan-chatbot -n <unique-app-name> --runtime "DOTNETCORE:10.0"
az webapp update -g rg-chatbot-dev -n <unique-app-name> --https-only true
az webapp identity assign -g rg-chatbot-dev -n <unique-app-name>

$o = az deployment group show -g rg-chatbot-dev -n main --query properties.outputs -o json | ConvertFrom-Json
az webapp config appsettings set -g rg-chatbot-dev -n <unique-app-name> --settings `
  ASPNETCORE_ENVIRONMENT=Production UseMockAuthentication=false UseMockResponses=false `
  AzureAd__Instance=https://login.microsoftonline.com/ AzureAd__TenantId=<tenant-id> `
  AzureAd__ClientId=<client-id> AzureAd__Audience=<client-id> AzureAd__Scopes=access_as_user `
  "Cors__AllowedOrigins__0=https://<tenant>.sharepoint.com" "Cors__AllowedOrigins__1=https://teams.microsoft.com" `
  Rag__SearchEndpoint=$($o.searchEndpoint.value) Rag__FoundryEndpoint=$($o.foundryEndpoint.value) `
  Cosmos__Endpoint=$($o.cosmosEndpoint.value) `
  "AzureAd__ClientSecret=@Microsoft.KeyVault(SecretUri=$($o.keyVaultUri.value)secrets/AzureAd--ClientSecret)"

cd src/backend/api
dotnet publish -c Release -o publish
Compress-Archive publish\* publish.zip -Force
az webapp deploy -g rg-chatbot-dev -n <unique-app-name> --src-path publish.zip --type zip
```

   Use code-level CORS (above), not the platform CORS blade, so there is one source of truth. Container Apps works the same way (same settings as environment variables) but isn't scripted here.
2. **Auth settings**: as in the commands above, with **`UseMockAuthentication=false`**. Never ship with the mock flag on.
3. **CORS**: APIM answers browser preflights for `allowedOrigins` and removes the `Origin` header before forwarding, so with APIM in front the API's `Cors:AllowedOrigins` only matters for direct calls. Keep both lists the same: your exact tenant origin, e.g. `https://<tenant>.sharepoint.com`, plus the Teams origins you actually observe. The credentials are in the `Authorization` header, so `AllowCredentials` is not needed. Microsoft publishes no canonical origin list, so **check the `Origin` header the deployed web part sends from a SharePoint page and from Teams (web and desktop)** and allow exactly those. In Teams, an SPFx tab runs on a SharePoint-hosted page inside the Teams frame, so its calls are expected to carry your **SharePoint** origin. The `https://teams.microsoft.com` entry is a precaution; remove it if you never observe it (not yet verified in this repo). For local work keep `https://localhost:4321` in the API list.
4. **Streaming**: APIM forwards with `buffer-response="false"`. Disable response buffering and compression for `/api/messages/stream` on any other reverse proxy or gateway. Otherwise users see the whole answer arrive at once.
5. **Azure service access**: the API uses `DefaultAzureCredential` only in Development and a system-assigned managed identity in Production. These permissions are separate from the incoming user's JWT and SharePoint consent. No API keys in the app or the web part.
6. **Wire it into the Bicep deployment**: run the deployment from 7.1 again with `apiPrincipalId=$(az webapp identity show -g rg-chatbot-dev -n <unique-app-name> --query principalId -o tsv)`, so the managed identity gets the roles, and `apiBackendUrl=https://<unique-app-name>.azurewebsites.net`, so APIM forwards to App Service. Store the client secret in the vault (7.2) if you haven't. `webPartApiUrl` doesn't change.
7. **Search index**: nothing to run. At startup the API creates the `Rag:IndexName` index (default `my-documents`) on the Search service, or updates it if fields were added. That needs the *Search Service Contributor* role, and the Search service must allow Entra ID (RBAC) authentication (Bicep sets both). The API does not start if this fails; the log says why. The index holds only the users' own documents, and every query filters on `ownerId`. To change `Rag:EmbeddingDimensions` or the embedding model, set a new `Rag__IndexName` and have users add their documents again.
8. **Skills** ship with the API: everything under `src/backend/api/skills` is copied to the publish output. Adding or changing a skill means redeploying the API (see [Skills](./src/backend/api/README.md#skills)).

   Optional: `Graph__OutputDriveId` (a document library's drive ID) and `Graph__OutputFolderPath` (default `Chatbot`) choose where generated files are saved; by default they go to the user's OneDrive.
## 8. Package and deploy the web part

### 8.1 Build a clean package

Stop any running `npm run start` first. The watcher shares the output folders and leaks development assets into the package.

```powershell
cd src/frontend/spfx
npm run clean
npm run build:prod
npm run package
```

Output: `src/frontend/spfx/sharepoint/solution/hello-copilot.sppkg`. The `release/assets` folder should hold one hashed `hello-copilot-web-part_<hash>.js` and no `hot-update` files.

### 8.2 Deploy to SharePoint

1. Open the **tenant app catalog** (SharePoint admin center → More features → Apps, or `https://<tenant>.sharepoint.com/sites/appcatalog`) → **Apps for SharePoint** → upload the `.sppkg`.
2. In the trust dialog choose **Deploy**. `skipFeatureDeployment` is `true`, so with "Make this solution available to all sites" the web part is available everywhere. Otherwise add the app to specific sites.
3. **Approve API access**: SharePoint admin center → **API access** (under Advanced in current tenants; navigation may vary). Select the pending `Hello Copilot Mock API / access_as_user` request → **Approve**. (PowerShell: `Get-SPOTenantServicePrincipalPermissionRequests`, `Approve-SPOTenantServicePrincipalPermissionRequest -RequestId <guid>`.)
4. Add the **Hello Copilot** web part to a modern page.

### 8.3 Deploy to Teams

1. In the app catalog, select the deployed solution and use the ribbon action **Add to Teams** (labelled **Sync to Teams** in some tenants and for custom `TeamsSPFxApp.zip` packages). It is shown when the package supports Teams.
2. Teams admin center → **Manage apps**: find *Hello Copilot*, review the permissions, and allow it for your users or teams (assign with an app permission/setup policy if needed).
3. Users then add it as a **tab** (channel, group chat or meeting) or as a **personal app**.

Redeploys: bump `solution.version` → upload → deploy → **Add/Sync to Teams** again. A new version does not need a new API approval unless `webApiPermissionRequests` changed.

### 8.4 Roll back / decommission

Remove in this order so nothing keeps working with a stale grant:

1. Teams admin center: block or delete the app. App catalog: remove the app (this also removes it from sites).
2. Revoke the permission grant: SharePoint admin center → API access → select the approved entry → **Remove/Revoke**, or `Get-SPOTenantServicePrincipalPermissionGrants` then `Revoke-SPOTenantServicePrincipalPermission -ObjectId <id>`.
3. Delete or disable the Entra app registration, then delete the API hosting resources (`az group delete -n rg-chatbot-dev`).
4. Verify: a token request for the resource now fails and the API URL no longer responds.

## 9. Verify

- [ ] Web part loads on a SharePoint page. Send a message and watch the Activity steps, then the streamed answer. **Stop** keeps the partial text.
- [ ] Browser dev tools → Network: the `stream` request has a bearer access-token header and status 200, and its response is `text/event-stream`. Inspect the token locally: `ver` = `2.0`, `aud` = the bare API client ID, `scp` contains `access_as_user`, `tid` = your tenant. Never upload or share the token.
- [ ] API rejects a call without a token (401) and with a token lacking the scope (403).
- [ ] Teams tab and personal app: light, dark and high-contrast themes. The tab fills the canvas and the sidebar collapses to a drawer when narrow.
- [ ] Teams personal app **Settings** tab: save a different API URL; the Chat tab uses it without reloading, and **Reset to defaults** restores the manifest defaults. A channel tab's **Settings** opens the property pane.
- [ ] Both hosts send an `Origin` that is in `Cors:AllowedOrigins`. Note which origin Teams actually sends (see 7.3).
- [ ] Chat history appears in every host (SharePoint page, Teams web, Teams desktop) for the same user, and deleting a chat removes it everywhere.
- [ ] **Library**: upload a small PDF from the Library; it moves from *Processing* to *Ready* within a minute. Select it, **Attach to chat → New chat**, ask about its content and check the answer cites it. Another user must not find it.
- [ ] **From SharePoint**: the picker opens on the current site, the picked file becomes *Ready*, and its name links back to SharePoint.
- [ ] **Skill**: ask "Extract the key figures from my document into an Excel file". The Activity log shows the skill being loaded and the reply links to a new file in OneDrive (or the configured library).

## 10. Troubleshooting

| Symptom | Likely cause and fix |
|---|---|
| No pending request on the API access page | `resource` doesn't match the app's **display name**, the package wasn't deployed, or the solution `version` wasn't bumped |
| `AADSTS65001` / consent error, or the token request fails | Request not approved yet, or `apiResource` isn't the exact Application ID URI |
| 401 from the API | Wrong `Audience`/`ClientId`/`TenantId`, `UseMockAuthentication` mismatch, or the token's `aud`/`iss` doesn't match the configuration |
| 401 or 403 from APIM (body `Unauthorized`/`Forbidden`, nothing in the API log) | Same causes as for the API: APIM checks tenant, audience (named value `api-client-id`) and `scp` before forwarding |
| 404 from APIM | `apiUrl` lacks the `/chat` prefix; it must be `https://<apim>.azure-api.net/chat/api/messages/stream` |
| 502 or 504 from APIM | The backend is unreachable: the `tunnel` resource isn't running (or `devtunnel host chatbot-api` without Aspire), the API isn't listening on port 5198, or `apiBackendUrl` is wrong |
| 429 from APIM | The per-user limit (`callsPerMinute`, default 30) was reached |
| 403 from the API | Token has no `scp` = `access_as_user` (scope not requested/approved) |
| Auth works with canned responses but real RAG returns an SSE `error` | Check API logs using the reference ID; verify backend identity, Azure RBAC, service network access and model deployment |
| Changing a JSON mock flag has no effect | Restart the API and check for environment or command-line overrides; the Development file also overrides the base file |
| CORS error in the console | The `Origin` isn't in `allowedOrigins` (APIM, [main.bicepparam](./infra/main.bicepparam)) or, without APIM, `Cors:AllowedOrigins` (check scheme, host and no trailing slash) |
| Teams calls `https://localhost:7198` with `ERR_CONNECTION_REFUSED` while SharePoint works | Teams has separate settings; SharePoint page property-pane values do not carry over. Set the Teams personal app's **Settings** API URL (or channel tab's property pane) to deployment output `webPartApiUrl`, and clear a saved localhost companion base URL. The checked-in manifest now defaults to APIM; rebuild, upload and Sync to Teams for new defaults. In the personal app, **Reset to defaults** clears old overrides after deployment. Aspire listens on HTTP 5198 behind the dev tunnel, not HTTPS 7198; keep the API and tunnel running. This is a connectivity/configuration failure, not a CORS rejection. |
| Answer arrives all at once | A proxy/gateway is buffering or compressing SSE |
| Works on a page, fails in Teams | The Teams-hosted page sends an origin that isn't in CORS (check the `Origin` header), the app wasn't synced, or a custom Teams manifest lacks `webApplicationInfo` |
| Chat list shows "Couldn't load your chats" | `Cosmos:Endpoint` is wrong or the API identity lacks the Cosmos data-plane role (Bicep assigns it; role assignments can take a few minutes) |
| Cosmos DB returns 403 "Request originated from IP … through public internet" | The caller's IP isn't in the network security perimeter. Add it to `cosmosAllowedIps` in `main.bicepparam` and redeploy. A Cosmos DB account can be in only one perimeter; if the deployment fails with `AnmCustomerRequestConflictException`, remove the existing association first |
| Document stays *Failed* with "interrupted" | The API restarted while processing it (the queue is in memory). Remove and add it again |
| Document *Failed* right after upload | Check the API log: *Cognitive Services User* on the Foundry resource (Document Intelligence), embedding deployment name and dimensions |
| SharePoint picker or file creation fails with 403/`invalid_grant` | Graph `Files.ReadWrite.All` has no admin consent, or `AzureAd:ClientSecret` is missing or expired (check the Key Vault reference status in App Service → Environment variables) |
| Picker shows a blank frame | The browser blocked the SharePoint token or the frame; check the console for the `FilePicker.aspx` request |
| Package contains `hot-update` files | The `heft start` watcher was running while you packaged; stop it and run `clean` first |
| Dev page shows the old web part | Hard-refresh; check `https://localhost:4321/temp/build/manifests.js` loads and the dev cert is trusted |

## 11. Known limits

- Not validated in real Teams in this repository's development so far. Verify section 9 in your tenant.
- Chats are kept until the user deletes them; there is no retention policy or admin export yet. A stopped or failed reply leaves the user's message in the stored history, and *Regenerate* stores the question again.
- Ingestion runs in the API process with an in-memory queue (one document at a time). A restart loses unfinished documents; they are reported as failed once they have been processing for 15 minutes. Scale-out would need a durable queue. Files are limited to 10 MB.
- Private documents are separated by a search filter on `ownerId`, not by SharePoint permissions: a file a user picks stays searchable by that user even if their SharePoint access is later removed, and it is not updated when the file changes.
- Skills are instructions only (no code execution). They change only with an API deployment.
- The web part cannot render inside a Copilot conversation. See section 12 for what is and isn't possible.
- SPFx Heft-era releases are SharePoint Online only.
- SPFx 1.24 (React 18, Node 24/26) is in preview. Staying on 1.23.2 until it reaches GA is deliberate.
- Fluent UI v9 inside SPFx is not an officially documented path yet. `src/frontend/spfx/README.md` and `docs/research/` list the open questions.

## 12. Other hosts: Outlook, Microsoft 365 Copilot, Copilot chat and iframe tabs

Summary of [docs/research/teams-iframe-tab-vs-spfx.md](./docs/research/teams-iframe-tab-vs-spfx.md) (sources and open items are there). Nothing in this section has been tested in this repo.

**Recommendation:** keep SPFx as the host. It is the only option that serves SharePoint pages and Teams from one package without hosting a web app. Add other hosts only when a requirement needs them, and reuse the chat UI and the API rather than the web part.

### 12.1 Outlook and the Microsoft 365 Copilot app

- Since SPFx 1.16, SPFx **personal tabs** are supported in Outlook and the Microsoft 365 app ([Extend a Teams personal tab across Microsoft 365](https://learn.microsoft.com/microsoftteams/platform/m365-apps/extend-m365-teams-personal-tab)). The Microsoft 365 app is being renamed **Microsoft 365 Copilot app**, so this is how the web part can appear in the Copilot app today, as a pinned app.
- That is the app shell, **not** the Copilot conversation (12.2).
- It needs a Teams manifest version that supports the extension (v1.13 or later). The repo's own manifest (6.4) uses v1.17; running it in Outlook and the Microsoft 365 app is **not verified** yet.

### 12.2 Copilot chat

A Teams tab or SPFx web part can't be rendered inside a Copilot conversation. The options all need new work:

| Option | Status | Reuse from this repo |
|---|---|---|
| Declarative agent with an API plugin returning Adaptive Cards | Available ([Adaptive Cards for agents](https://learn.microsoft.com/agents/design-guidelines/adaptive-cards-for-agent-design)) | The API, behind an OpenAPI description. Cards replace the React UI |
| MCP apps (interactive widgets in a declarative agent) | New; GA vs. preview not confirmed ([MCP apps](https://learn.microsoft.com/microsoft-365/copilot/extensibility/plugin-mcp-apps)) | The backend logic behind an MCP server. The custom SSE contract isn't MCP |
| SPFx Copilot UX components | **Preview**, SPFx 1.24+, not for production ([Overview](https://learn.microsoft.com/sharepoint/dev/spfx/copilot/overview-copilot-apps)) | Can ship in the same `.sppkg`, but it is a different component (`BaseCopilotComponent`). Share the React UI, not the web part class |

To prepare, decouple the chat component from `WebPartContext`. Today it uses SPFx only for the token (`aadTokenProviderFactory`) and the storage key (`instanceId`). Pass in `getToken` and `storageKey` instead.

### 12.3 Iframe (manifest-pointed) Teams tab

Instead of SPFx, a Teams app manifest can point `staticTabs[].contentUrl` at a page you host. Static tabs work in personal scope and, with manifest v1.16+, in group chats, channels and meetings ([Create a tab](https://learn.microsoft.com/microsoftteams/platform/tabs/how-to/create-personal-tab)). It gives you nothing on SharePoint pages and adds work:

| Area | What changes |
|---|---|
| Hosting | You host the SPA (for example App Service or Static Web Apps). |
| Framing | Send CSP `frame-ancestors` with the Teams, Microsoft 365 and Outlook hosts, including `*.cloud.microsoft` ([tab requirements](https://learn.microsoft.com/microsoftteams/platform/tabs/how-to/tab-requirements)). `X-Frame-Options` can't hold that list; it is only a legacy single-origin fallback. |
| TeamsJS | Call `app.initialize()` (TeamsJS 2.19+), or the tab isn't shown. `app.notifySuccess()` is only required if the manifest sets `showLoadingIndicator: true`. Read the theme with `app.getContext()`. |
| Auth, option A: Nested App Authentication (recommended for SPAs) | Add an **SPA platform** with redirect `brk-multihub://<your-domain>` to an app registration, use MSAL's `createNestablePublicClientApplication`, and request `api://<client-id>/access_as_user`. No pre-authorized clients or OBO ([NAA](https://learn.microsoft.com/microsoftteams/platform/concepts/authentication/nested-authentication)). |
| Auth, option B: Teams SSO (`getAuthToken`) | Add `webApplicationInfo` to the Teams manifest. Teams requires the Application ID URI `api://<tab-host-domain>/<client-id>` ([register](https://learn.microsoft.com/microsoftteams/platform/tabs/how-to/authentication/tab-sso-register-aad)). Add it as a **second** identifier URI next to `api://<client-id>` (which SPFx keeps using), pre-authorize the Teams client IDs, and make the API accept both audiences (`TokenValidationParameters.ValidAudiences`). OBO is only needed if the API calls another API as the user. |
| Auth, option C: popup (`authentication.authenticate`) | A sign-in popup flow you implement yourself. Works with Entra ID too, but A or B is simpler. |
| API | Add the SPA origin to `Cors:AllowedOrigins`. The SSE contract and the `access_as_user` scope stay the same. |
| UI | Reuse the chat component once it is decoupled (12.2). The Teams theme mapping is reusable, but get TeamsJS from `@microsoft/teams-js` instead of `context.sdks`. |
