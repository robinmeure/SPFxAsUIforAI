# Chat API

Controller-based .NET 10 API with interchangeable mock and RAG chat services.
[Program.cs](./Program.cs) configures DI, authentication, CORS and the HTTP pipeline;
[MessagesController](./Controllers/MessagesController.cs) owns the streaming route.

## Run locally

```powershell
cd src/backend/api
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

`UseMockResponses=false` (the default) enables the agent in
[AgentChatService](./Services/Agent/AgentChatService.cs). Each turn calls deployment
`gpt-6.1-sol` through the **Responses API** at
`https://foundry-rtf5eizu6t6z2.openai.azure.com/openai/v1/responses` with the stored chat
history (last `Rag:HistoryMessageCount` messages, default 20) and these tools
([AgentTools](./Services/Agent/AgentTools.cs)):

| Tool | What it does |
|---|---|
| `list_my_documents` | Documents attached to this conversation and their status |
| `load_skill` / `read_skill_resource` | Loads a skill's instructions or one of its reference files |
| `create_document` | Builds a DOCX or XLSX (PDF via DOCX conversion in Graph) and saves it to the user's OneDrive or `Graph:OutputDriveId` |

The loop stops when the model answers, or after `Rag:MaxAgentIterations` (default 8),
where the final call disables tools. Each tool call emits an `activity` event. Created
files are appended under `### Files`. Source evidence is structured metadata, not a Markdown appendix.

### Application-owned retrieval and citations

Before answer generation, [DocumentRetrieval](./Services/Agent/DocumentRetrieval.cs):

1. Uses the authoritative attachment IDs resolved before loading history. **No attachments bypasses the document registry,
   planner, embeddings and Azure AI Search entirely**, and emits no document-search progress steps. General chat is allowed;
   document facts still require evidence. With attachments, checks ownership/readiness in the caller's registry.
2. Asks the existing planner for a strict `plan_retrieval` result: standalone query,
   `thread`/`document`/`section`/`clarify`/`reuse`/`conversation` scope, document IDs, optional section/page.
   `conversation` skips retrieval for non-documentary exchanges. `reuse` is allowed for factual follow-ups as well as
   transformations, but only when supplied prior passages support the question. New facts or broader coverage require search.
   Only the last assistant message's **saved, cited quotes** are candidates, never assistant prose alone. The attachment set
   and every attached document's ready status and `UpdatedAt` must match the saved snapshot. Document-change events,
   interrupted turns, legacy messages without snapshots, or unavailable documents invalidate reuse. Reused passages are
   renumbered and supplied again as current evidence. Decision quotes are bounded to 1,200 characters each; full saved
   quotes (up to 6,000 characters) are supplied to the answer model.
3. Section catalogs (up to 200 paths) are cached for ten minutes, with at most 100 catalog entries per API process,
   keyed by owner, ready document IDs and versions. Repeated turns with unchanged documents reuse the catalog;
   changed versions/attachments cannot use the old entry. Both named-section and broader questions retain a single
   planner call, avoiding an extra decision roundtrip. A cold/expired catalog still requires one section-facet request,
   including for reuse/conversation decisions. Missing or ambiguous targets require clarification.
   The application validates IDs and section paths, then calls Azure AI Search **only for search scopes**.
   Every query is constrained by owner AND attached document IDs, with optional section/page filters.
   Vector prefiltering preserves this boundary. No attachments means no Search request, never a search of the entire user library.
4. Returns up to **8 hybrid keyword/vector + semantic results**, at most 6,000 characters per passage.
   Whole-document requests search across the document but still return only top passages; summaries/extractions are not exhaustive.
5. Supplies numbered passages to the answer model, which no longer has search/read tools.
   Search/classification failures fail the turn explicitly instead of generating an answer without retrieval.

SSE `citations` events carry passage number, chunk/document ID, file title, `sourceType`,
`sourceUrl`, zero-based `chunkIndex`, section path, page number when known, quoted excerpt and retrieval method.
The initial event supplies retrieved evidence; the final event keeps only referenced passage numbers.
SSE `retrieval` contains the query, scope, selected IDs, section/page, result count, notices, and `method`
(`search`, `reuse`, or `none`). Only actual searches emit "Searched"; reuse emits "Reusing earlier source passages".
Attachment/version snapshots are stored in retrieval metadata for subsequent validation.
The final citations and retrieval details are stored with the assistant message and returned when reopening a chat.
Inline `[1]` markers identify **passages**, so two citations may refer to different parts of the same file.
Unknown numbers have no evidence and are shown as unverified. A retrieved excerpt is inspectable evidence, not an automated guarantee that the model's claim follows from it.

The answer model returns Markdown and one to three suggested questions in a strict structured response, **in the same
model call**. The application decodes and streams the answer as it arrives, then emits `followUpQuestions`, persists
them with the assistant message, and returns them on history reload. Questions are limited to 240 characters each.
For an explicit goodbye or a request not to suggest questions, the model can suppress them and the application emits
an empty list. Ordinary skill/file tools retain their existing tool loop; malformed structured output fails the turn.

Settings live under `Rag` in [appsettings.json](./appsettings.json). Environment
overrides use `Rag__SearchEndpoint`, `Rag__IndexName`, `Rag__FoundryEndpoint`,
`Rag__ModelDeployment`, `Rag__EmbeddingDeployment` and `Rag__EmbeddingDimensions`
(defaults: `my-documents`, `text-embedding-3-large`, 1024 dimensions).

For chats with ready attachments, the retrieval planner (step 2 above) runs before the answer can start, so it adds directly to the wait.
`Rag__PlannerModelDeployment` selects its deployment (default: `Rag__ModelDeployment`) and
`Rag__PlannerReasoningEffort` its reasoning effort (default `low`; leave it empty for non-reasoning
models, which reject the setting). Smaller models plan faster but can mis-scope requests, so check plans
before switching.

Foundry tokens are cached by [CachedTokenCredential](./Services/CachedTokenCredential.cs): the OpenAI SDK
requests a token on every call, which costs seconds with `AzureCliCredential` in development. Traces
include `retrieval.documents`, `retrieval.sections`, `retrieval.plan`, `retrieval.search` and
`agent.response` (with a `first_token` event) spans for each phase of a turn.

### Search index

The API owns its index. At startup [KnowledgeIndexInitializer](./Services/Ingestion/KnowledgeIndex.cs)
creates `Rag:IndexName`, or updates it when fields were added, from the definition in
[KnowledgeIndex.CreateDefinition](./Services/Ingestion/KnowledgeIndex.cs). If that fails,
the API does not start and logs the reason.

| Field | Type | Use |
|---|---|---|
| `id` | key | `<documentId>-c<chunkIndex>` |
| `documentId`, `ownerId`, `sourceType` | filterable | Document registry ID, caller's object ID, `upload` or `sharepoint` |
| `chunkIndex` | Int32, sortable | Stable zero-based passage location |
| `sectionPath` | retrievable | Heading hierarchy shown in citations |
| `sectionPaths` | string collection, filterable/facetable | Ancestor heading paths; section searches include subsections |
| `pageNumber` | Int32, filterable | Document Intelligence page when available; null for unpaginated text |
| `fileName`, `content` | searchable | Semantic configuration `semantic` (title `fileName`, content `content`) |
| `sourceUrl` | retrievable | Link to the SharePoint file in the sources list |
| `contentVector` | vector, HNSW | `Rag:EmbeddingDimensions` dimensions; not retrievable |

There is no integrated vectorizer: the API embeds both chunks and queries itself. Changing
the embedding model or dimensions can't be done in place. Set a new `Rag:IndexName` and have
users add their documents again.

The new location fields are added at startup without deleting existing chunks. **Re-add existing documents**
to populate section/page metadata. Existing chunks remain searchable and citable by passage number, but
cannot satisfy section/page filters until re-ingested. Section catalogs are limited to 200 paths per turn;
unknown/unlisted sections trigger clarification, not an unrestricted search.

The backend uses `DefaultAzureCredential` locally (for example, `az login`) and a
system-assigned `ManagedIdentityCredential` outside Development. Grant that identity:

- **Search Service Contributor** on the Search service (it creates and updates the index at startup).
- **Search Index Data Contributor** on the Search service (it writes the users' chunks).
- **Cognitive Services OpenAI User** on the Foundry/Azure OpenAI resource (chat and embeddings).
- **Cognitive Services User** on the same Foundry resource (Document Intelligence; the OpenAI role
  does not cover it).
- **Cosmos DB Built-in Data Contributor** on Cosmos DB.

[infra/main.bicep](../../../infra/main.bicep) creates these resources and assigns all of these roles
to `apiPrincipalId` and `developerPrincipalId` (see the [root README](../../../README.md#71-provision-azure)).
For local runs, point `Rag:SearchEndpoint`, `Rag:FoundryEndpoint` and `Cosmos:Endpoint` at the
deployment outputs with `dotnet user-secrets` (the project has a `UserSecretsId`).
The Search service must allow Entra ID (RBAC) authentication.
No API keys are stored. Calls to Microsoft Graph use the on-behalf-of flow with the
caller's token; see the [root README](../../../README.md#5-create-the-entra-app-registration).

The OpenAI SDK marks its Responses surface experimental (`OPENAI001`); consuming
files explicitly opt in. Requests use `store=false` and do not persist response IDs;
reasoning items are passed back as encrypted content within a turn.

## Documents and ingestion

`POST /api/documents` (upload) and `POST /api/documents/sharepoint` (picked file,
downloaded with Graph) register a document in Cosmos DB as `processing` and queue it.
A background worker ([DocumentIngestor](./Services/Ingestion/DocumentIngestor.cs)) then:

1. Extracts Markdown with Document Intelligence `prebuilt-layout` on the Foundry (AIServices)
   resource at `Rag:FoundryEndpoint` (`.txt` and `.md` are read directly). There is no separate
   Document Intelligence resource or endpoint setting.
2. Splits it on headings and paragraphs into ~2,000-character chunks with 300 characters of
   overlap (`Ingestion:ChunkSizeCharacters`, `Ingestion:ChunkOverlapCharacters`).
3. Embeds the chunks with `Rag:EmbeddingDeployment` and uploads them to the index with
   owner/source metadata, heading ancestry and available page numbers set. Chunks do not overlap across section/page boundaries.
4. Marks the document `ready` (or `failed` with a user-facing reason).

Settings:
`Ingestion__MaxFileSizeBytes` (default 10 MB). The queue is in memory with two workers.

### Deletion and ingestion concurrency

Document registration is create-only. Worker completion uses conditional storage
updates (ETags in Cosmos DB) and cannot recreate a deleted ID. Deletion first
persists a tombstone, making the document unavailable to library and retrieval
reads, and then removes its Search chunks. A worker that finishes later acknowledges
the deletion and removes anything it indexed.

`DELETE /api/documents/{id}` returns 204 after cleanup, or 202 when deletion is
durable but cleanup is still pending. Repeating deletion is safe; an unknown ID
returns 404. [DocumentCleanupWorker](./Services/Ingestion/DocumentCleanup.cs) retries
one page of at most 50 pending deletions per minute, with logged failures.
Tombstones are retained to prevent ID reuse. If a processing job is lost on restart,
its deletion remains pending and is periodically swept: the system does not guess
that a writer on another instance has stopped. This trades retained tombstones and
cleanup requests for safety; durable job recovery/lease expiry is not implemented.

Deploy this lifecycle change with old ingestion workers drained/stopped: an older
worker still using unconditional upsert does not honor the new tombstones.
Already-generated answers and saved citation excerpts are not retroactively erased.

### Spreadsheet value fidelity

The `create_document` worksheet schema supplies strings. XLSX generation stores
them as literal text, preserving leading zeros, long identifiers and whitespace;
formula-looking text is not executed. Native numeric cells and calculations require
an explicit typed-cell contract and are not inferred from strings.

## Chat history

With `Cosmos__Endpoint` set (and `UseMockResponses=false`), chats and the document
registry are stored in Cosmos DB for NoSQL using Entra authentication. The `chats`
container uses a hierarchical partition key (`/userId`, `/chatId`) so one chat is a
single-partition read and a user's chat list is a prefix query; `documents` is
partitioned by `/userId`. Without an endpoint the API uses in-memory stores, which
are lost on restart; that is intended for local development and tests only.

### One conversation per document set

A conversation owns one document set, and no two conversations of a user share the same non-empty set
(compared as a set, ignoring order). An empty set is a general chat and isn't deduplicated.
[ChatDocumentSets](./Services/Storage/ChatDocumentSets.cs) enforces this for both endpoints below:

- `POST /api/messages/stream` accepts optional `documentIds` (maximum 20). For a new chat, the set is
  validated before the stream starts. Later turns use the stored set when `documentIds` is omitted.
- `POST /api/chats/{chatId}/documents` with `{ "documentIds": ["..."] }` replaces the set (`[]` makes it a
  general chat) and returns `{ documentIds, event }`.
- Both return **409** when another conversation already uses exactly that set. The ProblemDetails body adds
  `conflictChatId` and `conflictTitle` so the client can offer to open that conversation. Unknown or foreign
  newly added document IDs return 400. An existing unavailable ID may be retained
  while another is removed, allowing multiple deleted attachments to be removed
  individually. Once removed, an unavailable ID cannot be re-added.
- Changing the set of a conversation that has messages stores a message with role `event`
  (for example "Added Q3 risk report.pdf · Removed Rent index 2026.xlsx"). The web part shows it as a divider.
  Events are never sent to the model.

The duplicate check reads the user's chat list before writing, so two simultaneous requests can still create
a duplicate. That's acceptable for a per-user UI; it isn't a transactional guarantee.

`GET /api/chats/{chatId}` returns IDs, available document metadata, messages (including events), citations
and retrieval details. Deleted documents stay as unavailable IDs until removed and are never searched.
Removing a document affects future retrieval; historical messages keep their saved excerpts.
Older chats start without documents (general chats) until you add some.

## Skills

A skill is a folder in [skills](./skills) with a `SKILL.md` file and optional
`references/` or `assets/` files. Skills are instructions, not code: they tell the
model how to combine the tools above for a task. At startup
[SkillCatalog](./Services/Agent/SkillCatalog.cs) loads every valid skill and lists its
name and description in the system prompt; the model calls `load_skill` when a request
matches and `read_skill_resource` for extra files.

Users can also pick a skill explicitly. `GET /api/skills` returns `[{ name, description }]`
(available in mock mode too). `POST /api/messages/stream` accepts an optional `skill`
(the skill name; unknown or malformed names return 400 before anything is stored). For that
message the skill's instructions and resource list are added to the model's instructions, so
it doesn't need `load_skill`. The name is stored on the user message (`skill` in
`GET /api/chats/{id}`) so the thread can show it. A chosen skill applies to one message only.
The skill's display name in the web part comes from its `name` (`extract-and-generate` →
"Extract and generate"), so write `description` for people as well as the model.

```markdown
---
name: my-skill
description: What the skill does and when to use it. The model only sees this until it loads the skill.
---

# Instructions

Step-by-step guidance that refers to the available tools.
```

Rules: `name` is lowercase letters, digits and hyphens (max 64) and equals the folder
name; `description` is required (max 1,024 characters). Resource files must be
`.md`, `.txt`, `.json`, `.csv`, `.yaml`, `.yml`, `.xml` or `.html` and at most 256 KB.
Invalid skills are skipped with an error in the log. Skills are copied to the build
output, so restart (locally) or redeploy the API after changes.

The bundled [extract-and-generate](./skills/extract-and-generate/SKILL.md) skill
extracts data from the user's documents and creates a Word, Excel or PDF file. Saving
files needs the Graph permission and client secret from the root README.

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
3. In [package-solution.json](../../frontend/spfx/config/package-solution.json),
   `webApiPermissionRequests[].resource` must match the API app registration's
   **display name**, currently `Hello Copilot Mock API`; `scope` must be
   `access_as_user`. It is not the client ID or Application ID URI.
4. Package/deploy the SPFx solution and approve the pending permission in
   **SharePoint admin center > API access**. Permission changes require a version
   bump, repackaging and redeployment. No separate SPFx app registration, API
   client secret or OBO flow is required for this Search/Foundry backend.
5. In the [web part manifest](../../frontend/spfx/src/webparts/helloCopilot/HelloCopilotWebPart.manifest.json),
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
| HTTP 200 with an SSE `error` after enabling real responses | Inspect API logs using the reference ID; check Azure identity/RBAC, network access and model deployment |
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

The `{activity}`, `{text}`, `{error}` and `[DONE]` SSE contract is unchanged; a
`{chatId}` event is now sent first. Send `chatId` in the body to continue a chat.
Only successful streams end with `[DONE]`; upstream failures are logged and return
an `error` event with a reference ID, not internal Azure details. Disconnects cancel
the agent loop. Search failures are reported to the model as a tool error so it can
answer without sources.

## Tests

From the repository root:

```powershell
dotnet test .\src\backend\api.Tests\api.Tests.csproj
```
