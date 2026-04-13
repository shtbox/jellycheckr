param(
    [Parameter(Mandatory = $true)]
    [string]$ZipPath,

    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedPackageVersion,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedManifestVersion,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedAssemblyVersion,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedTargetAbi,

    [string]$ExpectedZipFileName = "",
    [string]$AssemblyFileName = "Jellycheckr.Server.dll"
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = "Stop"

function ConvertFrom-JsonCompat {
    param([Parameter(Mandatory = $true)][string]$Json)

    $convertFromJsonCommand = Get-Command -Name ConvertFrom-Json
    if ($convertFromJsonCommand.Parameters.ContainsKey("Depth")) {
        return $Json | ConvertFrom-Json -Depth 20
    }

    return $Json | ConvertFrom-Json
}

function Ensure-ZipFileType {
    try {
        [void][System.IO.Compression.ZipFile]
    }
    catch {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
    }
}

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Required JSON file was not found: $Path"
    }

    return ConvertFrom-JsonCompat -Json (Get-Content -LiteralPath $Path -Raw)
}

function Get-RelativeFileSet {
    param([Parameter(Mandatory = $true)][string]$RootPath)

    $rootFullPath = [System.IO.Path]::GetFullPath($RootPath)
    $prefixLength = $rootFullPath.Length

    $files = Get-ChildItem -LiteralPath $rootFullPath -File -Recurse | ForEach-Object {
        $relativePath = $_.FullName.Substring($prefixLength).TrimStart([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
        $relativePath.Replace("\", "/")
    }

    return @($files | Sort-Object -Unique)
}

function Assert-MetaJson {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$MetaJson,

        [Parameter(Mandatory = $true)]
        [string]$Location
    )

    if ($ExpectedManifestVersion -ne $ExpectedAssemblyVersion) {
        throw "Expected manifest version '$ExpectedManifestVersion' must match expected assembly version '$ExpectedAssemblyVersion'."
    }

    if ($MetaJson.version -ne $ExpectedAssemblyVersion) {
        throw "meta.json version mismatch in $Location. Expected assembly-aligned version '$ExpectedAssemblyVersion' but found '$($MetaJson.version)'."
    }

    if ($MetaJson.packageVersion -ne $ExpectedPackageVersion) {
        throw "meta.json packageVersion mismatch in $Location. Expected '$ExpectedPackageVersion' but found '$($MetaJson.packageVersion)'."
    }

    if ($MetaJson.targetAbi -ne $ExpectedTargetAbi) {
        throw "meta.json targetAbi mismatch in $Location. Expected '$ExpectedTargetAbi' but found '$($MetaJson.targetAbi)'."
    }

    if (-not ($MetaJson.PSObject.Properties.Name -contains "dependencies")) {
        throw "meta.json dependencies property is missing in $Location."
    }

    $dependencies = @($MetaJson.dependencies)
    if ($dependencies.Count -ne 0) {
        throw "meta.json dependencies must be an empty array in $Location."
    }
}

if (-not (Test-Path -LiteralPath $ZipPath)) {
    throw "Zip package was not found: $ZipPath"
}

if (-not (Test-Path -LiteralPath $PublishDirectory)) {
    throw "Publish output directory was not found: $PublishDirectory"
}

$zipFile = Get-Item -LiteralPath $ZipPath
if ([string]::IsNullOrWhiteSpace($ExpectedZipFileName)) {
    $expectedSuffix = "_$ExpectedPackageVersion.zip"
    if (-not $zipFile.Name.EndsWith($expectedSuffix, [System.StringComparison]::Ordinal)) {
        throw "Zip file name '$($zipFile.Name)' does not end with the expected package version suffix '$expectedSuffix'."
    }
}
elseif ($zipFile.Name -ne $ExpectedZipFileName) {
    throw "Zip file name mismatch. Expected '$ExpectedZipFileName' but found '$($zipFile.Name)'."
}

$publishMetaPath = Join-Path $PublishDirectory "meta.json"
$publishMetaJson = Read-JsonFile -Path $publishMetaPath
Assert-MetaJson -MetaJson $publishMetaJson -Location $publishMetaPath

$assemblyPath = Join-Path $PublishDirectory $AssemblyFileName
if (-not (Test-Path -LiteralPath $assemblyPath)) {
    throw "Expected plugin assembly was not found in publish output: $assemblyPath"
}

$assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Version.ToString()
if ($assemblyVersion -ne $ExpectedAssemblyVersion) {
    throw "Assembly version mismatch. Expected '$ExpectedAssemblyVersion' but found '$assemblyVersion'."
}

$publishMetaVersion = [string]$publishMetaJson.version
if ($publishMetaVersion -ne $assemblyVersion) {
    throw "Publish output meta.json version '$publishMetaVersion' did not match assembly version '$assemblyVersion'."
}

$extractDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("jellycheckr-package-" + [System.Guid]::NewGuid().ToString("N"))
[System.IO.Directory]::CreateDirectory($extractDirectory) | Out-Null

try {
    Ensure-ZipFileType
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zipFile.FullName, $extractDirectory)

    $extractedMetaPath = Join-Path $extractDirectory "meta.json"
    $extractedMetaJson = Read-JsonFile -Path $extractedMetaPath
    Assert-MetaJson -MetaJson $extractedMetaJson -Location $extractedMetaPath

    if ([string]$extractedMetaJson.version -ne $assemblyVersion) {
        throw "Zip meta.json version '$($extractedMetaJson.version)' did not match assembly version '$assemblyVersion'."
    }

    $publishFiles = Get-RelativeFileSet -RootPath $PublishDirectory
    $zipFiles = Get-RelativeFileSet -RootPath $extractDirectory

    if ($publishFiles.Count -ne $zipFiles.Count -or (Compare-Object -ReferenceObject $publishFiles -DifferenceObject $zipFiles)) {
        throw "The zip contents do not match the publish output contents."
    }
}
finally {
    if (Test-Path -LiteralPath $extractDirectory) {
        Remove-Item -LiteralPath $extractDirectory -Recurse -Force
    }
}

[ordered]@{
    valid = $true
    zipFile = $zipFile.FullName
    packageVersion = $ExpectedPackageVersion
    manifestVersion = $ExpectedManifestVersion
    assemblyVersion = $ExpectedAssemblyVersion
    targetAbi = $ExpectedTargetAbi
} | ConvertTo-Json -Depth 5
