// API Management: the single public entry point. It validates the user's Entra token and scope, rate-limits per
// user and forwards the original token to the chat API, streaming the response (SSE). In private mode it runs in
// External VNet mode so it can reach backends on private endpoints.

param location string
param name string
param sku string = 'Developer'
param publisherEmail string
param publisherName string
param tenantId string
param apiClientId string
@description('Base URL of the chat API (for local development, the dev tunnel URL). Paths are forwarded unchanged.')
param apiBackendUrl string
param allowedOrigins array
param callsPerMinute int = 30
param timeoutSeconds int = 120
param appInsightsId string
param appInsightsConnectionString string
param subnetId string = ''

var isPrivate = !empty(subnetId)

resource publicIp 'Microsoft.Network/publicIPAddresses@2024-05-01' = if (isPrivate) {
  name: 'pip-${name}'
  location: location
  sku: { name: 'Standard' }
  properties: {
    publicIPAllocationMethod: 'Static'
    dnsSettings: { domainNameLabel: name }
  }
}

resource apim 'Microsoft.ApiManagement/service@2024-05-01' = {
  name: name
  location: location
  sku: { name: sku, capacity: 1 }
  identity: { type: 'SystemAssigned' }
  properties: {
    publisherEmail: publisherEmail
    publisherName: publisherName
    virtualNetworkType: isPrivate ? 'External' : 'None'
    virtualNetworkConfiguration: isPrivate ? { subnetResourceId: subnetId } : null
    publicIpAddressId: isPrivate ? publicIp.id : null
  }
}

resource tenantIdValue 'Microsoft.ApiManagement/service/namedValues@2024-05-01' = {
  parent: apim
  name: 'tenant-id'
  properties: { displayName: 'tenant-id', value: tenantId }
}

resource clientIdValue 'Microsoft.ApiManagement/service/namedValues@2024-05-01' = {
  parent: apim
  name: 'api-client-id'
  properties: { displayName: 'api-client-id', value: apiClientId }
}

resource callsValue 'Microsoft.ApiManagement/service/namedValues@2024-05-01' = {
  parent: apim
  name: 'chat-calls-per-minute'
  properties: { displayName: 'chat-calls-per-minute', value: string(callsPerMinute) }
}

resource timeoutValue 'Microsoft.ApiManagement/service/namedValues@2024-05-01' = {
  parent: apim
  name: 'chat-timeout-seconds'
  properties: { displayName: 'chat-timeout-seconds', value: string(timeoutSeconds) }
}

resource backend 'Microsoft.ApiManagement/service/backends@2024-05-01' = {
  parent: apim
  name: 'chat-api'
  properties: {
    protocol: 'http'
    url: apiBackendUrl
    tls: { validateCertificateChain: true, validateCertificateName: true }
  }
}

resource logger 'Microsoft.ApiManagement/service/loggers@2024-05-01' = {
  parent: apim
  name: 'appinsights'
  properties: {
    loggerType: 'applicationInsights'
    resourceId: appInsightsId
    credentials: { connectionString: appInsightsConnectionString }
  }
}

resource api 'Microsoft.ApiManagement/service/apis@2024-05-01' = {
  parent: apim
  name: 'chat'
  properties: {
    displayName: 'Chat API'
    path: 'chat'
    protocols: ['https']
    serviceUrl: apiBackendUrl
    subscriptionRequired: false
  }
}

resource operations 'Microsoft.ApiManagement/service/apis/operations@2024-05-01' = [for method in ['GET', 'POST', 'DELETE']: {
  parent: api
  name: toLower(method)
  properties: {
    displayName: '${method} /*'
    method: method
    urlTemplate: '/*'
  }
}]

resource policy 'Microsoft.ApiManagement/service/apis/policies@2024-05-01' = {
  parent: api
  name: 'policy'
  dependsOn: [tenantIdValue, clientIdValue, callsValue, timeoutValue, backend]
  properties: {
    format: 'rawxml'
    value: replace(loadTextContent('chat-api-policy.xml'), '__ALLOWED_ORIGINS__',
      join(map(allowedOrigins, origin => '<origin>${origin}</origin>'), ''))
  }
}

// Metadata only: no headers (so never Authorization) and no request or response bodies, which would also break SSE.
resource diagnostics 'Microsoft.ApiManagement/service/apis/diagnostics@2024-05-01' = {
  parent: api
  name: 'applicationinsights'
  properties: {
    loggerId: logger.id
    alwaysLog: 'allErrors'
    verbosity: 'information'
    logClientIp: false
    sampling: { samplingType: 'fixed', percentage: 100 }
    frontend: {
      request: { headers: [], body: { bytes: 0 } }
      response: { headers: [], body: { bytes: 0 } }
    }
    backend: {
      request: { headers: [], body: { bytes: 0 } }
      response: { headers: [], body: { bytes: 0 } }
    }
  }
}

output gatewayUrl string = apim.properties.gatewayUrl
