// Deploy separately after the existing App Service and private network are ready.
// Public access is closed explicitly in the cutover runbook, not as a side effect of this deployment.
targetScope = 'resourceGroup'

@description('Region of the private endpoint subnet.')
param location string = resourceGroup().location

@description('Resource ID of the existing App Service (the parent site, including when targeting a slot).')
param appServiceResourceId string

@description('Existing VNet used by APIM and the private deployment agent.')
param vnetResourceId string

@description('Private endpoint subnet, not the delegated App Service integration subnet.')
param privateEndpointSubnetId string

@description('Empty for production. A named slot needs its own endpoint and public-access cutover.')
param slotName string = ''

var suffix = uniqueString(appServiceResourceId, slotName)

resource zone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.azurewebsites.net'
  location: 'global'
}

resource link 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: zone
  name: 'api-${uniqueString(vnetResourceId)}'
  location: 'global'
  properties: {
    virtualNetwork: { id: vnetResourceId }
    registrationEnabled: false
  }
}

module endpoint 'modules/private-endpoint.bicep' = {
  name: 'api-ingress-${suffix}'
  params: {
    location: location
    name: 'pe-api-${suffix}'
    subnetId: privateEndpointSubnetId
    serviceId: appServiceResourceId
    groupId: empty(slotName) ? 'sites' : 'sites-${slotName}'
    privateDnsZoneIds: [zone.id]
  }
}

output privateDnsZoneId string = zone.id
output privateEndpointName string = 'pe-api-${suffix}'
output cutoverTargetResourceId string = empty(slotName) ? appServiceResourceId : '${appServiceResourceId}/slots/${slotName}'
