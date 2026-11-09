// Foundry (AIServices) account with the chat and embedding deployments. The same account serves Document
// Intelligence (prebuilt-layout), so there is no separate Document Intelligence resource.

param location string
param name string
param principals array
param chatModel object
param embeddingModel object
param privateEndpointSubnetId string = ''
param privateDnsZoneIds array = []

var isPrivate = !empty(privateEndpointSubnetId)
var roleIds = [
  '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd' // Cognitive Services OpenAI User: Responses API and embeddings
  'a97b65f3-24c7-4388-baec-2e87135dc908' // Cognitive Services User: Document Intelligence
]
var assignments = flatten(map(principals, principal => map(roleIds, roleId => { principal: principal, roleId: roleId })))

resource foundry 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: name
  location: location
  kind: 'AIServices'
  sku: { name: 'S0' }
  identity: { type: 'SystemAssigned' }
  properties: {
    customSubDomainName: name
    allowProjectManagement: true
    disableLocalAuth: true
    publicNetworkAccess: isPrivate ? 'Disabled' : 'Enabled'
    networkAcls: { defaultAction: isPrivate ? 'Deny' : 'Allow' }
  }
}

resource chat 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundry
  name: chatModel.name
  sku: { name: chatModel.sku, capacity: chatModel.capacity }
  properties: {
    model: { format: 'OpenAI', name: chatModel.name, version: chatModel.version }
  }
}

// Deployments on one account must be created one at a time.
resource embedding 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundry
  name: embeddingModel.name
  dependsOn: [chat]
  sku: { name: embeddingModel.sku, capacity: embeddingModel.capacity }
  properties: {
    model: { format: 'OpenAI', name: embeddingModel.name, version: embeddingModel.version }
  }
}

resource roles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for assignment in assignments: {
  scope: foundry
  name: guid(foundry.id, assignment.principal.id, assignment.roleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', assignment.roleId)
    principalId: assignment.principal.id
    principalType: assignment.principal.type
  }
}]

module endpoint 'private-endpoint.bicep' = if (isPrivate) {
  name: 'pe-foundry'
  params: {
    location: location
    name: 'pe-${name}'
    subnetId: privateEndpointSubnetId
    serviceId: foundry.id
    groupId: 'account'
    privateDnsZoneIds: privateDnsZoneIds
  }
}

// The API uses this host for the OpenAI v1 API and for Document Intelligence.
output endpoint string = 'https://${name}.openai.azure.com'
output chatDeployment string = chat.name
output embeddingDeployment string = embedding.name
