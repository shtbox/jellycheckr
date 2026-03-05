param(
    [ValidateSet("build", "up", "smoke", "down", "matrix")]
    [string]$Mode = "smoke",

    [string]$Version = "",
    [int]$Port = 58096,
    [string]$Username = "harness-admin",
    [string]$Password = "harness-password",
    [string]$Token = "",
    [string]$ArtifactsDir = "",
    [switch]$SkipBuild
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $PSCommandPath
$harnessRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot ".."))
$composeFile = Join-Path $harnessRoot "docker/compose.harness.yml"
$versionsFile = Join-Path $harnessRoot "versions.json"

if ([string]::IsNullOrWhiteSpace($ArtifactsDir)) {
    $ArtifactsDir = Join-Path $harnessRoot "artifacts"
}
New-Item -ItemType Directory -Force -Path $ArtifactsDir | Out-Null

$harnessLibraryName = "HarnessMedia"
$harnessMediaContainerDirectory = "/media/harness"
$harnessMediaContainerVideoPath = "/media/harness/test-video.mp4"
$harnessMediaSourcePath = Join-Path $harnessRoot "test-video.mp4"
$harnessDeviceId = "jellycheckr-harness"

function Read-VersionsConfig {
    if (-not (Test-Path -LiteralPath $versionsFile)) {
        throw "Harness versions config not found: $versionsFile"
    }

    $json = Get-Content -LiteralPath $versionsFile -Raw
    return $json | ConvertFrom-Json
}

$versionsConfig = Read-VersionsConfig
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = [string]$versionsConfig.defaultVersion
}

function Resolve-TargetFramework {
    param([Parameter(Mandatory = $true)][string]$JellyfinVersion)

    if ($JellyfinVersion -match '^10\.11\.') {
        return "net9.0"
    }

    if ($JellyfinVersion -match '^10\.9\.') {
        return "net8.0"
    }

    throw "Unsupported Jellyfin version '$JellyfinVersion'. Add a mapping to Resolve-TargetFramework in Invoke-Harness.ps1."
}

function Get-DeterministicHarnessServerId {
    param([Parameter(Mandatory = $true)][string]$JellyfinVersion)

    $seed = "jellycheckr-harness-$JellyfinVersion"
    $md5 = [System.Security.Cryptography.MD5]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($seed)
        $hash = $md5.ComputeHash($bytes)
        return -join ($hash | ForEach-Object { $_.ToString("x2") })
    }
    finally {
        $md5.Dispose()
    }
}

function Get-HarnessServerName {
    param([Parameter(Mandatory = $true)][string]$JellyfinVersion)

    $versionToken = $JellyfinVersion -replace '[^0-9A-Za-z]', '-'
    return "jellycheckr-harness-$versionToken"
}

function New-HarnessContext {
    param(
        [Parameter(Mandatory = $true)][string]$JellyfinVersion,
        [Parameter(Mandatory = $true)][int]$JellyfinPort
    )

    $projectSuffix = ($JellyfinVersion -replace '[^0-9A-Za-z]', '')
    $serverId = Get-DeterministicHarnessServerId -JellyfinVersion $JellyfinVersion
    $serverName = Get-HarnessServerName -JellyfinVersion $JellyfinVersion

    return [pscustomobject]@{
        Version = $JellyfinVersion
        TargetFramework = Resolve-TargetFramework -JellyfinVersion $JellyfinVersion
        ServerId = $serverId
        ServerName = $serverName
        Port = $JellyfinPort
        ProjectName = "jellycheckrharness$projectSuffix"
        BaseUrl = "http://127.0.0.1:$JellyfinPort"
    }
}

function Invoke-ComposeCommand {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [switch]$CaptureOutput
    )

    $envMap = @{
        "JELLYFIN_VERSION" = [string]$Context.Version
        "DOTNET_TARGET_FRAMEWORK" = [string]$Context.TargetFramework
        "HARNESS_SERVER_ID" = [string]$Context.ServerId
        "HARNESS_SERVER_NAME" = [string]$Context.ServerName
        "JELLYFIN_PORT" = [string]$Context.Port
        "COMPOSE_PROJECT_NAME" = [string]$Context.ProjectName
    }

    $oldValues = @{}
    foreach ($entry in $envMap.GetEnumerator()) {
        $oldValues[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key)
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
    }

    try {
        if ($CaptureOutput) {
            $output = & docker compose -f $composeFile @Arguments 2>&1 | Out-String
            if ($LASTEXITCODE -ne 0) {
                throw "docker compose failed ($($Arguments -join ' ')): $output"
            }

            return $output
        }

        & docker compose -f $composeFile @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "docker compose failed ($($Arguments -join ' '))."
        }
    }
    finally {
        foreach ($entry in $oldValues.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
        }
    }
}

function Wait-ForServerReady {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [int]$TimeoutSeconds = 180
    )

    $uri = "$($Context.BaseUrl)/System/Info/Public"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $uri -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                return
            }
        }
        catch {
        }

        Start-Sleep -Seconds 2
    }

    throw "Timed out waiting for Jellyfin readiness at $uri"
}

function Wait-ForEndpoint200 {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [hashtable]$Headers = @{},
        [int]$TimeoutSeconds = 120
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $Uri -Headers $Headers -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                return
            }
        }
        catch {
        }

        Start-Sleep -Seconds 2
    }

    throw "Endpoint did not become healthy (HTTP 200): $Uri"
}

function Invoke-WebRequestAllowError {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [ValidateSet("GET", "POST", "PUT", "DELETE", "PATCH")][string]$Method = "GET",
        [hashtable]$Headers = @{},
        [string]$Body = "",
        [string]$ContentType = "application/json",
        [int]$TimeoutSeconds = 15
    )

    try {
        $requestParams = @{
            UseBasicParsing = $true
            Method = $Method
            Uri = $Uri
            Headers = $Headers
            TimeoutSec = $TimeoutSeconds
        }

        if ($Method -ne "GET" -and -not [string]::IsNullOrWhiteSpace($Body)) {
            $requestParams.Body = $Body
            $requestParams.ContentType = $ContentType
        }

        $response = Invoke-WebRequest @requestParams
        return [pscustomobject]@{
            StatusCode = [int]$response.StatusCode
            Content = [string]$response.Content
            Headers = $response.Headers
        }
    }
    catch [System.Net.WebException] {
        $webResponse = $_.Exception.Response
        if ($null -eq $webResponse) {
            throw
        }

        $statusCode = [int]$webResponse.StatusCode
        $responseBody = ""
        $headers = @{}
        try {
            foreach ($headerName in $webResponse.Headers.AllKeys) {
                $headers[$headerName] = $webResponse.Headers[$headerName]
            }

            $stream = $webResponse.GetResponseStream()
            if ($null -ne $stream) {
                $reader = New-Object System.IO.StreamReader($stream)
                try {
                    $responseBody = $reader.ReadToEnd()
                }
                finally {
                    $reader.Dispose()
                }
            }
        }
        finally {
            $webResponse.Dispose()
        }

        return [pscustomobject]@{
            StatusCode = $statusCode
            Content = $responseBody
            Headers = $headers
        }
    }
}

function Assert-StatusCode {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][int]$Expected,
        [Parameter(Mandatory = $true)][int]$Actual
    )

    if ($Expected -ne $Actual) {
        throw "$Name expected HTTP $Expected but got HTTP $Actual."
    }
}

function New-EmbyAuthorizationHeader {
    return 'MediaBrowser Client="JellycheckrHarness", Device="HarnessRunner", DeviceId="jellycheckr-harness", Version="1.0.0"'
}

function Get-PublicServerInfo {
    param([Parameter(Mandatory = $true)]$Context)

    try {
        return Invoke-RestMethod -Method Get -Uri "$($Context.BaseUrl)/System/Info/Public" -TimeoutSec 10
    }
    catch {
        return $null
    }
}

function Get-PublicUsers {
    param([Parameter(Mandatory = $true)]$Context)

    try {
        $users = Invoke-RestMethod -Method Get -Uri "$($Context.BaseUrl)/Users/Public" -TimeoutSec 10
        if ($null -eq $users) {
            return @()
        }

        if ($users -is [System.Array]) {
            return $users
        }

        return @($users)
    }
    catch {
        return @()
    }
}

function Get-HarnessContainerId {
    param([Parameter(Mandatory = $true)]$Context)

    $output = Invoke-ComposeCommand -Context $Context -Arguments @("ps", "-q", "jellyfin-harness") -CaptureOutput
    $containerId = ($output -split "\r?\n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($containerId)) {
        throw "Unable to resolve harness container id from docker compose."
    }

    return [string]$containerId
}

function Copy-HarnessTestVideoToContainer {
    param([Parameter(Mandatory = $true)]$Context)

    if (-not (Test-Path -LiteralPath $harnessMediaSourcePath)) {
        throw "Harness test media file not found: $harnessMediaSourcePath"
    }

    $containerId = Get-HarnessContainerId -Context $Context
    & docker exec $containerId sh -lc "mkdir -p $harnessMediaContainerDirectory"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create harness media directory inside container."
    }

    & docker cp $harnessMediaSourcePath "${containerId}:$harnessMediaContainerVideoPath"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to copy harness test media into container."
    }

    & docker exec $containerId sh -lc "test -s $harnessMediaContainerVideoPath"
    if ($LASTEXITCODE -ne 0) {
        throw "Harness test media file is missing or empty after copy: $harnessMediaContainerVideoPath"
    }
}

function Ensure-HarnessLibrary {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][hashtable]$Headers
    )

    $encodedName = [System.Uri]::EscapeDataString($harnessLibraryName)
    $encodedPath = [System.Uri]::EscapeDataString($harnessMediaContainerDirectory)
    $createUri = "$($Context.BaseUrl)/Library/VirtualFolders?name=$encodedName&paths=$encodedPath&collectionType=movies&refreshLibrary=true"
    $createResponse = Invoke-WebRequestAllowError -Method "POST" -Uri $createUri -Headers $Headers -TimeoutSeconds 20
    if ($createResponse.StatusCode -notin @(200, 204, 400, 409)) {
        throw "Failed to create harness media library. HTTP $($createResponse.StatusCode): $($createResponse.Content)"
    }

    $virtualFolders = Invoke-RestMethod -Method Get -Uri "$($Context.BaseUrl)/Library/VirtualFolders" -Headers $Headers -TimeoutSec 15
    $folders = if ($virtualFolders -is [System.Array]) { $virtualFolders } else { @($virtualFolders) }
    $library = $folders | Where-Object {
        [string]::Equals([string]$_.Name, $harnessLibraryName, [System.StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1

    if ($null -eq $library -or [string]::IsNullOrWhiteSpace([string]$library.ItemId)) {
        throw "Harness media library '$harnessLibraryName' was not discoverable after create call."
    }

    return $library
}

function Wait-ForSessionByDeviceId {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][hashtable]$Headers,
        [Parameter(Mandatory = $true)][string]$DeviceId,
        [int]$TimeoutSeconds = 45
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $sessionsResponse = Invoke-RestMethod -Method Get -Uri "$($Context.BaseUrl)/Sessions" -Headers $Headers -TimeoutSec 15
        $sessions = if ($sessionsResponse -is [System.Array]) { $sessionsResponse } else { @($sessionsResponse) }
        $session = $sessions | Where-Object {
            [string]::Equals([string]$_.DeviceId, $DeviceId, [System.StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1

        if ($null -ne $session) {
            return $session
        }

        Start-Sleep -Seconds 2
    }

    throw "Timed out waiting for active session for device '$DeviceId'."
}

function Wait-ForHarnessMediaItem {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][hashtable]$Headers,
        [Parameter(Mandatory = $true)][string]$UserId,
        [Parameter(Mandatory = $true)][string]$LibraryItemId,
        [int]$TimeoutSeconds = 120
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $encodedLibraryId = [System.Uri]::EscapeDataString($LibraryItemId)
    $itemsUri = "$($Context.BaseUrl)/Users/$UserId/Items?Recursive=true&ParentId=$encodedLibraryId&IncludeItemTypes=Movie,Video&Fields=Path&Limit=100"

    while ((Get-Date) -lt $deadline) {
        $response = Invoke-RestMethod -Method Get -Uri $itemsUri -Headers $Headers -TimeoutSec 15
        $items = @($response.Items)

        $directMatch = $items | Where-Object {
            -not [string]::IsNullOrWhiteSpace([string]$_.Path) -and ([string]$_.Path).EndsWith("/test-video.mp4", [System.StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1

        if ($null -ne $directMatch) {
            return $directMatch
        }

        if ($items.Count -eq 1 -and -not [string]::IsNullOrWhiteSpace([string]$items[0].Id)) {
            return $items[0]
        }

        Start-Sleep -Seconds 2
    }

    throw "Timed out waiting for harness media item under library '$harnessLibraryName'."
}

function Set-HarnessDeveloperFallbackConfig {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][hashtable]$Headers
    )

    $adminConfigUri = "$($Context.BaseUrl)/Plugins/Aysw/admin/config"
    $currentConfig = Invoke-RestMethod -Method Get -Uri $adminConfigUri -Headers $Headers -TimeoutSec 15
    $currentConfig.Enabled = $true
    $currentConfig.EnableServerFallback = $true
    $currentConfig.DeveloperMode = $true
    $currentConfig.DeveloperPromptAfterSeconds = 15
    $currentConfig.ServerFallbackDryRun = $true
    $currentConfig.MinimumLogLevel = "Information"

    $body = $currentConfig | ConvertTo-Json -Depth 20
    $updated = Invoke-RestMethod -Method Put -Uri $adminConfigUri -Headers $Headers -Body $body -ContentType "application/json" -TimeoutSec 20
    return $updated
}

function Wait-ForDeveloperFallbackTriggerLog {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][string]$SessionId,
        [Parameter(Mandatory = $true)][DateTimeOffset]$SinceUtc,
        [int]$TimeoutSeconds = 60
    )

    $containerId = Get-HarnessContainerId -Context $Context
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $sessionSearch = "session=$SessionId"
    $sinceArgument = $SinceUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

    while ((Get-Date) -lt $deadline) {
        $logs = & docker logs $containerId --since $sinceArgument 2>&1 | Out-String
        $line = ($logs -split "\r?\n" | Where-Object {
            $_.Contains("Server fallback trigger") `
            -and $_.Contains($sessionSearch) `
            -and $_.Contains("developer_mode_after_15s")
        } | Select-Object -First 1)

        if (-not [string]::IsNullOrWhiteSpace($line)) {
            return [string]$line
        }

        Start-Sleep -Seconds 5
    }

    throw "Did not observe developer fallback trigger log for session '$SessionId' within $TimeoutSeconds seconds."
}

function Set-HarnessStartupWizardIncomplete {
    param([Parameter(Mandatory = $true)]$Context)

    $containerId = Get-HarnessContainerId -Context $Context
    & docker exec $containerId sh -lc "if [ -f /config/config/system.xml ]; then sed -i 's#<IsStartupWizardCompleted>true</IsStartupWizardCompleted>#<IsStartupWizardCompleted>false</IsStartupWizardCompleted>#' /config/config/system.xml; fi"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to patch /config/config/system.xml inside harness container."
    }

    Invoke-ComposeCommand -Context $Context -Arguments @("restart", "jellyfin-harness")
    Wait-ForServerReady -Context $Context
}

function Try-AuthenticateByName {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][string]$LoginUser,
        [Parameter(Mandatory = $true)][string]$LoginPassword
    )

    $headers = @{ "X-Emby-Authorization" = New-EmbyAuthorizationHeader }
    $body = @{
        Username = $LoginUser
        Pw = $LoginPassword
    } | ConvertTo-Json

    try {
        $response = Invoke-RestMethod -Method Post -Uri "$($Context.BaseUrl)/Users/AuthenticateByName" -Headers $headers -Body $body -ContentType "application/json" -TimeoutSec 15
        if ($response.AccessToken) {
            return [string]$response.AccessToken
        }
    }
    catch {
    }

    return $null
}

function Try-BootstrapStartupCredentials {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][string]$LoginUser,
        [Parameter(Mandatory = $true)][string]$LoginPassword
    )

    $headers = @{ "X-Emby-Authorization" = New-EmbyAuthorizationHeader }
    $startupBody = @{
        Name = $LoginUser
        Password = $LoginPassword
    } | ConvertTo-Json

    try {
        Invoke-WebRequest -UseBasicParsing -Method Get -Uri "$($Context.BaseUrl)/Startup/User" -Headers $headers -TimeoutSec 15 | Out-Null
    }
    catch {
        try {
            Invoke-WebRequest -UseBasicParsing -Method Get -Uri "$($Context.BaseUrl)/Startup/FirstUser" -Headers $headers -TimeoutSec 15 | Out-Null
        }
        catch {
        }
    }

    try {
        Invoke-WebRequest -UseBasicParsing -Method Post -Uri "$($Context.BaseUrl)/Startup/User" -Headers $headers -Body $startupBody -ContentType "application/json" -TimeoutSec 15 | Out-Null
    }
    catch {
    }

    try {
        Invoke-WebRequest -UseBasicParsing -Method Post -Uri "$($Context.BaseUrl)/Startup/Complete" -Headers $headers -TimeoutSec 15 | Out-Null
    }
    catch {
    }

    return Try-AuthenticateByName -Context $Context -LoginUser $LoginUser -LoginPassword $LoginPassword
}

function Ensure-AuthToken {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [string]$ExplicitToken,
        [Parameter(Mandatory = $true)][string]$LoginUser,
        [Parameter(Mandatory = $true)][string]$LoginPassword
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitToken)) {
        return $ExplicitToken
    }

    $forcedStartupWizard = $false
    $lastStartupCompleted = $false
    $lastPublicUserCount = 0
    $deadline = (Get-Date).AddSeconds(180)

    while ((Get-Date) -lt $deadline) {
        $token = Try-BootstrapStartupCredentials -Context $Context -LoginUser $LoginUser -LoginPassword $LoginPassword
        if (-not [string]::IsNullOrWhiteSpace($token)) {
            return $token
        }

        $publicInfo = Get-PublicServerInfo -Context $Context
        $publicUsers = Get-PublicUsers -Context $Context
        $lastPublicUserCount = @($publicUsers).Count
        $lastStartupCompleted = $false
        if ($null -ne $publicInfo -and ($publicInfo.PSObject.Properties.Name -contains "StartupWizardCompleted")) {
            $lastStartupCompleted = [bool]$publicInfo.StartupWizardCompleted
        }

        if (-not $forcedStartupWizard -and $lastStartupCompleted -and $lastPublicUserCount -eq 0) {
            Write-Host "[harness] Jellyfin reports startup complete with no users; forcing startup wizard mode for auth bootstrap..."
            Set-HarnessStartupWizardIncomplete -Context $Context
            $forcedStartupWizard = $true
            continue
        }

        Start-Sleep -Seconds 2
    }

    throw "Unable to obtain a Jellyfin auth token. Provide -Token or validate startup user credentials ($LoginUser). StartupWizardCompleted=$lastStartupCompleted PublicUserCount=$lastPublicUserCount"
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $Value | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function New-RunArtifactsDirectory {
    param([Parameter(Mandatory = $true)]$Context)

    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $path = Join-Path $ArtifactsDir ("harness-{0}-{1}" -f $Context.Version, $stamp)
    New-Item -ItemType Directory -Force -Path $path | Out-Null
    return $path
}

function Start-Harness {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [switch]$NoBuild
    )

    if (-not $NoBuild) {
        Write-Host "[harness] Building image for Jellyfin $($Context.Version) (target $($Context.TargetFramework))..."
        Invoke-ComposeCommand -Context $Context -Arguments @("build")
    }

    Write-Host "[harness] Starting Jellyfin harness container on port $($Context.Port)..."
    Invoke-ComposeCommand -Context $Context -Arguments @("up", "-d", "--force-recreate", "--remove-orphans", "--renew-anon-volumes")
}

function Stop-Harness {
    param([Parameter(Mandatory = $true)]$Context)

    Write-Host "[harness] Stopping harness for Jellyfin $($Context.Version)..."
    Invoke-ComposeCommand -Context $Context -Arguments @("down", "--remove-orphans", "--volumes")
}

function Capture-HarnessLogs {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][string]$RunArtifactsDirectory
    )

    $logsPath = Join-Path $RunArtifactsDirectory "docker-compose.log"
    try {
        $logs = Invoke-ComposeCommand -Context $Context -Arguments @("logs", "--no-color") -CaptureOutput
        Set-Content -LiteralPath $logsPath -Value $logs -Encoding UTF8
    }
    catch {
        Set-Content -LiteralPath $logsPath -Value "Failed to capture logs: $($_.Exception.Message)" -Encoding UTF8
    }
}

function Invoke-DeveloperFallbackPlaybackCheck {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][hashtable]$Headers,
        [Parameter(Mandatory = $true)][string]$RunArtifactsDirectory
    )

    Write-Host "[harness] Copying harness test media into Jellyfin container"
    Copy-HarnessTestVideoToContainer -Context $Context

    Write-Host "[harness] Ensuring harness media library exists"
    $library = Ensure-HarnessLibrary -Context $Context -Headers $Headers

    Write-Host "[harness] Enabling developer fallback mode for playback check"
    $effectiveAdminConfig = Set-HarnessDeveloperFallbackConfig -Context $Context -Headers $Headers

    $session = Wait-ForSessionByDeviceId -Context $Context -Headers $Headers -DeviceId $harnessDeviceId
    if ([string]::IsNullOrWhiteSpace([string]$session.UserId)) {
        throw "Harness session user id is missing; cannot resolve media item."
    }

    Write-Host "[harness] Waiting for harness media item indexing"
    $item = Wait-ForHarnessMediaItem -Context $Context -Headers $Headers -UserId ([string]$session.UserId) -LibraryItemId ([string]$library.ItemId)

    $playbackStartedUtc = (Get-Date).ToUniversalTime()
    $playSessionId = "harness-developer-fallback"
    $playingBody = @{
        ItemId = [string]$item.Id
        MediaSourceId = [string]$item.Id
        PlaySessionId = $playSessionId
        PlayMethod = "DirectPlay"
        CanSeek = $true
        PositionTicks = 0
        IsPaused = $false
    } | ConvertTo-Json -Compress

    $playingResponse = Invoke-WebRequestAllowError -Method "POST" -Uri "$($Context.BaseUrl)/Sessions/Playing" -Headers $Headers -Body $playingBody -ContentType "application/json" -TimeoutSeconds 15
    if ($playingResponse.StatusCode -notin @(200, 204)) {
        throw "Failed to publish /Sessions/Playing for harness media item. HTTP $($playingResponse.StatusCode): $($playingResponse.Content)"
    }

    $progressPositions = @(60000000, 120000000, 180000000, 240000000)
    foreach ($positionTicks in $progressPositions) {
        $progressBody = @{
            ItemId = [string]$item.Id
            MediaSourceId = [string]$item.Id
            PlaySessionId = $playSessionId
            PlayMethod = "DirectPlay"
            CanSeek = $true
            PositionTicks = [long]$positionTicks
            IsPaused = $false
            EventName = "TimeUpdate"
        } | ConvertTo-Json -Compress

        $progressResponse = Invoke-WebRequestAllowError -Method "POST" -Uri "$($Context.BaseUrl)/Sessions/Playing/Progress" -Headers $Headers -Body $progressBody -ContentType "application/json" -TimeoutSeconds 15
        if ($progressResponse.StatusCode -notin @(200, 204)) {
            throw "Failed to publish /Sessions/Playing/Progress for harness media item. HTTP $($progressResponse.StatusCode): $($progressResponse.Content)"
        }

        Start-Sleep -Seconds 6
    }

    Write-Host "[harness] Waiting for developer fallback trigger log marker"
    $triggerLogLine = Wait-ForDeveloperFallbackTriggerLog -Context $Context -SessionId ([string]$session.Id) -SinceUtc $playbackStartedUtc -TimeoutSeconds 75

    $stoppedBody = @{
        ItemId = [string]$item.Id
        MediaSourceId = [string]$item.Id
        PlaySessionId = $playSessionId
        PositionTicks = [long]240000000
    } | ConvertTo-Json -Compress
    $null = Invoke-WebRequestAllowError -Method "POST" -Uri "$($Context.BaseUrl)/Sessions/Playing/Stopped" -Headers $Headers -Body $stoppedBody -ContentType "application/json" -TimeoutSeconds 15

    $result = [ordered]@{
        developerMode = [bool]$effectiveAdminConfig.DeveloperMode
        developerPromptAfterSeconds = [int]$effectiveAdminConfig.DeveloperPromptAfterSeconds
        serverFallbackDryRun = [bool]$effectiveAdminConfig.ServerFallbackDryRun
        minimumLogLevel = [string]$effectiveAdminConfig.MinimumLogLevel
        libraryId = [string]$library.ItemId
        mediaItemId = [string]$item.Id
        sessionId = [string]$session.Id
        triggerReason = "developer_mode_after_15s"
        triggerLogLine = $triggerLogLine
    }

    Write-JsonFile -Value $result -Path (Join-Path $RunArtifactsDirectory "developer-fallback-check.json")
    return $result
}

function Invoke-WebUiInjectionCheck {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][string]$RunArtifactsDirectory
    )

    $containerId = Get-HarnessContainerId -Context $Context
    $logs = & docker logs $containerId --since 10m 2>&1 | Out-String
    $logLines = $logs -split "\r?\n"

    $registeredLine = $logLines | Where-Object {
        $_.Contains("Registered Jellyfin Web index.html transformation for web client injection.")
    } | Select-Object -First 1

    $notDetectedLine = $logLines | Where-Object {
        $_.Contains("File Transformation plugin not detected")
    } | Select-Object -First 1

    $registrationFailureLine = $logLines | Where-Object {
        $_.Contains("Failed to register File Transformation callback for web UI injection")
    } | Select-Object -First 1

    $requiresFileTransformation = $Context.Version -match '^10\.11\.'

    if ($requiresFileTransformation -and [string]::IsNullOrWhiteSpace([string]$registeredLine)) {
        $details = if (-not [string]::IsNullOrWhiteSpace([string]$registrationFailureLine)) {
            [string]$registrationFailureLine
        }
        elseif (-not [string]::IsNullOrWhiteSpace([string]$notDetectedLine)) {
            [string]$notDetectedLine
        }
        else {
            "No registration success line was present in container logs."
        }

        throw "Web UI injection did not register on Jellyfin $($Context.Version). Details: $details"
    }

    $status = if (-not [string]::IsNullOrWhiteSpace([string]$registeredLine)) {
        "registered"
    }
    elseif ($requiresFileTransformation) {
        "missing"
    }
    else {
        "skipped_unsupported_jellyfin_version"
    }

    $result = [ordered]@{
        expected = $requiresFileTransformation
        status = $status
        registeredLogLine = [string]$registeredLine
        notDetectedLogLine = [string]$notDetectedLine
        failureLogLine = [string]$registrationFailureLine
    }

    Write-JsonFile -Value $result -Path (Join-Path $RunArtifactsDirectory "web-ui-injection-check.json")
    return $result
}

function Invoke-SmokeChecks {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][string]$RunArtifactsDirectory
    )

    Write-Host "[harness] Waiting for Jellyfin readiness..."
    Wait-ForServerReady -Context $Context

    $authToken = Ensure-AuthToken -Context $Context -ExplicitToken $Token -LoginUser $Username -LoginPassword $Password
    $authHeaders = @{
        "X-Emby-Token" = $authToken
        "X-Emby-Authorization" = New-EmbyAuthorizationHeader
    }

    $assetEndpoints = @(
        "/Plugins/Aysw/web/jellycheckr-web.js",
        "/Plugins/Aysw/web/jellycheckr-config-ui.js",
        "/Plugins/Aysw/web/jellycheckr-config-ui.css",
        "/Plugins/Aysw/web/jellycheckr-config-ui-host.html"
    )

    $assetHeaderChecks = [System.Collections.Generic.List[object]]::new()
    foreach ($endpoint in $assetEndpoints) {
        $uri = "$($Context.BaseUrl)$endpoint"
        Write-Host "[harness] Checking asset endpoint $endpoint"
        Wait-ForEndpoint200 -Uri $uri
        $assetResponse = Invoke-WebRequestAllowError -Uri $uri -Method "GET" -TimeoutSeconds 15
        Assert-StatusCode -Name "Asset endpoint $endpoint" -Expected 200 -Actual $assetResponse.StatusCode
        $nosniffHeader = [string]$assetResponse.Headers["X-Content-Type-Options"]
        if (-not [string]::Equals($nosniffHeader, "nosniff", [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Asset endpoint $endpoint did not return X-Content-Type-Options=nosniff."
        }

        $assetHeaderChecks.Add([ordered]@{
            endpoint = $endpoint
            status = $assetResponse.StatusCode
            xContentTypeOptions = $nosniffHeader
        }) | Out-Null
    }

    $configUri = "$($Context.BaseUrl)/Plugins/Aysw/config"
    Write-Host "[harness] Checking authenticated config endpoint"
    Wait-ForEndpoint200 -Uri $configUri -Headers $authHeaders
    $configResponse = Invoke-RestMethod -Method Get -Uri $configUri -Headers $authHeaders -TimeoutSec 15
    if (-not ($configResponse.PSObject.Properties.Name -contains "enabled")) {
        throw "Config response did not contain 'enabled'."
    }
    Write-JsonFile -Value $configResponse -Path (Join-Path $RunArtifactsDirectory "config-response.json")

    $registerUri = "$($Context.BaseUrl)/Plugins/Aysw/web-client/register"
    Write-Host "[harness] Checking authenticated register endpoint"
    $registerBody = @{ deviceId = "jellycheckr-harness-device" } | ConvertTo-Json
    $registerResponse = Invoke-RestMethod -Method Post -Uri $registerUri -Headers $authHeaders -Body $registerBody -ContentType "application/json" -TimeoutSec 15
    if (-not ($registerResponse.PSObject.Properties.Name -contains "registered")) {
        throw "Register response did not contain 'registered'."
    }

    if ([bool]$registerResponse.registered -ne $false) {
        throw "Expected register response to be unresolved (registered=false) when no active playback session exists."
    }

    if ([string]$registerResponse.reason -ne "session_unresolved") {
        throw "Expected register reason 'session_unresolved' but got '$($registerResponse.reason)'."
    }

    Write-JsonFile -Value $registerResponse -Path (Join-Path $RunArtifactsDirectory "register-response.json")

    $heartbeatUri = "$($Context.BaseUrl)/Plugins/Aysw/web-client/heartbeat"
    Write-Host "[harness] Checking authenticated heartbeat endpoint"
    $heartbeatBody = @{
        deviceId = "jellycheckr-harness-device"
        sessionId = "harness-missing-session"
    } | ConvertTo-Json
    $heartbeatResponse = Invoke-RestMethod -Method Post -Uri $heartbeatUri -Headers $authHeaders -Body $heartbeatBody -ContentType "application/json" -TimeoutSec 15
    if (-not ($heartbeatResponse.PSObject.Properties.Name -contains "accepted")) {
        throw "Heartbeat response did not contain 'accepted'."
    }

    if ([bool]$heartbeatResponse.accepted -ne $false) {
        throw "Expected heartbeat response to be unresolved (accepted=false) when no active playback session exists."
    }

    if ([string]$heartbeatResponse.reason -ne "session_unresolved") {
        throw "Expected heartbeat reason 'session_unresolved' but got '$($heartbeatResponse.reason)'."
    }

    Write-JsonFile -Value $heartbeatResponse -Path (Join-Path $RunArtifactsDirectory "heartbeat-response.json")

    $ownershipChecks = @(
        [ordered]@{
            name = "ack-owner-check"
            method = "POST"
            endpoint = "/Plugins/Aysw/sessions/harness-foreign-session/ack"
            body = (@{ ackType = "continue"; clientType = "web" } | ConvertTo-Json -Compress)
            expectedStatus = 403
        },
        [ordered]@{
            name = "interaction-owner-check"
            method = "POST"
            endpoint = "/Plugins/Aysw/sessions/harness-foreign-session/interaction"
            body = (@{ eventType = "keydown"; clientType = "web" } | ConvertTo-Json -Compress)
            expectedStatus = 403
        },
        [ordered]@{
            name = "prompt-owner-check"
            method = "POST"
            endpoint = "/Plugins/Aysw/sessions/harness-foreign-session/prompt-shown"
            body = (@{ timeoutSeconds = 30; clientType = "web" } | ConvertTo-Json -Compress)
            expectedStatus = 403
        },
        [ordered]@{
            name = "unregister-owner-check"
            method = "POST"
            endpoint = "/Plugins/Aysw/web-client/unregister"
            body = (@{ sessionId = "harness-foreign-session" } | ConvertTo-Json -Compress)
            expectedStatus = 403
        }
    )

    $securityResults = [System.Collections.Generic.List[object]]::new()
    foreach ($check in $ownershipChecks) {
        $uri = "$($Context.BaseUrl)$($check.endpoint)"
        Write-Host "[harness] Security check $($check.name) expects HTTP $($check.expectedStatus)"
        $response = Invoke-WebRequestAllowError -Method $check.method -Uri $uri -Headers $authHeaders -Body $check.body -ContentType "application/json" -TimeoutSeconds 15
        Assert-StatusCode -Name $check.name -Expected ([int]$check.expectedStatus) -Actual $response.StatusCode
        $securityResults.Add([ordered]@{
            name = $check.name
            endpoint = $check.endpoint
            status = $response.StatusCode
        }) | Out-Null
    }
    Write-JsonFile -Value $securityResults -Path (Join-Path $RunArtifactsDirectory "security-checks.json")

    $developerFallbackCheck = Invoke-DeveloperFallbackPlaybackCheck -Context $Context -Headers $authHeaders -RunArtifactsDirectory $RunArtifactsDirectory
    $webUiInjectionCheck = Invoke-WebUiInjectionCheck -Context $Context -RunArtifactsDirectory $RunArtifactsDirectory

    $summary = [ordered]@{
        version = $Context.Version
        targetFramework = $Context.TargetFramework
        baseUrl = $Context.BaseUrl
        checkedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        configEndpoint = $configUri
        registerEndpoint = $registerUri
        registerReason = $registerResponse.reason
        heartbeatEndpoint = $heartbeatUri
        heartbeatReason = $heartbeatResponse.reason
        assetHeaderChecks = $assetHeaderChecks
        securityChecks = $securityResults
        developerFallbackCheck = $developerFallbackCheck
        webUiInjectionCheck = $webUiInjectionCheck
    }

    Write-JsonFile -Value $summary -Path (Join-Path $RunArtifactsDirectory "smoke-summary.json")
    Write-Host "[harness] Smoke checks passed for Jellyfin $($Context.Version)."
}

switch ($Mode) {
    "build" {
        $context = New-HarnessContext -JellyfinVersion $Version -JellyfinPort $Port
        Write-Host "[harness] Building image for Jellyfin $($context.Version) (target $($context.TargetFramework))..."
        Invoke-ComposeCommand -Context $context -Arguments @("build")
    }
    "up" {
        $context = New-HarnessContext -JellyfinVersion $Version -JellyfinPort $Port
        Start-Harness -Context $context -NoBuild:$SkipBuild
    }
    "down" {
        $context = New-HarnessContext -JellyfinVersion $Version -JellyfinPort $Port
        Stop-Harness -Context $context
    }
    "smoke" {
        $context = New-HarnessContext -JellyfinVersion $Version -JellyfinPort $Port
        $runArtifactsDirectory = New-RunArtifactsDirectory -Context $context

        try {
            Start-Harness -Context $context -NoBuild:$SkipBuild
            Invoke-SmokeChecks -Context $context -RunArtifactsDirectory $runArtifactsDirectory
        }
        finally {
            Capture-HarnessLogs -Context $context -RunArtifactsDirectory $runArtifactsDirectory
        }

        Write-Host "[harness] Smoke artifacts: $runArtifactsDirectory"
    }
    "matrix" {
        if (-not $versionsConfig.versions) {
            throw "No versions were listed in $versionsFile"
        }

        $matrixResults = [System.Collections.Generic.List[object]]::new()
        foreach ($matrixVersion in $versionsConfig.versions) {
            $context = New-HarnessContext -JellyfinVersion ([string]$matrixVersion) -JellyfinPort $Port
            $runArtifactsDirectory = New-RunArtifactsDirectory -Context $context
            $failure = $null

            try {
                Start-Harness -Context $context -NoBuild:$false
                Invoke-SmokeChecks -Context $context -RunArtifactsDirectory $runArtifactsDirectory
            }
            catch {
                $failure = $_.Exception.Message
            }
            finally {
                Capture-HarnessLogs -Context $context -RunArtifactsDirectory $runArtifactsDirectory
                try {
                    Stop-Harness -Context $context
                }
                catch {
                }
            }

            $matrixResults.Add([pscustomobject]@{
                Version = $context.Version
                TargetFramework = $context.TargetFramework
                Passed = [string]::IsNullOrWhiteSpace($failure)
                Details = if ([string]::IsNullOrWhiteSpace($failure)) { "ok" } else { $failure }
                Artifacts = $runArtifactsDirectory
            })
        }

        $matrixResults | Format-Table -AutoSize | Out-String | Write-Host

        if ($matrixResults | Where-Object { -not $_.Passed }) {
            throw "One or more harness matrix runs failed. See matrix output and artifacts in $ArtifactsDir"
        }
    }
}
