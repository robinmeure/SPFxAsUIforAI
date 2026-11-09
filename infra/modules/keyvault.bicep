// Holds the API app registration's client secret for the Graph on-behalf-of flow. The secret value is added
// manually (az keyvault secret set) and is never stored in source.

param location string
param name string
param principals array
param privateEndpointSubnetId string = ''
param privateDnsZoneIds array = []

var isPrivate = !empty(privateEndpointSubnetId)
var secretsUser = '4633458b-17de-408a-b874-0445c86b69e6'
// A developer also stores the secret, so users get Secrets Officer; the API identity only reads.
var secretsOfficer = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: isPrivate ? 'Disabled' : 'Enabled'
  }
}

resource roles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principal in principals: {
  scope: keyVault
  name: guid(keyVault.id, principal.id, principal.type == 'User' ? secretsOfficer : secretsUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', principal.type == 'User' ? secretsOfficer : secretsUser)
    principalId: principal.id
    principalType: principal.type
  }
}]

module endpoint 'private-endpoint.bicep' = if (isPrivate) {
  name: 'pe-keyvault'
  params: {
    location: location
    name: 'pe-${name}'
    subnetId: privateEndpointSubnetId
    serviceId: keyVault.id
    groupId: 'vault'
    privateDnsZoneIds: privateDnsZoneIds
  }
}

output name string = keyVault.name
output uri string = keyVault.properties.vaultUri
