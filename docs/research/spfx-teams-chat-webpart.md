# SPFx Web Part with a Copilot-Style Chat UI, Surfaced in Teams — Research Report

**Date of research:** 2026-10-21
**Scope constraint (explicit project decision):** No Bot Framework / Azure Bot Service / Teams AI Library (Teams SDK) bot runtime / Microsoft 365 Agents SDK activity protocol / Direct Line / Bot Framework Web Chat / Copilot Studio bot embedding. The solution is a **pure SPFx web part UI that calls a custom Web API directly over HTTPS** and renders the response itself. This constraint shapes the architecture, the chat-UI component choices, and the "alternatives" section below.

> Citation convention: every claim is followed by its source URL. Items that could not be confirmed against a primary source are explicitly marked **UNVERIFIED**. Primary sources used: `learn.microsoft.com`, `github.com/SharePoint/sp-dev-docs`, `github.com/pnp/sp-dev-fx-webparts`, `github.com/microsoft/fluentui-contrib`, `github.com/AzureAD/microsoft-identity-web`, `github.com/AzureAD/microsoft-authentication-library-for-js`, npm registry (publisher-authoritative package metadata).

---

## 1. Toolchain & scaffolding

### 1.1 Current SPFx version and compatibility matrix

- **Latest GA release: SPFx v1.23.2** (released June 30, 2026) — "a *minor bump* that addresses known npm vulnerabilities, fixes reported issues, and provides readiness for the list panel override." Install the generator with `npm install @microsoft/generator-sharepoint@latest --global`.
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/release-1.23.2
  (v1.23.1 was delisted due to regressions, per the same page.)
- v1.23.0 (May 13, 2026) added list-view command-set grouping and a **preview SPFx CLI** (`@microsoft/spfx-cli`) intended to eventually replace the Yeoman generator, and **deprecated the hosted/online workbench** (retiring Dec 1, 2026, replaced by the SharePoint Framework Debug Toolbar).
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/release-1.23.0
- A **v1.24 preview** line is underway (not GA as of this research): reached `rc.0` on Oct 2, 2026, adds **React 18 support**, **Node.js v24 & v26 support**, and preview "Copilot UX components." Install via `npm install @microsoft/generator-sharepoint@next --global`.
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/release-1.24.0
- **Compatibility matrix** (`https://learn.microsoft.com/en-us/sharepoint/dev/spfx/compatibility`):

  | SPFx | Node.js (LTS) | TypeScript | React |
  |---|---|---|---|
  | 1.24 (preview) | v22, v24, v26 | v2.9–v5.8 | v18.3.1 |
  | 1.23.2 / 1.23.0 (current GA) | **v22 only** | v2.9–v5.8 | **v17.0.1** |
  | 1.21.x | v22 | v5.3 | v17.0.1 |

  The page warns: "Using incompatible React versions can cause silent runtime failures... use `npm install react@17.0.1 react-dom@17.0.1 --save-exact`."
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/compatibility

  **Recommendation for this project:** target current GA **SPFx 1.23.2** with **Node.js v22 LTS** and **React 17.0.1** unless there is an explicit reason to adopt the 1.24 preview (React 18) before it reaches GA.

### 1.2 Heft-based build toolchain (new default since v1.22)

- "Starting with SPFx v1.22.0, new projects use **Heft** as the build task orchestrator instead of the gulp-based toolchain used in SPFx v1.0 – v1.21.1... the underlying bundling technology (webpack) remains the same." (Released Dec 10, 2025.)
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/release-1.22.0
- Heft is "a config-driven toolchain that invokes other popular tools like TypeScript, ESLint, Jest, Webpack, and API Extractor." Build scripts change from `gulp bundle` / `gulp clean` / `gulp test` → `heft build` / `heft clean` / `heft start`.
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/toolchain/sharepoint-framework-toolchain-rushstack-heft
- Package-level migration: gulp packages (`@microsoft/sp-build-web`, `gulp`, `@microsoft/rush-stack-compiler-*`) are replaced by `@microsoft/spfx-web-build-rig`, `@microsoft/spfx-heft-plugins`, `@rushstack/heft`, `@typescript-eslint/parser`. New config files: `heft.json`, `config/rig.json`, `config/typescript.json`.
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/toolchain/migrate-gulptoolchain-hefttoolchain
- Global install: `npm install @rushstack/heft yo @microsoft/generator-sharepoint --global`. "SharePoint Framework v1.22.* is supported on Node.js v22 LTS (aka Jod)."
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/set-up-your-development-environment
- Existing (pre-1.22) gulp-based projects are **not force-migrated**: they "can continue using their current gulp-based builds without modification" during a transition window; upgrade via `npx -p @pnp/cli-microsoft365@latest m365 spfx project upgrade --toVersion 1.22.2 --output md`. The generator still offers a `--use-gulp` flag for teams that need the legacy toolchain.
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/set-up-your-development-environment
- Packaging commands in the Heft era: `heft build --production` / `heft package-solution --production` (superseding `gulp bundle --ship` / `gulp package-solution --ship`).
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/integrate-with-teams-introduction

### 1.3 Scaffolding with the Yeoman generator

- Command: `yo @microsoft/sharepoint --skip-install`. The generator is now branded "**Microsoft 365 SPFx Yeoman Generator**." Prompts: solution name → component type (WebPart/Extension/…) → web part name → template (e.g. **Minimal**, added in v1.14, "includes no theme or host environment code").
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/yeoman-generator-for-spfx-intro
- Scaffolded projects now include Heft-era config by default (`config/rig.json`, `config/typescript.json`, `.eslintrc.js`) **plus auto-generated Teams icon placeholders** — `teams/{guid}_outline.png` and `teams/{guid}_color.png` are scaffolded for every new web part out of the box.
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/yeoman-generator-for-spfx-intro
- A successor **SPFx CLI** is in preview: `npm install @microsoft/spfx-cli --global`; scaffold with `spfx create --template webpart-react --library-name my-spfx-library --component-name "Hello World"`. Templates are open-sourced at `github.com/SharePoint/spfx`. Microsoft's stated GA target was June 2026 (not confirmed GA in later release notes reviewed — treat as **in progress**).
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/release-1.23.0

### 1.4 Enabling Teams support — `supportedHosts`

Primary docs: "Building Microsoft Teams Tabs using SharePoint Framework" and the step-by-step tutorial.
https://learn.microsoft.com/en-us/sharepoint/dev/spfx/integrate-with-teams-introduction
https://learn.microsoft.com/en-us/sharepoint/dev/spfx/web-parts/get-started/using-web-part-as-ms-teams-tab

- "Starting with SharePoint Framework v1.8, you can build tabs for Microsoft Teams with the SharePoint Framework tooling... As part of SharePoint Framework v1.10 you can also publish your solution as a Microsoft Teams personal app." (same URL)
- Manifest configuration (`*.manifest.json` of the web part):
  ```json
  "supportedHosts": ["SharePointWebPart", "TeamsTab", "TeamsPersonalApp"]
  ```
  - `SharePointWebPart` — normal use on SharePoint pages.
  - `TeamsTab` — usable as a tab inside a Teams channel/group chat.
  - `TeamsPersonalApp` — usable as a personal app pinned in the Teams left rail.
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/integrate-with-teams-introduction
- **`TeamsMeetingStage`** — **UNVERIFIED / NOT FOUND**. The fetched SPFx primary docs enumerate only `SharePointWebPart`, `TeamsTab`, and `TeamsPersonalApp` as `supportedHosts` values; no explicit SPFx documentation of a `TeamsMeetingStage` value was located in this pass. If meeting-stage support is required, verify directly against `https://learn.microsoft.com/en-us/microsoftteams/platform/apps-in-teams-meetings/meeting-apps-apis` and the SPFx manifest schema before relying on it.

### 1.5 Deployment via app catalog and "Sync to Teams"

https://learn.microsoft.com/en-us/sharepoint/dev/spfx/deployment-spfx-teams-solutions

Two supported models:

1. **Automatic manifest/package generation** — uploading a qualifying SPFx package to the tenant app catalog enables an "**Add to Teams**" button; SharePoint Online "dynamically creates the Microsoft Teams app manifest file" and "dynamically creates the Microsoft Teams app package," then "deploys the generated… package to the tenant's Microsoft Teams app store." Field mapping: Teams manifest `packageName` ← solution `alias`, `id` ← solution `id`, `name.short` / `description.short/long` ← the web part's `preconfiguredEntries[0].title/description`. A REST endpoint can download the generated package: `_api/web/tenantappcatalog/downloadteamssolution(id)/$value`.
2. **Developer-provided manifest/package** — place a `TeamsSPFxApp.zip` (the zipped *contents* of `./teams`, not the folder itself) in the solution's `./teams` folder. When an admin clicks "**Sync to Teams**," SharePoint looks for `./teams/TeamsSPFxApp.zip` and deploys it instead of auto-generating one.

   The Teams manifest must include:
   ```json
   "webApplicationInfo": {
     "resource": "https://{teamSiteDomain}",
     "id": "00000003-0000-0ff1-ce00-000000000000"
   }
   ```
   for the SharePoint-brokered token acquisition described in §2 to work. Dynamic placeholders usable in manifest URLs: `{teamSiteDomain}`, `{teamSitePath}`, `{locale}`.
- Side-loading alternative: Team → Manage team → Apps → Upload a custom app, using the manifest from `./teams`.
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/integrate-with-teams-introduction

### 1.6 Detecting Teams context — `this.context.sdks.microsoftTeams`

```ts
const teamsContext = await this.context.sdks.microsoftTeams?.teamsJs.app.getContext();
```
> "The page context in a web part contains a reference to the Teams JavaScript SDK so that you can easily get access to the Teams context when your web part is rendered as a tab."
https://learn.microsoft.com/en-us/sharepoint/dev/spfx/integrate-with-teams-introduction

- `context.sdks.microsoftTeams.teamsJs` exposes the Teams JS SDK v2 instance; `.context` is a legacy v1-style sub-property.
- **Deprecation**: the v1.16.0 release notes state precisely: "Deprecated `context.sdks.microsoftTeams.context`. Use `context.sdks.microsoftTeams.teamJs.app.getContext` instead." (note: the broader `sdks.microsoftTeams` object itself is *not* deprecated — only the `.context` sub-property; be careful, as the "integrate-with-teams" overview page's summary wording is less precise than the dated release notes.)
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/release-1.16.0
- Important operational note: "Installing and initializing custom versions of the Teams JavaScript client SDK is **unsupported**… Although Teams JavaScript client SDK types are tied to the SPFx version used, the actual version loaded to the tenant may differ." Current default Teams JS SDK version bundled by SPFx: **v2.32** (per the v1.21.0 release notes).
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/integrate-with-teams-introduction
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/release-1.21.0

### 1.7 Theme handling in Teams

Two complementary, **non-bot**, mechanisms:

- **A. SPFx's own `ThemeProvider` (`@microsoft/sp-component-base`)** — canonical, host-agnostic mechanism that works the same whether the web part is rendered on a SharePoint page or inside a Teams tab:
  ```ts
  import { ThemeProvider, IReadonlyTheme } from '@microsoft/sp-component-base';
  const themeProvider = this.context.serviceScope.consume(ThemeProvider.serviceKey);
  const theme: IReadonlyTheme | undefined = themeProvider.tryGetTheme();
  // or, for Fluent UI v9 tokens:
  const themeV2 = themeProvider.tryGetThemeV2(); // returns a @fluentui/tokens v9 Theme
  ```
  Enable with `"supportsThemeVariants": true` in the manifest; subscribe to theme-change events; in React, pass `themeVariant: IReadonlyTheme | undefined` as a prop and read `semanticColors` (e.g. `semanticColors.bodyBackground`).
  https://learn.microsoft.com/javascript/api/sp-component-base/themeprovider?view=sp-typescript-latest
  https://learn.microsoft.com/sharepoint/dev/spfx/web-parts/guidance/supporting-section-backgrounds#making-your-web-part-theme-aware
  Full theme token reference: https://learn.microsoft.com/sharepoint/dev/spfx/use-theme-colors-in-your-customizations
- **B. `@microsoft/teams-js` `app.getContext()`** — when running specifically as a Teams tab, the Teams JS context also carries theme info (legacy v1 shape exposed `theme` directly, e.g. `?...&theme=dark` on tab config URLs; TeamsJS v2's `app.Context` restructures this under nested properties and additionally exposes a `sharepoint`/`sharePointSite` property when hosted in SharePoint).
  https://learn.microsoft.com/en-us/microsoftteams/platform/tabs/how-to/access-teams-context#access-context-information
  https://learn.microsoft.com/en-us/javascript/api/@microsoft/teams-js/app.context?view=msteams-client-js-latest

  **Recommended pattern:** use `ThemeProvider.tryGetThemeV2()` as the primary/canonical theme source (it already resolves to the host's light/dark/contrast variant on both SharePoint and Teams), and optionally cross-check `microsoftTeams.app.getContext()`'s theme value as a fallback when running inside Teams specifically. **UNVERIFIED**: no primary SPFx or Teams doc was found that states this exact combined pattern explicitly — it is a reasonable synthesis of the two independently-documented mechanisms above, not a single cited "best practice" page.

### 1.8 Deprecations to flag

1. **Isolated (domain-isolated) web parts retirement** — "As of **April 2nd, 2026** domain isolated web parts will be fully retired and stop working for existing tenants… New tenants onboarding from April 2nd, 2025 will not be able to use domain isolated web parts. This applies to all environments including Government Clouds and DoD." Domain-isolated web parts previously let a web part "securely communicate with APIs secured with Azure AD without exposing the access token to other components on the page" — losing this removes that extra token-isolation layer; standard (non-isolated) web parts must rely on other mitigations.
   https://learn.microsoft.com/en-us/sharepoint/dev/spfx/web-parts/isolated-web-parts-retirement
2. **Hosted (online) workbench** — last supported in v1.23.0; retires **Dec 1, 2026**; replaced by the SharePoint Framework Debug Toolbar.
   https://learn.microsoft.com/en-us/sharepoint/dev/spfx/release-1.23.0
3. **`@microsoft/office-ui-fabric-react-bundle`** deprecated in favor of `@fluentui/react` — v1.16.0 release notes.
   https://learn.microsoft.com/en-us/sharepoint/dev/spfx/release-1.16.0
4. **Gulp toolchain** is legacy/unmaintained going forward (still works for pre-1.22 projects, but flagged as technical debt by Microsoft itself).
   https://learn.microsoft.com/en-us/sharepoint/dev/spfx/toolchain/sharepoint-framework-toolchain-rushstack-heft
5. **On-premises ceilings**: all current (Heft-era, v1.22+) SPFx releases are **SharePoint Online only**; SharePoint Server Subscription Edition tops out at SPFx v1.5, SharePoint 2019 at v1.4.1, SharePoint 2016 FP2 at v1.1.
   https://learn.microsoft.com/en-us/sharepoint/dev/spfx/compatibility

---

## 2. Authentication — SPFx calling an Entra-protected custom API

### 2.1 AadHttpClient / AadTokenProvider — the recommended, SPFx-native approach

- Canonical doc: https://learn.microsoft.com/en-us/sharepoint/dev/spfx/use-aadhttpclient
  > "Client-side applications that are incapable of storing a secret, such as SharePoint Framework solutions, use a specific type of OAuth flow… In SharePoint Framework solutions, that's already done as part of the framework through **MSGraphClient** and **AadHttpClient**, both introduced in SharePoint Framework v1.4.1."
  > **"Directly using Microsoft Authentication Library for JavaScript with SPFx is not supported with SPFx v1.4.1 and beyond. `AADHttpClient` is the recommended approach to authenticate for using Entra ID secured resources."**
- API shape: `AadHttpClient.get/post/fetch(url, configuration, options)`, constructed via `(serviceScope, resourceEndpoint, options)`, where `resourceEndpoint` is "the resource for which the token should be obtained" (typically the API's Application ID URI).
  https://learn.microsoft.com/en-us/javascript/api/sp-http-base/aadhttpclient?view=sp-typescript-latest
- **`webApiPermissionRequests`** in `config/package-solution.json`:
  ```json
  {
    "$schema": "https://developer.microsoft.com/json-schemas/spfx-build/package-solution.schema.json",
    "solution": {
      "name": "spfx-chat-client-side-solution",
      "id": "5d16587c-5e87-44d7-b658-1148988f212a",
      "version": "1.0.0.0",
      "includeClientSideAssets": true,
      "skipFeatureDeployment": true,
      "webApiPermissionRequests": [
        { "resource": "Chatbot API", "scope": "access_as_user" }
      ]
    },
    "paths": { "zippedPackage": "solution/spfx-chat.sppkg" }
  }
  ```
  `resource` **must be the Entra app's `displayName`**, not its `objectId` ("you will get an error when trying to approve the permission request" otherwise).
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/use-aadhttpclient
- **Tenant admin approval** happens on the SharePoint Admin Center "**API access**" page (short link `https://go.microsoft.com/fwlink/?linkid=2185223`):
  > "The API access page shows pending and approved requests… The admin role required… depends on the API. To approve permissions to any of the third-party APIs registered in the tenant, the application administrator role is sufficient. To approve permissions for Microsoft Graph or any other Microsoft API, the Global Administrator role is required."
  > "Permissions of type delegated are added to the **SharePoint Online Client Extensibility Web Application Principal** in Microsoft Entra ID."
  https://learn.microsoft.com/en-us/sharepoint/api-access
  PowerShell equivalents: `Get-SPOTenantServicePrincipalPermissionRequests`, `Approve-SPOTenantServicePrincipalPermissionRequest -RequestId <Guid>`, `Deny-SPOTenantServicePrincipalPermissionRequest`, `Get-SPOTenantServicePrincipalPermissionGrants`, `Revoke-SPOTenantServicePrincipalPermission -ObjectId <String>`.
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/use-aadhttpclient
- **Service-principal rename check (explicitly investigated):** as of the most recently fetched doc revision (`sharepoint/api-access`, updated 2026-07-01), the principal is **still named "SharePoint Online Client Extensibility Web Application Principal."** No message-center post, announcement, or doc revision describing a rename to e.g. "SharePoint Online Web Client Extensibility" was found. **UNVERIFIED / NOT FOUND** — treat any claim of such a rename as unconfirmed until a primary source surfaces.
- **Multi-tenant / cross-tenant apps**: since **SPFx v1.15.2**, `webApiPermissionRequests` entries can carry extra `appId` and `replyUrl` properties to auto-register a service principal for the target app in the consuming tenant ("If either `appid` or `replyUrl` is included, they are both required"). Background: application objects are the global representation; service principals are the tenant-local representation, and permissions are granted against the service principal.
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/use-aadhttpclient

### 2.2 MSAL.js (`@azure/msal-browser`) directly inside an SPFx web part

- **Explicitly not supported**, in the exact wording quoted above (§2.1): "Directly using Microsoft Authentication Library for JavaScript with SPFx is not supported with SPFx v1.4.1 and beyond." This is a compatibility disclaimer, not merely a style preference.
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/use-aadhttpclient
- **Teams iframe/popup problems**: no single Learn page enumerates "iframe restrictions / third-party cookies / popup blockers" as a bullet list for raw MSAL-in-Teams; the evidence is indirect — the Teams **tab SSO** model's reliance on `TeamsJS.authentication.getAuthToken()` with Teams-mediated **token prefetching** ("The access token is prefetched by Teams to improve performance and load time of the app in the Teams environment") exists specifically because iframe-embedded SPAs cannot reliably run silent/popup/redirect MSAL flows inside Teams' sandboxed iframe (desktop/web) due to cookie/iframe restrictions — this is **inferred from the documented workaround's existence**, not a verbatim enumeration.
  https://learn.microsoft.com/en-us/microsoftteams/platform/tabs/how-to/authentication/tab-sso-overview
- **Nested App Authentication (NAA)** — the modern replacement for the `getAuthToken()` host-mediated model:
  > "Nested app authentication (NAA) is supported only in single-page application (SPA), such as tabs." "NAA is a new authentication protocol for SPAs that are embedded in host environments, such as Teams, Outlook, and Microsoft 365… Microsoft utilizes this model in Teams tabs, personal apps, and Office Add-ins." "You don't need to use the `getAuthToken` function in TeamsJS." "You don't need to preauthorize your hosts, such as Teams or Microsoft 365, to call your endpoints."
  https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/authentication/nested-authentication
  Steps: 1) Register your SPA (Entra app registration with SPA redirect type), 2) Add trusted brokers, 3) Initialize public client app, 4) Acquire your first token, 5) Call an API.
  The `createNestablePublicClientApplication` API is documented primarily on the Office Add-ins side:
  > "Configure your add-in to use NAA by calling the `createNestablePublicClientApplication` function in MSAL. MSAL returns a public client application that can be nested in a native application host (for example, Outlook) to acquire tokens."
  https://learn.microsoft.com/en-us/office/dev/add-ins/develop/enable-nested-app-authentication-in-your-add-in#configure-msal-config-to-use-naa
  - **UNVERIFIED**: exact `@azure/msal-browser` semver that first introduced `createNestablePublicClientApplication` (the visible CHANGELOG window only reached back to v5.18.0/Aug 2026, which predates this feature's introduction — not confirmed in this pass).
  - **UNVERIFIED**: minimum `@microsoft/teams-js` version required for NAA (not stated in the fetched Teams NAA doc; it describes a runtime capability check instead: "you can invoke the specified API to determine its status. A return value of `true` indicates support for NAA.").
  - **Does NAA apply to SPFx Teams tabs?** **No explicit primary-source statement either way.** SPFx's own "Build for Microsoft Teams" overview frames all Entra auth for SPFx Teams tabs around **AadHttpClient/MSGraphClient**, not MSAL/NAA:
    > "Your Microsoft Teams solutions typically need to connect to other services exposed via APIs… SharePoint Framework has integrated support for requesting and managing API permission access. Additionally, it contains ready-to-use libraries for communicating with the Microsoft Graph and other APIs secured with Microsoft Entra ID."
    https://learn.microsoft.com/en-us/sharepoint/dev/spfx/build-for-teams-overview
    Combined with the "not supported" statement for raw MSAL in SPFx, the reasonable **inference** (not an explicit documented statement) is that NAA is designed/documented for "normal" Teams SPA tabs built outside SPFx, and the SPFx "not supported" stance would extend to MSAL-based NAA too. **Flag this conclusion as inferred/UNVERIFIED** — do not treat it as settled unless Microsoft publishes an explicit statement.

  **Recommendation:** use **AadHttpClient** as the primary mechanism for this project; do not attempt MSAL/NAA inside the SPFx web part given the explicit "not supported" language, unless a later, explicit Microsoft statement reverses this guidance.

### 2.3 App registration setup for the backend API

- **Expose an API / Application ID URI** (generic Entra guidance — no SPFx-specific override format was found):
  > "Select Expose an API. At the top of the page, select Add next to Application ID URI. **This defaults to `api://<application-client-id>`**. The App ID URI acts as the prefix for the scopes you'll reference in your API's code, and it must be globally unique."
  https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-configure-app-expose-web-apis
  **UNVERIFIED**: no SPFx-specific doc was found mandating a different required format (e.g. `api://<tenantId>/<appId>`); `AadHttpClient` resolves the resource by **Entra app `displayName`** in `webApiPermissionRequests`, and `AadHttpClient.getClient(resourceId)` takes the resource identifier (commonly the Application ID URI) at runtime — the generic default (`api://<appId>`) is sufficient for this flow.
  https://learn.microsoft.com/en-us/javascript/api/sp-http-base/aadhttpclient?view=sp-typescript-latest
- **Delegated scope** (e.g. `access_as_user`): defined via "Add a scope" on the same "Expose an API" page — scope name, who can consent (admins/users or admins-only), admin/user consent display text, state (enabled).
  https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-configure-app-expose-web-apis
- **Separate client-app registration — when needed**:
  - For **AadHttpClient**, **no separate client-app registration is required**. Delegated permissions are granted to the tenant-wide, Microsoft-provisioned **"SharePoint Online Client Extensibility Web Application Principal"**; you only register/expose the **backend API** app. https://learn.microsoft.com/en-us/sharepoint/api-access
  - For **MSAL/NAA**, a dedicated SPA app registration **is** required — NAA's "Configure NAA" step 1 is explicitly "Register your SPA… The app registration must have a name, supported account type, and SPA redirect." https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/authentication/nested-authentication#configure-naa
  - Given the project uses AadHttpClient (§2.2 recommendation), **only the API app registration is needed** — no separate SPFx client app registration.
- **App roles vs. scopes, and "Authorized client applications" (pre-authorization)**:
  > "Add app role: … Allowed member types… Applications… Value… the value displayed in the `roles` claim of a token."
  > "(Optional) To suppress prompting for consent… you can pre-authorize the client application… Under Authorized client applications, select Add a client application. Enter the Application (client) ID of the client application you want to pre-authorize… select the scopes for which you want to suppress consent prompting."
  https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-configure-app-expose-web-apis
  (This pre-authorization/PCA mechanism is what Teams/NAA-style scenarios use to avoid consent prompts for well-known first-party client IDs; for the AadHttpClient flow, the equivalent "pre-approval" step is the admin approving the pending request on the SharePoint API access page, §2.1.)
- **Multi-tenant considerations**: application objects are the global representation of an app; service principals are the tenant-local representation; permissions are granted to service principals. To use an Entra app cross-tenant, a service principal must exist in the consuming tenant — automatable via `appId`/`replyUrl` in `webApiPermissionRequests` since SPFx v1.15.2 (§2.1). Further reading: https://learn.microsoft.com/en-us/entra/identity-platform/app-objects-and-service-principals and https://learn.microsoft.com/en-us/entra/identity-platform/single-and-multi-tenant-apps

### 2.4 Deprecations / 2025–2026 changes relevant to auth

- **Isolated web parts retirement** (April 2, 2026 for existing tenants) removes an optional token-isolation layer that some AadHttpClient-based web parts relied on for defense-in-depth; AadHttpClient itself is unaffected and continues to work in standard (non-isolated) web parts.
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/web-parts/isolated-web-parts-retirement
- No other explicit 2024–2026 SPFx↔Entra-auth breaking changes were found beyond the v1.15.2 cross-tenant `webApiPermissionRequests` enhancement and the isolated-web-parts retirement.

---

## 3. Backend Web API design

### 3.1 Token validation (ASP.NET Core minimal API)

- Add `Microsoft.Identity.Web` (current NuGet package, v4.16.1 as surfaced in the .NET API docs) and wire it up:
  ```csharp
  builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
      .AddMicrosoftIdentityWebApi(builder.Configuration);
  ```
  > "In this API, we use the JSON Web Token (JWT) ... as the default authentication mechanism. Use the `AddAuthentication` method to register the JWT bearer scheme."
  https://learn.microsoft.com/entra/identity-platform/tutorial-web-api-dotnet-core-build-app
  Method reference (3 overloads) and package: https://learn.microsoft.com/dotnet/api/microsoft.identity.web.microsoftidentitywebapiauthenticationbuilderextensions.addmicrosoftidentitywebapi
  Canonical source repo: https://github.com/AzureAD/microsoft-identity-web
  > "Microsoft Identity Web is a library which contains a set of reusable classes used in conjunction with ASP.NET Core for integrating with the Microsoft identity platform... for Web applications... Protected web APIs, which optionally call protected downstream web APIs."
- `appsettings.json` audience configuration:
  ```json
  {
    "AzureAd": {
      "Instance": "https://login.microsoftonline.com/",
      "TenantId": "<tenant-id>",
      "ClientId": "<api-app-client-id>",
      "Audience": "api://<api-app-client-id>"
    }
  }
  ```
  > "If you've accepted the default App ID URI proposed by the Azure portal, you don't need to specify the audience. Otherwise, add an `Audience` property whose value is the App ID URI for your web API. This typically starts with `api://`."
  https://learn.microsoft.com/entra/identity-platform/scenario-protected-web-api-app-configuration
  Multiple accepted audiences: `options.TokenValidationParameters.ValidAudiences = new[] { ... };` (same URL).
- **Azure Functions**: two options.
  - **In-code Microsoft.Identity.Web** — same pattern as above, works for a standard `Authorization: Bearer <token>` header sent by an SPFx/Teams client.
  - **Easy Auth (App Service/Functions built-in auth)** — "a platform feature requiring little/no code": "You can use them to sign in users and access data by writing little or no code… For REST APIs, Azure Functions, and JavaScript browser clients… the client submits the authentication token to App Service for validation" — but note this client-directed flow typically uses an **`X-ZUMO-AUTH`** header and a server-side token store, not a plain `Authorization: Bearer` JWT.
    https://learn.microsoft.com/azure/app-service/overview-authentication-authorization
  - **UNVERIFIED**: no single Learn page directly compares Easy Auth vs. in-code Microsoft.Identity.Web specifically for an SPFx/Teams bearer-token caller. Given the SPFx client sends a standard bearer token (not going through App Service's provider-redirect flow), **in-code Microsoft.Identity.Web validation is the more directly-applicable, better-documented pattern** for this scenario — recommended for this project.

### 3.2 CORS

- Standard ASP.NET Core CORS configuration:
  ```csharp
  builder.Services.AddCors(options =>
  {
      options.AddPolicy("SpfxTeamsPolicy", policy =>
          policy.WithOrigins(
                  "https://<tenant>.sharepoint.com",
                  "https://teams.microsoft.com",
                  "https://*.teams.microsoft.com",
                  "https://*.cloud.microsoft")
              .AllowAnyHeader()
              .AllowAnyMethod());
  });
  // ...
  app.UseCors("SpfxTeamsPolicy");
  ```
  https://learn.microsoft.com/aspnet/core/security/cors
- **Credentials simplification**: "By default, the browser doesn't send credentials with a cross-origin request. Credentials include cookies and HTTP authentication schemes. To send credentials… the client must set `XMLHttpRequest.withCredentials` to `true`." Because the SPFx/Teams client authenticates via a bearer token in the `Authorization` header (not cookies, not browser-native HTTP auth), **`.AllowCredentials()` is not needed** and a non-wildcard `WithOrigins(...)` list is sufficient — this materially simplifies the CORS policy versus a cookie-based auth design.
  https://learn.microsoft.com/aspnet/core/security/cors
  https://learn.microsoft.com/entra/identity-platform/quickstart-v2-javascript-auth-code-acquire-token (bearer-header pattern)
- **UNVERIFIED (gap)**: no canonical Microsoft Learn page enumerates the exact origin set to allow for SharePoint/Teams callers (the list above is a reasonable, commonly-cited set, but not confirmed against a single authoritative source). The closest related, but not matching, doc concerns the Teams JS SDK's `validDomains` manifest property for `navigateCrossDomain` iframe navigation, which is a different concern (iframe navigation, not server CORS).
  https://learn.microsoft.com/javascript/api/@microsoft/teams-js/?view=msteams-client-js-1.13.1
  **Action item:** verify the actual `Origin` header sent by the deployed web part (via browser dev tools, in both SharePoint-page and Teams-tab hosting modes) and lock the CORS policy down to those exact confirmed origins rather than relying on an assumed list.

### 3.3 On-Behalf-Of (OBO) flow — if the API needs to call Microsoft Graph

- Protocol: "The client application makes a request to API A with token A… API A authenticates to the Microsoft identity platform token issuance endpoint and requests a token to access API B. The Microsoft identity platform token issuance endpoint validates API A's credentials along with token A and issues the access token for API B (token B) to API A."
  https://learn.microsoft.com/entra/identity-platform/v2-oauth2-on-behalf-of-flow#protocol-diagram
- Recommended implementation: "If you are building a web API on top of ASP.NET Core or ASP.NET Classic, we recommend that you use `Microsoft.Identity.Web`." Underlying MSAL.NET call: `IConfidentialClientApplication.AcquireTokenOnBehalfOf(scopes, UserAssertion)` — "make sure to pass an access token, not an ID token," and use the tenant-specific authority (from the inbound token's `tid` claim), not `/common` or `/organizations`.
  https://learn.microsoft.com/entra/msal/dotnet/acquiring-tokens/web-apps-apis/on-behalf-of-flow
- Wiring pattern:
  ```csharp
  builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration)
      .EnableTokenAcquisitionToCallDownstreamApi();
  builder.Services.AddInMemoryTokenCaches();
  builder.Services.AddMicrosoftGraph();
  ```
  https://learn.microsoft.com/entra/agent-id/interactive-agent-authentication-authorization-flow#acquire-tokens-for-downstream-apis
  `EnableTokenAcquisitionToCallDownstreamApi()` reference: https://learn.microsoft.com/dotnet/api/microsoft.identity.web.microsoftidentitywebapiauthenticationbuilderwithconfiguration.enabletokenacquisitiontocalldownstreamapi
- Generic downstream-API alternative (non-Graph OBO targets): `AddDownstreamApis(builder.Configuration.GetSection("DownstreamApis"))` + `IDownstreamApi`.
  https://learn.microsoft.com/entra/msidweb/call-downstream-apis/from-web-apis

### 3.4 Calling Azure OpenAI / Microsoft Foundry with managed identity (no API keys)

- "A managed identity from Microsoft Entra ID lets your app access other Microsoft Entra protected resources such as Azure OpenAI. Azure manages the identity and doesn't require you to provision, manage, or rotate any secrets."
  https://learn.microsoft.com/dotnet/ai/how-to/app-service-aoai-auth
- Role assignment: **Cognitive Services OpenAI User** on the Azure OpenAI resource, assigned to the App Service/Function's managed identity:
  ```azurecli
  az role assignment create --assignee "<managedIdentityObjectID>" \
    --role "Cognitive Services OpenAI User" \
    --scope "/subscriptions/<subId>/resourceGroups/<rg>/providers/Microsoft.CognitiveServices/accounts/<resourceName>"
  ```
  https://learn.microsoft.com/dotnet/ai/how-to/app-service-aoai-auth
- Code pattern:
  ```csharp
  var credential = new DefaultAzureCredential(); // discovers managed identity in Azure, dev credentials locally
  ```
  https://learn.microsoft.com/dotnet/ai/how-to/app-service-aoai-auth
- **Foundry vs. plain Azure OpenAI**: both the "classic" Foundry portal docs and the newer "Azure AI Foundry Models" docs describe the **same** underlying mechanism — RBAC role assignment + `DefaultAzureCredential` — i.e., Foundry does not introduce a functionally distinct auth pattern; the difference is in doc-set branding/endpoint shape, not the auth mechanism itself.
  https://learn.microsoft.com/azure/foundry-classic/openai/how-to/managed-identity
  https://learn.microsoft.com/azure/ai-foundry/openai/how-to/managed-identity
  https://learn.microsoft.com/azure/foundry/foundry-models/how-to/configure-entra-id

### 3.5 Streaming responses to the SPFx client

- **Azure OpenAI/Foundry side**: the Chat Completions API supports a `stream: true` request parameter for token-by-token streaming (standard OpenAI-compatible schema).
  https://learn.microsoft.com/azure/ai-foundry/openai/reference-preview-latest#create-chat-completion
  https://learn.microsoft.com/rest/api/microsoft-foundry/azureopenai/chat#create-chat-completion
- **ASP.NET Core side — native SSE support landed in .NET 10**:
  > "ASP.NET Core now supports returning a `ServerSentEvents` result using the `TypedResults.ServerSentEvents` API. This feature is supported in both Minimal APIs and controller-based apps… event messages are represented as `SseItem<T>` objects."
  ```csharp
  app.MapGet("/chat/stream", (CancellationToken cancellationToken) =>
  {
      async IAsyncEnumerable<string> GetChatTokens(
          [EnumeratorCancellation] CancellationToken ct)
      {
          await foreach (var chunk in openAiClient.StreamChatCompletionAsync(..., ct))
              yield return chunk.Content;
      }
      return TypedResults.ServerSentEvents(GetChatTokens(cancellationToken), eventType: "message");
  }).RequireAuthorization();
  ```
  https://learn.microsoft.com/aspnet/core/release-notes/aspnetcore-10.0 ("Support for Server-Sent Events (SSE)")
  Sample: https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/fundamentals/minimal-apis/samples/10.0/MinimalServerSentEvents/Program.cs
  For earlier .NET versions (8/9), the equivalent is a manual `text/event-stream` response with `Response.BodyWriter`/`IAsyncEnumerable` and no first-class `TypedResults.ServerSentEvents` helper — confirm target .NET version before relying on the native helper.
- **Does SPFx's `AadHttpClient` support streaming responses?** The official API reference states only that "the parameters and semantics for `HttpClient.fetch()` are essentially the same as the WHATWG API standard" but **does not document response-body streaming, chunked reading, or SSE at all**.
  https://learn.microsoft.com/javascript/api/sp-http-base/aadhttpclient?view=sp-typescript-latest
  **UNVERIFIED**: no explicit statement (positive or negative) confirms whether `AadHttpClient`'s `HttpClientResponse` exposes a true streamable `ReadableStream` body or buffers the full response before resolving.
  **Practical workaround (community-pattern, not an officially documented streaming API, but consistent with the documented factory architecture)**: obtain a scoped `AadHttpClient` (and thus an implicit token) via `context.aadHttpClientFactory.getClient(resourceEndpoint)`, **or** acquire a raw token through SPFx's token-provider factory and call the browser's native `fetch()` directly (bypassing `AadHttpClient`) to get a real `ReadableStream` body for manual SSE/chunk parsing:
  https://learn.microsoft.com/sharepoint/dev/spfx/use-aadhttpclient#connect-to-entra-id-applications-using-the-aadhttpclient
  **UNVERIFIED**: the specific `context.aadTokenProviderFactory.getTokenProvider()` → `provider.getToken(resourceUri)` API surface referenced in community write-ups was **not located** in the SPFx reference docs fetched in this research pass (`use-aadhttpclient`, `use-aadhttpclient-enterpriseapi`, the `AadHttpClient` class reference). It may exist under this or a similar name in `@microsoft/sp-http-base`'s TypeScript typings — **verify directly against the installed package's `.d.ts` files before relying on it**, rather than assuming the exact method signature from secondary sources.
  **Project recommendation:** plan for the "get a raw bearer token, then call native `fetch()` for the streaming endpoint" pattern as the working assumption, but validate the exact token-provider API name/signature against the installed `@microsoft/sp-http-base` typings (or the SPFx GitHub samples) during implementation, since it could not be confirmed from docs alone.

---

## 4. Chat UI

### 4.1 Fluent UI React v9 and SPFx's React version

- Current `@fluentui/react-components` (Fluent UI v9) latest version: **9.74.10** (npm, Oct 2026), peer dependency `"react": ">=16.14.0 <20.0.0"` — a wide range covering both React 17 and 18.
  https://registry.npmjs.org/@fluentui/react-components/9.74.10
- SPFx's compatibility matrix requires an **exact** React version match per SPFx release (not just "compatible range"): **1.21–1.23.x → React 17.0.1**; **1.24 (preview) → React 18.3.1**.
  https://learn.microsoft.com/en-us/sharepoint/dev/spfx/compatibility
- **Verdict**: there is no hard version-range conflict — Fluent UI v9's peer range covers React 17.0.1 — but SPFx's own strict pinning (exact version, `--save-exact`) means the project must match whichever React SPFx bundles for the targeted SPFx release.
- **Gap**: the official SPFx↔Fluent-UI integration doc (`https://learn.microsoft.com/en-us/sharepoint/dev/spfx/fluent-ui-integration`) still documents only **Fluent UI v8** (`@fluentui/react`) as of this research: "Starting with SPFx v1.18, new projects use the `@fluentui/react` (v8) package." **No official SPFx guidance for Fluent UI v9 (`@fluentui/react-components`) was found — UNVERIFIED/NOT FOUND.** This is corroborated by the closest real-world precedent (§4.4): the PnP `react-azure-openai-api-stream` sample is pinned to Fluent UI **v8**, not v9.

  **Recommendation**: Fluent UI v9 (`@fluentui/react-components`) can likely be used in SPFx 1.21–1.23 (React 17.0.1 is within its peer range), but this is not an officially-blessed SPFx integration path — budget extra verification/testing time, and consider Fluent UI v8 if you want to stay strictly within documented SPFx guidance.

### 4.2 Fluent UI "Copilot" components — NOT production-supported for external use

- Package `@fluentui-copilot/react-copilot` (latest `0.30.5`, Oct 2026) — its own README states explicitly:
  > "**For internal use only. External use is not supported at this time.** React components for Fluent AI React Copilot."
  (fetched via `https://app.unpkg.com/@fluentui-copilot/react-copilot@0.30.5/files/README.md`; registry metadata: `https://registry.npmjs.org/@fluentui-copilot/react-copilot/0.30.5`)
- It requires Fluent UI v9 (`"@fluentui/react-components": ">=9.69.0 <10.0.0"`) and bundles chat-oriented sub-packages (`react-copilot-chat`, `react-chat-input`, `react-prompt-starter`, `react-latency`, `react-feedback-buttons`, `react-output-card`, etc.) — but **should not be used** in an external customer-facing SPFx solution given the explicit "internal use only, external use is not supported" statement.
- **Recommendation**: do not take a dependency on `@fluentui-copilot/*` packages for this project.

### 4.3 `@fluentui-contrib/react-chat` — the closest officially-maintained, non-bot chat component

- Repo: https://github.com/microsoft/fluentui-contrib — "an official collection of extensions and components that enhance Fluent UI… maintained by the Microsoft Fluent UI team with community contributions."
- Package: `@fluentui-contrib/react-chat` — its own README states: **"Chat component powered by Fluent UI and used in Microsoft Teams."**
  https://github.com/microsoft/fluentui-contrib/blob/main/packages/react-chat/README.md
  ```tsx
  import { Avatar } from '@fluentui/react-components';
  import { Chat, ChatMessage, ChatMyMessage } from '@fluentui-contrib/react-chat';

  <Chat>
    <ChatMessage avatar={<Avatar name="Ashley McCarthy" />}>Hello I am Ashley</ChatMessage>
    <ChatMyMessage>Nice to meet you!</ChatMyMessage>
  </Chat>
  ```
- Provides `Chat`, `ChatMessage`, `ChatMyMessage` with Avatar integration — notably battle-tested in Teams' own UI chrome (non-bot chat surfaces). No built-in markdown rendering — compose separately (§4.4). **Gap**: no explicit "stable"/"preview" maturity badge was captured in the fetched README excerpt — **UNVERIFIED** beyond the quoted text; check the package's own CHANGELOG/package.json before committing to it for production.
- **Recommendation**: `@fluentui-contrib/react-chat` (Fluent UI v9-based) is the best-positioned officially-maintained, non-bot chat-bubble component for this project, with the caveat above.

### 4.4 Markdown rendering

- No dedicated Microsoft Learn article on "rendering markdown safely in SPFx/Fluent UI" was found — **UNVERIFIED/NOT FOUND** as an official guide.
- Two official PnP samples demonstrate concrete patterns (verified via direct `package.json` fetch):
  - **`react-azure-openai-api-stream`** uses `react-remark` + `remark-gfm` (markdown → React elements, no `dangerouslySetInnerHTML`).
    https://raw.githubusercontent.com/pnp/sp-dev-fx-webparts/main/samples/react-azure-openai-api-stream/package.json
  - **`react-chat-gpt`** uses `interweave` + `@types/dompurify` — explicit evidence of deliberate HTML sanitization before rendering model output.
    https://raw.githubusercontent.com/pnp/sp-dev-fx-webparts/main/samples/react-chat-gpt/package.json
- **Recommendation**: prefer a markdown-to-React-tree renderer (`react-remark`/`react-markdown` family) over raw HTML string injection; if any raw HTML pass-through is ever needed, sanitize with DOMPurify first, following the `react-chat-gpt` precedent.

### 4.5 Official PnP samples for chat / Azure OpenAI (non-bot precedents)

Source: PnP sample index `https://raw.githubusercontent.com/pnp/sp-dev-fx-webparts/main/samples.json` (368 entries), cross-checked against each sample's own README/`package.json`.

| Sample | URL | Summary | Key packages |
|---|---|---|---|
| **react-azure-openai-api-stream** | https://github.com/pnp/sp-dev-fx-webparts/tree/main/samples/react-azure-openai-api-stream | "Calling Azure OpenAI API in Streaming mode… shows data coming from the API in chunks… cancel the streaming response… AI responses render Markdown." Closest architectural analog to this project: a pure SPFx web part directly calling a chat-completions HTTPS endpoint and self-rendering the streamed response, **no Bot Framework involved**. | `@microsoft/fetch-event-source` (SSE), `react-remark`+`remark-gfm` (markdown), `@fluentui/react` **v8**, `@microsoft/mgt-components` (Graph Toolkit Person component). SPFx 1.21.1, React 17.0.1. |
| **react-chat-gpt** | https://github.com/pnp/sp-dev-fx-webparts/tree/main/samples/react-chat-gpt | "Uses Chat GPT in an SPFx web part. You can ask questions… most recent answers first." | `openai` SDK `6.7.0`, `interweave`+`dompurify` sanitization, `@fluentui/react` v8. SPFx 1.21.1. |
| **react-chatgpt-app** | https://github.com/pnp/sp-dev-fx-webparts/tree/main/samples/react-chatgpt-app | Dual-host: runs on SharePoint, Teams personal app, **and** a Teams Message Extension. (Note: the Message Extension surface specifically *is* bot/extension-plumbing-based — only its SharePoint/personal-tab mode is a relevant non-bot reference.) | SPFx 1.16.1. |
| **react-azure-openai-connector** | https://github.com/pnp/sp-dev-fx-webparts/tree/main/samples/react-azure-openai-connector | "Shows how to use the new feature of Azure OpenAI Connectors API in a SharePoint Framework web part." | Same family as above (not deep-dived). |
| **react-openai-devops** | https://github.com/pnp/sp-dev-fx-webparts/tree/main/samples/react-openai-devops | View recent tasks/bugs/commits summarized via OpenAI, against Azure DevOps. | External-API-calling pattern (not chat-UI-first). |
| **aad-api-spo-cookie** | https://github.com/pnp/sp-dev-fx-webparts/tree/main/samples/aad-api-spo-cookie | Older (2017) precedent: "web part showing how to access a custom API secured with Azure Active Directory (AAD)." Useful as the generic AadHttpClient-calling-custom-API template, pre-dating chat UX. | — |
| *(excluded)* react-bot-framework / react-bot-framework-sso / react-roomchat | — | Use `botframework-webchat` (Bot Framework v4 Web Chat) or Azure Communication Services Room Chat. | **Out of scope per this project's constraints** — bot/ACS-based, not applicable as an architecture reference. |

**Important caveat**: `react-azure-openai-api-stream` and `react-chat-gpt` both call Azure OpenAI / the OpenAI API **directly from the client with an API key configured in the web part**, not through a custom Entra-ID-secured backend with AadHttpClient. They are excellent references for the **streaming + markdown chat UI** pattern, but **not** for the secure-backend-API architecture this project requires — combine their UI/streaming approach with the AadHttpClient-secured-backend pattern from `aad-api-spo-cookie` and §2–§3 of this report, rather than copying their direct-to-OpenAI-with-API-key auth model.

### 4.6 Teams theming for the chat UI

Covered in §1.7 — same `ThemeProvider`/`IReadonlyTheme` (and optional `app.getContext()` cross-check) mechanism applies to the chat UI's own custom components; no Bot Framework or Teams adaptive-card theming is involved.

---

## 5. Alternatives — explicitly out of scope for this project

Per the project's explicit constraint, these are documented only to confirm *why* they are excluded, not evaluated as recommendations:

- **Teams AI Library / "Teams SDK"** (`github.com/microsoft/teams-sdk`, formerly `teams-ai`) — its own README: "Teams SDK represents a fundamental reimagining of how Teams apps and AI agents are built, **while maintaining compatibility with existing botframework-based agents**," bootstrapped via `new App(...)` with activity handlers (`app.on('message', ...)`). **Bot/agent-runtime based — excluded.**
- **Microsoft 365 Agents SDK** — "Activity Protocol is a standard communication protocol used across Microsoft… used by Microsoft Copilot, Microsoft Copilot Studio, Microsoft Teams, and the Microsoft 365 Agents SDK." `Microsoft.Agents.Core` "contains the Activity Protocol models and core interfaces." **Activity-protocol/bot-based successor to Bot Framework — excluded.**
  https://learn.microsoft.com/microsoft-365/agents-sdk/activity-protocol
  https://learn.microsoft.com/dotnet/api/copilot-sdk-docs-dotnet/overview?view=m365-agents-sdk
- **Copilot Studio agent embedding** — integration uses "the default Web Chat Embed code… iFrame" or the Agents SDK client, backed by the **Direct Line** channel. **Channel/Direct-Line/iframe-based — excluded.**
  https://learn.microsoft.com/microsoft-copilot-studio/publication-integrate-web-or-native-app-m365-agents-sdk
  https://learn.microsoft.com/microsoft-copilot-studio/configure-web-security
- **Declarative agents (Microsoft 365 Copilot extensibility)** — "conversational AI experiences that result from declared configurations loaded into Microsoft 365 Copilot… through configuration rather than custom code," hosted and orchestrated **inside Microsoft 365 Copilot itself**. Even the SPFx-flavored variant ("SharePoint Copilot Apps," pairing SPFx Copilot components with a declarative-agent definition) still requires the Copilot conversational host as the rendering surface. **Copilot-host-based, not a standalone web part — excluded.**
  https://learn.microsoft.com/microsoft-365/copilot/extensibility/declarative-agent-architecture
  https://learn.microsoft.com/sharepoint/dev/spfx/supported-extensibility-platforms-overview#microsoft-365-copilot

---

## 6. Recommended end-to-end architecture

```
┌───────────────────────────────────────────────────────────────────┐
│  SPFx Web Part (React, Fluent UI v9 + @fluentui-contrib/react-chat)│
│  Hosted as: SharePointWebPart + TeamsTab + TeamsPersonalApp         │
│  - ThemeProvider.tryGetThemeV2() for Teams/SPO theme               │
│  - context.sdks.microsoftTeams for Teams-context detection         │
└───────────────┬─────────────────────────────────────────────────────┘
                │  (1) AadHttpClient — short requests / non-streamed calls
                │  (2) raw bearer token (via token-provider) + native fetch()
                │      for the streaming chat endpoint (SSE)
                ▼
┌───────────────────────────────────────────────────────────────────┐
│  Custom Backend Web API (ASP.NET Core minimal API, .NET 10)        │
│  - Microsoft.Identity.Web: AddMicrosoftIdentityWebApi               │
│  - CORS: confirmed SharePoint/Teams origins, no credentials needed │
│  - /chat/stream → TypedResults.ServerSentEvents                    │
│  - (optional) OBO → Microsoft Graph via EnableTokenAcquisition...   │
│  - DefaultAzureCredential (managed identity) → Azure OpenAI/Foundry │
└───────────────┬─────────────────────────────────────────────────────┘
                ▼
┌───────────────────────────────────────────────────────────────────┐
│  Azure OpenAI / Microsoft Foundry — chat completions, stream:true  │
│  RBAC: Cognitive Services OpenAI User → App Service managed identity│
└───────────────────────────────────────────────────────────────────┘
```

### 6.1 Step-by-step build plan

1. **Scaffold the SPFx solution**
   `npm install @microsoft/generator-sharepoint@latest --global` then `yo @microsoft/sharepoint` → React web part, `Minimal` template. (§1.1, §1.3)
2. **Register the backend API app in Entra ID**
   - App registration → Expose an API → accept default `api://<appId>` Application ID URI → add delegated scope `access_as_user`. (§2.3)
   - Note the app's **display name** (needed verbatim in `webApiPermissionRequests`).
3. **Configure `config/package-solution.json`** with `webApiPermissionRequests` (§2.1 snippet below) and enable Teams hosting in the web part manifest (`supportedHosts`, §1.4).
4. **Build the ASP.NET Core minimal API**
   - `Microsoft.Identity.Web` for token validation (§3.1), CORS policy limited to confirmed SharePoint/Teams origins without `AllowCredentials()` (§3.2), `/chat/stream` SSE endpoint (§3.5), managed identity → Azure OpenAI (§3.4), optional OBO → Graph (§3.3).
5. **Build the chat UI** with `@fluentui-contrib/react-chat` + a markdown renderer (`react-remark`) (§4.3–4.4), theme-aware via `ThemeProvider` (§1.7).
6. **Wire authentication in the web part**: `AadHttpClient` for simple calls; for the streaming endpoint, acquire a bearer token and call native `fetch()` directly, since `AadHttpClient` streaming support is unverified (§3.5).
7. **Deploy**: `heft package-solution --production`, upload to the tenant app catalog, have a tenant admin **approve the pending permission request** on the SharePoint Admin Center API access page (§2.1), then use **Sync to Teams** (or let SharePoint auto-generate the Teams package) (§1.5).

### 6.2 Minimal code snippets

**`config/package-solution.json`** (§2.1):
```json
{
  "solution": {
    "webApiPermissionRequests": [
      { "resource": "Chatbot API", "scope": "access_as_user" }
    ]
  }
}
```

**Web part calling the API with `AadHttpClient`** (non-streaming call, e.g. fetching chat history):
```ts
const client: AadHttpClient = await this.context.aadHttpClientFactory
  .getClient('api://<api-app-client-id>');
const response = await client.get(
  'https://chatbot-api.contoso.com/api/history',
  AadHttpClient.configurations.v1
);
const history = await response.json();
```
(API: https://learn.microsoft.com/en-us/javascript/api/sp-http-base/aadhttpclient?view=sp-typescript-latest)

**Streaming fetch with a raw token** (workaround per §3.5 — verify the exact token-provider API against installed typings before using in production):
```ts
// Pattern only — exact AadTokenProvider method name/signature is UNVERIFIED; confirm
// against @microsoft/sp-http-base typings or SPFx samples before relying on this.
const tokenProvider = await this.context.aadTokenProviderFactory.getTokenProvider();
const token = await tokenProvider.getToken('api://<api-app-client-id>');

const response = await fetch('https://chatbot-api.contoso.com/api/chat/stream', {
  method: 'POST',
  headers: {
    Authorization: `Bearer ${token}`,
    'Content-Type': 'application/json'
  },
  body: JSON.stringify({ messages })
});

const reader = response.body!.getReader();
const decoder = new TextDecoder();
while (true) {
  const { done, value } = await reader.read();
  if (done) break;
  const chunk = decoder.decode(value, { stream: true });
  // parse SSE "data: ..." lines and append tokens to the chat message
}
```

**ASP.NET Core `Program.cs`** (§3.1, §3.2, §3.5):
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration); // reads "AzureAd" section

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy("SpfxTeamsPolicy", policy =>
        policy.WithOrigins(
                "https://contoso.sharepoint.com",
                "https://teams.microsoft.com",
                "https://*.teams.microsoft.com",
                "https://*.cloud.microsoft") // verify actual Origin headers before locking down
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();
app.UseCors("SpfxTeamsPolicy");
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/chat/stream", (ChatRequest req, CancellationToken ct) =>
{
    var credential = new DefaultAzureCredential(); // managed identity in Azure
    var openAiClient = new AzureOpenAIClient(new Uri(builder.Configuration["OpenAI:Endpoint"]!), credential);

    async IAsyncEnumerable<string> GetTokens([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var update in openAiClient.GetChatClient("gpt-4o")
            .CompleteChatStreamingAsync(req.ToChatMessages(), cancellationToken: cancellationToken))
        {
            foreach (var part in update.ContentUpdate)
                yield return part.Text;
        }
    }

    return TypedResults.ServerSentEvents(GetTokens(ct), eventType: "message");
}).RequireAuthorization();

app.Run();
```
(Streaming/.NET 10 SSE API: https://learn.microsoft.com/aspnet/core/release-notes/aspnetcore-10.0 ;
Managed identity + Azure OpenAI: https://learn.microsoft.com/dotnet/ai/how-to/app-service-aoai-auth ;
Microsoft.Identity.Web: https://learn.microsoft.com/entra/identity-platform/tutorial-web-api-dotnet-core-build-app)

---

## 7. Gotchas and open questions

1. **MSAL-in-SPFx is explicitly "not supported."** Do not use `@azure/msal-browser`/NAA directly inside the web part; use `AadHttpClient`. (§2.2)
2. **`AadHttpClient` streaming support is undocumented.** Plan for the raw-token + native-`fetch()` workaround for the SSE endpoint, but confirm the exact token-provider API (`aadTokenProviderFactory`/`AadTokenProvider.getToken` or equivalent) against the installed `@microsoft/sp-http-base` typings before implementation — it was **not verifiable** from the docs fetched in this research pass. (§3.5)
3. **No canonical CORS origin list for SharePoint/Teams exists in Microsoft docs.** Verify actual `Origin` headers from both SharePoint-page and Teams-tab hosting modes in a real deployment before finalizing the CORS policy. (§3.2)
4. **Fluent UI v9 inside SPFx is not an officially documented integration path** — the SPFx↔Fluent-UI doc still only covers v8. Budget extra testing time if using `@fluentui/react-components`/`@fluentui-contrib/react-chat`. (§4.1)
5. **`@fluentui-copilot/react-copilot` must not be used** — its own README says "external use is not supported at this time." (§4.2)
6. **No SPFx-specific Application ID URI format requirement was found** beyond the generic Entra default (`api://<appId>`) — if a different format is mandated elsewhere, it wasn't located in this research pass; validate empirically. (§2.3)
7. **No confirmed rename of the "SharePoint Online Client Extensibility Web Application Principal"** as of the most recent fetched doc revision (2026-07-01) — treat any claim of a rename as unconfirmed. (§2.1)
8. **`TeamsMeetingStage` as a `supportedHosts` value was not found** in the fetched SPFx docs — verify against Teams meeting-apps docs if meeting-stage support is required. (§1.4)
9. **Isolated web parts are retiring April 2, 2026** (existing tenants) — don't build new reliance on that isolation feature for token protection. (§1.8, §2.4)
10. **The closest official PnP chat samples (`react-azure-openai-api-stream`, `react-chat-gpt`) call OpenAI directly with a client-side API key**, not through an Entra-secured custom backend — use them only for UI/streaming/markdown patterns, not for the auth architecture; combine with `aad-api-spo-cookie`'s AadHttpClient pattern and this report's §2–§3 for the secure-backend design. (§4.5)
11. **Hosted/online workbench retires Dec 1, 2026** — use the SharePoint Framework Debug Toolbar for local development going forward. (§1.8)
12. **Easy Auth vs. in-code token validation for Azure Functions has no official side-by-side comparison** for this exact scenario — in-code `Microsoft.Identity.Web` is recommended here because the SPFx/Teams client sends a plain bearer token, not an Easy-Auth-style `X-ZUMO-AUTH` session. (§3.1)
