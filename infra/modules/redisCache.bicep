// Azure Cache for Redis for the Del 13 dashboard-stats cache — the other
// resource that, until Del 19, only ever existed as a plan in
// docs/AZURE_DEPLOYMENT.md section 10, never actually provisioned.
// Basic/C0: cheapest tier, no SLA, no replication — matches section 10's
// reasoning exactly (same "cheapest tier that still proves the pattern"
// call as Standard_LRS for the storage account).
//
// Unlike Key Vault, Azure SQL, Blob Storage and Service Bus, classic
// (non-Enterprise) Azure Cache for Redis has no managed-identity/RBAC
// path at all — only key-based auth (see docs/ARCHITECTURE.md's Del 13
// known-simplification). That's why, uniquely among this project's Azure
// resources, main.bicep writes this module's connection string straight
// into Key Vault as a secret rather than granting the Web App's identity
// a role here.

@description('Name of the Redis cache. Must be globally unique.')
param name string

@description('Azure region for the cache.')
param location string

resource redis 'Microsoft.Cache/redis@2023-08-01' = {
  name: name
  location: location
  properties: {
    sku: {
      name: 'Basic'
      family: 'C'
      capacity: 0
    }
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
  }
}

output hostName string = redis.properties.hostName
output sslPort int = redis.properties.sslPort
// Consumed directly by main.bicep to build the Redis--ConnectionString
// Key Vault secret value — see main.bicep's comment above the redisSecret
// module for the known limitation this implies (module outputs can't be
// marked @secure(), so this ends up in the deployment history).
#disable-next-line outputs-should-not-contain-secrets
output primaryKey string = redis.listKeys().primaryKey
