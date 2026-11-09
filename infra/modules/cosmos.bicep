// Cosmos DB for NoSQL (serverless, Entra-only auth) for chat history and the users' document registry.

param location string
param name string
param principals array
param privateEndpointSubnetId string = ''
param privateDnsZoneIds array = []

var isPrivate = !empty(privateEndpointSubnetId)
var dataContributor = '00000000-0000-0000-0000-000000000002'

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: name
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    capabilities: [{ name: 'EnableServerless' }]
    consistencyPolicy: { defaultConsistencyLevel: 'Session' }
    locations: [{ locationName: location, failoverPriority: 0, isZoneRedundant: false }]
    disableLocalAuth: true
    minimalTlsVersion: 'Tls12'
    // Public mode: only traffic allowed by the network security perimeter (main.bicep) gets in, like in rg-kairos.
    publicNetworkAccess: isPrivate ? 'Disabled' : 'SecuredByPerimeter'
  }

  resource database 'sqlDatabases' = {
    name: 'chatbot'
    properties: { resource: { id: 'chatbot' } }

    // One logical partition per chat ([userId, chatId]); listing a user's chats is a prefix query on userId.
    resource chats 'containers' = {
      name: 'chats'
      properties: {
        resource: {
          id: 'chats'
          partitionKey: { paths: ['/userId', '/chatId'], kind: 'MultiHash', version: 2 }
          indexingPolicy: {
            indexingMode: 'consistent'
            includedPaths: [{ path: '/*' }]
            excludedPaths: [{ path: '/content/?' }, { path: '/"_etag"/?' }]
          }
        }
      }
    }

    resource documents 'containers' = {
      name: 'documents'
      properties: {
        resource: {
          id: 'documents'
          partitionKey: { paths: ['/userId'], kind: 'Hash', version: 2 }
        }
      }
    }
  }
}

resource roles 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = [for principal in principals: {
  parent: cosmos
  name: guid(cosmos.id, principal.id, dataContributor)
  properties: {
    roleDefinitionId: '${cosmos.id}/sqlRoleDefinitions/${dataContributor}'
    principalId: principal.id
    scope: cosmos.id
  }
}]

module endpoint 'private-endpoint.bicep' = if (isPrivate) {
  name: 'pe-cosmos'
  params: {
    location: location
    name: 'pe-${name}'
    subnetId: privateEndpointSubnetId
    serviceId: cosmos.id
    groupId: 'Sql'
    privateDnsZoneIds: privateDnsZoneIds
  }
}

output endpoint string = cosmos.properties.documentEndpoint
output id string = cosmos.id
