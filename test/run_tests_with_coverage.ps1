# Test execution & code coverage report generation script
$ErrorActionPreference = "Stop"

Write-Host "Running tests with Code Coverage collection..." -ForegroundColor Cyan

# Clear zombie processes
$processes = Get-Process -Name "TagBasedVideoManager", "TagBasedVideoManager.Tests" -ErrorAction SilentlyContinue
if ($processes) {
    Write-Host "Killing active TagBasedVideoManager / Tests processes to avoid file lock..." -ForegroundColor Yellow
    $processes | Stop-Process -Force
}

$dotnetHosts = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Where-Object {
    try {
        $_.Modules | Where-Object { $_.ModuleName -like "*TagBasedVideoManager*" }
    } catch {
        $false
    }
}
if ($dotnetHosts) {
    Write-Host "Killing dotnet host processes that locked DLLs..." -ForegroundColor Yellow
    $dotnetHosts | Stop-Process -Force
}

# Determine unique timestamped folder for this run
$timestamp = Get-Date -Format "yyyy-MM-dd_HH_mm_ss"
$testRunDir = Join-Path $PSScriptRoot "TestResults\TestRun_$timestamp"
New-Item -ItemType Directory -Force -Path $testRunDir | Out-Null

$logFileName = "TestRun_${timestamp}_net10.0.trx"
Write-Host "Output Directory: $testRunDir" -ForegroundColor Yellow

# Build first
dotnet build (Join-Path $PSScriptRoot "TagBasedVideoManager.Tests.fsproj")

$dllPath = Join-Path $PSScriptRoot "bin\Debug\net10.0\TagBasedVideoManager.Tests.dll"

Write-Host "Running UNIT tests with coverage..." -ForegroundColor Green
$unitFilter = @(
    "--filter-class", "TagBasedVideoManager.Tests.Unit.*",
    "--filter-class", "TagBasedVideoManager.Tests.DbInitTests"
)
$unitDir = Join-Path $testRunDir "Unit"
dotnet exec $dllPath --results-directory $unitDir --report-xunit-trx --report-xunit-trx-filename "Unit_net10.0.trx" --coverlet --coverlet-output-format cobertura --coverlet-include "[TagBasedVideoManager]*" --parallel none @unitFilter

Write-Host "Running INTEGRATION tests with coverage..." -ForegroundColor Green
$integrationFilter = @(
    "--filter-class", "TagBasedVideoManager.Tests.TagTests",
    "--filter-class", "TagBasedVideoManager.Tests.RuleTests",
    "--filter-class", "TagBasedVideoManager.Tests.SearchTests",
    "--filter-class", "TagBasedVideoManager.Tests.StreamingTests",
    "--filter-class", "TagBasedVideoManager.Tests.PortTests",
    "--filter-class", "TagBasedVideoManager.Tests.StaticFileTests"
)
$integrationDir = Join-Path $testRunDir "Integration"
dotnet exec $dllPath --results-directory $integrationDir --report-xunit-trx --report-xunit-trx-filename "Integration_net10.0.trx" --coverlet --coverlet-output-format cobertura --coverlet-include "[TagBasedVideoManager]*" --parallel none @integrationFilter

Write-Host "Running E2E tests with coverage..." -ForegroundColor Green
$e2eFilter = @(
    "--filter-class", "TagBasedVideoManager.Tests.E2ETests"
)
$e2eDir = Join-Path $testRunDir "E2E"
dotnet exec $dllPath --results-directory $e2eDir --report-xunit-trx --report-xunit-trx-filename "E2E_net10.0.trx" --coverlet --coverlet-output-format cobertura --coverlet-include "[TagBasedVideoManager]*" --parallel none @e2eFilter

Write-Host "Test run complete. Generating individual coverage reports..." -ForegroundColor Green

# Call report generator script for each category
& (Join-Path $PSScriptRoot "generate_coverage_report.ps1") -TargetFolder $unitDir -ReportName "CoverageReport_Unit"
& (Join-Path $PSScriptRoot "generate_coverage_report.ps1") -TargetFolder $integrationDir -ReportName "CoverageReport_Integration"
& (Join-Path $PSScriptRoot "generate_coverage_report.ps1") -TargetFolder $e2eDir -ReportName "CoverageReport_E2E"
