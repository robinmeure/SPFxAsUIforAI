# Hello Copilot — SharePoint and Teams as the front end for your own RAG/AI applications

## Why this repo exists

Organizations build their own AI applications: RAG over company documents, agents over line-of-business data, domain-specific assistants. They then have to answer a second question: **where do users talk to them?** A standalone web app means another URL to find, another sign-in, another host to secure and operate, and a UI that looks nothing like the tools people already use.

This repo shows the alternative. **A SharePoint Framework (SPFx) web part becomes the chat interface, and SharePoint Online and Microsoft Teams become the front end**, while your RAG/AI logic stays in your own API. One package gives you:

- **A chat on SharePoint pages, as a Teams tab and as a Teams personal app**, all from the same `.sppkg`.
- **A Copilot-like experience** that users already recognize: streaming answers, progress steps while the backend retrieves and reasons, stop and retry, chat history, and Markdown answers.
- **Your backend, your choice**: the web part calls any HTTPS endpoint that streams Server-Sent Events. The [API](./api) includes Azure AI Search retrieval and Foundry generation through the Responses API, plus a selectable mock. There is no bot runtime and no Copilot license dependency in the front end.

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
| [spfx/](./spfx) | The web part (SPFx 1.23.2, Heft, React 17, Fluent UI v9) |
| [api/](./api) | .NET 10 controllers, Azure AI Search + Foundry RAG, selectable mock, SSE streaming, CORS, Microsoft.Identity.Web |
| [docs/research/](./docs/research/spfx-teams-chat-webpart.md) | Sourced research behind these choices, including open questions. See also [Teams iframe tab vs. SPFx](./docs/research/teams-iframe-tab-vs-spfx.md) for the manifest-pointed tab alternative and Copilot reach |

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
       Web API (.NET 10, Microsoft.Identity.Web)  ── validates the JWT, scope access_as_user
              │
              ▼
       Your RAG/AI logic: retrieval (e.g. Azure AI Search), model (Azure OpenAI / Foundry via managed identity),
       orchestration and tools — or the mock in this repo
```

Key decisions:

- **Auth: SPFx's own token provider, not MSAL.** Microsoft states that using MSAL.js directly in SPFx is not supported. The web part gets a delegated token for your API from SPFx. Delegated permissions are granted to the tenant-wide *SharePoint Online Client Extensibility Web Application Principal*, so **only the API needs an app registration**. There is no separate client registration.
- **Why native `fetch` and not `AadHttpClient`.** `AadHttpClient` is the documented way to call an Entra API, but its docs don't say whether the response body is streamable. This project takes the token from `AadTokenProvider` and calls `fetch()` itself so it can read `text/event-stream`. Confirm the method signature against the installed `@microsoft/sp-http-base` typings if you change it.
- **One message per request.** Chat history lives in `localStorage` (per web part instance, up to 50 chats). The API receives only the latest message.

## 2. Prerequisites

| Need | Version / role |
|---|---|
| Node.js | **22.x** (SPFx 1.23 supports Node 22 only). Install with `nvm install 22 && nvm use 22` (nvm-windows) or from <https://nodejs.org> |
| npm | 11+ |
| .NET SDK | 10 (<https://dotnet.microsoft.com/download>) |
| Azure CLI | Only for the App Service steps in section 7 (`az login`) |
| SPFx / React / TypeScript | 1.23.2 / **17.0.1 (exact)** / 5.8.3 |
| SharePoint Online | A tenant with a **tenant app catalog**. Heft-era SPFx is SharePoint Online only. |
| Entra roles | Create the app registration (Application Developer or higher); approve API permissions (Application Administrator is enough for your own API) |
| Teams | Custom app upload/sync allowed in the Teams admin center |

SPFx pins React exactly. After any dependency change, check that `react` and `react-dom` are still `17.0.1`. The repo has no lockfile and uses caret ranges for Fluent UI and `react-markdown`, so installs are not byte-reproducible. Commit a lockfile and use `npm ci` if you need that.

## 3. Build and run locally

### 3.1 Web part

```powershell
cd spfx
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
cd api
dotnet dev-certs https --trust
dotnet run --launch-profile https     # https://localhost:7198
```

Development defaults to real authentication and real RAG responses. Run `az login` and grant the backend identity access to Search and Foundry as described in the [API setup](./api/README.md). For local UI-only work, set both `UseMockAuthentication=true` and `UseMockResponses=true` before starting the API. Never enable mock authentication in hosting configuration.

Both switches can be set directly as **top-level properties** in
[appsettings.Development.json](./api/appsettings.Development.json) or
[appsettings.json](./api/appsettings.json):

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
See the [API configuration and auth checklist](./api/README.md#authentication)
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

`POST /api/messages/stream` with body `{ "message": "..." }` returns `text/event-stream`. Each event is `data: <json>` followed by a blank line:

| Event | Meaning in the UI |
|---|---|
| `{ "activity": "Searching documents" }` | A short, user-facing progress step shown in the collapsible **Activity** section. Send summaries, not raw model reasoning. |
| `{ "text": "…" }` | Appends answer content (Markdown) |
| `{ "error": "…" }` | Fails the reply; any partial text is kept |
| `[DONE]` | Ends the stream |

Plain `{ text }` streams work without any activity events.

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

[HelloCopilotWebPart.ts](./spfx/src/webparts/helloCopilot/HelloCopilotWebPart.ts) picks the Fluent theme once and re-renders on change:

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
4. **Do not** add authorized client applications, a client secret or SPA/web redirect URIs for this flow. Consent is granted by approving the SharePoint API access request (section 8).
5. If the API will call Microsoft Graph or other APIs on the user's behalf (OBO), also add the delegated permissions there, create a certificate or secret (or use a federated credential), and wire `EnableTokenAcquisitionToCallDownstreamApi()`.

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
- [ ] `api/appsettings.json` (or hosting settings) → your `TenantId`, `ClientId`, `Audience`
- [ ] `Cors:AllowedOrigins` → your tenant origin
- [ ] `solution.version` bumped

Property-pane overrides only affect SharePoint page instances; they do **not** configure the Teams personal app.

| # | File | Setting | Value |
|---|---|---|---|
| 1 | [spfx/config/package-solution.json](./spfx/config/package-solution.json) | `solution.webApiPermissionRequests[]` | `{ "resource": "<Entra app display name>", "scope": "access_as_user" }` |
| 2 | [spfx/src/webparts/helloCopilot/HelloCopilotWebPart.manifest.json](./spfx/src/webparts/helloCopilot/HelloCopilotWebPart.manifest.json) | `preconfiguredEntries[0].properties` | `apiUrl`, `apiResource` = `api://<client-id>`, `apiScope` = `api://<client-id>/access_as_user` |
| 3 | [api/appsettings.json](./api/appsettings.json) | `AzureAd` | `TenantId`, `ClientId`, `Audience` = `<client-id>` (bare ID for v2 tokens), `Scopes` = `access_as_user`; check Development and hosting overrides too |
| 4 | Teams manifest (only for a custom `TeamsSPFxApp.zip`) | `webApplicationInfo` | See 6.4 |

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

`apiResource` is what the code passes to `getToken(...)`. `apiScope` is informational in this sample. In a **Teams personal app** there is no property pane, so these `preconfiguredEntries` defaults are the production configuration. Set the real values **before** packaging. On SharePoint pages they can also be overridden per instance in the property pane.

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
and the consent checklist are in the [API README](./api/README.md#authentication).

`Logging:EnablePiiLogging` currently defaults to `true` in the
[Development settings](./api/appsettings.Development.json) for authentication
troubleshooting. It is honored only in Development and logs a startup warning.
Set it to `false` after troubleshooting; do not share unredacted authentication logs.

### 6.4 Teams app manifest

- **Default path (recommended, and the one this repo supports):** do nothing. SharePoint generates the Teams manifest from your solution (`packageName` ← solution alias, `id` ← solution id, names/descriptions ← `preconfiguredEntries`).
- **Custom manifest (not included in this repo):** if you need your own Teams manifest, zip its contents (manifest.json plus both icons at the zip root) as `spfx/teams/TeamsSPFxApp.zip` and use **Sync to Teams**. It must contain:

```json
"webApplicationInfo": {
  "resource": "https://{teamSiteDomain}",
  "id": "00000003-0000-0ff1-ce00-000000000000"
}
```

  That is SharePoint's own application ID, used for SharePoint-brokered token acquisition. **It is not your API's client ID.** Your API is reached through the `webApiPermissionRequests` grant above.
- `spfx/teams/<web-part-id>_color.png` and `_outline.png` must exist (they are scaffolded). Replace the placeholders with real 192×192 and 32×32 icons.

## 7. Configure and deploy the API

The API uses controllers and an injectable chat service. By default it retrieves parliamentary questions with Azure AI Search hybrid/semantic search and streams grounded answers through Foundry's Responses API. See the [API setup](./api/README.md) for configuration, identity permissions and the independent `UseMockResponses` switch. The SSE contract is unchanged: `{ "activity": "Searching documents" }` events for progress, `{ "text": "..." }` chunks for the answer, `{ "error": "..." }` on failure, and `[DONE]` on success.

1. **Host it** on Azure App Service. Example (adjust names; check supported runtimes with `az webapp list-runtimes`):

```powershell
az login
az group create -n rg-hello-copilot -l westeurope
az appservice plan create -g rg-hello-copilot -n plan-hello-copilot --sku B1
az webapp create -g rg-hello-copilot -p plan-hello-copilot -n <unique-app-name> --runtime "DOTNETCORE:10.0"
az webapp update -g rg-hello-copilot -n <unique-app-name> --https-only true
az webapp identity assign -g rg-hello-copilot -n <unique-app-name>

az webapp config appsettings set -g rg-hello-copilot -n <unique-app-name> --settings `
  ASPNETCORE_ENVIRONMENT=Production UseMockAuthentication=false UseMockResponses=false `
  AzureAd__Instance=https://login.microsoftonline.com/ AzureAd__TenantId=<tenant-id> `
  AzureAd__ClientId=<client-id> AzureAd__Audience=<client-id> AzureAd__Scopes=access_as_user `
  "Cors__AllowedOrigins__0=https://<tenant>.sharepoint.com" "Cors__AllowedOrigins__1=https://teams.microsoft.com"

cd api
dotnet publish -c Release -o publish
Compress-Archive publish\* publish.zip -Force
az webapp deploy -g rg-hello-copilot -n <unique-app-name> --src-path publish.zip --type zip

curl.exe -i -X POST https://<unique-app-name>.azurewebsites.net/api/messages/stream   # expect 401
```

   Use code-level CORS (above), not the platform CORS blade, so there is one source of truth. Container Apps works the same way (same settings as environment variables) but isn't scripted here.
2. **Auth settings**: as in the commands above, with **`UseMockAuthentication=false`**. Never ship with the mock flag on.
3. **CORS** (`Cors:AllowedOrigins`): add your exact tenant origin, e.g. `https://<tenant>.sharepoint.com`, plus the Teams origins you actually observe. The credentials are in the `Authorization` header, so `AllowCredentials` is not needed. Microsoft publishes no canonical origin list, so **check the `Origin` header the deployed web part sends from a SharePoint page and from Teams (web and desktop)** and allow exactly those. In Teams, an SPFx tab runs on a SharePoint-hosted page inside the Teams frame, so its calls are expected to carry your **SharePoint** origin. The `https://teams.microsoft.com` entry above is a precaution; remove it if you never observe it (not yet verified in this repo). For local work keep `https://localhost:4321`.
4. **Streaming**: disable response buffering and compression for `/api/messages/stream` on any reverse proxy or gateway. Otherwise users see the whole answer arrive at once.
5. **Azure service access**: the API uses `DefaultAzureCredential` only in Development and a system-assigned managed identity in Production. Grant its identity *Search Index Data Reader* on Search and *Cognitive Services OpenAI User* on Foundry. The Search service identity also needs *Cognitive Services OpenAI User* for the index's embedding vectorizer. These permissions are separate from the incoming user's JWT and SharePoint consent. See the [RAG configuration](./api/README.md#rag-configuration). No API keys in the app or the web part.
6. Update `apiUrl` in the web part manifest (6.2) to the deployed URL.

## 8. Package and deploy the web part

### 8.1 Build a clean package

Stop any running `npm run start` first. The watcher shares the output folders and leaks development assets into the package.

```powershell
cd spfx
npm run clean
npm run build:prod
npm run package
```

Output: `spfx/sharepoint/solution/hello-copilot.sppkg`. The `release/assets` folder should hold one hashed `hello-copilot-web-part_<hash>.js` and no `hot-update` files.

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
3. Delete or disable the Entra app registration, then delete the API hosting resources (`az group delete -n rg-hello-copilot`).
4. Verify: a token request for the resource now fails and the API URL no longer responds.

## 9. Verify

- [ ] Web part loads on a SharePoint page. Send a message and watch the Activity steps, then the streamed answer. **Stop** keeps the partial text.
- [ ] Browser dev tools → Network: the `stream` request has a bearer access-token header and status 200, and its response is `text/event-stream`. Inspect the token locally: `ver` = `2.0`, `aud` = the bare API client ID, `scp` contains `access_as_user`, `tid` = your tenant. Never upload or share the token.
- [ ] API rejects a call without a token (401) and with a token lacking the scope (403).
- [ ] Teams tab and personal app: light, dark and high-contrast themes. The tab fills the canvas and the sidebar collapses to a drawer when narrow.
- [ ] Both hosts send an `Origin` that is in `Cors:AllowedOrigins`. Note which origin Teams actually sends (see 7.3).
- [ ] Chat history persists across reloads in each host (SharePoint page, Teams web, Teams desktop). History is not expected to be shared between them (see section 11).

## 10. Troubleshooting

| Symptom | Likely cause and fix |
|---|---|
| No pending request on the API access page | `resource` doesn't match the app's **display name**, the package wasn't deployed, or the solution `version` wasn't bumped |
| `AADSTS65001` / consent error, or the token request fails | Request not approved yet, or `apiResource` isn't the exact Application ID URI |
| 401 from the API | Wrong `Audience`/`ClientId`/`TenantId`, `UseMockAuthentication` mismatch, or the token's `aud`/`iss` doesn't match the configuration |
| 403 from the API | Token has no `scp` = `access_as_user` (scope not requested/approved) |
| Auth works with canned responses but real RAG returns an SSE `error` | Check API logs using the reference ID; verify backend/Search identities, Azure RBAC, service network access, vectorizer and model deployment |
| Changing a JSON mock flag has no effect | Restart the API and check for environment or command-line overrides; the Development file also overrides the base file |
| CORS error in the console | The `Origin` isn't in `Cors:AllowedOrigins` (check scheme, host and no trailing slash) |
| Answer arrives all at once | A proxy/gateway is buffering or compressing SSE |
| Works on a page, fails in Teams | The Teams-hosted page sends an origin that isn't in CORS (check the `Origin` header), the app wasn't synced, or a custom Teams manifest lacks `webApplicationInfo` |
| History differs between the page and Teams | Expected: history is stored per web part instance and per browser storage partition (see section 11) |
| Package contains `hot-update` files | The `heft start` watcher was running while you packaged; stop it and run `clean` first |
| Dev page shows the old web part | Hard-refresh; check `https://localhost:4321/temp/build/manifests.js` loads and the dev cert is trusted |

## 11. Known limits

- Not validated in real Teams in this repository's development so far. Verify section 9 in your tenant.
- Chat history is local to the browser and web part instance; the API sees one message per request. The key includes the web part instance ID, and browsers partition storage for content framed inside another site, so a SharePoint page and Teams (and Teams web vs. desktop) keep separate histories. Add server-side history if you need continuity across hosts, devices or context.
- The web part cannot render inside a Copilot conversation. See section 12 for what is and isn't possible.
- SPFx Heft-era releases are SharePoint Online only.
- SPFx 1.24 (React 18, Node 24/26) is in preview. Staying on 1.23.2 until it reaches GA is deliberate.
- Fluent UI v9 inside SPFx is not an officially documented path yet. `spfx/README.md` and `docs/research/` list the open questions.

## 12. Other hosts: Outlook, Microsoft 365 Copilot, Copilot chat and iframe tabs

Summary of [docs/research/teams-iframe-tab-vs-spfx.md](./docs/research/teams-iframe-tab-vs-spfx.md) (sources and open items are there). Nothing in this section has been tested in this repo.

**Recommendation:** keep SPFx as the host. It is the only option that serves SharePoint pages and Teams from one package without hosting a web app. Add other hosts only when a requirement needs them, and reuse the chat UI and the API rather than the web part.

### 12.1 Outlook and the Microsoft 365 Copilot app

- Since SPFx 1.16, SPFx **personal tabs** are supported in Outlook and the Microsoft 365 app ([Extend a Teams personal tab across Microsoft 365](https://learn.microsoft.com/microsoftteams/platform/m365-apps/extend-m365-teams-personal-tab)). The Microsoft 365 app is being renamed **Microsoft 365 Copilot app**, so this is how the web part can appear in the Copilot app today, as a pinned app.
- That is the app shell, **not** the Copilot conversation (12.2).
- It needs a Teams manifest version that supports the extension. Which version the auto-generated package uses for SPFx 1.23 is **not verified**; you may need a custom `TeamsSPFxApp.zip` (6.4).

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
