<#
.SYNOPSIS
    Deploys FundFlow to Azure using only free-tier resources (see docs/deployment-azure.md).

.DESCRIPTION
    1. Generates the secrets (stored under %USERPROFILE%\.fundflow, never in the repository or on screen).
    2. Creates the resource group and deploys infra/azure/main.bicep: App Service F1 (Linux) + the Azure SQL free-offer
       database. Tries the given regions in order until one accepts both.
    3. Creates the least-privilege database user the running app uses, applies the EF Core migrations and reference data
       (and the demo organizations unless -NoDemoData) with the administrator login, from this machine.
    4. Builds the React app, publishes the API with the app inside wwwroot, zip-deploys it, and smoke-tests the result.

    Re-running is safe: secrets are reused, the template is incremental, migrations are idempotent, and the code is
    redeployed. Use -AppOnly to skip provisioning and only ship new code.

    Prerequisites: Azure CLI signed in (az login), .NET 10 SDK, and Node 22+ (unless -FrontendDist is given).

.EXAMPLE
    ./infra/azure/deploy.ps1 -Yes

.EXAMPLE
    $env:FUNDFLOW_SMTP_PASSWORD = '<smtp key>'
    ./infra/azure/deploy.ps1 -Yes -SmtpHost smtp-relay.brevo.com -SmtpUser you@example.com -MailFrom no-reply@your-domain.com
#>
[CmdletBinding()]
param(
    [string] $ResourceGroup = 'rg-fundflow-free',

    # Tried in order; the first region that accepts both the free App Service plan and the free SQL database wins.
    [string[]] $Locations = @('centralindia', 'southindia', 'southeastasia', 'westeurope', 'northeurope', 'eastus2', 'centralus'),

    [string] $NamePrefix = 'fundflow',

    # Platform operator (registry of organizations). Its password is generated and kept in the secrets file.
    [string] $SuperAdminEmail = 'superadmin@fundflow.test',

    # By default two sample organizations are created so the site is usable immediately, without email. Their shared,
    # generated password is in the secrets file.
    [switch] $NoDemoData,

    # A prebuilt frontend/dist folder. Without it the script runs `npm ci && npm run build` in frontend/.
    [string] $FrontendDist,

    # Optional SMTP relay (registration, invitation and password-reset emails). The password comes from the
    # FUNDFLOW_SMTP_PASSWORD environment variable so it never lands in shell history.
    [string] $SmtpHost,
    [int] $SmtpPort = 587,
    [ValidateSet('None', 'StartTls', 'SslOnConnect')] [string] $SmtpSecurity = 'StartTls',
    [string] $SmtpUser,
    [string] $MailFrom = 'no-reply@fundflow.example',

    # Ship new code to an existing deployment; skips provisioning and migrations.
    [switch] $AppOnly,

    # Optional cost guard: a monthly budget on the resource group that emails this address at 50% and 100%.
    # Strongly recommended on pay-as-you-go subscriptions, which have no spending cap. The amount is in the subscription's
    # billing currency (5 means 5 rupees on an INR subscription, 5 dollars on a USD one): treat it as a canary, since
    # nothing deployed here should cost anything, and raise it if you want to be told at a larger amount.
    [string] $BudgetEmail,
    [int] $BudgetAmount = 5,

    # Generate new SQL passwords and a new JWT signing key (access tokens issued before stop working; the web app renews them).
    [switch] $RotateSecrets,

    [string] $SecretsFile,

    # Skip the confirmation prompt.
    [switch] $Yes
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$InfraFile = Join-Path $PSScriptRoot 'main.bicep'
$WorkDir = Join-Path ([System.IO.Path]::GetTempPath()) 'fundflow-azure'
if (-not $SecretsFile) {
    $SecretsFile = Join-Path (Join-Path $env:USERPROFILE '.fundflow') "azure-$ResourceGroup.json"
}

function Write-Step([string] $Message) { Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Note([string] $Message) { Write-Host "    $Message" -ForegroundColor DarkGray }

# Runs az and returns exit code, stdout and stderr; throws unless -AllowFailure. stderr goes to a file so PowerShell 5.1
# does not turn az's warnings into terminating errors.
function Invoke-Az {
    param([Parameter(Mandatory)] [string[]] $Arguments, [switch] $AllowFailure)
    $errFile = [System.IO.Path]::GetTempFileName()
    # Windows PowerShell 5.1 raises a terminating NativeCommandError for anything a native command writes to a redirected
    # stderr while ErrorActionPreference is 'Stop'; az's failures must be reported by this function instead.
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $stdout = & az @Arguments 2> $errFile
        $code = $LASTEXITCODE
        $stderr = (Get-Content -Raw -ErrorAction SilentlyContinue $errFile)
        if (-not $stderr) { $stderr = '' }
    }
    finally {
        $ErrorActionPreference = $previousPreference
        Remove-Item $errFile -Force -ErrorAction SilentlyContinue
    }
    if ($code -ne 0 -and -not $AllowFailure) {
        throw "az $($Arguments -join ' ') failed with exit code $code.`n$stderr"
    }
    [pscustomobject]@{ ExitCode = $code; Output = (($stdout | ForEach-Object { "$_" }) -join "`n"); Error = $stderr }
}

function New-RandomSecret {
    param([int] $Length = 32, [switch] $WithSymbols)
    $sets = @('ABCDEFGHJKLMNPQRSTUVWXYZ', 'abcdefghijkmnopqrstuvwxyz', '23456789')
    if ($WithSymbols) { $sets += '!#-_' }
    $all = -join $sets
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $buffer = New-Object byte[] 4
    $randomBelow = {
        param([uint64] $max)
        $limit = [uint64]4294967296 - ([uint64]4294967296 % $max)
        do { $rng.GetBytes($buffer); $value = [uint64][BitConverter]::ToUInt32($buffer, 0) } while ($value -ge $limit)
        [int]($value % $max)
    }
    $chars = New-Object 'System.Collections.Generic.List[char]'
    foreach ($set in $sets) { $chars.Add($set[(& $randomBelow $set.Length)]) }        # at least one of every class
    while ($chars.Count -lt $Length) { $chars.Add($all[(& $randomBelow $all.Length)]) }
    for ($i = $chars.Count - 1; $i -gt 0; $i--) {                                     # Fisher-Yates
        $j = & $randomBelow ($i + 1)
        $swap = $chars[$i]; $chars[$i] = $chars[$j]; $chars[$j] = $swap
    }
    -join $chars
}

function Save-Secrets($Secrets) {
    $dir = Split-Path -Parent $SecretsFile
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText($SecretsFile, ($Secrets | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
}

function Get-SecretProperty($Secrets, [string] $Name, $Default = '') {
    if ($Secrets.PSObject.Properties[$Name]) { return $Secrets.$Name }
    return $Default
}

function Set-SecretProperty($Secrets, [string] $Name, $Value) {
    if ($Secrets.PSObject.Properties[$Name]) { $Secrets.$Name = $Value }
    else { Add-Member -InputObject $Secrets -NotePropertyName $Name -NotePropertyValue $Value }
}

function Invoke-Sql {
    param([Parameter(Mandatory)] [string] $ConnectionString, [Parameter(Mandatory)] [string] $Sql, [int] $Attempts = 20)
    Add-Type -AssemblyName System.Data
    for ($attempt = 1; ; $attempt++) {
        $connection = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
        try {
            $connection.Open()
            $command = $connection.CreateCommand()
            $command.CommandText = $Sql
            $command.CommandTimeout = 120
            [void] $command.ExecuteNonQuery()
            return
        }
        catch {
            if ($attempt -ge $Attempts) { throw }
            # A new firewall rule takes up to a few minutes to apply and a paused serverless database needs a moment to resume.
            Write-Note "database not reachable yet ($($_.Exception.Message.Split("`n")[0])); retrying in 15 s ($attempt/$Attempts)"
            Start-Sleep -Seconds 15
        }
        finally {
            $connection.Dispose()
        }
    }
}

function Wait-ForUrl {
    param([string] $Url, [int] $TimeoutSeconds = 300, [string] $Expect = '')
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastError = ''
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 100
            if ($response.StatusCode -eq 200 -and ($Expect -eq '' -or $response.Content -like "*$Expect*")) { return $response }
            $lastError = "HTTP $($response.StatusCode)"
        }
        catch {
            $lastError = $_.Exception.Message
        }
        Start-Sleep -Seconds 10
    }
    throw "Timed out waiting for $Url ($lastError)"
}

# ---------------------------------------------------------------------------------------------------------------------
Write-Step 'Checking prerequisites'
if (-not (Get-Command az -ErrorAction SilentlyContinue)) { throw 'Azure CLI not found. Install it from https://aka.ms/installazurecli and run "az login".' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK not found. Install the .NET 10 SDK.' }
if (-not $FrontendDist -and -not (Get-Command npm -ErrorAction SilentlyContinue)) { throw 'npm not found. Install Node 22+ or pass -FrontendDist <folder with a built app>.' }
if (-not $AppOnly) {
    Add-Type -AssemblyName System.Data -ErrorAction SilentlyContinue
    if (-not ('System.Data.SqlClient.SqlConnection' -as [type])) {
        throw 'This script uses System.Data.SqlClient to prepare the database, which Windows PowerShell 5.1 provides. Run it with powershell.exe (not pwsh).'
    }
}

$account = (Invoke-Az @('account', 'show', '-o', 'json', '--only-show-errors')).Output | ConvertFrom-Json
Write-Host "    Subscription: $($account.name) ($($account.id))"
Write-Host "    Signed in as: $($account.user.name)"
Write-Host "    Resource group: $ResourceGroup    Regions to try: $($Locations -join ', ')"
if (-not $Yes) {
    $answer = Read-Host 'Create free-tier resources in this subscription and deploy? (y/N)'
    if ($answer -notmatch '^(y|yes)$') { Write-Host 'Cancelled.'; return }
}

# ---- secrets ---------------------------------------------------------------------------------------------------------
Write-Step "Loading secrets from $SecretsFile"
if (Test-Path $SecretsFile) {
    $secrets = Get-Content -Raw $SecretsFile | ConvertFrom-Json
    Write-Note 'reusing existing secrets'
}
else {
    $secrets = [pscustomobject]@{
        sqlAdminLogin      = 'fundflowadmin'
        sqlAdminPassword   = New-RandomSecret -Length 32
        sqlAppUser         = 'fundflow_app'
        sqlAppPassword     = New-RandomSecret -Length 32
        jwtSigningKey      = New-RandomSecret -Length 64
        demoPassword       = New-RandomSecret -Length 20 -WithSymbols
        superAdminEmail    = $SuperAdminEmail
        superAdminPassword = New-RandomSecret -Length 20 -WithSymbols
        location           = ''
        siteName           = ''
        siteUrl            = ''
        sqlServerName      = ''
        sqlServerFqdn      = ''
        databaseName       = 'FundFlow'
    }
    Save-Secrets $secrets
    Write-Note 'generated new secrets (they are not printed)'
}

if ($RotateSecrets -and -not $AppOnly) {
    Write-Note 'rotating the SQL passwords and the JWT signing key'
    $secrets.sqlAdminPassword = New-RandomSecret -Length 32
    $secrets.sqlAppPassword = New-RandomSecret -Length 32
    $secrets.jwtSigningKey = New-RandomSecret -Length 64
    Save-Secrets $secrets
}

# SMTP settings given on the command line are remembered, because every provisioning run rewrites all app settings.
if ($SmtpHost) {
    Set-SecretProperty $secrets 'smtpHost' $SmtpHost
    Set-SecretProperty $secrets 'smtpPort' $SmtpPort
    Set-SecretProperty $secrets 'smtpSecurity' $SmtpSecurity
    Set-SecretProperty $secrets 'smtpUser' "$SmtpUser"
    Set-SecretProperty $secrets 'mailFrom' $MailFrom
    if ($env:FUNDFLOW_SMTP_PASSWORD) { Set-SecretProperty $secrets 'smtpPassword' $env:FUNDFLOW_SMTP_PASSWORD }
    Save-Secrets $secrets
}
$smtpHostSetting = [string] (Get-SecretProperty $secrets 'smtpHost')

# ---- provisioning ----------------------------------------------------------------------------------------------------
if (-not $AppOnly) {
    Write-Step 'Provisioning Azure resources'
    $paramsFile = Join-Path $WorkDir 'parameters.json'
    New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

    $groupExists = (Invoke-Az @('group', 'exists', '-n', $ResourceGroup, '-o', 'tsv', '--only-show-errors')).Output.Trim() -eq 'true'
    $createdGroup = $false
    if ($groupExists -and $secrets.location) {
        # A previous run already chose a region: keep it (the SQL free offer pins the subscription to it anyway).
        $Locations = @($secrets.location)
    }

    $outputs = $null
    foreach ($location in $Locations) {
        if (-not $groupExists) {
            Write-Note "creating resource group $ResourceGroup"
            [void] (Invoke-Az @('group', 'create', '-n', $ResourceGroup, '-l', $location, '-o', 'none', '--only-show-errors'))
            $groupExists = $true
            $createdGroup = $true
        }

        Write-Note "deploying to $location ..."
        $parameters = @{
            location         = @{ value = $location }
            namePrefix       = @{ value = $NamePrefix }
            databaseName     = @{ value = $secrets.databaseName }
            sqlAdminLogin    = @{ value = $secrets.sqlAdminLogin }
            sqlAdminPassword = @{ value = $secrets.sqlAdminPassword }
            sqlAppUser       = @{ value = $secrets.sqlAppUser }
            sqlAppPassword   = @{ value = $secrets.sqlAppPassword }
            jwtSigningKey    = @{ value = $secrets.jwtSigningKey }
        }
        if ($smtpHostSetting) {
            $parameters.smtpHost = @{ value = $smtpHostSetting }
            $parameters.smtpPort = @{ value = [int] (Get-SecretProperty $secrets 'smtpPort' 587) }
            $parameters.smtpSecurity = @{ value = [string] (Get-SecretProperty $secrets 'smtpSecurity' 'StartTls') }
            $parameters.smtpUser = @{ value = [string] (Get-SecretProperty $secrets 'smtpUser') }
            $parameters.smtpPassword = @{ value = [string] (Get-SecretProperty $secrets 'smtpPassword') }
            $parameters.mailFromAddress = @{ value = [string] (Get-SecretProperty $secrets 'mailFrom' 'no-reply@fundflow.example') }
        }
        $document = @{
            '$schema'      = 'https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#'
            contentVersion = '1.0.0.0'
            parameters     = $parameters
        }
        [System.IO.File]::WriteAllText($paramsFile, ($document | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding($false)))

        try {
            $result = Invoke-Az @('deployment', 'group', 'create', '-g', $ResourceGroup, '-n', 'fundflow-infra', '-f', $InfraFile,
                '-p', "@$paramsFile", '--query', 'properties.outputs', '-o', 'json', '--only-show-errors') -AllowFailure
        }
        finally {
            Remove-Item $paramsFile -Force -ErrorAction SilentlyContinue
        }

        if ($result.ExitCode -eq 0) {
            $outputs = $result.Output | ConvertFrom-Json
            $secrets.location = $location
            break
        }

        $reason = ($result.Error -split "`n" | Where-Object { $_ -match '\S' } | Select-Object -First 6) -join "`n      "
        $regional = $result.Error -match '(?i)RegionDoesNotAllowProvisioning|ProvisioningDisabled|LocationNotAvailable|SkuNotAvailable|quota|not accepting|not available in|capacity|InvalidTemplateDeployment.*location'
        Write-Host "    $location was not accepted:`n      $reason" -ForegroundColor Yellow
        if (-not $regional -or $location -eq $Locations[-1]) {
            throw "Deployment failed in $location. See the message above."
        }
        if ($createdGroup) {
            Write-Note 'removing the partial deployment before trying the next region'
            [void] (Invoke-Az @('group', 'delete', '-n', $ResourceGroup, '--yes', '-o', 'none', '--only-show-errors'))
            $groupExists = $false
            $createdGroup = $false
        }
        else {
            throw "Deployment failed in $location and the resource group already existed, so nothing was cleaned up automatically. Fix the cause or delete the group and re-run."
        }
    }

    # Trust but verify: the whole point is that nothing here can bill. Stop loudly if either free SKU did not apply.
    $database = (Invoke-Az @('sql', 'db', 'show', '-g', $ResourceGroup, '-s', $outputs.sqlServerName.value, '-n', $outputs.databaseName.value,
            '--query', '{free:useFreeLimit, behaviour:freeLimitExhaustionBehavior, sku:currentSku.name}', '-o', 'json', '--only-show-errors')).Output | ConvertFrom-Json
    $plan = (Invoke-Az @('appservice', 'plan', 'show', '-g', $ResourceGroup, '-n', $outputs.planName.value,
            '--query', '{sku:sku.name}', '-o', 'json', '--only-show-errors')).Output | ConvertFrom-Json
    $freeReported = $null -ne $database.free
    if (($freeReported -and -not $database.free) -or ($database.behaviour -and $database.behaviour -ne 'AutoPause') -or $plan.sku -ne 'F1') {
        throw "The free tier did not apply (database free offer: $($database.free), behaviour: $($database.behaviour), plan SKU: $($plan.sku)). Run teardown.ps1 to avoid charges, then investigate."
    }
    if ($freeReported) {
        Write-Note "verified: App Service plan $($plan.sku); database on the free offer with auto-pause when the allowance is used up"
    }
    else {
        Write-Host "    Could not confirm the database is on the free offer from the CLI output; check 'Free monthly vCore amount' on the database's Overview page in the portal." -ForegroundColor Yellow
    }

    $secrets.siteName = $outputs.siteName.value
    $secrets.siteUrl = $outputs.siteUrl.value
    $secrets.sqlServerName = $outputs.sqlServerName.value
    $secrets.sqlServerFqdn = $outputs.sqlServerFqdn.value
    $secrets.databaseName = $outputs.databaseName.value
    Save-Secrets $secrets
    Write-Host "    Web app: $($secrets.siteUrl)   Region: $($secrets.location)"

    if ($BudgetEmail) {
        Write-Note "creating a monthly budget alert of $BudgetAmount (in the subscription's billing currency) for $BudgetEmail"
        $budget = Invoke-Az @('deployment', 'group', 'create', '-g', $ResourceGroup, '-n', 'fundflow-budget', '-f', (Join-Path $PSScriptRoot 'budget.bicep'),
            '-p', "amount=$BudgetAmount", "contactEmail=$BudgetEmail", '-o', 'none', '--only-show-errors') -AllowFailure
        if ($budget.ExitCode -ne 0) {
            Write-Host "    The budget alert could not be created (deployment continues):`n      $($budget.Error.Trim())" -ForegroundColor Yellow
        }
    }
}
elseif (-not $secrets.siteName) {
    throw "-AppOnly needs an existing deployment, but $SecretsFile has none. Run without -AppOnly first."
}

# ---- build -----------------------------------------------------------------------------------------------------------
Write-Step 'Building the application'
$publishDir = Join-Path $WorkDir 'publish'
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

if ($FrontendDist) {
    $dist = (Resolve-Path $FrontendDist).Path
}
else {
    Write-Note 'building the React app (npm ci && npm run build)'
    Push-Location (Join-Path $RepoRoot 'frontend')
    try {
        & npm ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
        & npm run build
        if ($LASTEXITCODE -ne 0) { throw 'npm run build failed' }
    }
    finally { Pop-Location }
    $dist = Join-Path $RepoRoot 'frontend\dist'
}
if (-not (Test-Path (Join-Path $dist 'index.html'))) { throw "No index.html in $dist. Build the frontend first." }

Write-Note 'publishing the API (dotnet publish -c Release)'
& dotnet publish (Join-Path $RepoRoot 'src\FundFlow.Api\FundFlow.Api.csproj') -c Release -o $publishDir --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $publishDir 'appsettings.Development.json') # development-only values never ship
$webRoot = Join-Path $publishDir 'wwwroot'
New-Item -ItemType Directory -Force -Path $webRoot | Out-Null
Copy-Item -Recurse -Force -Path (Join-Path $dist '*') -Destination $webRoot

# ---- database --------------------------------------------------------------------------------------------------------
if (-not $AppOnly) {
    Write-Step 'Preparing the database'
    $ip = (Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 30).ToString().Trim()
    $ruleName = 'fundflow-deploy-temp'
    Write-Note "allowing this machine ($ip) through the SQL firewall for the duration of the deployment"
    [void] (Invoke-Az @('sql', 'server', 'firewall-rule', 'create', '-g', $ResourceGroup, '-s', $secrets.sqlServerName,
            '-n', $ruleName, '--start-ip-address', $ip, '--end-ip-address', $ip, '-o', 'none', '--only-show-errors'))
    try {
        $adminConnection = "Server=tcp:$($secrets.sqlServerFqdn),1433;Initial Catalog=$($secrets.databaseName);User ID=$($secrets.sqlAdminLogin);Password=$($secrets.sqlAdminPassword);Encrypt=True;TrustServerCertificate=False;Connection Timeout=90"

        Write-Note "creating the least-privilege database user $($secrets.sqlAppUser)"
        $appUser = $secrets.sqlAppUser
        $appPassword = $secrets.sqlAppPassword # alphanumeric by construction, so it is safe inside a T-SQL literal
        $createUser = @"
IF DATABASE_PRINCIPAL_ID(N'$appUser') IS NULL
    CREATE USER [$appUser] WITH PASSWORD = N'$appPassword';
ELSE
    ALTER USER [$appUser] WITH PASSWORD = N'$appPassword';
ALTER ROLE db_datareader ADD MEMBER [$appUser];
ALTER ROLE db_datawriter ADD MEMBER [$appUser];
"@
        Invoke-Sql -ConnectionString $adminConnection -Sql $createUser

        Write-Note 'applying migrations and reference data'
        $saved = @{}
        $migrationEnvironment = [ordered]@{
            ASPNETCORE_ENVIRONMENT      = 'Production'
            ConnectionStrings__Default  = $adminConnection
            Jwt__SigningKey             = $secrets.jwtSigningKey
            Messaging__Transport        = 'InMemory'
            Hangfire__Enabled           = 'false'
            Database__SuperAdminEmail   = $secrets.superAdminEmail
            Database__SuperAdminPassword = $secrets.superAdminPassword
            Database__SeedDemoData      = $(if ($NoDemoData) { 'false' } else { 'true' })
            Database__DemoPassword      = $secrets.demoPassword
        }
        foreach ($name in $migrationEnvironment.Keys) {
            $saved[$name] = [Environment]::GetEnvironmentVariable($name)
            [Environment]::SetEnvironmentVariable($name, [string] $migrationEnvironment[$name])
        }
        try {
            & dotnet (Join-Path $publishDir 'FundFlow.Api.dll') --migrate
            if ($LASTEXITCODE -ne 0) { throw "The migration step failed with exit code $LASTEXITCODE" }
        }
        finally {
            foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
        }
    }
    finally {
        Write-Note 'removing the temporary firewall rule'
        [void] (Invoke-Az @('sql', 'server', 'firewall-rule', 'delete', '-g', $ResourceGroup, '-s', $secrets.sqlServerName,
                '-n', $ruleName, '-o', 'none', '--only-show-errors') -AllowFailure)
    }
}

# ---- ship ------------------------------------------------------------------------------------------------------------
Write-Step 'Deploying the application'
$zip = Join-Path $WorkDir 'fundflow.zip'
if (Test-Path $zip) { Remove-Item -Force $zip }
# Built entry by entry because Windows PowerShell 5.1 (.NET Framework) writes backslashes into ZipFile.CreateFromDirectory
# entry names, which the Linux extractor on App Service turns into files literally named "wwwroot\assets\x.js".
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipStream = [System.IO.File]::Open($zip, [System.IO.FileMode]::Create)
try {
    $archive = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $rootLength = (Resolve-Path $publishDir).Path.TrimEnd('\').Length + 1
        foreach ($file in Get-ChildItem -Path $publishDir -Recurse -File) {
            $entryName = $file.FullName.Substring($rootLength).Replace('\', '/')
            [void] [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally { $archive.Dispose() }
}
finally { $zipStream.Dispose() }
Write-Note ("package: {0:N1} MB" -f ((Get-Item $zip).Length / 1MB))
[void] (Invoke-Az @('webapp', 'deploy', '-g', $ResourceGroup, '-n', $secrets.siteName, '--src-path', $zip, '--type', 'zip',
        '--clean', 'true', '--restart', 'true', '-o', 'none', '--only-show-errors'))

# ---- verify ----------------------------------------------------------------------------------------------------------
Write-Step 'Waiting for the site to start (a cold start on the free tier can take a minute or two)'
$base = $secrets.siteUrl.TrimEnd('/')
[void] (Wait-ForUrl -Url "$base/health/live" -TimeoutSeconds 420)
Write-Note '/health/live is up'
$ready = Wait-ForUrl -Url "$base/health/ready" -TimeoutSeconds 300
Write-Note "/health/ready: $($ready.Content)"
[void] (Wait-ForUrl -Url "$base/login" -TimeoutSeconds 60 -Expect '<div id="root">')
Write-Note 'the React app is served'

if (-not $NoDemoData) {
    $body = @{ email = 'admin@hopefoundation.test'; password = $secrets.demoPassword } | ConvertTo-Json
    $login = Invoke-RestMethod -Method Post -Uri "$base/api/v1/auth/login" -ContentType 'application/json' -Body $body -TimeoutSec 100
    if (-not $login.accessToken) { throw 'Sign-in with the demo administrator did not return a token.' }
    $me = Invoke-RestMethod -Uri "$base/api/v1/auth/me" -Headers @{ Authorization = "Bearer $($login.accessToken)" } -TimeoutSec 60
    Write-Note "signed in as the demo administrator of '$($me.organization.name)'"
}

Write-Host ''
Write-Host 'FundFlow is live.' -ForegroundColor Green
Write-Host "  URL:      $base"
Write-Host "  Secrets:  $SecretsFile  (sign-in passwords and database credentials; keep it private)"
if (-not $NoDemoData) {
    Write-Host '  Demo sign-in: admin@hopefoundation.test (password: "demoPassword" in the secrets file)'
}
Write-Host "  Platform operator: $($secrets.superAdminEmail) (password: `"superAdminPassword`" in the secrets file)"
if (-not $smtpHostSetting) {
    Write-Host '  Email is not configured: registration, invitations and password resets will not be delivered.' -ForegroundColor Yellow
    Write-Host '  See docs/deployment-azure.md ("Email") to add a free SMTP relay.' -ForegroundColor Yellow
}
