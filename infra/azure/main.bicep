// FundFlow on Azure's free tier.
//
//   App Service plan F1 (Linux)  ->  one web app that serves the API *and* the React app (single origin)
//   Azure SQL Database           ->  the "free offer" serverless database (100,000 vCore-seconds + 32 GB per month)
//
// Every SKU here is free. There is deliberately no Redis (the API falls back to an in-process cache), no RabbitMQ
// (MassTransit uses its in-memory transport behind the same SQL transactional outbox) and no Application Insights.
// See docs/deployment-azure.md for what that means in practice and how to grow out of it.
//
//   az group create -n rg-fundflow-free -l centralindia
//   az deployment group create -g rg-fundflow-free -f infra/azure/main.bicep -p sqlAdminPassword=... sqlAppPassword=... jwtSigningKey=...
//
// infra/azure/deploy.ps1 runs all of this for you, including the parts a template cannot do (schema, users, app deploy).

targetScope = 'resourceGroup'

@description('Region for every resource. The SQL free offer pins a subscription to the first region it is used in, so choose deliberately.')
param location string = resourceGroup().location

@description('Lowercase prefix for resource names.')
@minLength(3)
@maxLength(16)
param namePrefix string = 'fundflow'

@description('Makes the globally unique names unique. Defaults to a short hash of the resource group id.')
param uniqueSuffix string = toLower(substring(uniqueString(subscription().id, resourceGroup().id), 0, 6))

@description('Database name.')
param databaseName string = 'FundFlow'

@description('SQL administrator login. Used only for migrations from the deployment machine, never by the running app.')
param sqlAdminLogin string = 'fundflowadmin'

@description('SQL administrator password.')
@secure()
param sqlAdminPassword string

@description('Least-privilege database user the running app connects as (created by deploy.ps1).')
param sqlAppUser string = 'fundflow_app'

@description('Password for the app database user.')
@secure()
param sqlAppPassword string

@description('HS256 signing key for access tokens. At least 32 characters.')
@secure()
@minLength(32)
param jwtSigningKey string

@description('Optional SMTP relay so registration, invitation and password-reset emails are delivered. Leave the host empty to skip.')
param smtpHost string = ''

param smtpPort int = 587

@allowed([ 'None', 'StartTls', 'SslOnConnect' ])
param smtpSecurity string = 'StartTls'

param smtpUser string = ''

@secure()
param smtpPassword string = ''

@description('From address for outgoing email. Must be a sender your SMTP provider has verified.')
param mailFromAddress string = 'no-reply@fundflow.example'

var siteName = '${namePrefix}-${uniqueSuffix}'
var sqlServerName = '${namePrefix}-sql-${uniqueSuffix}'
var planName = 'plan-${namePrefix}-free'

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  kind: 'linux'
  sku: {
    name: 'F1'
    tier: 'Free'
  }
  properties: {
    reserved: true // Linux
  }
}

resource site 'Microsoft.Web/sites@2023-12-01' = {
  name: siteName
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: false // not offered on the Free tier: the app is unloaded after ~20 idle minutes and cold-starts on the next request
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      webSocketsEnabled: false
    }
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// "Allow Azure services and resources to access this server": the Free App Service tier has no VNet integration and
// no stable outbound address, so this is how the web app reaches the database. SQL authentication and TLS still apply.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    // The free offer. AutoPause is what keeps this free: when the month's 100,000 vCore-seconds are used up the
    // database pauses until the 1st instead of billing overage. (With AutoPause Azure only accepts the default
    // auto-pause delay, 60 minutes, so autoPauseDelay must not be set.)
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368 // 32 GB, the free allowance
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    zoneRedundant: false
    requestedBackupStorageRedundancy: 'Local'
    readScale: 'Disabled'
  }
}

var siteUrl = 'https://${site.properties.defaultHostName}'

var baseSettings = {
  ASPNETCORE_ENVIRONMENT: 'Production'
  ConnectionStrings__Default: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${databaseName};User ID=${sqlAppUser};Password=${sqlAppPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;ConnectRetryCount=5;ConnectRetryInterval=10'
  Messaging__Transport: 'InMemory'
  Hangfire__Enabled: 'false' // its 15-second polling would keep the serverless database awake and burn the free allowance
  Jwt__SigningKey: jwtSigningKey
  Jwt__Issuer: siteUrl
  App__PublicBaseUrl: siteUrl
  Cors__AllowedOrigins__0: siteUrl
  Proxy__TrustForwardedHeaders: 'true' // App Service terminates TLS in front of the container
  Swagger__Enabled: 'false'
  Spa__Enabled: 'true'
  SCM_DO_BUILD_DURING_DEPLOYMENT: 'false'
}

var emailSettings = empty(smtpHost) ? {} : {
  Email__Host: smtpHost
  Email__Port: string(smtpPort)
  Email__Security: smtpSecurity
  Email__Username: smtpUser
  Email__Password: smtpPassword
  Email__FromAddress: mailFromAddress
}

resource appSettings 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: site
  name: 'appsettings'
  properties: union(baseSettings, emailSettings)
}

output siteName string = site.name
output siteUrl string = siteUrl
output sqlServerName string = sqlServer.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = database.name
output planName string = plan.name
