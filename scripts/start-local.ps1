#Requires -Version 5.1
<#
.SYNOPSIS
Starts the API and frontend for local development. Press Ctrl+C to stop.
.PARAMETER BootstrapAdmin
Prompts for credentials and creates the first administrator before starting.
.PARAMETER SkipInstall
Uses the existing frontend dependencies instead of running npm ci.
#>
[CmdletBinding()]
param(
    [switch]$BootstrapAdmin,
    [switch]$SkipInstall
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$apiDirectory = Join-Path $repoRoot 'backend/RestaurantOrders.Api'
$apiDll = Join-Path $apiDirectory 'bin/Debug/net10.0/RestaurantOrders.Api.dll'
$frontendDirectory = Join-Path $repoRoot 'frontend'
$apiProcess = $null
$savedEnvironment = @{}
foreach ($name in @('ASPNETCORE_ENVIRONMENT', 'DOTNET_ENVIRONMENT', 'API_URL')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

Push-Location $repoRoot
try {
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
    $node = (Get-Command node -CommandType Application -ErrorAction Stop).Source
    $npm = (Get-Command npm.cmd -CommandType Application -ErrorAction Stop).Source

    # Fail before migrations if another local server already owns either port.
    foreach ($port in @(5080, 5173)) {
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $port)
        try { $listener.Start() }
        catch { throw "Port $port is already in use. Stop the existing server and try again." }
        finally { $listener.Stop() }
    }

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:API_URL = 'http://127.0.0.1:5080'

    if (-not $SkipInstall) {
        & $npm --prefix $frontendDirectory ci
        if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency installation failed.' }
    }
    $vite = Join-Path $frontendDirectory 'node_modules/vite/bin/vite.js'
    if (-not (Test-Path $vite)) { throw 'Frontend dependencies are missing. Run again without -SkipInstall.' }

    & $dotnet build $apiDirectory --configuration Debug
    if ($LASTEXITCODE -ne 0) { throw 'API build failed.' }

    # Keep relative SQLite paths consistent with dotnet run --project.
    Set-Location $apiDirectory
    & $dotnet $apiDll --migrate
    if ($LASTEXITCODE -ne 0) { throw 'Database migration failed.' }

    if ($BootstrapAdmin) {
        $previousUsername = $env:Bootstrap__Username
        $previousPassword = $env:Bootstrap__Password
        try {
            $env:Bootstrap__Username = Read-Host 'Initial administrator username'
            $password = Read-Host 'Initial administrator password (12+ characters)' -AsSecureString
            $env:Bootstrap__Password = [System.Net.NetworkCredential]::new('', $password).Password
            & $dotnet $apiDll --bootstrap-admin
            if ($LASTEXITCODE -ne 0) { throw 'Administrator bootstrap failed.' }
        }
        finally {
            $env:Bootstrap__Username = $previousUsername
            $env:Bootstrap__Password = $previousPassword
            if ($null -ne $password) { $password.Dispose() }
        }
    }

    $apiProcess = Start-Process -FilePath $dotnet -ArgumentList @("`"$apiDll`"", '--urls', $env:API_URL) -WorkingDirectory $apiDirectory -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        if ($apiProcess.HasExited) { throw "API exited with code $($apiProcess.ExitCode)." }
        $ready = $false
        try {
            $response = Invoke-WebRequest "$env:API_URL/health/database" -UseBasicParsing -TimeoutSec 2
            $ready = $response.StatusCode -eq 200
        }
        catch { Start-Sleep -Milliseconds 300 }
        if (-not $ready -and [DateTime]::UtcNow -ge $deadline) { throw 'API did not become ready within 60 seconds.' }
    } until ($ready)

    Write-Host 'Open http://127.0.0.1:5173. Press Ctrl+C to stop both servers.'
    Set-Location $frontendDirectory
    & $node $vite --host 127.0.0.1 --port 5173 --strictPort
    if ($LASTEXITCODE -ne 0) { throw 'Frontend server exited with an error.' }
}
finally {
    if ($null -ne $apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -ErrorAction SilentlyContinue
    }
    foreach ($name in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process')
    }
    Pop-Location
}
