#Requires -Version 5.1
<#
.SYNOPSIS
Builds the application, creates artifacts/restaurant-orders-app.zip, and copies
the unpacked published files to a configurable SCP destination.
#>
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$ScpDestination = 'waritko@mrazitko.varak.net:/home/waritko/objednavky-run',
    [ValidateRange(1, 65535)]
    [int]$ScpPort = 22,
    [string]$ScpIdentityFile,
    [switch]$SkipScp
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $repoRoot 'artifacts'
$staging = Join-Path $artifacts ('publish-' + [Guid]::NewGuid().ToString('N'))
$archive = Join-Path $artifacts 'restaurant-orders-app.zip'

Push-Location $repoRoot
try {
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
    $node = (Get-Command node -CommandType Application -ErrorAction Stop).Source
    $npmName = if ($env:OS -eq 'Windows_NT') { 'npm.cmd' } else { 'npm' }
    $npm = (Get-Command $npmName -CommandType Application -ErrorAction Stop).Source
    if (-not $SkipScp) {
        $scp = (Get-Command scp -CommandType Application -ErrorAction Stop).Source
        if ($ScpIdentityFile) {
            $ScpIdentityFile = (Resolve-Path -LiteralPath $ScpIdentityFile -ErrorAction Stop).ProviderPath
        }
    }

    & $npm --prefix frontend ci
    if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency installation failed.' }
    & $npm --prefix frontend run build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
    & $dotnet publish backend/RestaurantOrders.Api/RestaurantOrders.Api.csproj --configuration Release --self-contained false -p:UseAppHost=false --output $staging
    if ($LASTEXITCODE -ne 0) { throw 'API publish failed.' }
    & $node scripts/package.mjs $staging
    if ($LASTEXITCODE -ne 0) { throw 'Application packaging failed.' }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stagedArchive = Join-Path $artifacts ('package-' + [Guid]::NewGuid().ToString('N') + '.zip')
    [System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $stagedArchive)
    Move-Item -LiteralPath $stagedArchive -Destination $archive -Force
    Write-Host "Published application: $staging"
    Write-Host "ZIP archive: $archive"

    if (-not $SkipScp) {
        $scpArguments = @('-r', '-P', "$ScpPort")
        if ($ScpIdentityFile) { $scpArguments += @('-i', $ScpIdentityFile) }
        Push-Location $staging
        try {
            # Relative paths avoid treating Windows drive letters as SCP hosts.
            # Copy the contents, including hidden files, rather than the staging folder.
            $publishedFiles = @(Get-ChildItem -Force | ForEach-Object { './' + $_.Name })
            & $scp @scpArguments -- @publishedFiles $ScpDestination
            if ($LASTEXITCODE -ne 0) { throw "SCP copy failed: $ScpDestination" }
        }
        finally {
            Pop-Location
        }
        Write-Host "Copied published files to: $ScpDestination"
    }
}
finally {
    Pop-Location
}
