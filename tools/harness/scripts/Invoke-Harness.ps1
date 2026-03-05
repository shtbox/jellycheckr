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

function New-HarnessContext {
    param(
        [Parameter(Mandatory = $true)][string]$JellyfinVersion,
        [Parameter(Mandatory = $true)][int]$JellyfinPort
    )

    $projectSuffix = ($JellyfinVersion -replace '[^0-9A-Za-z]', '')
    return [pscustomobject]@{
        Version = $JellyfinVersion
        TargetFramework = Resolve-TargetFramework -JellyfinVersion $JellyfinVersion
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

    foreach ($endpoint in $assetEndpoints) {
        $uri = "$($Context.BaseUrl)$endpoint"
        Write-Host "[harness] Checking asset endpoint $endpoint"
        Wait-ForEndpoint200 -Uri $uri
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

    $summary = [ordered]@{
        version = $Context.Version
        targetFramework = $Context.TargetFramework
        baseUrl = $Context.BaseUrl
        checkedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        configEndpoint = $configUri
        registerEndpoint = $registerUri
        registerReason = $registerResponse.reason
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
