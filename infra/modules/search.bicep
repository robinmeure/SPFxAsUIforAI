// Azure AI Search with Entra-only auth. The API creates its index at startup (Search Service Contributor) and
// writes and deletes the users' chunks (Search Index Data Contributor). The API embeds queries itself, so Search
// needs no access to Foundry.

param location string
param name string
param sku string = 'basic'
param principals array
param privateEndpointSubnetId string = ''
param privateDnsZoneIds array = []

var isPrivate = !empty(privateEndpointSubnetId)
var roleIds = [
  '7ca78c08-252a-4471-8644-bb5ff32d4ba0' // Search Service Contributor
  '8ebe5a00-799e-43f5-93ac-243d3dce84a7' // Search Index Data Contributor
]
var assignments = flatten(map(principals, principal => map(roleIds, roleId => { principal: principal, roleId: roleId })))

resource search 'Microsoft.Search/searchServices@2025-05-01' = {
  name: name
  location: location
  sku: { name: sku }
  properties: {
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'Default'
    semanticSearch: 'free'
    disableLocalAuth: true
    publicNetworkAccess: isPrivate ? 'disabled' : 'enabled'
  }
}

resource roles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for assignment in assignments: {
  scope: search
  name: guid(search.id, assignment.principal.id, assignment.roleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', assignment.roleId)
    principalId: assignment.principal.id
    principalType: assignment.principal.type
  }
}]

module endpoint 'private-endpoint.bicep' = if (isPrivate) {
  name: 'pe-search'
  params: {
    location: location
    name: 'pe-${name}'
    subnetId: privateEndpointSubnetId
    serviceId: search.id
    groupId: 'searchService'
    privateDnsZoneIds: privateDnsZoneIds
  }
}

output endpoint string = 'https://${search.name}.search.windows.net'
