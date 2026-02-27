param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("pr", "push", "manual")]
    [string]$Mode,

    [string]$Repository = $env:GITHUB_REPOSITORY,
    [string]$EventPath = $env:GITHUB_EVENT_PATH,
    [string]$Sha = $env:GITHUB_SHA,
    [string]$RefName = $env:GITHUB_REF_NAME,
    [string]$GitHubToken = $env:GITHUB_TOKEN,

    [ValidateSet("", "beta", "rc")]
    [string]$PrereleaseChannel = "",

    [ValidateSet("auto", "major", "minor", "patch")]
    [string]$BumpOverride = "auto",

    [string]$ProjectFilePath = "apps/server-plugin/src/Jellycheckr.Server/Jellycheckr.Server.csproj"
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = "Stop"

function Get-StatusCode {
    param([System.Management.Automation.ErrorRecord]$ErrorRecord)

    $response = $ErrorRecord.Exception.Response
    if ($null -eq $response) {
        return $null
    }

    if ($response -is [System.Net.Http.HttpResponseMessage]) {
        return [int]$response.StatusCode
    }

    return [int]$response.StatusCode.value__
}

function Invoke-GitHubApi {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("GET", "POST", "PATCH", "DELETE")]
        [string]$Method,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [object]$Body,

        [switch]$AllowNotFound
    )

    if ([string]::IsNullOrWhiteSpace($GitHubToken)) {
        throw "A GitHub token is required to call the GitHub API."
    }

    $uri = if ($Path.StartsWith("http", [System.StringComparison]::OrdinalIgnoreCase)) {
        $Path
    }
    else {
        "https://api.github.com$Path"
    }

    $headers = @{
        Authorization = "Bearer $GitHubToken"
        Accept = "application/vnd.github+json"
        "User-Agent" = "jellycheckr-release-automation"
        "X-GitHub-Api-Version" = "2022-11-28"
    }

    try {
        if ($PSBoundParameters.ContainsKey("Body")) {
            $jsonBody = $Body | ConvertTo-Json -Depth 10 -Compress
            return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ContentType "application/json" -Body $jsonBody
        }

        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
    }
    catch {
        $statusCode = Get-StatusCode -ErrorRecord $_
        if ($AllowNotFound -and $statusCode -eq 404) {
            return $null
        }

        throw
    }
}

function Get-HeadSha {
    if (-not [string]::IsNullOrWhiteSpace($Sha)) {
        return $Sha
    }

    return (git rev-parse HEAD).Trim()
}

function New-ManifestReleaseBody {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ManifestVersion,

        [Parameter(Mandatory = $true)]
        [string]$TargetAbi
    )

    $lines = @(
        '```jellycheckr-manifest'
        '{'
        ('  "version": "' + $ManifestVersion + '",')
        ('  "targetAbi": "' + $TargetAbi + '",')
        '  "dependencies": []'
        '}'
        '```'
    )

    return [string]::Join("`n", $lines)
}

function Get-ProjectDefaults {
    $defaults = [ordered]@{
        BaselineVersion = "0.1.0"
        TargetAbi = "10.9.11.0"
    }

    if (-not (Test-Path -LiteralPath $ProjectFilePath)) {
        return [pscustomobject]$defaults
    }

    [xml]$projectXml = Get-Content -LiteralPath $ProjectFilePath

    $propertyGroups = @($projectXml.Project.PropertyGroup)
    foreach ($propertyGroup in $propertyGroups) {
        $versionText = if ($propertyGroup.Version) { [string]$propertyGroup.Version.InnerText } else { "" }
        if (-not [string]::IsNullOrWhiteSpace($versionText)) {
            $defaults.BaselineVersion = $versionText
            break
        }
    }

    foreach ($propertyGroup in $propertyGroups) {
        $jellyfinVersionText = if ($propertyGroup.JellyfinPackageVersion) { [string]$propertyGroup.JellyfinPackageVersion.InnerText } else { "" }
        if (-not [string]::IsNullOrWhiteSpace($jellyfinVersionText)) {
            $defaults.TargetAbi = "{0}.0" -f $jellyfinVersionText
            break
        }
    }

    return [pscustomobject]$defaults
}

function Parse-ConventionalCommit {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Title,

        [string]$Body
    )

    $pattern = "^(?<type>feat|fix|perf|refactor|docs|chore|test|build|ci)(\([^)]+\))?(?<breaking>!)?: (?<description>.+)$"
    $match = [System.Text.RegularExpressions.Regex]::Match($Title, $pattern)

    if (-not $match.Success) {
        throw "Title '$Title' does not match the required Conventional Commit format."
    }

    $type = $match.Groups["type"].Value
    $isBreaking = $match.Groups["breaking"].Success -or ($Body -match "(?im)^BREAKING CHANGE:")

    $releaseType = switch ($type) {
        "feat" { "minor" }
        "fix" { "patch" }
        "perf" { "patch" }
        default { "none" }
    }

    if ($isBreaking) {
        $releaseType = "major"
    }

    return [pscustomobject]@{
        Type = $type
        IsBreaking = $isBreaking
        ReleaseType = $releaseType
        Description = $match.Groups["description"].Value
    }
}

function Resolve-ReleaseType {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ComputedReleaseType,

        [string[]]$Labels,

        [string]$ManualOverride
    )

    if ($Labels -contains "release:skip") {
        return "none"
    }

    foreach ($label in @("release:major", "release:minor", "release:patch")) {
        if ($Labels -contains $label) {
            return $label.Split(":")[1]
        }
    }

    if ($ManualOverride -ne "auto") {
        return $ManualOverride
    }

    return $ComputedReleaseType
}

function ConvertTo-SemVerObject {
    param([Parameter(Mandatory = $true)][string]$VersionText)

    if ($VersionText -notmatch "^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$") {
        throw "Version '$VersionText' is not a supported stable semantic version."
    }

    return [pscustomobject]@{
        Major = [int]$matches.major
        Minor = [int]$matches.minor
        Patch = [int]$matches.patch
        Text = $VersionText
    }
}

function Get-StableTags {
    $tagNames = @(git tag --list "v*")
    $stableTags = foreach ($tagName in $tagNames) {
        if ($tagName -match "^v(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$") {
            [pscustomobject]@{
                Name = $tagName
                Major = [int]$matches.major
                Minor = [int]$matches.minor
                Patch = [int]$matches.patch
                Version = "$($matches.major).$($matches.minor).$($matches.patch)"
            }
        }
    }

    return @($stableTags | Sort-Object Major, Minor, Patch)
}

function Get-LatestStableTag {
    $stableTags = @(Get-StableTags)
    if ($stableTags.Count -eq 0) {
        return $null
    }

    return $stableTags[-1]
}

function Get-NextStableVersion {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$BaseVersion,

        [Parameter(Mandatory = $true)]
        [ValidateSet("major", "minor", "patch")]
        [string]$ReleaseType
    )

    $major = $BaseVersion.Major
    $minor = $BaseVersion.Minor
    $patch = $BaseVersion.Patch

    switch ($ReleaseType) {
        "major" {
            $major += 1
            $minor = 0
            $patch = 0
        }
        "minor" {
            $minor += 1
            $patch = 0
        }
        "patch" {
            $patch += 1
        }
    }

    return "{0}.{1}.{2}" -f $major, $minor, $patch
}

function Get-ReleaseTargetSha {
    param(
        [pscustomobject]$EventPayload
    )

    if ($Mode -eq "pr" -and $null -ne $EventPayload -and $null -ne $EventPayload.pull_request.head.sha) {
        return [string]$EventPayload.pull_request.head.sha
    }

    return Get-HeadSha
}

function Resolve-AssociatedPullRequest {
    param(
        [pscustomobject]$EventPayload,
        [string]$ResolvedSha
    )

    if ($Mode -eq "pr") {
        return $EventPayload.pull_request
    }

    if ($Mode -eq "push") {
        if (-not [string]::IsNullOrWhiteSpace($Repository) -and -not [string]::IsNullOrWhiteSpace($GitHubToken)) {
            $pullRequests = Invoke-GitHubApi -Method GET -Path "/repos/$Repository/commits/$ResolvedSha/pulls" -AllowNotFound
            if ($null -ne $pullRequests -and @($pullRequests).Count -gt 0) {
                return @($pullRequests)[0]
            }
        }
    }

    return $null
}

function Resolve-TitleAndBody {
    param(
        [pscustomobject]$EventPayload,
        [pscustomobject]$AssociatedPullRequest,
        [string]$ResolvedSha
    )

    if ($null -ne $AssociatedPullRequest) {
        $labels = @()
        if ($AssociatedPullRequest.labels) {
            $labels = @($AssociatedPullRequest.labels | ForEach-Object { [string]$_.name })
        }

        return [pscustomobject]@{
            Title = [string]$AssociatedPullRequest.title
            Body = [string]$AssociatedPullRequest.body
            Labels = $labels
            PullRequestNumber = if ($AssociatedPullRequest.number) { [int]$AssociatedPullRequest.number } else { $null }
        }
    }

    if ($Mode -eq "pr") {
        throw "The pull_request payload is required for PR mode."
    }

    $subject = (git log -1 --format=%s $ResolvedSha).Trim()
    $body = (git log -1 --format=%b $ResolvedSha)

    return [pscustomobject]@{
        Title = $subject
        Body = $body
        Labels = @()
        PullRequestNumber = $null
    }
}

function Get-PrereleaseCounter {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StableVersion
    )

    $highest = 0
    $tagNames = @(git tag --list "v$StableVersion-*")

    foreach ($tagName in $tagNames) {
        if ($tagName -match "^v$([System.Text.RegularExpressions.Regex]::Escape($StableVersion))-(beta|rc)\.(?<counter>\d+)$") {
            $counter = [int]$matches.counter
            if ($counter -gt $highest) {
                $highest = $counter
            }
        }
    }

    return ($highest + 1)
}

function Resolve-ExistingTagTargetSha {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TagName
    )

    if ([string]::IsNullOrWhiteSpace($Repository) -or [string]::IsNullOrWhiteSpace($GitHubToken)) {
        return $null
    }

    $encodedTag = [System.Uri]::EscapeDataString($TagName)
    $tagRef = Invoke-GitHubApi -Method GET -Path "/repos/$Repository/git/ref/tags/$encodedTag" -AllowNotFound
    if ($null -eq $tagRef) {
        return $null
    }

    if ($tagRef.object.type -eq "commit") {
        return [string]$tagRef.object.sha
    }

    if ($tagRef.object.type -eq "tag") {
        $tagObject = Invoke-GitHubApi -Method GET -Path "/repos/$Repository/git/tags/$($tagRef.object.sha)"
        return [string]$tagObject.object.sha
    }

    throw "Unsupported tag reference object type '$($tagRef.object.type)' for tag '$TagName'."
}

$projectDefaults = Get-ProjectDefaults
$eventPayload = $null
if (-not [string]::IsNullOrWhiteSpace($EventPath) -and (Test-Path -LiteralPath $EventPath)) {
    $eventPayload = Get-Content -LiteralPath $EventPath -Raw | ConvertFrom-Json -Depth 20
}

$resolvedSha = Get-ReleaseTargetSha -EventPayload $eventPayload
$associatedPullRequest = Resolve-AssociatedPullRequest -EventPayload $eventPayload -ResolvedSha $resolvedSha
$resolvedMetadata = Resolve-TitleAndBody -EventPayload $eventPayload -AssociatedPullRequest $associatedPullRequest -ResolvedSha $resolvedSha
$parsedCommit = $null

try {
    $parsedCommit = Parse-ConventionalCommit -Title $resolvedMetadata.Title -Body $resolvedMetadata.Body
}
catch {
    if ($BumpOverride -eq "auto") {
        throw
    }

    $parsedCommit = [pscustomobject]@{
        Type = "manual"
        IsBreaking = $false
        ReleaseType = "none"
        Description = $resolvedMetadata.Title
    }
}

$labels = @($resolvedMetadata.Labels | Sort-Object -Unique)
$labelPrereleaseChannels = @($labels | Where-Object { $_ -in @("release:beta", "release:rc") })
if ($labelPrereleaseChannels.Count -gt 1) {
    throw "Conflicting prerelease labels detected: $($labelPrereleaseChannels -join ', ')."
}

$effectivePrereleaseChannel = $PrereleaseChannel
if ([string]::IsNullOrWhiteSpace($effectivePrereleaseChannel) -and $labelPrereleaseChannels.Count -eq 1) {
    $effectivePrereleaseChannel = $labelPrereleaseChannels[0].Split(":")[1]
}

$resolvedReleaseType = Resolve-ReleaseType -ComputedReleaseType $parsedCommit.ReleaseType -Labels $labels -ManualOverride $BumpOverride
$shouldRelease = $resolvedReleaseType -ne "none"

$latestStableTag = Get-LatestStableTag
$baseVersionText = if ($null -ne $latestStableTag) { $latestStableTag.Version } else { $projectDefaults.BaselineVersion }
$baseVersion = ConvertTo-SemVerObject -VersionText $baseVersionText

$stableVersion = $null
$stableTag = $null
$stableManifestVersion = $null
$stableAssemblyVersion = $null
$stableTagTargetSha = $null
$stableTagState = "not_applicable"

if ($shouldRelease) {
    $stableVersion = Get-NextStableVersion -BaseVersion $baseVersion -ReleaseType $resolvedReleaseType
    $stableTag = "v$stableVersion"
    $stableManifestVersion = $stableVersion
    $stableAssemblyVersion = "$stableVersion.0"
    $stableTagTargetSha = Resolve-ExistingTagTargetSha -TagName $stableTag

    if ($null -eq $stableTagTargetSha) {
        $stableTagState = "absent"
    }
    elseif ($stableTagTargetSha -eq $resolvedSha) {
        $stableTagState = "same-sha"
    }
    else {
        $stableTagState = "different-sha"
    }
}

$prereleaseVersion = $null
$prereleaseTag = $null
$prereleaseManifestVersion = $null
$prereleaseAssemblyVersion = $null
$prereleaseCounter = $null
$prereleaseTagTargetSha = $null
$prereleaseTagState = "not_applicable"
$canPublishPrerelease = $false

if ($shouldRelease -and -not [string]::IsNullOrWhiteSpace($effectivePrereleaseChannel)) {
    $prereleaseCounter = Get-PrereleaseCounter -StableVersion $stableVersion
    $prereleaseVersion = "$stableVersion-$effectivePrereleaseChannel.$prereleaseCounter"
    $prereleaseTag = "v$prereleaseVersion"
    $prereleaseManifestVersion = "$stableVersion.$prereleaseCounter"
    $prereleaseAssemblyVersion = $prereleaseManifestVersion
    $prereleaseTagTargetSha = Resolve-ExistingTagTargetSha -TagName $prereleaseTag

    if ($null -eq $prereleaseTagTargetSha) {
        $prereleaseTagState = "absent"
    }
    elseif ($prereleaseTagTargetSha -eq $resolvedSha) {
        $prereleaseTagState = "same-sha"
    }
    else {
        $prereleaseTagState = "different-sha"
    }

    $canPublishPrerelease = $true
}

$tagState = if ($canPublishPrerelease) { $prereleaseTagState } elseif ($shouldRelease) { $stableTagState } else { "not_applicable" }

$result = [ordered]@{
    shouldRelease = $shouldRelease
    releaseType = $resolvedReleaseType
    baseVersion = $baseVersion.Text
    stableVersion = $stableVersion
    stableTag = $stableTag
    stableManifestVersion = $stableManifestVersion
    stableAssemblyVersion = $stableAssemblyVersion
    stableReleaseTitle = if ($stableVersion) { "Jellycheckr $stableVersion" } else { $null }
    stableReleaseBody = if ($stableManifestVersion) { New-ManifestReleaseBody -ManifestVersion $stableManifestVersion -TargetAbi $projectDefaults.TargetAbi } else { $null }
    prereleaseChannel = if ([string]::IsNullOrWhiteSpace($effectivePrereleaseChannel)) { $null } else { $effectivePrereleaseChannel }
    prereleaseCounter = $prereleaseCounter
    prereleaseVersion = $prereleaseVersion
    prereleaseTag = $prereleaseTag
    prereleaseManifestVersion = $prereleaseManifestVersion
    prereleaseAssemblyVersion = $prereleaseAssemblyVersion
    prereleaseReleaseTitle = if ($prereleaseVersion) { "Jellycheckr $prereleaseVersion" } else { $null }
    prereleaseReleaseBody = if ($prereleaseManifestVersion) { New-ManifestReleaseBody -ManifestVersion $prereleaseManifestVersion -TargetAbi $projectDefaults.TargetAbi } else { $null }
    lastStableTag = if ($null -ne $latestStableTag) { $latestStableTag.Name } else { $null }
    associatedPrNumber = $resolvedMetadata.PullRequestNumber
    associatedPrTitle = $resolvedMetadata.Title
    resolvedLabels = $labels
    computedTargetAbi = $projectDefaults.TargetAbi
    canPublishPrerelease = $canPublishPrerelease
    stableTagState = $stableTagState
    prereleaseTagState = $prereleaseTagState
    tagState = $tagState
    targetSha = $resolvedSha
}

$result | ConvertTo-Json -Depth 10
