using '../main.bicep'

param environmentName = 'prod'
param sqlAdminLogin = 'gggsadmin'

// Never a literal. The pipeline resolves it from the deployment Key Vault at deploy time.
param sqlAdminPassword = readEnvironmentVariable('GGGS_SQL_ADMIN_PASSWORD')

// Entra ID group that owns the SQL server, so no individual account is the owner.
param sqlAdminGroupObjectId = readEnvironmentVariable('GGGS_SQL_ADMIN_GROUP_ID')

param imageTag = readEnvironmentVariable('GGGS_IMAGE_TAG', 'latest')
