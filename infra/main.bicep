// Everything the chat API needs: APIM (public entry point), Azure AI Search, Foundry (chat, embeddings and Document
// Intelligence), Cosmos DB, Key Vault and monitoring, plus the RBAC for the API identity and an optional developer.
// The API itself runs elsewhere (locally behind a dev tunnel during development); pass its URL as apiBackendUrl.
//
// privateNetworking=false (default): public endpoints with Entra-only auth, so a locally running API can reach them.
// privateNetworking=true: VNet, private endpoints, public access disabled on the backends and APIM in External VNet
// mode. The API must then run inside the VNet (snet-app-integration), as in
// docs/design/apim-public-ingress-private-backends.md.
//
// Deploy: see README section 7.

targetScope = 'resourceGroup'

@description('Location for all resources.')
param location string = resourceGroup().location

@description('Short prefix for resource names (lowercase letters and digits).')
@minLength(3)
@maxLength(12)
param namePrefix string = 'chatbot'

@description('Deploy a VNet with private endpoints and disable public access on Search, Foundry, Cosmos DB and Key Vault.')
param privateNetworking bool = false

@description('Object ID of the API identity when it is hosted in Azure (managed identity). Leave empty for local development.')
param apiPrincipalId string = ''

@description('Object ID of a developer who runs the API locally with az login; gets the same data-plane roles.')
param developerPrincipalId string = ''

@description('Entra tenant ID that issues the users\' tokens.')
param tenantId string = tenant().tenantId

@description('Client ID of the API app registration (the token audience).')
param apiClientId string

@description('Base URL of the chat API that APIM forwards to, for example the dev tunnel URL https://<id>-5198.<region>.devtunnels.ms.')
param apiBackendUrl string

@description('Browser origins allowed to call the API through APIM (SharePoint tenant, Teams).')
param allowedOrigins array

@description('APIM tier. Developer has no SLA; use StandardV2 or Premium for production.')
param apimSku string = 'Developer'

@description('APIM publisher email (required by APIM; receives service notifications).')
param apimPublisherEmail string

@description('Per-user chat calls per minute at the gateway.')
param callsPerMinute int = 30

@description('Public client IPs allowed through the Cosmos DB network security perimeter (public mode), for example your machine running the API locally.')
param cosmosAllowedIps array = []

@description('Chat model deployment. The API setting Rag:ModelDeployment must match the name.')
param chatModel object = {
  name: 'gpt-6.1-sol'
  version: '2026-09-29'
  sku: 'GlobalStandard'
  capacity: 50
}

@description('Embedding model deployment. The index uses 1024 dimensions (Rag:EmbeddingDimensions).')
param embeddingModel object = {
  name: 'text-embedding-3-large'
  version: '1'
  sku: 'GlobalStandard'
  capacity: 100
}

var suffix = uniqueString(resourceGroup().id)
var baseName = '${namePrefix}-${suffix}'
var principals = filter([
  { id: apiPrincipalId, type: 'ServicePrincipal' }
  { id: developerPrincipalId, type: 'User' }
], principal => !empty(principal.id))

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    location: location
    name: baseName
  }
}

module network 'modules/network.bicep' = if (privateNetworking) {
  name: 'network'
  params: {
    location: location
    name: baseName
  }
}

var peSubnetId = privateNetworking ? network!.outputs.privateEndpointSubnetId : ''

module search 'modules/search.bicep' = {
  name: 'search'
  params: {
    location: location
    name: 'srch-${baseName}'
    principals: principals
    privateEndpointSubnetId: peSubnetId
    privateDnsZoneIds: privateNetworking ? [network!.outputs.zoneIds.search] : []
  }
}

module foundry 'modules/foundry.bicep' = {
  name: 'foundry'
  params: {
    location: location
    name: 'aif-${baseName}'
    principals: principals
    chatModel: chatModel
    embeddingModel: embeddingModel
    privateEndpointSubnetId: peSubnetId
    privateDnsZoneIds: privateNetworking ? [network!.outputs.zoneIds.cognitiveServices, network!.outputs.zoneIds.openAi, network!.outputs.zoneIds.aiServices] : []
  }
}

module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmos'
  params: {
    location: location
    name: 'cosmos-${baseName}'
    principals: principals
    privateEndpointSubnetId: peSubnetId
    privateDnsZoneIds: privateNetworking ? [network!.outputs.zoneIds.cosmos] : []
  }
}

// Public mode: Cosmos DB only accepts the listed client IPs and, when the API is hosted in Azure, managed identities
// from this subscription. Private mode uses the private endpoint instead.
module cosmosPerimeter 'modules/perimeter.bicep' = if (!privateNetworking) {
  name: 'cosmos-perimeter'
  params: {
    location: location
    name: 'nsp-${baseName}'
    profileName: 'cosmos-profile'
    resourceIds: [cosmos.outputs.id]
    allowedAddressPrefixes: cosmosAllowedIps
    allowedSubscriptionIds: empty(apiPrincipalId) ? [] : [subscription().subscriptionId]
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    location: location
    name: take('kv-${namePrefix}-${suffix}', 24)
    principals: principals
    privateEndpointSubnetId: peSubnetId
    privateDnsZoneIds: privateNetworking ? [network!.outputs.zoneIds.keyVault] : []
  }
}

module apim 'modules/apim.bicep' = {
  name: 'apim'
  params: {
    location: location
    name: 'apim-${baseName}'
    sku: apimSku
    publisherEmail: apimPublisherEmail
    publisherName: namePrefix
    tenantId: tenantId
    apiClientId: apiClientId
    apiBackendUrl: apiBackendUrl
    allowedOrigins: allowedOrigins
    callsPerMinute: callsPerMinute
    appInsightsId: monitoring.outputs.appInsightsId
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    subnetId: privateNetworking ? network!.outputs.apimSubnetId : ''
  }
}

output searchEndpoint string = search.outputs.endpoint
output foundryEndpoint string = foundry.outputs.endpoint
output chatDeployment string = foundry.outputs.chatDeployment
output embeddingDeployment string = foundry.outputs.embeddingDeployment
output cosmosEndpoint string = cosmos.outputs.endpoint
output keyVaultName string = keyVault.outputs.name
output keyVaultUri string = keyVault.outputs.uri
output apimGatewayUrl string = apim.outputs.gatewayUrl
@description('Set this as apiUrl in the web part (manifest or property pane).')
output webPartApiUrl string = '${apim.outputs.gatewayUrl}/chat/api/messages/stream'
output appIntegrationSubnetId string = privateNetworking ? network!.outputs.appIntegrationSubnetId : ''
output vnetId string = privateNetworking ? network!.outputs.vnetId : ''
output privateEndpointSubnetId string = privateNetworking ? network!.outputs.privateEndpointSubnetId : ''
