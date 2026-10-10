# Start the local AI, backend and Admin services.
# Usage: .\Start.ps1
param(
    [switch]$WithoutAI,
    [string]$ListenAddress = "127.0.0.1"
)

$ErrorActionPreference = "Stop"
$runner = Join-Path $PSScriptRoot "scripts\moodify-local.ps1"
if (-not (Test-Path -LiteralPath $runner)) {
    throw "Local service runner not found: $runner"
}

& $runner -Action Start -WithoutAI:$WithoutAI -ListenAddress $ListenAddress
