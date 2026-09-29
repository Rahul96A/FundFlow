<#
.SYNOPSIS
    Sets up outgoing email (registration, invitation and password-reset messages) for the deployed FundFlow site.

.DESCRIPTION
    1. Asks for the SMTP password in your terminal (hidden; it never appears on a command line, in shell history or in chat),
       or reads it from the FUNDFLOW_SMTP_PASSWORD environment variable.
    2. Sends a test message through the relay to -TestTo, which proves the host, port and login work before anything changes.
    3. Saves the settings to the deployment's secrets file, so later deploy.ps1 runs keep them.
    4. Applies only the Email__* settings to the web app (everything else is untouched) and waits for it to come back.

    You need an SMTP relay. Two things that are easy to get wrong:
      * A relay can only send as an address it is allowed to: use a sender your provider has verified (with Gmail, your own address).
      * Third-party relays (Brevo, Mailjet, SMTP2GO...) cannot deliver mail "from" a gmail.com address reliably (DMARC), so
        they need a domain you own. Sending through Gmail's own servers as yourself works without a domain.

.EXAMPLE
    # Gmail: turn on 2-Step Verification, create an App Password at https://myaccount.google.com/apppasswords, then:
    powershell -ExecutionPolicy Bypass -File infra\azure\set-smtp.ps1 -Preset Gmail -User you@gmail.com -TestTo you@gmail.com

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File infra\azure\set-smtp.ps1 -SmtpHost smtp-relay.brevo.com -User <login> `
        -From no-reply@your-domain.com -TestTo you@example.com
#>
[CmdletBinding()]
param(
    # Gmail fills in smtp.gmail.com:587 with STARTTLS and sends as -User.
    [ValidateSet('Gmail', 'Custom')] [string] $Preset = 'Custom',

    [string] $SmtpHost,
    [int] $Port = 587,
    [ValidateSet('StartTls', 'SslOnConnect', 'None')] [string] $Security = 'StartTls',

    # SMTP login (usually the sender address or an API-key id).
    [Parameter(Mandatory)] [string] $User,

    # Sender address; must be one the relay allows. Defaults to -User.
    [string] $From,
    [string] $FromName = 'FundFlow',

    # Where to send the verification message. Required unless -SkipTest.
    [string] $TestTo,
    [switch] $SkipTest,

    # Verify and save, but do not touch Azure (also handy for a dry run against a local Mailpit).
    [switch] $NoApply,

    [string] $ResourceGroup = 'rg-fundflow-free',
    [string] $SecretsFile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

if (-not $SecretsFile) { $SecretsFile = Join-Path (Join-Path $env:USERPROFILE '.fundflow') "azure-$ResourceGroup.json" }

function Write-Step([string] $Message) { Write-Host "==> $Message" -ForegroundColor Cyan }

function Get-SecretProperty($Secrets, [string] $Name, $Default = '') {
    if ($Secrets.PSObject.Properties[$Name]) { return $Secrets.$Name }
    return $Default
}

function Set-SecretProperty($Secrets, [string] $Name, $Value) {
    if ($Secrets.PSObject.Properties[$Name]) { $Secrets.$Name = $Value }
    else { Add-Member -InputObject $Secrets -NotePropertyName $Name -NotePropertyValue $Value }
}

# Windows PowerShell 5.1 turns anything a native command writes to a redirected stderr into a terminating error while
# ErrorActionPreference is 'Stop'; report az failures ourselves instead.
function Invoke-Az {
    param([Parameter(Mandatory)] [string[]] $Arguments)
    $errFile = [System.IO.Path]::GetTempFileName()
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $stdout = & az @Arguments 2> $errFile
        $code = $LASTEXITCODE
        $stderr = Get-Content -Raw -ErrorAction SilentlyContinue $errFile
    }
    finally {
        $ErrorActionPreference = $previous
        Remove-Item $errFile -Force -ErrorAction SilentlyContinue
    }
    if ($code -ne 0) { throw "az $($Arguments[0..2] -join ' ') failed with exit code $code.`n$stderr" }
    ($stdout | ForEach-Object { "$_" }) -join "`n"
}

# ---- settings --------------------------------------------------------------------------------------------------------
if ($Preset -eq 'Gmail' -and -not $SmtpHost) { $SmtpHost = 'smtp.gmail.com' }
if (-not $SmtpHost) { throw 'Give -SmtpHost <relay> (or -Preset Gmail).' }
if (-not $From) { $From = $User }
if ($From -notmatch '^[^@\s]+@[^@\s]+\.[^@\s]+$') { throw "-From must be an email address (got '$From'). Pass -From when -User is not one." }
if (-not $SkipTest -and -not $TestTo) { throw 'Give -TestTo <an address you can read> so the login can be verified, or -SkipTest.' }
if ($Security -eq 'SslOnConnect' -and -not $SkipTest) {
    throw 'PowerShell cannot test implicit TLS (port 465). Use port 587 with -Security StartTls, or pass -SkipTest.'
}

$password = $env:FUNDFLOW_SMTP_PASSWORD
if (-not $password) {
    $secure = Read-Host -Prompt "SMTP password for $User (input is hidden)" -AsSecureString
    $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { $password = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}
if ($Preset -eq 'Gmail') { $password = $password -replace '\s', '' } # Google shows app passwords in groups of four
if (-not $password) { throw 'No password given.' }

# ---- prove the relay works -------------------------------------------------------------------------------------------
if (-not $SkipTest) {
    Write-Step "Sending a test message through ${SmtpHost}:$Port to $TestTo"
    $client = New-Object System.Net.Mail.SmtpClient($SmtpHost, $Port)
    $client.EnableSsl = ($Security -eq 'StartTls')
    $client.Timeout = 30000
    $client.Credentials = New-Object System.Net.NetworkCredential($User, $password)
    $message = New-Object System.Net.Mail.MailMessage
    try {
        $message.From = New-Object System.Net.Mail.MailAddress($From, $FromName)
        $message.To.Add($TestTo)
        $message.Subject = 'FundFlow: outgoing email is set up'
        $message.Body = "This message was sent by set-smtp.ps1 to check the email settings of your FundFlow site.`r`n`r`n" +
            'If you can read it, registration, invitation and password-reset emails will be delivered from this address.'
        $client.Send($message)
    }
    catch {
        throw "The relay did not accept the message: $($_.Exception.GetBaseException().Message)"
    }
    finally {
        $message.Dispose()
        $client.Dispose()
    }
    Write-Host "    sent. Check the inbox of $TestTo (and its spam folder)."
}
else {
    Write-Host '    -SkipTest: the settings have NOT been verified.' -ForegroundColor Yellow
}

# ---- remember + apply ------------------------------------------------------------------------------------------------
Write-Step "Saving the settings to $SecretsFile"
if (Test-Path $SecretsFile) { $secrets = Get-Content -Raw $SecretsFile | ConvertFrom-Json }
elseif ($NoApply) { $secrets = [pscustomobject]@{} }
else { throw "No secrets file at $SecretsFile. Run deploy.ps1 first (or pass -SecretsFile)." }

Set-SecretProperty $secrets 'smtpHost' $SmtpHost
Set-SecretProperty $secrets 'smtpPort' $Port
Set-SecretProperty $secrets 'smtpSecurity' $Security
Set-SecretProperty $secrets 'smtpUser' $User
Set-SecretProperty $secrets 'smtpPassword' $password
Set-SecretProperty $secrets 'mailFrom' $From
$directory = Split-Path -Parent $SecretsFile
if ($directory -and -not (Test-Path $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
[System.IO.File]::WriteAllText($SecretsFile, ($secrets | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))

if ($NoApply) {
    Write-Host '    -NoApply: Azure was not changed.'
    return
}

Write-Step "Applying the Email settings to $($secrets.siteName)"
$settings = @(
    @{ name = 'Email__Host'; value = $SmtpHost; slotSetting = $false },
    @{ name = 'Email__Port'; value = "$Port"; slotSetting = $false },
    @{ name = 'Email__Security'; value = $Security; slotSetting = $false },
    @{ name = 'Email__Username'; value = $User; slotSetting = $false },
    @{ name = 'Email__Password'; value = $password; slotSetting = $false },
    @{ name = 'Email__FromAddress'; value = $From; slotSetting = $false },
    @{ name = 'Email__FromName'; value = $FromName; slotSetting = $false }
)
$settingsFile = [System.IO.Path]::GetTempFileName()
try {
    [System.IO.File]::WriteAllText($settingsFile, (ConvertTo-Json -InputObject $settings -Depth 3), (New-Object System.Text.UTF8Encoding($false)))
    [void] (Invoke-Az @('webapp', 'config', 'appsettings', 'set', '-g', $ResourceGroup, '-n', $secrets.siteName, '--settings', "@$settingsFile", '-o', 'none', '--only-show-errors'))
}
finally {
    Remove-Item $settingsFile -Force -ErrorAction SilentlyContinue # it holds the password
}

Write-Step 'Waiting for the site to restart with the new settings'
$base = ([string] $secrets.siteUrl).TrimEnd('/')
$deadline = (Get-Date).AddMinutes(5)
$up = $false
Start-Sleep -Seconds 10
while ((Get-Date) -lt $deadline -and -not $up) {
    try { $up = (Invoke-WebRequest -Uri "$base/health/ready" -UseBasicParsing -TimeoutSec 100).StatusCode -eq 200 }
    catch { Start-Sleep -Seconds 10 }
}
if (-not $up) { throw "The site did not report ready within 5 minutes. Check: az webapp log tail -g $ResourceGroup -n $($secrets.siteName)" }

Write-Host ''
Write-Host 'Outgoing email is configured.' -ForegroundColor Green
Write-Host "  Try it: register an organization at $base/register with an address you can read; the verification email should arrive within a minute."
Write-Host '  Emails are queued in the database and retried, so a temporary relay problem delays them rather than losing them.'
