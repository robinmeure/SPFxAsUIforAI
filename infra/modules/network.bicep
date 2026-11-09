// Private mode only: a spoke VNet with subnets for APIM (External VNet injection), the backends' private endpoints
// and a future hosted API (App Service VNet integration), plus the private DNS zones the endpoints register in.

param location string
param name string
param addressPrefix string = '10.40.0.0/16'

var zoneNames = {
  search: 'privatelink.search.windows.net'
  cosmos: 'privatelink.documents.azure.com'
  keyVault: 'privatelink.vaultcore.azure.net'
  cognitiveServices: 'privatelink.cognitiveservices.azure.com'
  openAi: 'privatelink.openai.azure.com'
  aiServices: 'privatelink.services.ai.azure.com'
}

// Rules required by API Management in External VNet mode (stv2).
resource apimNsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: 'nsg-${name}-apim'
  location: location
  properties: {
    securityRules: [
      {
        name: 'Allow-Client-Https'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: 'Internet'
          sourcePortRange: '*'
          destinationAddressPrefix: 'VirtualNetwork'
          destinationPortRanges: ['80', '443']
        }
      }
      {
        name: 'Allow-ApiManagement-Control'
        properties: {
          priority: 110
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: 'ApiManagement'
          sourcePortRange: '*'
          destinationAddressPrefix: 'VirtualNetwork'
          destinationPortRange: '3443'
        }
      }
      {
        name: 'Allow-LoadBalancer-Probe'
        properties: {
          priority: 120
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: 'AzureLoadBalancer'
          sourcePortRange: '*'
          destinationAddressPrefix: 'VirtualNetwork'
          destinationPortRange: '6390'
        }
      }
      {
        name: 'Allow-Storage'
        properties: {
          priority: 100
          direction: 'Outbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: 'VirtualNetwork'
          sourcePortRange: '*'
          destinationAddressPrefix: 'Storage'
          destinationPortRange: '443'
        }
      }
      {
        name: 'Allow-Sql'
        properties: {
          priority: 110
          direction: 'Outbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: 'VirtualNetwork'
          sourcePortRange: '*'
          destinationAddressPrefix: 'Sql'
          destinationPortRange: '1433'
        }
      }
      {
        name: 'Allow-KeyVault'
        properties: {
          priority: 120
          direction: 'Outbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: 'VirtualNetwork'
          sourcePortRange: '*'
          destinationAddressPrefix: 'AzureKeyVault'
          destinationPortRange: '443'
        }
      }
      {
        name: 'Allow-AzureMonitor'
        properties: {
          priority: 130
          direction: 'Outbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: 'VirtualNetwork'
          sourcePortRange: '*'
          destinationAddressPrefix: 'AzureMonitor'
          destinationPortRanges: ['1886', '443']
        }
      }
    ]
  }
}

// Only APIM and a VNet-integrated API may reach the private endpoints.
resource privateEndpointNsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: 'nsg-${name}-pe'
  location: location
  properties: {
    securityRules: [
      {
        name: 'Allow-Vnet-Https'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: 'VirtualNetwork'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '443'
        }
      }
      {
        name: 'Deny-All-Inbound'
        properties: {
          priority: 4096
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '*'
        }
      }
    ]
  }
}

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: 'vnet-${name}'
  location: location
  properties: {
    addressSpace: { addressPrefixes: [addressPrefix] }
    subnets: [
      {
        name: 'snet-apim'
        properties: {
          addressPrefix: cidrSubnet(addressPrefix, 27, 0)
          networkSecurityGroup: { id: apimNsg.id }
        }
      }
      {
        name: 'snet-private-endpoints'
        properties: {
          addressPrefix: cidrSubnet(addressPrefix, 24, 1)
          networkSecurityGroup: { id: privateEndpointNsg.id }
          privateEndpointNetworkPolicies: 'Enabled'
        }
      }
      {
        name: 'snet-app-integration'
        properties: {
          addressPrefix: cidrSubnet(addressPrefix, 24, 2)
          delegations: [{ name: 'web', properties: { serviceName: 'Microsoft.Web/serverFarms' } }]
        }
      }
    ]
  }
}

resource zones 'Microsoft.Network/privateDnsZones@2024-06-01' = [for zone in items(zoneNames): {
  name: zone.value
  location: 'global'
}]

resource links 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = [for (zone, i) in items(zoneNames): {
  parent: zones[i]
  name: vnet.name
  location: 'global'
  properties: {
    virtualNetwork: { id: vnet.id }
    registrationEnabled: false
  }
}]

output apimSubnetId string = vnet.properties.subnets[0].id
output vnetId string = vnet.id
output privateEndpointSubnetId string = vnet.properties.subnets[1].id
output appIntegrationSubnetId string = vnet.properties.subnets[2].id
output zoneIds {
  search: string
  cosmos: string
  keyVault: string
  cognitiveServices: string
  openAi: string
  aiServices: string
} = {
  search: resourceId('Microsoft.Network/privateDnsZones', zoneNames.search)
  cosmos: resourceId('Microsoft.Network/privateDnsZones', zoneNames.cosmos)
  keyVault: resourceId('Microsoft.Network/privateDnsZones', zoneNames.keyVault)
  cognitiveServices: resourceId('Microsoft.Network/privateDnsZones', zoneNames.cognitiveServices)
  openAi: resourceId('Microsoft.Network/privateDnsZones', zoneNames.openAi)
  aiServices: resourceId('Microsoft.Network/privateDnsZones', zoneNames.aiServices)
}
