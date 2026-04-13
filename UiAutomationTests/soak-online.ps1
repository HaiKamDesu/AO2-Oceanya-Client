# soak-online.ps1
# Runs the Online UI automation lane N times in sequence and reports aggregate pass/fail.
#
# Prerequisites: OceanyaClient must already be built (dotnet build) before running.
# UIA3 and SendKeys require an interactive Windows desktop session - do not run
# in a headless or Windows Service context.
#
# Usage:
#   .\soak-online.ps1
#   .\soak-online.ps1 -Iterations 20
#   .\soak-online.ps1 -Iterations 10 -Configuration Release -ResultsDir "C:\SoakResults"
#
# Exit code: number of failures (0 = all passed).

param(
    [int]$Iterations = 10,
    [string]$Configuration = "Debug",
    [string]$ResultsDir = "TestResults\OnlineSoak"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repoRoot "UiAutomationTests\UiAutomationTests.csproj"
$resultsBase = Join-Path $repoRoot $ResultsDir

$pass = 0
$fail = 0
$failDetails = New-Object 'System.Collections.Generic.List[string]'

# Resolve dotnet command in a PowerShell-5-compatible way.
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetCommand) {
    $dotnetCmd = $dotnetCommand.Source
} else {
    $fallbackDotnet = "C:\Program Files\dotnet\dotnet.exe"
    if (Test-Path $fallbackDotnet) {
        $dotnetCmd = $fallbackDotnet
    } else {
        throw "Could not find 'dotnet' on PATH and fallback path '$fallbackDotnet' does not exist."
    }
}

New-Item -ItemType Directory -Force -Path $resultsBase | Out-Null

Write-Host ""
Write-Host "=== Online Lane Soak Run ===" -ForegroundColor Cyan
Write-Host "  Iterations   : $Iterations"
Write-Host "  Configuration: $Configuration"
Write-Host "  Results root : $resultsBase"
Write-Host "  Dotnet       : $dotnetCmd"
Write-Host ""

for ($i = 1; $i -le $Iterations; $i++) {
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $runDir = Join-Path $resultsBase ("Run_{0}_{1}" -f $timestamp, $i)
    New-Item -ItemType Directory -Force -Path $runDir | Out-Null

    Write-Host ("--- Run {0} / {1} ---" -f $i, $Iterations) -ForegroundColor Cyan

    & $dotnetCmd test $proj `
        --configuration $Configuration `
        --no-build `
        --filter "Category=Online" `
        --logger "trx;LogFileName=online-soak.trx" `
        --results-directory $runDir

    if ($LASTEXITCODE -eq 0) {
        $pass++
        Write-Host "  Result: PASS" -ForegroundColor Green
    } else {
        $fail++
        $detail = "Run $i - see $runDir"
        $failDetails.Add($detail) | Out-Null
        Write-Host "  Result: FAIL ($detail)" -ForegroundColor Red
    }

    if ($i -lt $Iterations) {
        Start-Sleep -Seconds 3
    }
}

Write-Host ""
Write-Host "=== Soak Summary ===" -ForegroundColor Cyan

if ($fail -eq 0) {
    Write-Host ("  Passed : {0} / {1}" -f $pass, $Iterations) -ForegroundColor Green
    Write-Host ("  Failed : {0} / {1}" -f $fail, $Iterations) -ForegroundColor White
} else {
    Write-Host ("  Passed : {0} / {1}" -f $pass, $Iterations) -ForegroundColor White
    Write-Host ("  Failed : {0} / {1}" -f $fail, $Iterations) -ForegroundColor Red
}

if ($failDetails.Count -gt 0) {
    Write-Host ""
    Write-Host "  Failed runs:" -ForegroundColor Red
    foreach ($d in $failDetails) {
        Write-Host "    $d"
    }
}

Write-Host ""
Write-Host "TRX files and failure screenshots are in: $resultsBase"
Write-Host ""

exit $fail