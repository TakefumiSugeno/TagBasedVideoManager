param (
    [string]$TargetFolder,
    [string]$ReportName = "CoverageReport"
)

# Coverage HTML report generation script
$ErrorActionPreference = "Stop"

Write-Host "Checking for ReportGenerator tool..." -ForegroundColor Cyan
$generatorInstalled = (dotnet tool list -g | Where-Object { $_ -match "dotnet-reportgenerator-globaltool" })

if (-not $generatorInstalled) {
    Write-Host "ReportGenerator tool is not installed. Installing globally..." -ForegroundColor Yellow
    dotnet tool install -g dotnet-reportgenerator-globaltool
} else {
    Write-Host "ReportGenerator tool is already installed." -ForegroundColor Green
}

# If no target folder is provided, find the latest timestamped folder
if ([string]::IsNullOrEmpty($TargetFolder)) {
    $resultsDir = Join-Path $PSScriptRoot "TestResults"
    if (Test-Path $resultsDir) {
        $subDirs = Get-ChildItem -Path $resultsDir -Directory | Where-Object { $_.Name -like "TestRun_*" } | Sort-Object LastWriteTime -Descending
        if ($subDirs.Count -gt 0) {
            $TargetFolder = $subDirs[0].FullName
        } else {
            $TargetFolder = $resultsDir
        }
    } else {
        $TargetFolder = $resultsDir
    }
}

Write-Host "Searching for coverage files in $TargetFolder..." -ForegroundColor Cyan
if (-not (Test-Path $TargetFolder)) {
    Write-Host "Target directory $TargetFolder does not exist." -ForegroundColor Red
    exit 1
}

$coverageFiles = Get-ChildItem -Path $TargetFolder -Filter "coverage.cobertura.*xml" -Recurse

if ($coverageFiles.Count -eq 0) {
    Write-Host "No coverage.cobertura.xml files found in $TargetFolder." -ForegroundColor Red
    Write-Host "Please run test script first." -ForegroundColor Yellow
    exit 1
}

$reportsParam = $coverageFiles.FullName -join ";"
$coverageReportDir = Join-Path $TargetFolder $ReportName

Write-Host "Generating HTML coverage report into $coverageReportDir..." -ForegroundColor Cyan
reportgenerator -reports:$reportsParam -targetdir:$coverageReportDir -reporttypes:Html

Write-Host "Code coverage report generated successfully!" -ForegroundColor Green
Write-Host "Report Path: $(Join-Path $coverageReportDir 'index.html')" -ForegroundColor Green

# Open browser
Start-Process (Join-Path $coverageReportDir "index.html")
