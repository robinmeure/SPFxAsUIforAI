# Chat API

Controller-based .NET 10 API with interchangeable mock and RAG chat services.
[Program.cs](./Program.cs) configures DI, authentication, CORS and the HTTP pipeline;
[MessagesController](./Controllers/MessagesController.cs) owns the streaming route.

## Run locally

```powershell
cd api
dotnet dev-certs https --trust
dotnet restore
az login --tenant 63645c73-a00c-4659-b911-eb6c4c2d4a8f
dotnet run --launch-profile https
```

The HTTPS profile runs at `https://localhost:7198`. Azure sign-in is needed for
real RAG responses, not canned responses. Incoming API authentication is a
separate concern: `az login` does not sign the SharePoint user into this API.

## Appsettings and mock switches

Both switches are top-level settings in [appsettings.json](./appsettings.json)
or [appsettings.Development.json](./appsettings.Development.json), not inside
`AzureAd` or `Rag`:

```json
{
  "UseMockAuthentication": false,
  "UseMockResponses": true
}
```

This example keeps real user authentication but returns the canned answer.
Merge these properties into the existing JSON; do not replace the whole file.
The checked-in configuration currently sets **both switches to `false`**.

| `UseMockAuthentication` | `UseMockResponses` | Behavior |
|---|---|---|
| `false` | `false` | User JWT + delegated scope required; real Search + Foundry responses |
| `false` | `true` | User JWT + delegated scope required; canned responses, no Azure service calls |
| `true` | `false` | Anonymous API; real Search + Foundry responses; local testing only |
| `true` | `true` | Anonymous API + canned responses; local UI testing only |

For Development, settings are applied in this order (later values override earlier
ones): `appsettings.json`, `appsettings.Development.json`, environment variables,
then command-line arguments. The launch profiles select the Development
environment. Other environments do not load the Development file.

For example, `$env:UseMockResponses = "true"` overrides `false` in either JSON
file. To return to the JSON value in the current PowerShell session:

```powershell
Remove-Item Env:\UseMockResponses -ErrorAction SilentlyContinue
Remove-Item Env:\UseMockAuthentication -ErrorAction SilentlyContinue
```

**Restart the API after changing either switch or authentication settings.**
They are read at startup to configure services and authorization.

## RAG configuration

`UseMockResponses=false` (the default) enables the real pipeline:

1. Hybrid keyword/vector search against `kamervragen-openai` at
   `https://aisearch-rtf5eizu6t6z2.search.windows.net`, using the index's integrated
   vectorizer on `contentVector` and its `semantic` ranking configuration.
2. Retrieve up to five results with `id`, `content`, `onderwerp` and `documentNummer`.
   Each content excerpt is limited to 6,000 characters.
3. Stream an answer from deployment `gpt-6.1-sol` using the **Responses API**
   at `https://foundry-rtf5eizu6t6z2.openai.azure.com/openai/v1/responses`.
   Answers are grounded in retrieved text, cite sources as `[1]`, and include a source list.

Settings live under `Rag` in [appsettings.json](./appsettings.json). Environment
overrides use `Rag__SearchEndpoint`, `Rag__IndexName`, `Rag__FoundryEndpoint` and
`Rag__ModelDeployment`. The index field names, vector field and semantic configuration
are specific to this index; adapt [RagChatService](./Services/RagChatService.cs) for another schema.

The backend uses `DefaultAzureCredential` locally (for example, `az login`) and a
system-assigned `ManagedIdentityCredential` outside Development. Grant that identity:

- **Search Index Data Reader** on the Search service.
- **Cognitive Services OpenAI User** on the Foundry/Azure OpenAI resource.

The Search service's own managed identity also needs **Cognitive Services OpenAI User**
on the resource used by its existing `text-embedding-3-large` vectorizer.
Network access to both services and semantic ranking availability are required.
No API keys are stored. Azure service authentication is separate from the incoming
user JWT; no on-behalf-of token exchange is performed.

The OpenAI SDK marks its Responses surface experimental (`OPENAI001`); the two
consuming source files explicitly opt in to it. Requests use `store=false`, send
only the current question plus retrieved context, and do not persist response IDs.
The index is shared across authenticated callers: this sample has no per-document
authorization filter and must only contain documents every permitted user may read.

### Mock responses

To run the original canned response without contacting Azure:

```powershell
$env:UseMockResponses = "true"
dotnet run --launch-profile https
```

`UseMockResponses` and `UseMockAuthentication` are independent. To allow anonymous
calls for local testing only, also set `$env:UseMockAuthentication = "true"`.
Never enable mock authentication in hosting configuration.

## Authentication

Development defaults to `UseMockAuthentication=false` in `appsettings.Development.json`. Production or non-development builds must set `UseMockAuthentication=false` and configure AzureAd TenantId, ClientId, and Audience.

### Required Entra and SharePoint setup

1. In the API's Entra app registration, expose the Application ID URI
   `api://1c389841-5b43-4575-8acb-8135d21ca809` and enable the delegated
   `access_as_user` scope.
2. In the app registration's Microsoft Graph manifest, set
   `api.requestedAccessTokenVersion` to `2` for the v2 access-token setup documented
   here. After changing token settings, obtain a fresh token.
3. In [package-solution.json](../spfx/config/package-solution.json),
   `webApiPermissionRequests[].resource` must match the API app registration's
   **display name**, currently `Hello Copilot Mock API`; `scope` must be
   `access_as_user`. It is not the client ID or Application ID URI.
4. Package/deploy the SPFx solution and approve the pending permission in
   **SharePoint admin center > API access**. Permission changes require a version
   bump, repackaging and redeployment. No separate SPFx app registration, API
   client secret or OBO flow is required for this Search/Foundry backend.
5. In the [web part manifest](../spfx/src/webparts/helloCopilot/HelloCopilotWebPart.manifest.json),
   keep `apiResource` as `api://1c389841-5b43-4575-8acb-8135d21ca809`
   and `apiUrl` as `https://localhost:7198/api/messages/stream` locally, or the
   deployed streaming URL. The code calls `AadTokenProvider.getToken(apiResource)`.
   `apiScope` remains `api://1c389841-5b43-4575-8acb-8135d21ca809/access_as_user`,
   but is informational in this sample.

Use the IDs below only for the current tenant; replace them consistently if you
deploy into another tenant. The relevant API configuration is:

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "63645c73-a00c-4659-b911-eb6c4c2d4a8f",
    "ClientId": "1c389841-5b43-4575-8acb-8135d21ca809",
    "Audience": "1c389841-5b43-4575-8acb-8135d21ca809",
    "Scopes": "access_as_user"
  },
  "UseMockAuthentication": false,
  "UseMockResponses": false
}
```

### Authentication adjustments that matter

- **Audience:** for the current v2 access tokens, `AzureAd:Audience` must be the
  **bare API client ID**, matching the token's `aud` claim, not
  `api://1c389841-5b43-4575-8acb-8135d21ca809`. Do not copy the frontend's
  `apiResource` into `Audience`. You do not need to accept both audience forms for
  this v2-only setup. See Microsoft's [access-token claims reference](https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference).
- **Scope validation:** the `AccessAsUser` policy in [Program.cs](./Program.cs)
  uses `RequireAuthenticatedUser().RequireScope("access_as_user")`, rather than a
  literal `RequireClaim("scp", ...)`. Microsoft.Identity.Web handles mapped and
  unmapped scope claims and space-separated scope lists. The controller
  authorization filter applies this policy when mock authentication is disabled.
  `AzureAd:Scopes` alone is not a replacement for the policy.
- **CORS and HTTPS:** trust the local API certificate and allow the actual
  requesting page's origin in `Cors:AllowedOrigins` (for example,
  `https://mngenvmcap935538.sharepoint.com`). Do not put a path or trailing slash
  in an origin. The API still requires a valid token even when CORS allows an origin.
- **Configuration overrides:** update both base and Development settings as
  needed, remove stale environment overrides, restart the API, and refresh the
  web part/token after changing app registration settings.

The streaming endpoint requires an authenticated user with the delegated `access_as_user` scope. Its policy uses Microsoft.Identity.Web's `RequireScope` to handle both mapped and unmapped scope claims and space-separated scope lists. A valid token without this scope is forbidden (403).

### Verify authentication separately from RAG

First use `UseMockAuthentication=false` and `UseMockResponses=true`. Send a message
from a real SharePoint page with the web part installed (or debug-loaded). This
tests user authentication without requiring Search or Foundry access.

| Result | Meaning / next check |
|---|---|
| HTTP 401 | Missing, expired or invalid access token; check API client ID, tenant, issuer and the bare v2 audience |
| HTTP 403 | Token authenticated but lacks delegated `access_as_user`; check exposed scope and SharePoint API approval |
| HTTP 200 with canned SSE reply and `[DONE]` | User authentication and authorization work |
| HTTP 200 with an SSE `error` after enabling real responses | Inspect API logs using the reference ID; check Azure identity/RBAC, network access, Search vectorizer and model deployment |
| Browser CORS/TLS failure | Check the request's `Origin`, allowed origins and trusted development certificate |

Inspect token claims **locally**, without pasting tokens into external sites or
logs: expect `ver=2.0`, `aud=1c389841-5b43-4575-8acb-8135d21ca809`,
`tid=63645c73-a00c-4659-b911-eb6c4c2d4a8f`, and `scp` containing `access_as_user`.
Use an **access token**, not an ID token or a Graph token. An application-only
token is not a substitute for the delegated user scope.

Once the canned authenticated reply works, set `UseMockResponses=false`, ensure
the backend identity has the roles listed above, restart, and verify a grounded
answer with sources.

### PII logging for authentication troubleshooting

`Logging:EnablePiiLogging` is enabled in `appsettings.Development.json` to show personally identifiable information in IdentityModel authentication diagnostics. It is honored only in the Development environment and defaults to disabled when missing. A startup warning is logged when it is active.

Set `Logging:EnablePiiLogging` to `false` after troubleshooting. Authentication logs may contain sensitive personal data; do not share them without redaction.

## Test SSE

With the API running in local anonymous/mock mode:

```powershell
Invoke-WebRequest -Uri https://localhost:7198/api/messages/stream -Method Post -ContentType 'application/json' -Body '{"message":"Hello Copilot"}' |
    Select-Object -ExpandProperty Content
```

With authentication enabled, send `Authorization: Bearer <access-token>` or use
the SharePoint web part. Requests require a nonblank `message` of at most 8,000
characters; invalid bodies return HTTP 400 before streaming starts.

The existing `{activity}`, `{text}`, `{error}` and `[DONE]` SSE contract is unchanged.
Only successful streams end with `[DONE]`; upstream failures are logged and return
an `error` event with a reference ID, not internal Azure details. Disconnects cancel
retrieval and generation. No matching content returns an explicit no-sources answer
without calling the model.

## Tests

From the repository root:

```powershell
dotnet test .\api.Tests\api.Tests.csproj
```
