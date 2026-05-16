import { TagsType } from 'types.bicep'

param location string
param tags TagsType
param environmentName string
param subnetId string
param logAnalyticsWorkspaceId string

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  tags: tags
  properties: {
    vnetConfiguration: {
      infrastructureSubnetId: subnetId
      internal: false
    }
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: { customerId: reference(logAnalyticsWorkspaceId, '2022-10-01').customerId }
    }
  }
}

output environmentId string = environment.id
output environmentName string = environment.name
output defaultDomain string = environment.properties.defaultDomain
