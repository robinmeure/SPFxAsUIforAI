// Network Security Perimeter: the associated PaaS resources only accept public traffic that matches the profile's
// inbound rules (client IPs, and optionally resources in listed subscriptions that authenticate with a managed
// identity). Traffic from outside is denied in Enforced mode.

param location string
param name string
param profileName string
param resourceIds array

@description('Public client IPs or CIDR ranges allowed inbound, for example a developer machine.')
param allowedAddressPrefixes array = []

@description('Subscription IDs whose managed identities are allowed inbound, for example a hosted API.')
param allowedSubscriptionIds array = []

@allowed(['Enforced', 'Learning'])
param accessMode string = 'Enforced'

resource perimeter 'Microsoft.Network/networkSecurityPerimeters@2024-07-01' = {
  name: name
  location: location

  resource profile 'profiles' = {
    name: profileName
  }
}

resource allowIps 'Microsoft.Network/networkSecurityPerimeters/profiles/accessRules@2024-07-01' = if (!empty(allowedAddressPrefixes)) {
  parent: perimeter::profile
  name: 'allow-client-ips'
  properties: {
    direction: 'Inbound'
    addressPrefixes: [for prefix in allowedAddressPrefixes: contains(prefix, '/') ? prefix : '${prefix}/32']
  }
}

resource allowSubscriptions 'Microsoft.Network/networkSecurityPerimeters/profiles/accessRules@2024-07-01' = if (!empty(allowedSubscriptionIds)) {
  parent: perimeter::profile
  name: 'allow-subscriptions'
  properties: {
    direction: 'Inbound'
    subscriptions: [for id in allowedSubscriptionIds: { id: '/subscriptions/${id}' }]
  }
}

resource associations 'Microsoft.Network/networkSecurityPerimeters/resourceAssociations@2024-07-01' = [for (id, i) in resourceIds: {
  parent: perimeter
  name: '${last(split(id, '/'))}-association'
  properties: {
    privateLinkResource: { id: id }
    profile: { id: perimeter::profile.id }
    accessMode: accessMode
  }
}]
