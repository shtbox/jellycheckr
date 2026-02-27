param(
    [string]$FromTag = "",
    [string]$PendingTitle = "",
    [string]$PendingBody = "",
    [string]$OutputPath = "",
    [string]$PreviewPath = "",
    [string]$SummaryPath = ""
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = "Stop"

function Parse-ConventionalCommit {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Title,

        [string]$Body
    )

    $pattern = "^(?<type>feat|fix|perf|refactor|docs|chore|test|build|ci)(\([^)]+\))?(?<breaking>!)?: (?<description>.+)$"
    $match = [System.Text.RegularExpressions.Regex]::Match($Title, $pattern)
    if (-not $match.Success) {
        return $null
    }

    $type = $match.Groups["type"].Value
    $isBreaking = $match.Groups["breaking"].Success -or ($Body -match "(?im)^BREAKING CHANGE:")
    $section = switch ($type) {
        "feat" { "Features" }
        "fix" { "Fixes" }
        "perf" { "Fixes" }
        default { "Other" }
    }

    if ($isBreaking) {
        $section = "Breaking Changes"
    }

    return [pscustomobject]@{
        Type = $type
        Section = $section
        Title = $Title
    }
}

function Get-GitLogEntries {
    $format = "%H%x1f%s%x1f%b%x1e"
    $arguments = @("log")

    if (-not [string]::IsNullOrWhiteSpace($FromTag)) {
        $arguments += "$FromTag..HEAD"
    }

    $arguments += "--pretty=format:$format"

    $rawLog = [string]::Join("`n", (& git @arguments))
    if ([string]::IsNullOrWhiteSpace($rawLog)) {
        return @()
    }

    $records = $rawLog -split [char]0x1e
    $entries = @()

    foreach ($record in $records) {
        if ([string]::IsNullOrWhiteSpace($record)) {
            continue
        }

        $parts = $record -split [char]0x1f, 3
        if ($parts.Count -lt 2) {
            continue
        }

        $entries += [pscustomobject]@{
            Sha = $parts[0].Trim()
            Title = $parts[1].Trim()
            Body = if ($parts.Count -ge 3) { $parts[2] } else { "" }
            IsPending = $false
        }
    }

    return $entries
}

function Add-EntryToSection {
    param(
        [hashtable]$Sections,
        [string]$SectionName,
        [string]$BulletText
    )

    if (-not $Sections.ContainsKey($SectionName)) {
        $Sections[$SectionName] = New-Object System.Collections.Generic.List[string]
    }

    $Sections[$SectionName].Add($BulletText)
}

$sectionOrder = @("Breaking Changes", "Features", "Fixes", "Other")
$sections = @{}
foreach ($section in $sectionOrder) {
    $sections[$section] = New-Object System.Collections.Generic.List[string]
}

$omittedCount = 0
$entries = Get-GitLogEntries

foreach ($entry in $entries) {
    $parsed = Parse-ConventionalCommit -Title $entry.Title -Body $entry.Body
    if ($null -eq $parsed) {
        $omittedCount += 1
        continue
    }

    $shortSha = if ($entry.Sha.Length -ge 7) { $entry.Sha.Substring(0, 7) } else { $entry.Sha }
    Add-EntryToSection -Sections $sections -SectionName $parsed.Section -BulletText ("- {0} ({1})" -f $entry.Title, $shortSha)
}

if (-not [string]::IsNullOrWhiteSpace($PendingTitle)) {
    $pendingParsed = Parse-ConventionalCommit -Title $PendingTitle -Body $PendingBody
    if ($null -eq $pendingParsed) {
        Add-EntryToSection -Sections $sections -SectionName "Other" -BulletText ("- {0} (pending PR)" -f $PendingTitle)
    }
    else {
        Add-EntryToSection -Sections $sections -SectionName $pendingParsed.Section -BulletText ("- {0} (pending PR)" -f $PendingTitle)
    }
}

$releaseNotesLines = New-Object System.Collections.Generic.List[string]
foreach ($section in $sectionOrder) {
    $releaseNotesLines.Add("## $section")
    $releaseNotesLines.Add("")

    if ($sections[$section].Count -eq 0) {
        $releaseNotesLines.Add("- None")
    }
    else {
        foreach ($line in $sections[$section]) {
            $releaseNotesLines.Add($line)
        }
    }

    $releaseNotesLines.Add("")
}

if ($omittedCount -gt 0) {
    $releaseNotesLines.Add("Non-conventional commits omitted from grouped changelog: $omittedCount.")
}

$releaseNotes = ($releaseNotesLines -join "`n").TrimEnd()
$previewMarkdown = "### Changelog Preview`n`n$releaseNotes"
$summaryMarkdown = "### Changelog Summary`n`n$releaseNotes"

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    Set-Content -LiteralPath $OutputPath -Value $releaseNotes -NoNewline
}

if (-not [string]::IsNullOrWhiteSpace($PreviewPath)) {
    Set-Content -LiteralPath $PreviewPath -Value $previewMarkdown -NoNewline
}

if (-not [string]::IsNullOrWhiteSpace($SummaryPath)) {
    Set-Content -LiteralPath $SummaryPath -Value $summaryMarkdown -NoNewline
}

[ordered]@{
    releaseNotes = $releaseNotes
    previewMarkdown = $previewMarkdown
    summaryMarkdown = $summaryMarkdown
    omittedNonConventionalCount = $omittedCount
} | ConvertTo-Json -Depth 5
