# Hello Copilot SPFx Sample

**Status**: Approved
**Created**: 2026-10-21
**Mode**: Planning

## 1. Project Summary

Build a small SPFx-hosted "Hello Copilot" interface and a separately hosted ASP.NET Core minimal API that returns mocked sample data through a Server-Sent Events stream. The implementation must exclude Bot Framework, Azure Bot Service, Teams AI/bot SDKs, Copilot Studio bot embedding, Direct Line, and Bot Framework Web Chat.

## 2. Requirements

- Target SPFx 1.23.2 GA with Node.js 22 LTS and React 17.0.1.
- Use TypeScript for the SPFx web part.
- Use ASP.NET Core Minimal API in C# with .NET 10.
- Return sample data as `text/event-stream` SSE chunks.
- Acquire an Entra access token through SPFx `AadHttpClient`/`AadTokenProvider` capabilities; do not use raw MSAL inside the web part.
- Enforce delegated authentication through Microsoft.Identity.Web and expose the delegated scope `access_as_user`.
- Support SharePoint web-part, Teams-tab, and Teams-personal-app hosts.
- No database, file store, queue, cache, or other datastore is required.

## 3. Architecture

The SPFx React web part obtains an Entra token for the API Application ID URI and calls a dedicated HTTPS ASP.NET Core API. Short initialization/configuration calls may use `AadHttpClient`; the streaming endpoint should use a scoped bearer token with native browser `fetch()` if the installed `AadHttpClient` implementation does not expose a usable `ReadableStream`, as the supplied research report recommends.

```
SPFx web part
  -> SharePoint/Teams host
  -> AadHttpClient/AadTokenProvider (Entra delegated token)
  -> ASP.NET Core Minimal API (/api/messages/stream)
  -> fixed mocked sample data as SSE
```

The API validates the bearer token, applies authorization, validates CORS origins, and emits `data:` SSE events. No Microsoft Graph, Azure OpenAI/Foundry, managed identity, Bot Framework, or external model dependency is included.

## 4. Services & Components

### SPFx web part — `spfx-webpart`

- React component named `HelloCopilotWebPart`.
- A simple heading/message area displaying streamed mocked text.
- `@fluentui/react-components` and `@fluentui-contrib/react-chat` components as appropriate, subject to package compatibility validation.
- Implemented in `spfx/`.
- `supportedHosts`: `SharePointWebPart`, `TeamsTab`, `TeamsPersonalApp`.
- Web API permission request for the API's Entra app display name and `access_as_user`.

### ASP.NET Core minimal API — `dotnet-mock-api`

- Implemented in `api/`.
- Entra JWT validation with Microsoft.Identity.Web.
- Mock SSE endpoint, for example `POST /api/messages/stream`.
- Local HTTPS development profile and CORS policy for confirmed SharePoint/Teams origins.
- No database or external service.

## 5. API Contract

`POST /api/messages/stream`

Request:

```json
{
  "message": "Hello Copilot"
}
```

Response headers:

```text
Content-Type: text/event-stream
Cache-Control: no-cache
```

Response body:

```text
data: {"text":"Hello"}

data: {"text":" Copilot"}

data: {"text":" from the mock API"}

data: [DONE]
```

Authentication:

```text
Authorization: Bearer <Entra access token>
```

The API requires the delegated scope configured on the Entra application. SSE parsing in the web part must support fragmented chunks, split lines, multiline buffering, cancellation, and reconnection-safe UI state.

## 6. Design System & UI

**Component Library**: Fluent UI v9 (`@fluentui/react-components`) with `@fluentui-contrib/react-chat` for chat primitives

- Keep the screen simple: a heading titled "Hello Copilot", one text input, one send button, and a response area.
- Use Fluent UI spacing, typography, button, field, and message primitives.
- Theme with `ThemeProvider.tryGetThemeV2()`/SPFx theme tokens and detect Teams context through the installed SPFx Teams SDK only for theme/host adaptation.
- Display streamed text incrementally and retain a stable error state for denied auth, CORS failure, and backend outage.
- Do not use `@fluentui-copilot/react-copilot`; it is marked external-use unsupported in the supplied research report.

## 7. Workspace Structure

```text
chatbot/
├── .azure/
│   ├── requirements.json
│   └── project-plan.md
├── docs/
│   └── research/
│       └── spfx-teams-chat-webpart.md
├── spfx/
│   ├── config/
│   │   └── package-solution.json
│   ├── src/
│   │   └── webparts/
│   │       └── helloCopilot/
│   │           ├── HelloCopilotWebPart.ts
│   │           ├── HelloCopilotWebPart.manifest.json
│   │           └── components/
│   │               ├── HelloCopilot.tsx
│   │               └── HelloCopilot.module.scss
│   ├── heft.json
│   └── package.json
└── api/
    ├── Program.cs
    ├── api.csproj
    └── appsettings.json
```

Implementation must not begin from this planning step; the structure records the intended separation only.

## 8. Implementation Sequence

1. Verify Node.js 22, npm, Heft, Yo, and the .NET 10 SDK.
2. Scaffold the SPFx solution under `spfx/` using the current GA generator.
3. Scaffold the minimal API under `api/`.
4. Register and configure the API app registration: application ID URI `api://<api-client-id>`, delegated scope `access_as_user`.
5. Configure the SPFx `webApiPermissionRequests` entry using the API application's display name verbatim.
6. Implement JWT validation and a restricted CORS policy in the API.
7. Implement fixed mocked SSE sample output, cancellation, and authorization.
8. Implement the web part UI and token acquisition via the supported SPFx APIs.
9. Verify the exact `@microsoft/sp-http-base` token-provider API shape before native `fetch()` streaming is coded.
10. Package, upload, approve API access, and validate SharePoint/Teams hosts only after deployment prerequisites are available.

## 9. Build and Run Commands

Prerequisites:

```powershell
node --version
node -v
nvm install 22
nvm use 22
dotnet --version
```

Install planning toolchain for the implementation phase:

```powershell
npm install --global @microsoft/generator-sharepoint@latest
npm install --global yo @rushstack/heft
```

Scaffold from the workspace root during implementation:

```powershell
mkdir spfx
cd spfx
yo @microsoft/sharepoint --skip-install
npm install
```

Build and run the SPFx web part:

```powershell
cd spfx
npm install
heft build
heft start
```

Package the SPFx solution:

```powershell
cd spfx
heft build --production
heft package-solution --production
```

Run the API:

```powershell
cd api
dotnet restore
dotnet run --launch-profile https
```

Exact local test sequence:

```powershell
# Terminal 1: API
cd d:\repos\nibc\chatbot\api
dotnet run --launch-profile https

# Terminal 2: SPFx
cd d:\repos\nibc\chatbot\spfx
heft start
```

The package and deployment commands intentionally stop before tenant app-catalog upload, permission approval, and Sync to Teams; those actions require tenant context and explicit authorization.

## 10. Testing & Acceptance Criteria

- The web part renders the "Hello Copilot" heading and input/response UI locally.
- Clicking send triggers an Entra-authenticated API call.
- The API rejects missing or invalid bearer tokens.
- SSE events arrive incrementally and are concatenated into the visible response.
- The UI handles an empty stream, network interruption, cancellation, and [DONE].
- SharePoint and Teams hosting paths render without runtime auth or theme regressions.
- No Bot Framework or Teams bot SDK package or API reference exists.
- No datastore is introduced.
- Build succeeds in SPFx through the Heft toolchain and in the API through `dotnet build`.

## 11. Security

- Never place an API secret or client secret in SPFx.
- Use delegated Entra authentication and SharePoint's API permission mechanism.
- Configure a narrow tenant-specific CORS policy after confirming actual SharePoint and Teams origins.
- Validate issuer, audience, and scope on every API call.
- Keep API logs free from tokens, prompt bodies containing secrets, and full authorization headers.
- No credentials or secrets are persisted in the repository.

## 12. Deployment

- Upload the generated `.sppkg` to the SharePoint app catalog.
- Have a tenant administrator approve the pending API permission request in SharePoint Admin Center API access.
- Deploy the .NET API over HTTPS, ideally behind a hosted environment with a stable URL.
- Configure API app registration Application ID URI and scope to match the deployed API and SPFx package.
- Add the confirmed SharePoint/Teams origins to CORS.
- Use Sync to Teams only through the tenant-supported SPFx deployment flow when Teams availability is approved.

## 13. Key Risks & Mitigations

- `AadHttpClient` may not guarantee streaming response-body behavior: verify browser stream exposure; retain native `fetch()` with the raw token-provider token as the fallback.
- Token-provider method names may differ across the installed SPFx typings: inspect `.d.ts` files and use the exact supported API.
- CORS origin lists vary by host: verify runtime `Origin` headers from SharePoint and Teams before release.
- Fluent UI v9 is usable with the report's React 17/SPFx 1.23 compatibility assumption but is not as explicitly documented as v8: test the package integration and pin compatible versions.
- `.NET 10` native SSE support should be used if installed SDK behavior confirms the first-class helper; otherwise implement `text/event-stream` using the response body writer.
