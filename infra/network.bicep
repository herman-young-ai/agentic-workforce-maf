import { TagsType } from 'types.bicep'

param location string
param tags TagsType
param vnetName string
param vnetAddressPrefix string = '10.0.0.0/16'

resource vnet 'Microsoft.Network/virtualNetworks@2023-11-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: [vnetAddressPrefix] }
    subnets: [
      {
        name: 'container-apps'
        properties: {
          addressPrefix: '10.0.0.0/23'
          delegations: [
            { name: 'aca', properties: { serviceName: 'Microsoft.App/environments' } }
          ]
        }
      }
      {
        name: 'private-endpoints'
        properties: { addressPrefix: '10.0.2.0/24' }
      }
      {
        name: 'postgresql'
        properties: {
          addressPrefix: '10.0.3.0/24'
          delegations: [
            { name: 'postgresql', properties: { serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers' } }
          ]
        }
      }
    ]
  }
}

output vnetId string = vnet.id
output containerAppsSubnetId string = vnet.properties.subnets[0].id
output privateEndpointsSubnetId string = vnet.properties.subnets[1].id
output postgresqlSubnetId string = vnet.properties.subnets[2].id
