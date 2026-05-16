param keyVaultId string
param principalId string

var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVaultId, principalId, keyVaultSecretsUserRoleId)
  scope: resourceId('Microsoft.KeyVault/vaults', split(keyVaultId, '/')[8])
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
