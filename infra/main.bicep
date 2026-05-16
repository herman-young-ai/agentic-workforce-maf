import { TagsType } from 'types.bicep'

targetScope = 'resourceGroup'

param location string = resourceGroup().location
param tags TagsType
param appName string
param environment string
param apiContainerImage string
param workerContainerImage string
param containerRegistry string
param azureAdTenantId string
param azureAdClientId string
@secure()
param postgresAdminPassword string
param postgresEntraAdminObjectId string
param postgresEntraAdminPrincipalName string
param corsOrigins string = ''
param logAnalyticsWorkspaceId string

// -- Naming --
var prefix = '${appName}-${environment}'
var vnetName = 'vnet-${prefix}'
var kvName = 'kv-${prefix}'
var postgresServerName = 'pg-${prefix}'
var redisName = 'redis-${prefix}'
var acaEnvName = 'acae-${prefix}'
var acaApiName = 'aca-${prefix}-api'
var acaWorkerName = 'aca-${prefix}-worker'
var managedIdentityName = 'id-${prefix}'
var databaseName = appName

// -- Managed Identity --
resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: managedIdentityName
  location: location
  tags: tags
}

// -- Network --
module network 'network.bicep' = {
  name: 'network'
  params: { location: location, tags: tags, vnetName: vnetName }
}

// -- Key Vault --
module keyVault 'key-vault.bicep' = {
  name: 'key-vault'
  params: { location: location, tags: tags, keyVaultName: kvName }
}

module keyVaultRoles 'key-vault-roles.bicep' = {
  name: 'key-vault-roles'
  params: {
    keyVaultId: keyVault.outputs.keyVaultId
    principalId: managedIdentity.properties.principalId
  }
}

// -- PostgreSQL --
module postgresql 'postgresql.bicep' = {
  name: 'postgresql'
  params: {
    location: location
    tags: tags
    serverName: postgresServerName
    administratorLogin: 'pgadmin'
    administratorPassword: postgresAdminPassword
    databaseName: databaseName
    subnetId: network.outputs.postgresqlSubnetId
    entraAdminObjectId: postgresEntraAdminObjectId
    entraAdminPrincipalName: postgresEntraAdminPrincipalName
  }
}

// -- Redis --
module redisCache 'redis.bicep' = {
  name: 'redis'
  params: { location: location, tags: tags, redisName: redisName }
}

// -- Container Apps Environment --
module acaEnvironment 'compute.bicep' = {
  name: 'aca-environment'
  params: {
    location: location
    tags: tags
    environmentName: acaEnvName
    subnetId: network.outputs.containerAppsSubnetId
    logAnalyticsWorkspaceId: logAnalyticsWorkspaceId
  }
}

// -- API Container App --
module apiApp 'compute-containerapp.bicep' = {
  name: 'container-app-api'
  params: {
    location: location
    tags: tags
    appName: acaApiName
    environmentId: acaEnvironment.outputs.environmentId
    containerImage: apiContainerImage
    containerRegistry: containerRegistry
    containerRegistryIdentityId: managedIdentity.id
    managedIdentityId: managedIdentity.id
    managedIdentityClientId: managedIdentity.properties.clientId
    keyVaultUri: keyVault.outputs.keyVaultUri
    postgresConnectionString: 'Host=${postgresql.outputs.serverFqdn};Database=${databaseName};Ssl Mode=Require'
    redisConnectionString: redisCache.outputs.redisConnectionString
    azureAdTenantId: azureAdTenantId
    azureAdClientId: azureAdClientId
    corsOrigins: corsOrigins
    isWorker: false
  }
  dependsOn: [keyVaultRoles]
}

// -- Worker Container App --
module workerApp 'compute-containerapp.bicep' = {
  name: 'container-app-worker'
  params: {
    location: location
    tags: tags
    appName: acaWorkerName
    environmentId: acaEnvironment.outputs.environmentId
    containerImage: workerContainerImage
    containerRegistry: containerRegistry
    containerRegistryIdentityId: managedIdentity.id
    managedIdentityId: managedIdentity.id
    managedIdentityClientId: managedIdentity.properties.clientId
    keyVaultUri: keyVault.outputs.keyVaultUri
    postgresConnectionString: 'Host=${postgresql.outputs.serverFqdn};Database=${databaseName};Ssl Mode=Require'
    redisConnectionString: redisCache.outputs.redisConnectionString
    azureAdTenantId: azureAdTenantId
    azureAdClientId: azureAdClientId
    isWorker: true
    minReplicas: 1
    maxReplicas: 5
  }
  dependsOn: [keyVaultRoles]
}

// -- Outputs --
output apiUrl string = 'https://${apiApp.outputs.fqdn}'
output keyVaultUri string = keyVault.outputs.keyVaultUri
output postgresServerFqdn string = postgresql.outputs.serverFqdn
output redisHostName string = redisCache.outputs.redisHostName
