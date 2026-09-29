<#
.SYNOPSIS
    Deletes everything deploy.ps1 and setup-acs-email.ps1 created: the resource group (web app, plan, SQL server and
    database, and the Communication Services email resources) and the Entra application used for SMTP logins.

.DESCRIPTION
    This is permanent: the database and its data are removed. The Entra application lives in your tenant, outside the
    resource group, so it is deleted separately (its client secret would otherwise outlive the deployment). The local secrets
    file is kept unless -RemoveSecrets is given.
#>
[CmdletBinding()]
param(
    [string] $ResourceGroup = 'rg-fundflow-free',
    [string] $SecretsFile,
    [switch] $RemoveSecrets,
    [switch] $Yes
)

$ErrorActionPreference = 'Stop'
if (-not $SecretsFile) { $SecretsFile = Join-Path (Join-Path $env:USERPROFILE '.fundflow') "azure-$ResourceGroup.json" }

$smtpAppId = $null
if (Test-Path $SecretsFile) {
    $saved = Get-Content -Raw $SecretsFile | ConvertFrom-Json
    if ($saved.PSObject.Properties['acsSmtpAppId']) { $smtpAppId = [string] $saved.acsSmtpAppId }
}

$exists = (& az group exists -n $ResourceGroup -o tsv --only-show-errors).Trim()
if ($exists -ne 'true') {
    Write-Host "Resource group $ResourceGroup does not exist; nothing to delete there."
}
else {
    Write-Host "Resources in ${ResourceGroup}:"
    & az resource list -g $ResourceGroup --query "[].{name:name,type:type}" -o table --only-show-errors
    if ($smtpAppId) { Write-Host "Also deleted: the Entra application $smtpAppId (SMTP logins for Azure Communication Services)." }
    if (-not $Yes) {
        $answer = Read-Host "Permanently delete the resource group $ResourceGroup and all its data? (y/N)"
        if ($answer -notmatch '^(y|yes)$') { Write-Host 'Cancelled.'; return }
    }
    & az group delete -n $ResourceGroup --yes --only-show-errors
    if ($LASTEXITCODE -ne 0) { throw 'az group delete failed' }
    Write-Host "Deleted $ResourceGroup."
}

if ($smtpAppId) {
    & az ad app delete --id $smtpAppId --only-show-errors
    if ($LASTEXITCODE -eq 0) { Write-Host "Deleted the Entra application $smtpAppId." }
    else { Write-Host "Could not delete the Entra application $smtpAppId; remove it in the portal (Entra ID > App registrations)." -ForegroundColor Yellow }
}

if ($RemoveSecrets -and (Test-Path $SecretsFile)) {
    Remove-Item -Force $SecretsFile
    Write-Host "Removed $SecretsFile."
}
