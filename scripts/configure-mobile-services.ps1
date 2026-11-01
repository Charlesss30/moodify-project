[CmdletBinding()]
param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$GmailAddress,
    [Security.SecureString]$GmailAppPassword
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-JsonProperty {
    param($Value, [string]$Name)
    if ($null -eq $Value) { return $null }
    $taskProperty = $Value.PSObject.Properties[$Name]
    if ($null -ne $taskProperty) { return $taskProperty.Value }
    return $null
}

function Set-JsonProperty {
    param($Value, [string]$Name, $PropertyValue)
    $Value | Add-Member -MemberType NoteProperty -Name $Name -Value $PropertyValue -Force
}

function Get-OrCreateSection {
    param($Value, [string]$Name)
    $taskSection = Get-JsonProperty $Value $Name
    if ($null -eq $taskSection) {
        $taskSection = [pscustomobject]@{}
        Set-JsonProperty $Value $Name $taskSection
    }
    return $taskSection
}

function Write-LocalJson {
    param([string]$Path, $Value)
    $taskJson = ConvertTo-Json -InputObject $Value -Depth 50
    [IO.File]::WriteAllText($Path, $taskJson + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

$taskRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$taskBackendDir = Join-Path $taskRoot 'backend'
$taskAndroidDir = Join-Path $taskRoot 'mobile-app/android/app'
$taskAndroidPath = Join-Path $taskAndroidDir 'google-services.json'
$taskServicePath = Join-Path $taskBackendDir 'firebase-adminsdk.json'
$taskSettingsPath = Join-Path $taskBackendDir 'appsettings.Development.json'
$taskFlagsPath = Join-Path $taskRoot 'mobile-app/firebase-config.local.json'

foreach ($taskRequired in @(
    (Join-Path $taskBackendDir 'mood_recommendation.csproj'),
    $taskAndroidPath,
    $taskServicePath
)) {
    if (-not (Test-Path -LiteralPath $taskRequired -PathType Leaf)) {
        throw "Missing required file: $taskRequired. See docs/AUTH_NOTIFICATIONS_SETUP.md."
    }
}
if ($GmailAppPassword -and [string]::IsNullOrWhiteSpace($GmailAddress)) {
    throw 'Supply -GmailAddress when providing -GmailAppPassword.'
}

$taskAndroid = Get-Content -LiteralPath $taskAndroidPath -Raw -Encoding UTF8 | ConvertFrom-Json
$taskService = Get-Content -LiteralPath $taskServicePath -Raw -Encoding UTF8 | ConvertFrom-Json
$taskProjectInfo = Get-JsonProperty $taskAndroid 'project_info'
$taskProjectId = Get-JsonProperty $taskProjectInfo 'project_id'
$taskProjectNumber = Get-JsonProperty $taskProjectInfo 'project_number'
if ([string]::IsNullOrWhiteSpace($taskProjectId) -or [string]::IsNullOrWhiteSpace($taskProjectNumber)) {
    throw 'Android Firebase project_id or project_number is missing.'
}
if ((Get-JsonProperty $taskService 'type') -ne 'service_account' -or
    [string]::IsNullOrWhiteSpace((Get-JsonProperty $taskService 'private_key')) -or
    (Get-JsonProperty $taskService 'project_id') -ne $taskProjectId) {
    throw 'The Firebase service account is invalid or belongs to a different project.'
}

$taskGradle = Get-Content -LiteralPath (Join-Path $taskAndroidDir 'build.gradle.kts') -Raw
$taskPackageMatch = [regex]::Match($taskGradle, 'applicationId\s*=\s*"([^"]+)"')
if (-not $taskPackageMatch.Success) { throw 'Could not determine Android applicationId.' }
$taskPackageId = $taskPackageMatch.Groups[1].Value
$taskClients = @(Get-JsonProperty $taskAndroid 'client') | Where-Object {
    $taskClientInfo = Get-JsonProperty $_ 'client_info'
    (Get-JsonProperty (Get-JsonProperty $taskClientInfo 'android_client_info') 'package_name') -eq $taskPackageId
}
$taskClients = @($taskClients)
if ($taskClients.Count -ne 1) { throw 'google-services.json must contain one matching Android client.' }
$taskClient = $taskClients[0]
$taskOauth = @(Get-JsonProperty $taskClient 'oauth_client')
$taskAppInvite = Get-JsonProperty (Get-JsonProperty $taskClient 'services') 'appinvite_service'
$taskOauth += @(Get-JsonProperty $taskAppInvite 'other_platform_oauth_client')
$taskWebClients = @($taskOauth | Where-Object { (Get-JsonProperty $_ 'client_type') -eq 3 })
if ($taskWebClients.Count -eq 0) {
    throw 'No Web OAuth client found. Enable Google sign-in, add the Android signing SHA, then download google-services.json again.'
}
$taskWebClientId = Get-JsonProperty $taskWebClients[0] 'client_id'
$taskApiKeys = @(Get-JsonProperty $taskClient 'api_key')
$taskApiKey = if ($taskApiKeys.Count -gt 0) { Get-JsonProperty $taskApiKeys[0] 'current_key' } else { $null }
$taskAppId = Get-JsonProperty (Get-JsonProperty $taskClient 'client_info') 'mobilesdk_app_id'
if ([string]::IsNullOrWhiteSpace($taskWebClientId) -or [string]::IsNullOrWhiteSpace($taskApiKey) -or
    [string]::IsNullOrWhiteSpace($taskAppId)) { throw 'Firebase client configuration is incomplete.' }

$taskSettingsSource = if (Test-Path -LiteralPath $taskSettingsPath) {
    $taskSettingsPath
} else {
    Join-Path $taskBackendDir 'appsettings.Development.example.json'
}
$taskSettings = Get-Content -LiteralPath $taskSettingsSource -Raw -Encoding UTF8 | ConvertFrom-Json
$taskFirebaseSection = Get-OrCreateSection $taskSettings 'Firebase'
Set-JsonProperty $taskFirebaseSection 'ProjectId' $taskProjectId
Set-JsonProperty $taskFirebaseSection 'ServiceAccountPath' 'firebase-adminsdk.json'
Set-JsonProperty $taskFirebaseSection 'GoogleServerClientId' $taskWebClientId

$taskJwtSection = Get-OrCreateSection $taskSettings 'Jwt'
$taskJwtKey = Get-JsonProperty $taskJwtSection 'Key'
if ([string]::IsNullOrWhiteSpace($taskJwtKey)) {
    $taskKeyBytes = New-Object byte[] 48
    $taskRandom = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $taskRandom.GetBytes($taskKeyBytes) } finally { $taskRandom.Dispose() }
    Set-JsonProperty $taskJwtSection 'Key' ([Convert]::ToBase64String($taskKeyBytes))
} elseif ([Text.Encoding]::UTF8.GetByteCount($taskJwtKey) -lt 32) {
    throw 'Existing Jwt.Key is shorter than 32 bytes. Replace it with a strong local secret.'
}

if (-not [string]::IsNullOrWhiteSpace($GmailAddress)) {
    $taskAddress = $GmailAddress.Trim()
    try { $taskParsedAddress = [System.Net.Mail.MailAddress]::new($taskAddress) }
    catch { throw 'GmailAddress must be a valid email address.' }
    if ($taskParsedAddress.Address -ne $taskAddress) { throw 'Use an email address without a display name.' }
    if (-not $GmailAppPassword) {
        $GmailAppPassword = Read-Host 'Gmail app password (hidden; not your normal Gmail password)' -AsSecureString
    }
    $taskSecretPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($GmailAppPassword)
    try {
        $taskPlainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($taskSecretPointer) -replace '\s', ''
        if ($taskPlainPassword.Length -ne 16) { throw 'Gmail app passwords must contain 16 characters, excluding spaces.' }
        $taskEmailSection = Get-OrCreateSection $taskSettings 'Email'
        Set-JsonProperty $taskEmailSection 'Host' 'smtp.gmail.com'
        Set-JsonProperty $taskEmailSection 'Port' 587
        Set-JsonProperty $taskEmailSection 'Username' $taskAddress
        Set-JsonProperty $taskEmailSection 'From' $taskAddress
        Set-JsonProperty $taskEmailSection 'Password' $taskPlainPassword
    } finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($taskSecretPointer)
        $taskPlainPassword = $null
    }
}

# Preserve prior local settings before writing. This folder is ignored by Git.
$taskBackupDir = Join-Path $taskRoot ('.local-backups/mobile-services-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff'))
$null = New-Item -ItemType Directory -Path $taskBackupDir -Force
if (Test-Path -LiteralPath $taskSettingsPath) {
    Copy-Item -LiteralPath $taskSettingsPath -Destination (Join-Path $taskBackupDir 'appsettings.Development.json')
}
if (Test-Path -LiteralPath $taskFlagsPath) {
    Copy-Item -LiteralPath $taskFlagsPath -Destination (Join-Path $taskBackupDir 'firebase-config.local.json')
}
Write-LocalJson $taskSettingsPath $taskSettings
Write-LocalJson $taskFlagsPath ([ordered]@{
    FIREBASE_API_KEY = $taskApiKey
    FIREBASE_APP_ID = $taskAppId
    FIREBASE_PROJECT_ID = $taskProjectId
    FIREBASE_MESSAGING_SENDER_ID = $taskProjectNumber
    GOOGLE_SERVER_CLIENT_ID = $taskWebClientId
})

Write-Host "Configured Firebase project: $taskProjectId ($taskPackageId)"
Write-Host 'Updated ignored local backend settings and optional Android Dart defines.'
Write-Host 'Existing TMDB, Spotify, AI and Gmail settings were preserved unless explicitly updated.'
Write-Host 'Restart the backend and fully stop/run the Android app to apply native configuration.'
Write-Host 'No email, notification or Firebase project changes were sent.'
