import { TagsType } from 'types.bicep'

param location string
param tags TagsType
param serverName string
param administratorLogin string = 'pgadmin'
@secure()
param administratorPassword string
param databaseName string
param subnetId string
param tenantId string = subscription().tenantId
param entraAdminObjectId string
param entraAdminPrincipalName string
param skuName string = 'Standard_D2ds_v5'
param skuTier string = 'GeneralPurpose'
param storageSizeGB int = 128
param backupRetentionDays int = 35
param highAvailabilityMode string = 'Disabled'

resource postgresServer 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: serverName
  location: location
  tags: tags
  sku: { name: skuName, tier: skuTier }
  properties: {
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorPassword
    version: '16'
    storage: { storageSizeGB: storageSizeGB, autoGrow: 'Enabled' }
    backup: { backupRetentionDays: backupRetentionDays, geoRedundantBackup: 'Enabled' }
    highAvailability: { mode: highAvailabilityMode }
    network: {
      delegatedSubnetResourceId: subnetId
      privateDnsZoneArmResourceId: privateDnsZone.id
    }
    authConfig: {
      activeDirectoryAuth: 'Enabled'
      passwordAuth: 'Disabled'
      tenantId: tenantId
    }
  }
}

resource privateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: '${serverName}.private.postgres.database.azure.com'
  location: 'global'
  tags: tags
}

// pgvector extension required for embeddings
resource pgvectorExtension 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2023-12-01-preview' = {
  parent: postgresServer
  name: 'azure.extensions'
  properties: { value: 'VECTOR', source: 'user-override' }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2023-12-01-preview' = {
  parent: postgresServer
  name: databaseName
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
}

resource entraAdmin 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2023-06-01-preview' = {
  parent: postgresServer
  name: entraAdminObjectId
  properties: {
    principalType: 'ServicePrincipal'
    principalName: entraAdminPrincipalName
    tenantId: tenantId
  }
}

output serverFqdn string = postgresServer.properties.fullyQualifiedDomainName
output databaseName string = database.name
output connectionString string = 'Host=${postgresServer.properties.fullyQualifiedDomainName};Database=${databaseName};Ssl Mode=Require'
