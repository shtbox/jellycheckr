param(
    [Parameter(Mandatory = $true)]
    [string]$Repository,

    [Parameter(Mandatory = $true)]
    [string]$GitHubToken,

    [Parameter(Mandatory = $true)]
    [string]$TagName,

    [Parameter(Mandatory = $true)]
    [string]$TargetSha,

    [Parameter(Mandatory = $true)]
    [string]$ReleaseTitle,

    [Parameter(Mandatory = $true)]
    [string]$ManifestVersion,

    [Parameter(Mandatory = $true)]
    [string]$TargetAbi,

    [Parameter(Mandatory = $true)]
    [string]$ZipAssetPath,

    [Parameter(Mandatory = $true)]
    [string]$ChangelogAssetPath,

    [switch]$Prerelease
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

function Invoke-GitHubJsonApi {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("GET", "POST", "PATCH", "DELETE")]
        [string]$Method,

        [Parameter(Mandatory = $true)]
        [string]$Uri,

        [object]$Body,

        [switch]$AllowNotFound
    )

    $headers = @{
        Authorization = "Bearer $GitHubToken"
        Accept = "application/vnd.github+json"
        "User-Agent" = "jellycheckr-release-automation"
        "X-GitHub-Api-Version" = "2022-11-28"
    }

    try {
        if ($PSBoundParameters.ContainsKey("Body")) {
            $jsonBody = $Body | ConvertTo-Json -Depth 10 -Compress
            return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -ContentType "application/json" -Body $jsonBody
        }

        return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers
    }
    catch {
        $statusCode = Get-StatusCode -ErrorRecord $_
        if ($AllowNotFound -and $statusCode -eq 404) {
            return $null
        }

        throw
    }
}

function Invoke-GitHubAssetUpload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri,

        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string]$ContentType
    )

    $headers = @{
        Authorization = "Bearer $GitHubToken"
        Accept = "application/vnd.github+json"
        "User-Agent" = "jellycheckr-release-automation"
        "X-GitHub-Api-Version" = "2022-11-28"
    }

    return Invoke-RestMethod -Method POST -Uri $Uri -Headers $headers -ContentType $ContentType -InFile $FilePath
}

function New-ManifestReleaseBody {
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

function New-ChecksumSidecarFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath
    )

    $fileItem = Get-Item -LiteralPath $FilePath
    $hash = (Get-FileHash -LiteralPath $fileItem.FullName -Algorithm MD5).Hash.ToLowerInvariant()
    $sidecarPath = Join-Path ([System.IO.Path]::GetTempPath()) ($fileItem.Name + ".md5")
    $sidecarContent = "{0} *{1}" -f $hash, $fileItem.Name
    Set-Content -LiteralPath $sidecarPath -Value $sidecarContent -NoNewline

    return $sidecarPath
}

function Resolve-ExistingTagTargetSha {
    $encodedTag = [System.Uri]::EscapeDataString($TagName)
    $tagRef = Invoke-GitHubJsonApi -Method GET -Uri "https://api.github.com/repos/$Repository/git/ref/tags/$encodedTag" -AllowNotFound
    if ($null -eq $tagRef) {
        return $null
    }

    if ($tagRef.object.type -eq "commit") {
        return [string]$tagRef.object.sha
    }

    if ($tagRef.object.type -eq "tag") {
        $tagObject = Invoke-GitHubJsonApi -Method GET -Uri "https://api.github.com/repos/$Repository/git/tags/$($tagRef.object.sha)"
        return [string]$tagObject.object.sha
    }

    throw "Unsupported tag reference object type '$($tagRef.object.type)' for tag '$TagName'."
}

function Ensure-Tag {
    $existingTargetSha = Resolve-ExistingTagTargetSha
    if ($null -ne $existingTargetSha) {
        if ($existingTargetSha -ne $TargetSha) {
            throw "Tag '$TagName' already exists but points to '$existingTargetSha' instead of '$TargetSha'."
        }

        return
    }

    $tagObject = Invoke-GitHubJsonApi -Method POST -Uri "https://api.github.com/repos/$Repository/git/tags" -Body @{
        tag = $TagName
        message = $ReleaseTitle
        object = $TargetSha
        type = "commit"
    }

    try {
        Invoke-GitHubJsonApi -Method POST -Uri "https://api.github.com/repos/$Repository/git/refs" -Body @{
            ref = "refs/tags/$TagName"
            sha = $tagObject.sha
        } | Out-Null
    }
    catch {
        $existingTargetSha = Resolve-ExistingTagTargetSha
        if ($null -eq $existingTargetSha -or $existingTargetSha -ne $TargetSha) {
            throw
        }
    }
}

function Get-OrCreateRelease {
    param([Parameter(Mandatory = $true)][string]$Body)

    $release = Invoke-GitHubJsonApi -Method GET -Uri "https://api.github.com/repos/$Repository/releases/tags/$([System.Uri]::EscapeDataString($TagName))" -AllowNotFound
    if ($null -eq $release) {
        return Invoke-GitHubJsonApi -Method POST -Uri "https://api.github.com/repos/$Repository/releases" -Body @{
            tag_name = $TagName
            name = $ReleaseTitle
            body = $Body
            prerelease = [bool]$Prerelease
            generate_release_notes = $false
        }
    }

    return Invoke-GitHubJsonApi -Method PATCH -Uri "https://api.github.com/repos/$Repository/releases/$($release.id)" -Body @{
        tag_name = $TagName
        name = $ReleaseTitle
        body = $Body
        prerelease = [bool]$Prerelease
        draft = $false
    }
}

function Sync-ReleaseAsset {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Release,

        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string]$ContentType
    )

    if (-not (Test-Path -LiteralPath $FilePath)) {
        throw "Release asset file was not found: $FilePath"
    }

    $fileName = (Get-Item -LiteralPath $FilePath).Name
    $assets = @($Release.assets)
    foreach ($asset in @($assets | Where-Object { $_.name -eq $fileName })) {
        Invoke-GitHubJsonApi -Method DELETE -Uri "https://api.github.com/repos/$Repository/releases/assets/$($asset.id)" | Out-Null
    }

    $uploadBaseUri = [string]$Release.upload_url
    $uploadBaseUri = $uploadBaseUri -replace "\{\?name,label\}$", ""
    $uploadUri = "{0}?name={1}" -f $uploadBaseUri, [System.Uri]::EscapeDataString($fileName)

    Invoke-GitHubAssetUpload -Uri $uploadUri -FilePath $FilePath -ContentType $ContentType | Out-Null
}

if (-not (Test-Path -LiteralPath $ZipAssetPath)) {
    throw "The release zip asset does not exist: $ZipAssetPath"
}

if (-not (Test-Path -LiteralPath $ChangelogAssetPath)) {
    throw "The changelog asset does not exist: $ChangelogAssetPath"
}

$releaseBody = New-ManifestReleaseBody
$expectedPattern = '^```jellycheckr-manifest\n\{\n  "version": "[^"]+",\n  "targetAbi": "[^"]+",\n  "dependencies": \[\]\n\}\n```$'
if ($releaseBody -notmatch $expectedPattern) {
    throw "The generated release body did not match the required manifest-only format."
}

$checksumSidecarPath = $null

try {
    $checksumSidecarPath = New-ChecksumSidecarFile -FilePath $ZipAssetPath

    Ensure-Tag
    $release = Get-OrCreateRelease -Body $releaseBody
    Sync-ReleaseAsset -Release $release -FilePath $ZipAssetPath -ContentType "application/zip"
    Sync-ReleaseAsset -Release $release -FilePath $checksumSidecarPath -ContentType "text/plain; charset=utf-8"
    Sync-ReleaseAsset -Release $release -FilePath $ChangelogAssetPath -ContentType "text/markdown; charset=utf-8"
}
finally {
    if ($checksumSidecarPath -and (Test-Path -LiteralPath $checksumSidecarPath)) {
        Remove-Item -LiteralPath $checksumSidecarPath -Force
    }
}

[ordered]@{
    tag = $TagName
    releaseId = $release.id
    releaseUrl = $release.html_url
    prerelease = [bool]$Prerelease
    checksumAsset = if ($checksumSidecarPath) { ([System.IO.Path]::GetFileName($checksumSidecarPath)) } else { $null }
} | ConvertTo-Json -Depth 5
