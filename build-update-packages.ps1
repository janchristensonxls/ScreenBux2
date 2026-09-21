# Run ./build-update-packages.ps1

<#
.SYNOPSIS
    Builds the Service and Agent release packages for the ScreenBux AutoUpdater.

.DESCRIPTION
    Builds (unless -SkipBuild is passed) ScreenBux.Service and ScreenBux.Agent in the given
    configuration, zips each project's build output (flat, so ZipFile.ExtractToDirectory in
    ScreenBux.Updater lands the files directly in the install directory), and writes both
    zips plus a README.md documenting the ScreenBux.WebServer appsettings.json "Updates"
    section changes (version + SHA-256) into the /latest output folder.

    The zips still need to be uploaded to blob storage (with a SAS URL) by hand - this script
    only prepares them and computes the hashes the manifest needs.

    1. Set the new version in root/Directory.Build.props.
    2. Run this script: (./build-update-packages.ps1) to build the zips and README.md.
    3. Open the folder /latest.
    4. Upload agent.zip & service.zip to the SBX blobstorage, container "latest"
    5. Update ScreenBux.WebServer's appsettings.json "Updates" section with the new version, blob URLs, and SHA-256 hashes, found in the readme.md
    6. Publish the new ScreenBux.WebServer build to Azure App Service (or wherever it's hosted) - the new "Updates" section will be picked up automatically.

.PARAMETER Configuration
    Build configuration to use. Defaults to "Release".

.PARAMETER SkipBuild
    Skip the `dotnet build` step and zip whatever is already in each project's bin/<Configuration>
    output folder.

.PARAMETER OutputDir
    Folder the zips and README.md are written to. Defaults to <repo root>/latest.

.EXAMPLE
    ./build-update-packages.ps1

.EXAMPLE
    ./build-update-packages.ps1 -SkipBuild
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [string]$OutputDir = (Join-Path $PSScriptRoot "latest")
)

$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot

function Get-XmlElementValue {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$ElementName
    )

    $node = Select-Xml -Path $Path -XPath "//$ElementName" | Select-Object -First 1
    if (-not $node) {
        throw "Could not find <$ElementName> in $Path."
    }

    return $node.Node.InnerText.Trim()
}

function New-ComponentPackage {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$ProjectRelativePath,
        [Parameter(Mandatory)] [string]$ZipName
    )

    $csprojPath = Join-Path $RepoRoot $ProjectRelativePath
    if (-not (Test-Path $csprojPath)) {
        throw "$Name project file not found: $csprojPath"
    }

    $tfm = Get-XmlElementValue -Path $csprojPath -ElementName "TargetFramework"
    $projectDir = Split-Path $csprojPath -Parent
    $buildOutputDir = Join-Path $projectDir "bin\$Configuration\$tfm"

    if (-not $SkipBuild) {
        # Remove any previous output first, so a stray file left over in bin/ from something
        # else (a manual test, an old artifact) can never silently end up inside the package.
        if (Test-Path $buildOutputDir) {
            Write-Host "Cleaning previous $Name output ($buildOutputDir)..." -ForegroundColor DarkGray
            Remove-Item $buildOutputDir -Recurse -Force
        }

        Write-Host "Building $Name ($Configuration)..." -ForegroundColor Cyan

        # Capture dotnet build's stdout into a variable rather than letting it flow straight
        # through - otherwise it becomes part of *this function's* output stream and gets mixed
        # into the PSCustomObject this function returns below, corrupting $results.
        $buildOutput = dotnet build $csprojPath -c $Configuration --nologo 2>&1
        $buildOutput | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed for $Name (exit code $LASTEXITCODE)."
        }
    }

    if (-not (Test-Path $buildOutputDir)) {
        throw "$Name build output not found at '$buildOutputDir'. Build it first (omit -SkipBuild), or check TargetFramework/Configuration."
    }

    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

    $zipPath = Join-Path $OutputDir $ZipName
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    Write-Host "Zipping $Name output ($tfm) -> $zipPath" -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $buildOutputDir "*") -DestinationPath $zipPath -Force

    $hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash
    $sizeBytes = (Get-Item $zipPath).Length

    return [PSCustomObject]@{
        Name        = $Name
        Tfm         = $tfm
        SourceDir   = $buildOutputDir
        ZipName     = $ZipName
        ZipPath     = $zipPath
        SizeBytes   = $sizeBytes
        Sha256      = $hash
    }
}

# --- Version -----------------------------------------------------------

$directoryBuildPropsPath = Join-Path $RepoRoot "Directory.Build.props"
if (-not (Test-Path $directoryBuildPropsPath)) {
    throw "Directory.Build.props not found at $directoryBuildPropsPath."
}

$version = Get-XmlElementValue -Path $directoryBuildPropsPath -ElementName "VersionPrefix"
Write-Host "Version (from Directory.Build.props): $version" -ForegroundColor Yellow

# --- Build + package -----------------------------------------------------

$results = @(
    New-ComponentPackage -Name "Service" -ProjectRelativePath "src\ScreenBux.Service\ScreenBux.Service.csproj" -ZipName "service.zip"
    New-ComponentPackage -Name "Agent"   -ProjectRelativePath "src\ScreenBux.Agent\ScreenBux.Agent.csproj"     -ZipName "agent.zip"
) | Where-Object { $_ -is [System.Management.Automation.PSCustomObject] }

$serviceResult = $results | Where-Object { $_.Name -eq "Service" } | Select-Object -First 1
$agentResult = $results | Where-Object { $_.Name -eq "Agent" } | Select-Object -First 1

if (-not $serviceResult -or -not $agentResult) {
    throw "Failed to determine package results for Service/Agent."
}

# --- README --------------------------------------------------------------

$readmePath = Join-Path $OutputDir "README.md"

$updatesJson = @"
  "Updates": {
    "Service": {
      "Version": "$version",
      "DownloadUrl": "https://sbx.blob.core.windows.net/latest/service.zip?sp=r&st=2026-09-18T12:32:25Z&se=2036-09-18T20:47:25Z&spr=https&sv=2026-02-06&sr=c&sig=8XnUgFPHcE%2BXXTfHJltU%2FUfMVskOJLuJ%2BW9tyqDS4K0%3D",
      "Sha256": "$($serviceResult.Sha256)"
    },
    "Agent": {
      "Version": "$version",
      "DownloadUrl": "https://sbx.blob.core.windows.net/latest/agent.zip?sp=r&st=2026-09-18T12:32:25Z&se=2036-09-18T20:47:25Z&spr=https&sv=2026-02-06&sr=c&sig=8XnUgFPHcE%2BXXTfHJltU%2FUfMVskOJLuJ%2BW9tyqDS4K0%3D",
      "Sha256": "$($agentResult.Sha256)"
    }
  }
"@

$componentRows = $results | ForEach-Object {
    "| $($_.Name) | $($_.ZipName) | $($_.Tfm) | $([math]::Round($_.SizeBytes / 1MB, 2)) MB | ``$($_.Sha256)`` |"
}

$readme = @"
# ScreenBux AutoUpdater release package - $version

Generated $(Get-Date -Format "yyyy-MM-dd HH:mm:ss") by ``build-update-packages.ps1``.

## Packages

| Component | Zip | TargetFramework | Size | SHA-256 |
|---|---|---|---|---|
$($componentRows -join "`n")

## 1. Upload

Upload both zips in this folder to the blob storage container the WebServer's update manifest
points at (the same container/SAS pattern already used for ``Updates:Agent:DownloadUrl`` in
``src/ScreenBux.WebServer/appsettings.json``), overwriting the previous ``service.zip``/``agent.zip``.

## 2. Update ScreenBux.WebServer configuration

Update the ``Updates`` section in ``src/ScreenBux.WebServer/appsettings.json`` (or the
equivalent Azure App Service configuration for a deployed environment) with the version and
hashes below, filling in each ``DownloadUrl`` with the actual blob URL + SAS token for the
file you just uploaded:

``````json
{
$updatesJson
}
``````

## 3. Verify

- ``GET /api/updates/latest`` on the WebServer should return the new version/hashes above.
- ``ScreenBux.Updater`` polls this endpoint (``CheckIntervalMinutes``, default 60) and verifies
  each download's SHA-256 against the manifest before installing - a mismatch is logged and the
  update is skipped, so a wrong/stale hash here just blocks the update rather than installing a
  bad package.
"@

Set-Content -Path $readmePath -Value $readme -Encoding utf8

# --- Summary ---------------------------------------------------------------

Write-Host ""
Write-Host "Done. Version $version packaged to $OutputDir" -ForegroundColor Green
foreach ($result in $results) {
    Write-Host "  $($result.ZipName): $($result.Sha256)" -ForegroundColor Green
}
Write-Host "  README.md written to $readmePath" -ForegroundColor Green
