# Private networking for the API behind an SPFx web part in SharePoint Online and Teams

Research date: 2026-11-04. This is documentation research only. No live tenant, browser, or Azure environment was tested. Official pages cited below may predate this date, so re-check them before deciding anything. No SKU recipes or prices are given. Items marked **[documented limitation]** come from official product documentation. Items marked **[unvalidated]** or **[unverified]** are not covered by official documentation I found, so their suitability is unknown and needs a proof of concept (PoC); they are not claimed to be unsupported.

## 1. Short answer

**Yes, but only if the user's browser can route to the API. Teams and SharePoint Online never relay the call.**

The SPFx web part runs in the user's browser and calls the API directly with `fetch`. Microsoft 365 has no server-side hop that could reach a private API on the user's behalf. SPFx's own client, `AadHttpClient`, obtains a token and the browser then calls the API; it is not a relay ([use-aadhttpclient](https://learn.microsoft.com/sharepoint/dev/spfx/use-aadhttpclient)).

Two things that are easy to confuse:

- **Token, CORS and Teams `validDomains` do not create a network route.** `validDomains` only tells the Teams client which domains it may load or navigate to ([root object](https://learn.microsoft.com/en-us/microsoft-365/extensibility/schema/root#validdomains)). CORS only tells the browser whether a response may be read. Neither makes a private IP reachable.
- **There are two readings of "private backend"**, and they lead to different designs:
  - **Strict: no public entry point at all.** Every client must be on a network that can reach the API privately.
  - **Private origin, controlled public ingress.** The API workload has no public origin, but a managed gateway is deliberately public and authenticated.

## 2. Repository facts that shape the options

Paths are relative to this file.

| Fact | Source |
|---|---|
| The browser calls `POST /api/messages/stream` with `fetch`, sending `Authorization`, JSON, `Accept: text/event-stream` and an `AbortSignal`, then reads the SSE body. | [`HelloCopilot.tsx`](../../spfx/src/webparts/helloCopilot/components/HelloCopilot.tsx) (`streamReply`, about line 64 onward) |
| The property pane exposes `apiUrl`, `apiResource` and `apiScope`. The API README says `apiScope` is informational and the token request actually uses `apiResource`. | [`HelloCopilotWebPart.ts`](../../spfx/src/webparts/helloCopilot/HelloCopilotWebPart.ts), [`api/README.md`](../../api/README.md) |
| CORS runs before authentication, with an exact origin list. The `access_as_user` scope policy is enforced. | [`Program.cs`](../../api/Program.cs) |
| Each SSE event is flushed. The response sets `no-cache` and `X-Accel-Buffering: no`, ends with `[DONE]`, and passes the `CancellationToken` down to the service. There is no periodic heartbeat. | [`MessagesController.cs`](../../api/Controllers/MessagesController.cs) |
| The runbook hosts on Azure App Service. Section 7.3 says to inspect the actual `Origin` in each host rather than assuming `teams.microsoft.com`. | [`README.md`](../../README.md) sections 7 and 7.3 |
| Search and Azure OpenAI use normal HTTPS FQDNs, and private-link DNS normally preserves those names. | `api/appsettings*.json` (Rag section) |

**Existing web part instances keep their saved properties.** Changing manifest defaults for `apiUrl`, `apiResource` or `apiScope` does not reconfigure web parts already added to a page. Existing instances keep their saved values. After any API URL or audience change, edit each placed instance (or re-add it) and verify.

## 3. Four patterns, kept distinct

### 3.1 Private-only API, reached over LAN, VPN, ExpressRoute or private DNS

The API has a private IP (for example an App Service private endpoint) and public access is disabled. Every client has to resolve the name to that private IP and route to it.

**Network and DNS**
- A private endpoint is inbound-only. It gives the app an IP in your VNet. Outbound traffic does not use it; VNet integration is the separate outbound feature ([App Service private endpoint](https://learn.microsoft.com/en-us/azure/app-service/overview-private-endpoint)).
- You can reach the app from on-premises through VPN or ExpressRoute private peering. You must provide private DNS or an Azure DNS private zone (`privatelink.azurewebsites.net`). The URL has to match the app's default name, because the default certificate is for `*.azurewebsites.net`. A custom domain needs extra DNS work (same page).
- Public access and a private endpoint can coexist. To isolate the app, disable public network access explicitly (same page, "Security considerations").
- A client on the public internet resolving the public CNAME reaches a public IP that returns 403 (same page, "DNS").

**Where the browser must be.** Teams desktop, Teams web and Teams mobile all execute your page on the user's device. Reachability is therefore a property of each device and network, not of Teams. Any device off the corporate network (home, unmanaged, BYOD, mobile data) fails unless it has VPN or Entra Private Access.

**Entra Private Access / Global Secure Access client.** Official pages confirm clients for Windows, macOS, Android and iOS ([concept-clients](https://learn.microsoft.com/entra/global-secure-access/concept-clients)). Requirements vary by platform:
- **Windows:** 64-bit Windows 10 LTSC 2021 or newer, or Windows 11. AVD multi-session is not supported. The device must be Entra joined, hybrid joined or registered; on registered devices only Private Access traffic is supported ([Windows client](https://learn.microsoft.com/entra/global-secure-access/how-to-install-windows-client)).
- **iOS:** delivered through Defender for Endpoint as a local self-looping VPN, iOS 16 or newer, Entra registered device ([iOS client](https://learn.microsoft.com/entra/global-secure-access/how-to-install-ios-client)).
- **Android:** supports Android Enterprise scenarios (fully managed, corporate-owned with work profile, personal with work profile) ([Android client](https://learn.microsoft.com/entra/global-secure-access/how-to-install-android-client)).
- **macOS:** BYOD without device enrollment is not supported ([BYOD](https://learn.microsoft.com/entra/global-secure-access/concept-bring-your-own-device)).
- **Licensing:** Private Access has its own licence requirement; see the licensing table in [What is Global Secure Access](https://learn.microsoft.com/entra/global-secure-access/overview-what-is-global-secure-access). No prices here.
- **Streaming of an SSE `fetch` response through Private Access** was not found on these pages: **[unverified]**, test it.

### 3.2 Public authenticated ingress, private backend with no public API origin

Clients call a public, intentionally exposed gateway. The gateway reaches an API that has no public endpoint. This is not "no public entry"; it is a controlled public entry with a private origin.

**API Management.** Networking options differ by tier ([virtual-network-concepts](https://learn.microsoft.com/en-us/azure/api-management/virtual-network-concepts)):
- **Inbound private endpoint** secures client access to the gateway. It is inbound only. Public access can be disabled only after a private endpoint is configured.
- **Outbound VNet integration** (Standard v2 and Premium v2) lets the gateway reach private backends, while the gateway stays publicly reachable. Combining an inbound private endpoint with outbound integration on Standard v2 gives end-to-end isolation, but then clients are private again, which is pattern 3.1.
- **VNet injection** has separate classic-tier external/internal modes and a Premium v2 mode.
- **Do not equate "outbound VNet integration" with "private inbound".** Choose the tier from the current table; this report gives no SKU recipe.

**Application Gateway.** No Microsoft Learn page retrieved gives a complete recipe for a public listener with a private App Service backend for this workload, so treat that exact combination as **[unverified for this workload]** and take it from the Application Gateway documentation. The docs do show APIM in an internal VNet behind an internet-facing Application Gateway ([virtual-network-concepts](https://learn.microsoft.com/en-us/azure/api-management/virtual-network-concepts), "Advanced networking").

**Authentication boundary.** In this design, keep the API's existing Entra JWT and `access_as_user` scope validation enabled; don't assume that an Application Gateway listener or WAF authenticates the user's bearer token. Microsoft documents JWT validation for Azure Application Gateway as a **preview** feature ([JWT validation](https://learn.microsoft.com/en-us/azure/application-gateway/json-web-token-overview)); verify its current availability and suitability before relying on it. API Management, by contrast, has a documented `validate-azure-ad-token` policy for validating Entra tokens ([APIM OAuth 2.0 and Entra ID](https://learn.microsoft.com/en-us/azure/api-management/api-management-howto-protect-backend-with-aad)).

**Streaming support, sourced**
- **APIM:** SSE is supported in classic and v2 tiers, not Consumption. Set `<forward-request buffer-response="false"/>`. Avoid `validate-content`, avoid request/response body logging to Azure Monitor, Application Insights and Event Hubs, and disable response caching. Keep idle connections alive: the 4-minute Azure Load Balancer idle timeout applies, so use TCP keepalive or send traffic at least once per 4 minutes ([APIM SSE guidance](https://learn.microsoft.com/en-us/azure/api-management/how-to-server-sent-events)).
- **Application Gateway:** SSE is supported. Disable response buffering and set the Backend Settings request timeout above the longest idle gap between events ([SSE with Application Gateway](https://learn.microsoft.com/en-us/azure/application-gateway/use-server-sent-events)). Buffering is a resource-level setting, not per listener, and is not configurable through the portal. With a WAF SKU, request buffering cannot be fully disabled, but response buffering is unaffected ([proxy buffers](https://learn.microsoft.com/en-us/azure/application-gateway/proxy-buffers)).
- **This repo starts the stream with a POST body.** The cited Microsoft SSE pages describe SSE generally and do not explicitly cover a `fetch` POST with a JSON body and streamed response. Treat POST-plus-SSE through each intermediary as **[unverified]** until tested.
- **Azure Front Door** states it does not support SSE ([Front Door WebSocket](https://learn.microsoft.com/azure/frontdoor/standard-premium/websocket)), so it is not a candidate for this stream.
- **Heartbeat:** the API sends no periodic heartbeat today. Both the 4-minute APIM guidance and the Application Gateway idle-gap guidance suggest one if streams can pause that long. That is a possible small code change, outside this document's scope.

### 3.3 Entra Application Proxy

Application Proxy publishes an on-premises or private-cloud app through a public, Entra-fronted endpoint via a connector. Microsoft documents publishing APIs this way for native clients that use MSAL ([secure API access](https://learn.microsoft.com/en-us/entra/identity/app-proxy/application-proxy-secure-api-access)).

**It is not documented as a drop-in for browser `fetch` from an SPFx page.** Findings:
- **[documented limitation]** The official Application Proxy CORS page says some CORS issues cannot be resolved, for example an access token that expires during a CORS call that redirects to `login.microsoftonline.com` ([application-proxy-understand-cors-issues](https://learn.microsoft.com/entra/identity/app-proxy/application-proxy-understand-cors-issues)). Exact preauthentication behavior for a failed bearer `fetch` through Application Proxy is **unknown** from official docs I found.
- Official CORS guidance says some CORS issues cannot be resolved; one example is an access token expiring during a CORS call that redirects to sign-in ([application-proxy-understand-cors-issues](https://learn.microsoft.com/entra/identity/app-proxy/application-proxy-understand-cors-issues)).
- **[documented limitation]** Complex-application documentation says the Application Proxy cloud service **blocks `OPTIONS` calls** because preflight requests cannot contain authentication information. The documented mitigation is complex (wildcard) applications with app segments and optional CORS rules, with a stated limit of two segments on Entra ID P1/P2 ([complex applications](https://learn.microsoft.com/en-us/entra/identity/app-proxy/application-proxy-configure-complex-application)).
- **[unvalidated]** Whether an `Authorization: Bearer` token for your own API passes Application Proxy preauthentication for a browser `fetch` is not documented as an SPFx pattern in the pages I found. This is an unknown, not a documented limitation.
- **[unvalidated]** No official statement was found on streaming SSE responses through Application Proxy. The FAQ documents WebSocket limitations (cookie discarded, no SSO on the WebSocket request), which do not describe SSE ([FAQ](https://learn.microsoft.com/en-us/entra/identity/app-proxy/application-proxy-faq)). Do not present Application Proxy as seamless or as the baseline.
- It also needs connector infrastructure and Entra licensing; no prices here.

### 3.4 Public API with private downstream Search and Azure OpenAI

This protects only the API's outbound calls: App Service VNet integration for outbound traffic, private endpoints on the downstream services, private DNS, and public access disabled on those services. The API still has a public, internet-reachable front door.

This **is not** private API ingress and does not meet the strict interpretation, but it is useful defense in depth. It is the lowest-risk change here because the browser-to-API path is unchanged. Because private-link DNS keeps the same service FQDNs, the existing `Rag` endpoint configuration likely keeps working; confirm that in a test.

## 4. Browser rules that apply

**Secure contexts and mixed content.** Chrome's Local Network Access (LNA) permission can only be requested in secure contexts ([Chrome LNA blog](https://developer.chrome.com/blog/local-network-access)). SharePoint and Teams pages are HTTPS, so a private API needs HTTPS with a certificate the browser trusts; `http://` from an HTTPS page is a mixed-content problem.

**Local Network Access.**
- Chrome describes LNA as a permission gating requests *from a public site to local network or loopback destinations*. Its blog update dated 2025-09-29 says the permission prompt launches in Chrome 142; Chrome 142 stable was released 2025-10-28 ([release notes](https://developer.chrome.com/release-notes/142)). "Local network" is defined by the **resolved IP** (RFC 1918 ranges, link-local, ULA, and so on).
- **Likely impact on this repo:** a SharePoint or Teams page is a public-site context. If the API hostname resolves to an RFC 1918 address (for example a private endpoint address) through private DNS, Chrome may treat the `fetch` as a local network request and require the permission. Whether the prompt appears for a given browser and Teams client was **not verified**.
- Chrome says it plans an enterprise policy to pre-grant or deny the permission on managed browsers (blog, "Chrome 139 and beyond"). The blog also notes some connection types (WebSockets, WebTransport, WebRTC) were not yet gated when written. The blog may be out of date; check Chrome's current enterprise policy documentation.
- **Iframe permission delegation:** SPFx web parts and Teams tabs commonly run inside iframes. Whether and how LNA permission is delegated to nested frames, and whether Teams desktop (embedded web view) or Teams mobile honor it, was **not found in the sources retrieved**: **[unverified]**. Check the adoption guide linked from the blog and the [WICG spec](https://wicg.github.io/local-network-access/), and test in each client.
- The blog notes the earlier Private Network Access (CORS-preflight based) approach was put on hold. Do not design around PNA preflight headers.
- **Do not claim all Teams clients work.** Desktop, web, iOS and Android embed pages differently, and only Chrome's behavior was researched. Safari/WebKit and Firefox were not researched.

## 5. Decision table

| Requirement | Best fit | Condition | Main risk |
|---|---|---|---|
| **Strict: no public entry; all users on corporate LAN/VPN/ExpressRoute** | Pattern 3.1 | Private DNS reachable from all client networks; trusted HTTPS cert | Off-network and unmanaged devices fail; LNA prompt possible |
| **Strict, but users are remote** | Pattern 3.1 + Entra Private Access client (or VPN) | Device join/registration and client deployment per platform | Mobile and unmanaged coverage varies; SSE behavior unverified |
| **Private backends, uniform API access, controlled public ingress** | Pattern 3.2 with APIM (preferred for this project) | Streaming settings above; Entra token validation retained | A public endpoint exists by design; tier choice and cost |
| **Reuse existing connector estate** | Pattern 3.3 | Only after a proof of concept | OPTIONS blocking is a documented limitation; bearer `fetch` and SSE are unvalidated |
| **Only protect Search and OpenAI traffic** | Pattern 3.4 | VNet integration, private endpoints on downstream services | Not private ingress; API remains public |

**Recommendation.**
- Prefer **APIM as the uniform API entry point** for this project. The requirement is consistent interaction with multiple APIs, not just private connectivity. If "private" means *the workload has no public origin*, use pattern 3.2 with APIM after a streaming proof of concept. Application Gateway remains an alternative networking option, not the preferred API interface.
- If "private" means *no public API entry at all*, keep APIM as the uniform interface but make its gateway private too, with private backend connectivity. This follows pattern 3.1's client-connectivity requirement: only devices with private network reach will work. Run the checklist on every Teams client you must support.
- 3.4 is a safe incremental step either way.
- Do not make 3.3 the baseline. Its OPTIONS limitation is documented, and bearer `fetch` and SSE are unvalidated; consider it only after a PoC shows preflight, bearer token and streaming all work.

### Proposed uniform API contract

These are design recommendations, not deployed configuration:

- **Stable entry point:** expose APIs through a common APIM hostname with consistent API paths and versioning. Clients know the gateway URL, not backend hosting addresses. Preserve this chat API's POST and SSE contract when mapping its route.
- **Consistent authentication, explicit authorization:** validate Entra access tokens at APIM and retain backend token and scope validation. Configure the intended audience and permissions per API; a common hostname does not imply a single token grants access to every API. For this chat API, retain the current resource, audience and `access_as_user` scope, and forward the user's bearer token unchanged.
- **Shared policies with API-specific exceptions:** standardize allowed origins, preflight handling, rate limits, correlation IDs and metadata-only monitoring. Do not embed client secrets or shared APIM subscription keys in the SPFx bundle; they are not confidential in browser code.
- **Streaming-aware policies:** exclude this route from response buffering, response caching and request/response body logging. Preserve the existing SSE event and error format; do not wrap streamed replies in a generic JSON envelope. Validate cancellation and idle behavior through APIM.
- **Private origins:** APIM must resolve and reach each backend privately, with public backend access disabled. For public APIM ingress, users only need to reach the gateway; they do not need private DNS or a route to the backend.

The public-versus-private APIM ingress decision still requires agreement on network policy and supported user devices. Choosing APIM for a uniform interface does not itself settle that decision.

## 6. Diagrams

```text
Pattern 3.1  Strict private
 Browser (SPFx in SharePoint/Teams) --LAN/VPN/ExpressRoute/GSA--> private IP (PE) --> API --> Search/OpenAI
        ^ must resolve the private DNS name; public clients fail (403 or no route)

Pattern 3.2  Preferred uniform API interface, if public ingress is permitted
 Browser --internet, HTTPS, Bearer--> [APIM (public, token validation)]
                                        --VNet/private link--> API (public access disabled) --> Search/OpenAI

Pattern 3.4  Not private ingress
 Browser --internet--> API (public) --VNet integration--> private endpoints --> Search/OpenAI
```

## 7. Minimal repository changes

Mostly configuration; no code change is requested here.

1. **`apiUrl`**: point at the private FQDN (3.1) or the gateway URL (3.2). Update the saved properties on every placed web part (see section 2).
2. **`apiResource` and API audience: retain both.** `apiResource` is the token resource (the Application ID URI used when requesting the token); `apiScope` is informational. The current API validates the bare-client-ID v2 audience. When introducing a transparent gateway, keep the existing `apiResource` value and the API's accepted audience unchanged. Changing the host does not require a new resource or audience. If the gateway validates tokens itself, configure it to accept the same audience and still leave the API's validation in place.
3. **CORS**: the exact allowed origins in [`Program.cs`](../../api/Program.cs) stay the same. If a gateway sits in front, either it passes the API's CORS headers through unchanged or it answers `OPTIONS` itself. Pick one owner for CORS and test it. Per README section 7.3, inspect the real `Origin` in each host.
4. **Teams `validDomains`**: only needed for domains the Teams client must load; API hosts called with `fetch` need no listing for networking. The `staticTabs.contentUrl` domain is trusted automatically ([root object](https://learn.microsoft.com/en-us/microsoft-365/extensibility/schema/root#validdomains)).
5. **Possible later, if tests need it:** a periodic SSE heartbeat in [`MessagesController.cs`](../../api/Controllers/MessagesController.cs) to stay under intermediary idle limits.
6. **Public denial on the origin:** turn public network access off on the App Service once private routing is verified, and confirm the gateway can still reach it.

## 8. Licensing and cost caveats (no prices)

- Private endpoints incur Private Link charges, and App Service private endpoints are available only on certain plans ([App Service private endpoint](https://learn.microsoft.com/en-us/azure/app-service/overview-private-endpoint)).
- APIM networking features depend on tier ([virtual-network-concepts](https://learn.microsoft.com/en-us/azure/api-management/virtual-network-concepts)).
- Global Secure Access Private Access has separate licensing ([overview](https://learn.microsoft.com/entra/global-secure-access/overview-what-is-global-secure-access)).
- Application Proxy complex apps reference Entra ID P1/P2 ([complex applications](https://learn.microsoft.com/en-us/entra/identity/app-proxy/application-proxy-configure-complex-application)).

## 9. Validation acceptance checklist

Run from each client class you must support: Teams desktop, Teams web, Teams mobile, a SharePoint page in a browser; managed and unmanaged devices; on-network and off-network.

| # | Test | Expected |
|---|---|---|
| 1 | Unauthenticated `OPTIONS` preflight from an allowed origin | Success with correct CORS headers, no redirect to sign-in |
| 2 | `OPTIONS` from a disallowed origin | No allow-origin header |
| 3 | `POST` with no token | 401, no HTML sign-in redirect |
| 4 | `POST` with a token lacking `access_as_user` | 403 |
| 5 | `POST` with a wrong audience | 401 |
| 6 | Valid token, valid body | 200 `text/event-stream` |
| 7 | **First SSE chunk** arrives promptly, not after the full answer | Passes |
| 8 | **Ongoing flush**: chunks arrive incrementally through every intermediary | Passes |
| 9 | **Cancellation**: aborting the `fetch` stops server-side work | Passes |
| 10 | **Proxy idle**: a stream paused longer than the intermediary idle limit | Survives or fails predictably; result documented |
| 11 | Request from the public internet to the private API origin | Denied (403 or no route) |
| 12 | Console on a private-IP API: LNA permission prompt or block | Recorded per browser and client |
| 13 | Private DNS resolution from each client network | Resolves to the private address |
| 14 | An existing placed web part with saved properties | Still works after the change, or is reconfigured |

## 10. Open questions

- Does Chrome's LNA prompt appear inside SharePoint/Teams iframes and Teams desktop/mobile web views, and does permission delegation work there? **Not verified.**
- Does Entra Private Access pass a long-lived streamed `fetch` response without buffering? **Not verified.**
- Does an SSE `POST` with a JSON body behave like `EventSource` through APIM and Application Gateway? **Not verified**; the cited pages describe SSE generally.
- Can Application Proxy preauthentication accept a browser `fetch` with a bearer token for a custom API, handle OPTIONS (documented as blocked unless complex apps are used), and stream? **Unvalidated; PoC needed, not baseline.**
- Safari/WebKit and Firefox local-network behavior was not researched.

## 11. Sources

Retrieved 2026-11-04 (Microsoft Learn search and fetch; Chrome and WICG pages fetched directly).

- SPFx `AadHttpClient`: https://learn.microsoft.com/sharepoint/dev/spfx/use-aadhttpclient
- Teams/unified manifest `validDomains`: https://learn.microsoft.com/en-us/microsoft-365/extensibility/schema/root#validdomains
- App Service private endpoints: https://learn.microsoft.com/en-us/azure/app-service/overview-private-endpoint
- APIM virtual network concepts: https://learn.microsoft.com/en-us/azure/api-management/virtual-network-concepts
- APIM SSE: https://learn.microsoft.com/en-us/azure/api-management/how-to-server-sent-events
- Application Gateway SSE: https://learn.microsoft.com/en-us/azure/application-gateway/use-server-sent-events
- Application Gateway buffers: https://learn.microsoft.com/en-us/azure/application-gateway/proxy-buffers
- Front Door WebSocket/SSE note: https://learn.microsoft.com/azure/frontdoor/standard-premium/websocket
- Application Proxy API access: https://learn.microsoft.com/en-us/entra/identity/app-proxy/application-proxy-secure-api-access
- Application Proxy CORS: https://learn.microsoft.com/entra/identity/app-proxy/application-proxy-understand-cors-issues
- Application Proxy complex apps: https://learn.microsoft.com/en-us/entra/identity/app-proxy/application-proxy-configure-complex-application
- Application Proxy FAQ: https://learn.microsoft.com/en-us/entra/identity/app-proxy/application-proxy-faq
- Global Secure Access client overview: https://learn.microsoft.com/entra/global-secure-access/concept-clients
- GSA clients: https://learn.microsoft.com/entra/global-secure-access/how-to-install-windows-client , https://learn.microsoft.com/entra/global-secure-access/how-to-install-ios-client , https://learn.microsoft.com/entra/global-secure-access/how-to-install-android-client
- GSA BYOD: https://learn.microsoft.com/entra/global-secure-access/concept-bring-your-own-device
- GSA licensing: https://learn.microsoft.com/entra/global-secure-access/overview-what-is-global-secure-access
- Chrome Local Network Access: https://developer.chrome.com/blog/local-network-access
- Chrome 142 release notes: https://developer.chrome.com/release-notes/142
- WICG Local Network Access spec: https://wicg.github.io/local-network-access/
- SPFx `AadTokenProvider`: https://learn.microsoft.com/sharepoint/dev/spfx/use-aadtokenprovider


