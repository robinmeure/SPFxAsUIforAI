# Private API ingress cutover

This is a separately approved deployment, not something performed by the application
refactor. The API host remains externally owned. The template creates its private
endpoint and DNS; it does not recreate App Service or automatically close public access.

The Cosmos Direct-mode/private-network port issue is deferred. Do not certify a
fully functioning private chat deployment while it remains unresolved. Test API
ingress independently, and record any backend-dependent checks as blocked.

## Prerequisites

- An existing App Service on a private-endpoint-capable plan, running the API with
  real JWT authentication and its managed identity. Inventory all deployment slots.
- The private network from [main.bicep](../../infra/main.bicep), APIM with access to
  that VNet, and the API's **outbound** integration with the delegated integration
  subnet. Outbound integration does not provide private inbound access.
- A private release agent, VPN-connected workstation, or equivalent approved path
  to the endpoint subnet and its DNS. Test ZIP deployment from that path before
  closing public access; ordinary public hosted agents cannot reach private SCM.
- DNS forwarding to the linked Azure private zone when using custom DNS.
  Reuse the same zone resource group for all site/slot deployments. If a
  centrally managed `privatelink.azurewebsites.net` zone already exists, integrate
  with that zone rather than creating a conflicting zone/link.
- Review current App Service/APIM SKU capabilities and subnet NSGs. The endpoint
  subnet must differ from the delegated outbound integration subnet.
- Record current `publicNetworkAccess`, backend URL, DNS and release configuration.
  Changing public network access on Linux can restart the app.

## Prepare and inspect

Run commands from the repository root in an authenticated PowerShell session.
Use actual approved resource names; do not copy credentials into source or logs.

```powershell
$rg = '<network-resource-group>'
$appRg = '<app-resource-group>'
$appName = '<app-name>'
$app = az webapp show -g $appRg -n $appName -o json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Could not read the App Service.' }
$o = az deployment group show -g $rg -n main --query properties.outputs -o json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Could not read network outputs.' }
if (-not $o.vnetId.value -or -not $o.privateEndpointSubnetId.value) { throw 'Private networking is not deployed.' }

az webapp deployment slot list -g $appRg -n $appName -o table
az webapp vnet-integration list -g $appRg -n $appName -o table
az bicep build --file infra\api-private-ingress.bicep --stdout > $null
az deployment group what-if -g $rg -f infra\api-private-ingress.bicep `
  -p appServiceResourceId=$($app.id) vnetResourceId=$($o.vnetId.value) `
  privateEndpointSubnetId=$($o.privateEndpointSubnetId.value)
```

Deploy only after approving that what-if and the resource cost:

```powershell
az deployment group create -g $rg -n api-private-ingress -f infra\api-private-ingress.bicep `
  -p appServiceResourceId=$($app.id) vnetResourceId=$($o.vnetId.value) `
  privateEndpointSubnetId=$($o.privateEndpointSubnetId.value)
```

For each used slot, deploy again with `slotName=<slot>` and a distinct deployment
name. It uses the parent site resource ID and the `sites-<slot>` subresource.
An endpoint for production does not protect a slot.

If outbound integration is absent, configure it separately with the existing
`appIntegrationSubnetId` output, after reviewing its effect on dependency access:

```powershell
az webapp vnet-integration add -g $appRg -n $appName `
  --vnet $o.vnetId.value --subnet $o.appIntegrationSubnetId.value
```

## Verify the private path before cutover

1. Check that the private endpoint connection is approved.
2. From the APIM network and private release agent, resolve the application's
   actual default hostname and its SCM hostname to the private endpoint IP.
   The private DNS zone group creates both records. Use the actual hostnames from
   the resource, including any platform-generated suffix, not an assumed name.
3. Keep APIM's backend URL on the normal HTTPS application hostname, not a
   private IP or `privatelink` hostname. This preserves TLS certificate matching.
4. Verify authenticated APIM access. Verify an SSE response arrives incrementally,
   with backend buffering disabled. If a backend dependency blocks the response,
   record that separately; do not treat a 401 or an error stream as success.
5. Publish the release from the private agent through SCM, using the existing ZIP
   deployment procedure. Verify the resulting version before proceeding.

## Explicit public-access cutover

After the preceding checks succeed, obtain operator approval and disable public
access on production:

```powershell
az resource update --ids $app.id --set properties.publicNetworkAccess=Disabled
if ($LASTEXITCODE -ne 0) { throw 'Public-access cutover failed.' }
az resource show --ids $app.id --query properties.publicNetworkAccess -o tsv
```

Repeat against each slot resource ID (`<site-id>/slots/<slot>`). Disable or
decommission unused public slots; never leave one as an undocumented bypass.
Do not change backend JWT validation, APIM policy or Cosmos networking in this step.

## Acceptance and rollback

- From outside the VNet, direct application requests must be denied **even with
  a valid application token**. Authentication failure by itself proves nothing
  about network isolation.
- Check direct SCM access is denied externally and usable by the private release
  agent. Check every slot and custom hostname.
- Through APIM, verify chat persistence, document operations and incremental SSE
  once all backend network dependencies are working. Record the deferred Cosmos
  blocker explicitly if it prevents these checks.
- Keep evidence of private DNS, public-access settings, private release success
  and outside-network denial with the release record.
- Roll back the application release/private configuration first. Reopening public
  network access needs separate explicit approval and must be reported as degraded
  protection; it is not an automatic failure handler.
- Local development/tunnels remain public-development-only and are not changed
  by this procedure.

Reference: [Microsoft App Service private endpoints](https://learn.microsoft.com/azure/app-service/overview-private-endpoint).
