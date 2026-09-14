// Azure Managed Redis for the Del 13 dashboard-stats cache — the other
// resource that, until Del 19, only ever existed as a plan in
// docs/AZURE_DEPLOYMENT.md section 10, never actually provisioned.
//
// This module originally targeted the classic Microsoft.Cache/redis
// resource type (Basic/C0), matching what section 10 documented by hand —
// but a real `az deployment group create` against Peter's subscription
// (2026-09-14) failed with "Azure Cache for Redis is retiring, create
// Azure Managed Redis instance instead", a platform-level change neither
// of us knew about ahead of time. Rewritten against the replacement
// resource type, Microsoft.Cache/redisEnterprise (+ its required
// .../databases child resource) — see
// https://learn.microsoft.com/azure/redis/redis-cache-bicep-provision and
// https://learn.microsoft.com/azure/azure-cache-for-redis/retirement-faq.
// Balanced_B0 is the cheapest SKU in the new Balanced series — the same
// "cheapest tier that still proves the pattern" call section 10 made for
// the classic Basic/C0 tier (and Standard_LRS for the storage account).
//
// One real behavioural difference from classic Redis, worth knowing before
// wiring up ConnectionMultiplexer.Connect in RedisTicketStatsCache: the
// default port is 10000, not 6380 — StackExchange.Redis needs the port
// spelled out explicitly in the connection string either way (both
// classic and Managed Redis omit a well-known default the client can
// assume), so this only matters if a value gets hardcoded anywhere
// instead of read from the Redis--ConnectionString secret this module
// feeds into.
//
// Same RBAC gap as classic Redis: Azure Managed Redis still only supports
// key-based auth for a StackExchange.Redis client at this access-policy
// configuration (Entra ID auth exists for Managed Redis but needs a
// non-default access policy this module doesn't set up) — so main.bicep
// still writes this module's connection string straight into Key Vault
// as a secret, exactly as it did for classic Redis.

@description('Name of the Redis Enterprise cluster. Must be globally unique.')
param name string

@description('Azure region for the cache.')
param location string

resource redisEnterprise 'Microsoft.Cache/redisEnterprise@2024-05-01-preview' = {
  name: name
  location: location
  sku: {
    name: 'Balanced_B0'
  }
  identity: {
    type: 'None'
  }
  properties: {
    minimumTlsVersion: '1.2'
  }
}

resource redisDatabase 'Microsoft.Cache/redisEnterprise/databases@2024-05-01-preview' = {
  parent: redisEnterprise
  name: 'default'
  properties: {
    clientProtocol: 'Encrypted'
    port: 10000
    clusteringPolicy: 'OSSCluster'
    evictionPolicy: 'NoEviction'
    persistence: {
      aofEnabled: false
      rdbEnabled: false
    }
  }
}

output hostName string = redisEnterprise.properties.hostName
output port int = 10000
// Consumed directly by main.bicep to build the Redis--ConnectionString
// Key Vault secret value — see main.bicep's comment above the redisSecret
// module for the known limitation this implies (module outputs can't be
// marked @secure(), so this ends up in the deployment history).
#disable-next-line outputs-should-not-contain-secrets
output primaryKey string = redisDatabase.listKeys().primaryKey
