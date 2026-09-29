<#
.SYNOPSIS
    Sets up outgoing email for the deployed FundFlow site with Azure Communication Services (ACS) Email over SMTP.

.DESCRIPTION
    Everything is created inside your own Azure subscription; no third-party account is needed.

      1. Registers the Microsoft.Communication resource provider if the subscription has not used it yet (free).
      2. Creates an Entra application + service principal for SMTP logins (in your tenant), named <site>-smtp.
      3. Deploys infra/azure/email-acs.bicep into the resource group: Email Communication Service, an Azure-managed
         sender domain (<guid>.azurecomm.net), the Communication Service linked to it, the SMTP username, and a role
         assignment that lets the application use only that Communication Service.
      4. Creates a client secret (valid one year) for the application: this is the SMTP password. It is written only to the
         local secrets file and the web app's settings, never printed.
      5. Runs set-smtp.ps1, which sends a test message to -TestTo through smtp.azurecomm.net, then saves and applies the
         Email__* settings. New credentials can take a few minutes to work, so it retries.

    Know before you rely on it:
      * Azure-managed sender domains allow 5 emails/minute and 10 emails/hour per subscription and that cannot be raised. Fine
        for a demo (each registration, invitation or password reset is one email); a verified custom domain allows 30 and 100.
      * It is billed per email (about US$0.00025), not free-tier; the resource group's budget alert covers it.
      * Microsoft has announced the retirement of ACS Email for 2028-09-30. New resources still work today. FundFlow only speaks
        SMTP, so switching later is `set-smtp.ps1` with another relay.

    Safe to re-run: existing resources are kept. -RotateSecret issues a new client secret (the old one stops working).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File infra\azure\setup-acs-email.ps1 -Yes
#>
[CmdletBinding()]
param(
    [string] $ResourceGroup = 'rg-fundflow-free',
    [string] $NamePrefix = 'fundflow',

    # Data-at-rest location for the email and communication services, tried in order.
    [string[]] $DataLocations = @('India', 'Asia Pacific'),

    # Receives the verification message. Defaults to the address you are signed in to Azure with.
    [string] $TestTo,

    # Issue a new client secret (the SMTP password) and replace the old one.
    [switch] $RotateSecret,

    [string] $SecretsFile,

    # Skip the confirmation prompt.
    [switch] $Yes
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$InfraFile = Join-Path $PSScriptRoot 'email-acs.bicep'
if (-not $SecretsFile) { $SecretsFile = Join-Path (Join-Path $env:USERPROFILE '.fundflow') "azure-$ResourceGroup.json" }

function Write-Step([string] $Message) { Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Note([string] $Message) { Write-Host "    $Message" -ForegroundColor DarkGray }

# az wrapper: Windows PowerShell 5.1 raises a terminating NativeCommandError for stderr under ErrorActionPreference 'Stop'.
function Invoke-Az {
    param([Parameter(Mandatory)] [string[]] $Arguments, [switch] $AllowFailure)
    $errFile = [System.IO.Path]::GetTempFileName()
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $stdout = & az @Arguments 2> $errFile
        $code = $LASTEXITCODE
        $stderr = Get-Content -Raw -ErrorAction SilentlyContinue $errFile
        if (-not $stderr) { $stderr = '' }
    }
    finally {
        $ErrorActionPreference = $previous
        Remove-Item $errFile -Force -ErrorAction SilentlyContinue
    }
    if ($code -ne 0 -and -not $AllowFailure) {
        throw "az $($Arguments[0..3] -join ' ') failed with exit code $code.`n$stderr"
    }
    [pscustomobject]@{ ExitCode = $code; Output = (($stdout | ForEach-Object { "$_" }) -join "`n"); Error = $stderr }
}

# First value of a single-column TSV result, or $null when az printed nothing (it prints '', 'None' or 'null' for no value).
function Get-FirstValue([string] $Text) {
    foreach ($line in ($Text -split "`n")) {
        $value = $line.Trim()
        if ($value -and $value -ne 'None' -and $value -ne 'null') { return $value }
    }
    return $null
}

function Get-SecretProperty($Secrets, [string] $Name, $Default = '') {
    if ($Secrets.PSObject.Properties[$Name]) { return $Secrets.$Name }
    return $Default
}

function Set-SecretProperty($Secrets, [string] $Name, $Value) {
    if ($Secrets.PSObject.Properties[$Name]) { $Secrets.$Name = $Value }
    else { Add-Member -InputObject $Secrets -NotePropertyName $Name -NotePropertyValue $Value }
}

function Save-Secrets($Secrets) {
    [System.IO.File]::WriteAllText($SecretsFile, ($Secrets | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
}

# ---- preconditions -----------------------------------------------------------------------------------------------------
Write-Step 'Checking the deployment'
if (-not (Get-Command az -ErrorAction SilentlyContinue)) { throw 'Azure CLI not found. Install it and run "az login".' }
if (-not (Test-Path $SecretsFile)) { throw "No secrets file at $SecretsFile. Run deploy.ps1 first." }
$secrets = Get-Content -Raw $SecretsFile | ConvertFrom-Json
if (-not $secrets.siteName) { throw "$SecretsFile has no deployed site. Run deploy.ps1 first." }

$account = (Invoke-Az @('account', 'show', '-o', 'json', '--only-show-errors')).Output | ConvertFrom-Json
if (-not $TestTo) {
    if ($account.user.name -match '^[^@\s]+@[^@\s]+\.[^@\s]+$') { $TestTo = $account.user.name }
    else { throw 'Give -TestTo <an address you can read>.' }
}
$groupExists = (Invoke-Az @('group', 'exists', '-n', $ResourceGroup, '-o', 'tsv', '--only-show-errors')).Output.Trim() -eq 'true'
if (-not $groupExists) { throw "Resource group $ResourceGroup does not exist." }

Write-Host "    Subscription: $($account.name)    Signed in as: $($account.user.name)"
Write-Host "    Site: $($secrets.siteName)    Resource group: $ResourceGroup"
Write-Host "    Creates: Entra app '$($secrets.siteName)-smtp', ACS Email + Communication services (Azure-managed sender domain), an SMTP username."
Write-Host "    Test message goes to: $TestTo"
if (-not $Yes) {
    $answer = Read-Host 'Create these in your subscription and tenant? (y/N)'
    if ($answer -notmatch '^(y|yes)$') { Write-Host 'Cancelled.'; return }
}

# ---- resource provider -------------------------------------------------------------------------------------------------
Write-Step 'Microsoft.Communication resource provider'
$state = (Invoke-Az @('provider', 'show', '-n', 'Microsoft.Communication', '--query', 'registrationState', '-o', 'tsv', '--only-show-errors')).Output.Trim()
if ($state -ne 'Registered') {
    Write-Note "state is '$state'; registering (free, takes a minute or two)"
    [void] (Invoke-Az @('provider', 'register', '-n', 'Microsoft.Communication', '--wait', '--only-show-errors'))
}
else {
    Write-Note 'already registered'
}

# ---- Entra application for SMTP logins ---------------------------------------------------------------------------------
Write-Step 'Entra application and service principal'
$appName = "$($secrets.siteName)-smtp"
$appId = Get-FirstValue (Invoke-Az @('ad', 'app', 'list', '--display-name', $appName, '--query', "[?displayName=='$appName'].appId", '-o', 'tsv', '--only-show-errors')).Output
if ($appId) {
    Write-Note "reusing application $appName"
}
else {
    $appId = Get-FirstValue (Invoke-Az @('ad', 'app', 'create', '--display-name', $appName, '--sign-in-audience', 'AzureADMyOrg', '--query', 'appId', '-o', 'tsv', '--only-show-errors')).Output
    if (-not $appId) { throw "Creating the Entra application $appName returned no application id." }
    Write-Note "created application $appName"
}
$spId = Get-FirstValue (Invoke-Az @('ad', 'sp', 'list', '--filter', "appId eq '$appId'", '--query', '[].id', '-o', 'tsv', '--only-show-errors')).Output
if (-not $spId) {
    $spId = Get-FirstValue (Invoke-Az @('ad', 'sp', 'create', '--id', $appId, '--query', 'id', '-o', 'tsv', '--only-show-errors')).Output
    if (-not $spId) { throw "Creating the service principal for application $appId returned no object id." }
}

Set-SecretProperty $secrets 'acsSmtpAppId' $appId
Set-SecretProperty $secrets 'acsSmtpServicePrincipalId' $spId
Save-Secrets $secrets

# ---- ACS resources -----------------------------------------------------------------------------------------------------
Write-Step 'Deploying Azure Communication Services (email service, managed domain, SMTP username, role)'
$outputs = $null
foreach ($dataLocation in $DataLocations) {
    for ($attempt = 1; $attempt -le 4 -and -not $outputs; $attempt++) {
        Write-Note "data location '$dataLocation' (attempt $attempt)"
        $result = Invoke-Az @('deployment', 'group', 'create', '-g', $ResourceGroup, '-n', 'fundflow-email', '-f', $InfraFile,
            '-p', "namePrefix=$NamePrefix", "dataLocation=$dataLocation", "entraApplicationId=$appId", "servicePrincipalObjectId=$spId",
            "tenantId=$($account.tenantId)", '--query', 'properties.outputs', '-o', 'json', '--only-show-errors') -AllowFailure
        if ($result.ExitCode -eq 0) {
            $outputs = $result.Output | ConvertFrom-Json
            Set-SecretProperty $secrets 'acsDataLocation' $dataLocation
            break
        }
        if ($result.Error -match 'PrincipalNotFound|does not exist in the directory|replication') {
            Write-Note 'the new service principal has not replicated yet; retrying in 30 s'
            Start-Sleep -Seconds 30
            continue
        }
        $firstLines = ($result.Error -split "`n" | Where-Object { $_ -match '\S' } | Select-Object -First 6) -join "`n      "
        if ($result.Error -match '(?i)data.?location') {
            Write-Host "    '$dataLocation' was not accepted:`n      $firstLines" -ForegroundColor Yellow
            break # next data location
        }
        throw "The Communication Services deployment failed:`n$firstLines"
    }
    if ($outputs) { break }
}
if (-not $outputs) { throw "None of the data locations ($($DataLocations -join ', ')) was accepted." }

$senderDomain = $outputs.senderDomain.value
$smtpUsername = $outputs.smtpUsername.value
$communicationService = $outputs.communicationServiceName.value
$emailService = $outputs.emailServiceName.value
if (-not $senderDomain) { throw 'The managed sender domain has no mail-from domain yet. Wait a minute and re-run.' }

# The Azure-managed domain comes with a default sender username (DoNotReply); use whatever exists.
$senderUser = 'DoNotReply'
$listed = Invoke-Az @('rest', '--method', 'get', '--url',
    "https://management.azure.com/subscriptions/$($account.id)/resourceGroups/$ResourceGroup/providers/Microsoft.Communication/emailServices/$emailService/domains/AzureManagedDomain/senderUsernames?api-version=2025-09-01",
    '--query', 'value[].properties.username', '-o', 'tsv', '--only-show-errors') -AllowFailure
if ($listed.ExitCode -eq 0) {
    $names = @(($listed.Output -split "`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ -and $_ -ne 'None' })
    if ($names.Count -gt 0 -and ($names -notcontains $senderUser)) { $senderUser = $names[0] }
}
$sender = "$senderUser@$senderDomain"
Write-Note "sender address: $sender"

Set-SecretProperty $secrets 'acsEmailService' $emailService
Set-SecretProperty $secrets 'acsCommunicationService' $communicationService
Set-SecretProperty $secrets 'acsSmtpUsername' $smtpUsername
Set-SecretProperty $secrets 'acsSender' $sender
Save-Secrets $secrets

# ---- SMTP password = the application's client secret -------------------------------------------------------------------
Write-Step 'SMTP credentials'
$smtpPassword = [string] (Get-SecretProperty $secrets 'acsSmtpPassword')
if ($RotateSecret -or -not $smtpPassword) {
    $credential = (Invoke-Az @('ad', 'app', 'credential', 'reset', '--id', $appId, '--display-name', 'fundflow-smtp', '--years', '1', '--query', '{password:password}', '-o', 'json', '--only-show-errors')).Output | ConvertFrom-Json
    $smtpPassword = $credential.password
    Set-SecretProperty $secrets 'acsSmtpPassword' $smtpPassword
    Set-SecretProperty $secrets 'acsSmtpSecretExpires' (Get-Date).AddYears(1).ToString('yyyy-MM-dd')
    Save-Secrets $secrets
    Write-Note 'issued a new client secret (valid one year; not printed)'
}
else {
    Write-Note 'reusing the saved client secret (use -RotateSecret to issue a new one)'
}

# ---- verify + apply ----------------------------------------------------------------------------------------------------
Write-Step "Sending a test message and applying the settings (new credentials can take a few minutes to work)"
$env:FUNDFLOW_SMTP_PASSWORD = $smtpPassword
try {
    $attempts = 10
    for ($attempt = 1; $attempt -le $attempts; $attempt++) {
        try {
            & (Join-Path $PSScriptRoot 'set-smtp.ps1') -SmtpHost 'smtp.azurecomm.net' -Port 587 -Security StartTls -User $smtpUsername `
                -From $sender -FromName 'FundFlow' -TestTo $TestTo -ResourceGroup $ResourceGroup -SecretsFile $SecretsFile
            break
        }
        catch {
            if ($attempt -eq $attempts) { throw }
            $reason = $_.Exception.Message.Split("`n")[0]
            Write-Note "not accepted yet ($reason); retrying in 45 s ($attempt/$attempts)"
            Start-Sleep -Seconds 45
        }
    }
}
finally {
    Remove-Item Env:\FUNDFLOW_SMTP_PASSWORD -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "Email now goes through Azure Communication Services as $sender." -ForegroundColor Green
Write-Host '  Limits: 5 emails/minute and 10/hour on the Azure-managed domain (cannot be raised); more needs a verified custom domain.'
Write-Host '  Cost:   about US$0.00025 per email, covered by the resource group budget alert.'
Write-Host '  Secret: the SMTP client secret expires on' (Get-SecretProperty $secrets 'acsSmtpSecretExpires') '- re-run with -RotateSecret before then.'
Write-Host '  Note:   Microsoft has announced ACS Email retirement for 2028-09-30; set-smtp.ps1 can point the site at any other relay.'
