<#
.SYNOPSIS
    Generates a VAPID key pair for web push (Feature 11) and prints the configuration to set.

.DESCRIPTION
    Web push authenticates the server to the browser's push service with a VAPID P-256 key pair.
    Generate one pair per environment and keep it: changing the key pair invalidates every stored
    subscription, so every member has to turn notifications on again.

    This wraps the Api's hidden 'generate-vapid' argument, which prints a fresh pair and exits
    without reading configuration, touching the database, or opening a port.

    Nothing is written to disk. Copy the values into appsettings.Production.json (gitignored), an
    environment variable, or user secrets. NEVER commit the private key.

.PARAMETER Format
    EnvVars    - Push__VapidPublicKey=... lines, for a service definition or shell (default).
    Json       - a "Push" block to paste into appsettings.
    UserSecret - dotnet user-secrets commands for local development.

.EXAMPLE
    ./deploy/generate-vapid.ps1

.EXAMPLE
    ./deploy/generate-vapid.ps1 -Format Json
#>
[CmdletBinding()]
param(
    [ValidateSet('EnvVars', 'Json', 'UserSecret')]
    [string] $Format = 'EnvVars',

    [string] $Subject = 'mailto:commissioner@example.com'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $repoRoot 'src/NcaafPickEm.Api'

if (-not (Test-Path $apiProject)) {
    throw "Could not find the Api project at $apiProject. Run this script from the repository."
}

Write-Host 'Generating a VAPID key pair...' -ForegroundColor Cyan

# Keep this invocation minimal: adding --verbosity/--nologo makes `dotnet run` swallow the
# argument after `--` instead of forwarding it, and the app then starts a web host.
# Build noise is harmless here because only the Push__* lines are parsed out below.
$output = & dotnet run --project $apiProject --no-launch-profile -- generate-vapid
if ($LASTEXITCODE -ne 0) {
    throw "dotnet run failed with exit code $LASTEXITCODE."
}

$pairs = @{}
foreach ($line in $output) {
    if ($line -match '^(Push__\w+)=(.+)$') {
        $pairs[$Matches[1]] = $Matches[2]
    }
}

if (-not $pairs.ContainsKey('Push__VapidPublicKey') -or -not $pairs.ContainsKey('Push__VapidPrivateKey')) {
    throw "The generator did not print a key pair. Raw output: $($output -join [Environment]::NewLine)"
}

$publicKey = $pairs['Push__VapidPublicKey']
$privateKey = $pairs['Push__VapidPrivateKey']

Write-Host ''
Write-Host 'New VAPID key pair (the private key is a secret - do not commit it):' -ForegroundColor Yellow
Write-Host ''

switch ($Format) {
    'EnvVars' {
        Write-Output "Push__VapidPublicKey=$publicKey"
        Write-Output "Push__VapidPrivateKey=$privateKey"
        Write-Output "Push__Subject=$Subject"
    }
    'Json' {
        Write-Output '"Push": {'
        Write-Output "  `"VapidPublicKey`": `"$publicKey`","
        Write-Output "  `"VapidPrivateKey`": `"$privateKey`","
        Write-Output "  `"Subject`": `"$Subject`""
        Write-Output '}'
    }
    'UserSecret' {
        Write-Output "dotnet user-secrets --project src/NcaafPickEm.Api set `"Push:VapidPublicKey`" `"$publicKey`""
        Write-Output "dotnet user-secrets --project src/NcaafPickEm.Api set `"Push:VapidPrivateKey`" `"$privateKey`""
        Write-Output "dotnet user-secrets --project src/NcaafPickEm.Api set `"Push:Subject`" `"$Subject`""
    }
}

Write-Host ''
Write-Host 'Next steps:' -ForegroundColor Cyan
Write-Host '  1. Set all three values (Subject must be a real mailto: or https: contact).'
Write-Host '  2. Restart the app. GET /api/push/vapid-public-key answers 503 until they are set.'
Write-Host '  3. Keep the pair: replacing it invalidates every stored push subscription.'
