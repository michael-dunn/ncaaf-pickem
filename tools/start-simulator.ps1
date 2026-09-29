<#
.SYNOPSIS
    Starts the app in Simulator mode and the simulator beside it, each in its own window, then
    opens the simulator's dashboard (P11-01).

.DESCRIPTION
    The app runs with its 'simulator' launch profile (Providers=Simulator, database
    NcaafPickEm_Sim on LocalDB, jobs on, real time) at https://localhost:7092. The simulator runs
    at http://localhost:5299 against the same database. Both create and migrate the database on
    first start. Close either window to stop it.

.EXAMPLE
    ./tools/start-simulator.ps1
#>
[CmdletBinding()]
param(
    # Leave the dashboard closed (for example when it is already open in a tab).
    [switch] $NoBrowser
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Start-Process -FilePath 'dotnet' -WorkingDirectory $root -ArgumentList @(
    'run', '--project', 'src/NcaafPickEm.Api', '--launch-profile', 'simulator')

Start-Process -FilePath 'dotnet' -WorkingDirectory $root -ArgumentList @(
    'run', '--project', 'tools/NcaafPickEm.Simulator', '--launch-profile', 'simulator')

if (-not $NoBrowser) {
    # Give both a moment to build and migrate; the dashboard reconnects on its own if it is early.
    Start-Sleep -Seconds 20
    Start-Process 'http://localhost:5299'
}

Write-Host 'App:       https://localhost:7092  (Simulator mode)'
Write-Host 'Simulator: http://localhost:5299'
