import { TagsType } from 'types.bicep'

param location string
param tags TagsType
param redisName string
param skuName string = 'Standard'
param skuFamily string = 'C'
param skuCapacity int = 1

resource redis 'Microsoft.Cache/redis@2024-03-01' = {
  name: redisName
  location: location
  tags: tags
  properties: {
    sku: { name: skuName, family: skuFamily, capacity: skuCapacity }
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
    redisConfiguration: {
      'maxmemory-policy': 'volatile-lru'
      'aof-backup-enabled': 'yes'
    }
  }
}

output redisId string = redis.id
output redisHostName string = redis.properties.hostName
output redisConnectionString string = '${redis.properties.hostName}:${redis.properties.sslPort},ssl=true,abortConnect=false'
