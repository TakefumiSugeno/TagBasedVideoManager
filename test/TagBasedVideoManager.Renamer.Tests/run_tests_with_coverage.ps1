# Test execution & code coverage report generation script for Renamer
$ErrorActionPreference = "Stop"

Write-Host "Running Renamer tests with Code Coverage collection..." -ForegroundColor Cyan

# Clear zombie processes
$processes = Get-Process -Name "TagBasedVideoManager.Renamer", "TagBasedVideoManager.Renamer.Tests" -ErrorAction SilentlyContinue
if ($processes) {
    Write-Host "Killing active Renamer / Tests processes to avoid file lock..." -ForegroundColor Yellow
    $processes | Stop-Process -Force
}

# Determine unique timestamped folder for this run
$timestamp = Get-Date -Format "yyyy-MM-dd_HH_mm_ss"
$testRunDir = Join-Path $PSScriptRoot "TestResults\TestRun_$timestamp"
New-Item -ItemType Directory -Force -Path $testRunDir | Out-Null

$logFileName = "TestRun_${timestamp}_net10.0.trx"
Write-Host "Output Directory: $testRunDir" -ForegroundColor Yellow

# Build first
dotnet build (Join-Path $PSScriptRoot "TagBasedVideoManager.Renamer.Tests.fsproj")

$dllPath = Join-Path $PSScriptRoot "bin\Debug\net10.0\TagBasedVideoManager.Renamer.Tests.dll"

Write-Host "Running tests with coverage..." -ForegroundColor Green
dotnet exec $dllPath --results-directory $testRunDir --report-xunit-trx --report-xunit-trx-filename $logFileName --coverlet --coverlet-output-format cobertura --coverlet-include "[TagBasedVideoManager.Renamer]*" --parallel none

# Check ReportGenerator
$reportGenerator = Get-Command "reportgenerator" -ErrorAction SilentlyContinue
if (-not $reportGenerator) {
    Write-Host "Installing dotnet-reportgenerator-globaltool..." -ForegroundColor Yellow
    dotnet tool install -g dotnet-reportgenerator-globaltool
}

$coberturaFile = Get-ChildItem -Path $testRunDir -Filter "coverage.cobertura.*.xml" | Select-Object -First 1
if ($coberturaFile) {
    $reportDir = Join-Path $testRunDir "CoverageReport"
    Write-Host "Generating HTML coverage report..." -ForegroundColor Green
    reportgenerator -reports:$($coberturaFile.FullName) -targetdir:$reportDir -reporttypes:Html
    Write-Host "Coverage report generated: $reportDir\index.html" -ForegroundColor Green
}
