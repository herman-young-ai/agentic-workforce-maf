import { TagsType } from 'types.bicep'

param location string
param tags TagsType
param appName string
param environmentId string
param containerImage string
param containerRegistry string
param containerRegistryIdentityId string
param managedIdentityId string
param managedIdentityClientId string
param keyVaultUri string
param postgresConnectionString string
param redisConnectionString string = ''
param azureAdTenantId string
param azureAdClientId string
param appInsightsConnectionString string = ''
param corsOrigins string = ''
param minReplicas int = 1
param maxReplicas int = 3
param isWorker bool = false

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${managedIdentityId}': {} }
  }
  properties: {
    environmentId: environmentId
    configuration: {
      // Worker has no ingress — it's a background service
      ingress: isWorker ? null : {
        external: true
        targetPort: 8080
        corsPolicy: {
          allowedOrigins: empty(corsOrigins) ? [] : split(corsOrigins, ',')
          allowedMethods: ['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'OPTIONS']
          allowedHeaders: ['*']
          allowCredentials: true
        }
      }
      registries: [
        { server: containerRegistry, identity: containerRegistryIdentityId }
      ]
    }
    template: {
      containers: [
        {
          name: appName
          image: containerImage
          resources: { cpu: json(isWorker ? '1.0' : '0.5'), memory: isWorker ? '2Gi' : '1Gi' }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'KeyVault__Uri', value: keyVaultUri }
            { name: 'ConnectionStrings__agenticworkforce', value: postgresConnectionString }
            { name: 'ConnectionStrings__redis', value: redisConnectionString }
            { name: 'AzureAd__TenantId', value: azureAdTenantId }
            { name: 'AzureAd__ClientId', value: azureAdClientId }
            { name: 'ApplicationInsights__ConnectionString', value: appInsightsConnectionString }
            { name: 'Cors__AllowedOrigins__0', value: corsOrigins }
            { name: 'AZURE_CLIENT_ID', value: managedIdentityClientId }
          ]
          probes: isWorker ? [] : [
            {
              type: 'Liveness'
              httpGet: { path: '/alive', port: 8080 }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health', port: 8080 }
              initialDelaySeconds: 15
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: { minReplicas: minReplicas, maxReplicas: maxReplicas }
    }
  }
}

output fqdn string = isWorker ? '' : containerApp.properties.configuration.ingress.fqdn
output appId string = containerApp.id
