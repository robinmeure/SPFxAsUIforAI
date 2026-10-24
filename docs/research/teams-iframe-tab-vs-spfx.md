# Teams iframe tab (manifest-pointed) vs. SPFx — findings and validation for this repo

**Date of research:** October 2026
**Question:** Can we skip hosting an SPFx web part and instead point a Teams tab at a hosted page through the app manifest? How does that compare with this repo's SPFx app (one codebase for SharePoint Online, Teams and, eventually, Copilot)?
**Sources:** Microsoft Learn (cited inline). Items not confirmed against a primary source are marked **UNVERIFIED**. The citations come from a research pass that was not independently re-fetched, so re-check anything marked critical before committing to it.

## Summary

1. **Yes, it works, and it is the standard Teams tab model.** A tab is an iframe at a URL declared in the app manifest. Teams does not host your code. [Teams app package](https://learn.microsoft.com/microsoftteams/platform/concepts/build-and-test/apps-package)
2. **For this repo, SPFx remains the right default.** It is the only option that gives the same package on SharePoint pages and in Teams (tab and personal app), and it can reach Outlook and the Microsoft 365 app. The iframe model adds a web host you must operate, plus its own sign-in and framing configuration, and gives no SharePoint reuse.
3. **Neither model puts the web part or tab inside a Copilot conversation today.** A personal tab (SPFx since 1.16, or iframe) can appear in the **Microsoft 365 Copilot app** (the renamed Microsoft 365 app) as a pinned app, but that is the app shell, not the chat. SPFx "Copilot UX components" are a different base class and are **preview-only** (SPFx 1.24+, not for production); they can ship in the same `.sppkg`, but as a different component. MCP apps are a separate, new surface. So "eventually even in Copilot chat" means **reusing the chat UI and the API**, not the web part component.
4. **Recommended direction:** keep SPFx for SharePoint and Teams, make the chat UI host-agnostic (it nearly is already), and treat an iframe SPA and a Copilot surface as optional additional hosts that share the same UI and API.

## 1. Iframe tab: manifest mechanics

- Two capabilities: **`staticTabs`** (personal app; since manifest v1.16 also usable in group chat, channel and meeting contexts via `scopes`/`context`, with no configuration dialog — [Create a tab: extend static tabs](https://learn.microsoft.com/microsoftteams/platform/tabs/how-to/create-personal-tab#extend-static-tabs-to-group-chat,-channels,-and-meetings); the schema page's prose saying "personal only" is outdated) and **`configurableTabs`** (legacy channel/chat/meeting model that loads a `configurationUrl` first). Microsoft's guidance is to **migrate configurable tabs to static tabs**. [root.staticTabs](https://learn.microsoft.com/microsoft-365/extensibility/schema/root-static-tabs), [Create a configurable tab](https://learn.microsoft.com/microsoftteams/platform/tabs/how-to/create-channel-group-tab)
- Key fields: `entityId`, `name`, `contentUrl` (HTTPS), `websiteUrl` (needed for Teams mobile), `scopes` (`personal`, `groupChat`, `team`), `context` (e.g. `personalTab`, `channelTab`, `meetingSidePanel`, `meetingStage`), and top-level `validDomains` (allow-list for cross-domain navigation). `configurableTabs` adds `configurationUrl` and `canUpdateConfiguration`.
- Manifest generation: the schema is now the **Microsoft 365 app manifest / unified manifest**; the current version surfaced in the research is **v1.30** (Aug 2026). Same `staticTabs`/`configurableTabs` fields. [Schema reference](https://learn.microsoft.com/microsoft-365/extensibility/schema/)
- Packaging and distribution: zip manifest plus two icons; sideload (needs custom-app upload enabled; not available in GCC High, DoD or 21Vianet), Developer Portal, Microsoft 365 Agents Toolkit, org catalog, Teams admin center. [Upload your app](https://learn.microsoft.com/microsoftteams/platform/concepts/deploy-and-publish/apps-upload)
- Code changes on your host do not need a manifest re-upload; manifest or configuration changes do require an update. (Same page.)

## 2. Hosting requirements for an iframe tab

- HTTPS, and a CSP `frame-ancestors` response header that allows the hosts: `*.cloud.microsoft`, Teams (`teams.microsoft.com`, `*.teams.microsoft.com`), the Microsoft 365 app (`*.microsoft365.com`, `*.office.com`) and Outlook hosts. Add `*.cloud.microsoft` and **keep** the legacy entries. `X-Frame-Options` can't express this list; Microsoft mentions it only as a deprecated, single-origin `ALLOW-FROM` fallback for old browsers. [Tab prerequisites](https://learn.microsoft.com/microsoftteams/platform/tabs/how-to/tab-requirements)
- Initialize TeamsJS v2 with `app.initialize()` (v2.19.0+ on the new domain), otherwise the page isn't displayed. `app.notifySuccess()` is only mandatory when the manifest sets `showLoadingIndicator: true` (then Teams shows an error after 30 s without it); it is a different API from `authentication.notifySuccess()` used in popup auth. [Content page: native loading indicator](https://learn.microsoft.com/microsoftteams/platform/tabs/how-to/create-tab-pages/content-page#show-a-native-loading-indicator). Sign-in pages cannot render inside the iframe.
- Cookies: the tab is cross-site content, so use explicit `SameSite=None; Secure` where cookies are needed. [SameSite update](https://learn.microsoft.com/microsoftteams/platform/resources/samesite-cookie-update)
- Read theme and context yourself via TeamsJS (`app.getContext()`, `registerOnThemeChangeHandler`).

## 3. Authentication in an iframe tab

| Option | What it needs | Fit for this repo's API |
|---|---|---|
| **Teams SSO** (`authentication.getAuthToken`) | `webApplicationInfo` (client ID and Application ID URI) in the manifest; Application ID URI in the form **`api://<tab-host-domain>/<client-id>`**, matching the tab's domain ([register](https://learn.microsoft.com/microsoftteams/platform/tabs/how-to/authentication/tab-sso-register-aad)); **pre-authorized Teams/M365 client IDs**; an exposed scope. **OBO is only needed** if the API must call another API (e.g. Graph) as the user ([tab SSO code](https://learn.microsoft.com/microsoftteams/platform/tabs/how-to/authentication/tab-sso-code)) | Works with the existing `access_as_user` scope and no OBO (the API calls nothing downstream). Needs a second identifier URI next to `api://<client-id>` (SPFx keeps using that one), pre-authorization, and the API accepting both audiences (`ValidAudiences`) |
| **Nested App Authentication (NAA)** | An **SPA** registration with redirect `brk-multihub://<your-domain>`, TeamsJS initialized before MSAL, `createNestablePublicClientApplication` with `supportsNestedAppAuth: true`; no pre-authorization or OBO hop | Needs a SPA registration (add the SPA platform to the existing app or create a second one) and requesting `api://<client-id>/access_as_user`. The API validation code is unchanged. [NAA](https://learn.microsoft.com/microsoftteams/platform/concepts/authentication/nested-authentication) |
| **Popup auth** (`authentication.authenticate`) | A popup completes an OAuth flow you implement (authorize, redirect, state validation) and posts the result back with `authentication.notifySuccess()` | Works with Entra ID too (Microsoft's walkthrough uses Entra as the example) ([auth flow](https://learn.microsoft.com/microsoftteams/platform/tabs/how-to/authentication/auth-flow-tab)), but NAA or SSO is simpler for this API |

NAA is Microsoft's forward path for SPAs in Teams, Outlook and Microsoft 365. For an **SPFx** web part the opposite applies: MSAL.js directly is not supported, and the token comes from SPFx (`AadTokenProvider`). See the root README, section 1.

## 4. Reach beyond Teams (Outlook, Microsoft 365 app)

- A **personal** tab can be extended to Outlook and the Microsoft 365 app: bump the manifest version, add the Outlook/M365 `frame-ancestors`, then re-upload and preview. TeamsJS v2 is the SDK in all hosts. [Extend a Teams personal tab across Microsoft 365](https://learn.microsoft.com/microsoftteams/platform/m365-apps/extend-m365-teams-personal-tab)
- **SPFx personal tabs are supported there too since SPFx 1.16** (same manifest bump), so SPFx does not lose reach to the iframe model. (Same page.) Which manifest version SharePoint's auto-generated Teams package uses for SPFx 1.23 is **UNVERIFIED**; a custom `TeamsSPFxApp.zip` may be needed.
- The **Microsoft 365 app is being renamed Microsoft 365 Copilot app** (note on the same page), so this is already a way to appear in the Copilot app, as a pinned app rather than inside a conversation. [Extend to Microsoft 365](https://learn.microsoft.com/office/dev/add-ins/concepts/extend-to-microsoft-365)
- The research found no explicit statement that Office.js is not required; the tutorial simply does not mention it. **UNVERIFIED (soft).**

## 5. Microsoft 365 Copilot

| Mechanism | Status (Oct 2026) |
|---|---|
| Plain Teams tab iframe rendered inside a Copilot chat | **Not possible**; no documented path |
| Declarative agent or API plugin returning **Adaptive Cards** | Available; the card dialog variant is preview. Cards, not your hosted app. [Adaptive Cards for agents](https://learn.microsoft.com/agents/design-guidelines/adaptive-cards-for-agent-design) |
| **MCP apps / OpenAI Apps SDK widgets** in declarative agents | New surface with prerequisites (Agents Toolkit ≥ 6.12.0). Widgets run in a sandboxed per-server domain, driven by MCP tool responses, with their own JS surface rather than TeamsJS. GA vs preview **UNVERIFIED**. [MCP apps](https://learn.microsoft.com/microsoft-365/copilot/extensibility/plugin-mcp-apps) |
| **SPFx Copilot UX components** | **Preview** ("do not use in production"), SPFx **1.24+**, extends `BaseCopilotComponent` (not `BaseClientSideWebPart`), packaged in the same `.sppkg` with a declarative agent definition. During preview they render only in the Microsoft 365 Copilot experience; cross-surface reuse (web part + tab + Copilot) is described as the direction, **not yet functional**. [Overview](https://learn.microsoft.com/sharepoint/dev/spfx/copilot/overview-copilot-apps) |
| "Copilot pages", Loop components, SharePoint Embedded as a path to show a tab in Copilot | **Not found** in primary docs; treat as unconfirmed |

## 6. Comparison

| Dimension | SPFx web part (this repo) | Iframe tab (manifest `contentUrl`) |
|---|---|---|
| Hosting | None to operate; `.sppkg` in the SharePoint app catalog, assets served by SharePoint/CDN | You host an HTTPS web app (App Service, containers, ...) |
| SharePoint pages | Native, same package | Not applicable; separate SPA (an iframe/embed web part would be a different feature) |
| Teams (tab, personal app) | Yes (`supportedHosts`), needs Add/Sync to Teams | Native |
| Outlook / Microsoft 365 app | Personal tabs since SPFx 1.16 | Yes via manifest bump and CSP |
| Copilot | App shell: personal tab in the Microsoft 365 Copilot app (as above). Chat: preview-only Copilot UX components, a separate component type (SPFx 1.24+) that can ship in the same `.sppkg` | App shell: same. Chat: no direct path; needs an agent plus Adaptive Cards or an MCP app |
| Auth to a custom API | `AadTokenProvider` plus admin approval on the SharePoint API access page; no client app registration | NAA (SPA platform) or Teams SSO (domain-qualified Application ID URI, pre-authorized Teams clients; OBO only for downstream calls); consent through Entra/Teams admin |
| Theming | `ThemeProvider` on pages, TeamsJS in Teams | TeamsJS only; restyle yourself |
| Framing/CSP/cookies | Handled by SharePoint's hosting | You must send CSP `frame-ancestors`, handle SameSite cookies and set `validDomains` |
| Updates | Upload new `.sppkg` (bump version) | Deploy your site; manifest changes need a re-upload |
| Admin surface | SharePoint app catalog, API access, Teams admin center | Teams admin center (custom-app policy), Entra consent |
| Cost | No extra hosting beyond the tenant and the API | Your hosting plus the API |

Note: a statement in the source research that SPFx "inherits SPO/Graph auth, no OBO wiring" is only true for SharePoint/Graph data. For a custom API this repo still needs the explicit `webApiPermissionRequests` grant.

## 7. Validation against this repo

| Repo fact | Consequence |
|---|---|
| The web part targets SharePoint pages, Teams tabs and Teams personal apps from one package (`supportedHosts`). | This is exactly what the iframe model cannot give for SharePoint. Dropping SPFx would mean losing SharePoint-page reuse. |
| The chat component ([HelloCopilot.tsx](../../spfx/src/webparts/helloCopilot/components/HelloCopilot.tsx)) uses SPFx only for two things: `context.aadTokenProviderFactory` (token) and `context.instanceId` (storage key). Theme comes in as a prop. | A host-agnostic UI is a small refactor: inject `getToken(resource)`, `storageKey` and `theme`. No Fluent, Markdown or streaming code is SPFx-specific. |
| The API (`api/Program.cs`) validates `Audience = api://<client-id>` and the `access_as_user` scope, with CORS from `Cors:AllowedOrigins`. It calls no downstream API. | An iframe SPA reuses the same API, scope and SSE contract, and needs no OBO. CORS must add the SPA's own origin. NAA requesting `api://<client-id>/access_as_user` keeps the current audience. Teams SSO adds a domain-qualified audience, so the API must accept both (`ValidAudiences`). Keep the token version consistent (root README, section 5.2). In Teams, the SPFx web part is expected to send the **SharePoint** origin, so the current `https://teams.microsoft.com` CORS entry may be unnecessary (verify). |
| The documented registration (root README, section 5) has no pre-authorized clients and no SPA platform. The live tenant registration was not inspected. | Teams SSO would require a second identifier URI plus pre-authorizing Teams/M365 client IDs. NAA would require an SPA platform with `brk-multihub://` redirect. Neither is needed for SPFx. |
| Chat history is `localStorage`, keyed by `instanceId`. | Browsers partition storage for framed content, and the instance ID differs per host, so a SharePoint page and Teams already keep separate histories under SPFx too. An iframe tab needs a stable key (for example the Teams `entityId` plus user). Server-side history is the only way to share it. |
| The repo is on SPFx **1.23.2** (React 17), GA. | Copilot UX components need **SPFx 1.24 (preview)**; its React requirement is **UNVERIFIED**. Not recommended for production until GA. |
| The theme logic already handles Teams (TeamsJS) and SharePoint (`ThemeProvider`). | The Teams theme mapping is reusable in an iframe SPA, but TeamsJS must come from `@microsoft/teams-js` (initialized by the app) instead of `this.context.sdks.microsoftTeams`. The SharePoint branch would be dropped. |
| SPFx personal tabs reach Outlook and the Microsoft 365 (Copilot) app since 1.16. | Reach may be available through the existing package, depending on the manifest version SharePoint generates (UNVERIFIED). Not tested in this repo. |

## 8. Recommendation

1. **Keep SPFx as the primary host** for SharePoint and Teams. It is the only path that serves SharePoint pages from the same package and requires no app hosting.
2. **Decouple the chat UI from `WebPartContext`**: extract an adapter (`getToken`, `storageKey`, `theme`, `isTeams`) so the same component can run in other hosts.
3. **Add an iframe SPA host only if** you need (a) a surface outside SharePoint and Teams without the SPFx runtime, (b) independent deployment from the SharePoint app catalog, or (c) Outlook/Microsoft 365 behaviour you can't get from the SPFx package. If you do, prefer NAA with a SPA platform (keeps the current audience), add its origin to CORS and send CSP `frame-ancestors`.
4. **For Copilot chat, plan separately** and reuse the API: a declarative agent with Adaptive Cards (available) or an MCP app widget (new; confirm maturity) now, or SPFx Copilot UX components once SPFx 1.24 is GA. The web part component itself is not reused. For the Copilot **app shell**, the SPFx personal tab may already be enough (point 5).
5. **Before relying on any of this, verify in the target tenant**: Outlook/Microsoft 365 Copilot app rendering of the SPFx personal tab and the manifest version of the generated Teams package; the `Origin` headers sent to the API from each host (is `https://teams.microsoft.com` ever sent?); localStorage persistence in Teams desktop and web.

## Open items

- GA vs preview status of MCP apps; licensing and tenant gating (see the Copilot extensibility prerequisites page).
- Whether Office.js is ever required for the Outlook/M365 extension of a tab.
- Minimum `@microsoft/teams-js` and `@azure/msal-browser` versions for NAA, and whether NAA is endorsed inside SPFx (the SPFx docs only endorse `AadHttpClient`/`AadTokenProvider`).
- "Copilot pages", Loop components and SharePoint Embedded as Copilot surfaces: nothing found in primary sources.
