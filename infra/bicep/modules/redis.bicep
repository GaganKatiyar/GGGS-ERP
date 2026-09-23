metadata description = 'Redis: the shared second level behind each service in-process cache.'

param resourcePrefix string
param location string
param tags object
param skuName string
param capacity int

resource redis 'Microsoft.Cache/redis@2024-03-01' = {
  name: '${resourcePrefix}-redis'
  location: location
  tags: tags
  properties: {
    sku: {
      name: skuName
      family: skuName == 'Premium' ? 'P' : 'C'
      capacity: capacity
    }
    minimumTlsVersion: '1.2'
    enableNonSslPort: false
    publicNetworkAccess: 'Disabled'
    redisConfiguration: {
      // Cached data is rebuildable, so evicting the least recently used entry under pressure is
      // correct. The alternative - refusing writes when full - would fail requests instead.
      'maxmemory-policy': 'allkeys-lru'
    }
  }
}

output hostName string = redis.properties.hostName
output name string = redis.name
