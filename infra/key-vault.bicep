import { TagsType } from 'types.bicep'

param location string
param tags TagsType
param keyVaultName string
param tenantId string = subscription().tenantId

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    networkAcls: { bypass: 'AzureServices', defaultAction: 'Deny' }
  }
}

output keyVaultUri string = keyVault.properties.vaultUri
output keyVaultId string = keyVault.id
