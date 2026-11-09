using 'main.bicep'

// Development defaults for this tenant. Override per run, for example:
//   -p apiBackendUrl=https://<tunnel>-5198.euw.devtunnels.ms
param apiClientId = '1c389841-5b43-4575-8acb-8135d21ca809'
// admin@mngenvmcap935538.onmicrosoft.com: runs the API locally and gets the data-plane roles.
param developerPrincipalId = '62014bb7-28ce-4eeb-9cd4-348d3879ac2f'
param apiBackendUrl = 'https://localhost.invalid'
param allowedOrigins = [
  'https://mngenvmcap935538.sharepoint.com'
  'https://teams.microsoft.com'
]
param apimPublisherEmail = 'admin@mngenvmcap935538.onmicrosoft.com'
// Public IPs that may reach Cosmos DB through its network security perimeter (the machine running the API locally).
param cosmosAllowedIps = [
  '95.99.86.88'
]
