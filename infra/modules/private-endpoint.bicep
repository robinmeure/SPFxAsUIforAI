// One private endpoint plus its DNS zone group, so the service's public FQDN resolves to the private IP in the VNet.

param location string
param name string
param subnetId string
param serviceId string
param groupId string
param privateDnsZoneIds array

resource endpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: name
  location: location
  properties: {
    subnet: { id: subnetId }
    privateLinkServiceConnections: [
      {
        name: name
        properties: {
          privateLinkServiceId: serviceId
          groupIds: [groupId]
        }
      }
    ]
  }

  resource zones 'privateDnsZoneGroups' = {
    name: 'default'
    properties: {
      privateDnsZoneConfigs: [for zoneId in privateDnsZoneIds: {
        name: replace(last(split(zoneId, '/')), '.', '-')
        properties: { privateDnsZoneId: zoneId }
      }]
    }
  }
}
