# Starts everything the local flow needs, each piece in its own window: database, API, web and one runner per
# configured project. Pieces that already answer are left alone, so it is safe to run again.
# Works on Windows PowerShell 5.1 and PowerShell 7. -DryRun only reports what would happen.
param(
    [switch]$DryRun,
    [switch]$NoWeb,
    [switch]$NoRunners,
    [string]$ApiUrl = 'http://localhost:5080',
    [string]$WebUrl = 'http://localhost:3000'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$shell = (Get-Process -Id $PID).Path

function Test-Url([string]$Url) {
    try { Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3 | Out-Null; return $true } catch { return $false }
}

function Start-Window([string]$Title, [string]$Command) {
    $escapedTitle = $Title.Replace("'", "''")
    $script = "`$Host.UI.RawUI.WindowTitle = '$escapedTitle'; $Command"
    if ($DryRun) { Write-Output "[dry-run] would open a window '$Title'"; return }
    Start-Process -FilePath $shell -ArgumentList @('-NoExit', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $script) | Out-Null
    Write-Output "Started: $Title"
}

function Invoke-Step([string]$Description, [scriptblock]$Action) {
    if ($DryRun) { Write-Output "[dry-run] would run: $Description"; return }
    Write-Output $Description
    $global:LASTEXITCODE = 0
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "Failed: $Description" }
}

$quotedRoot = $root.Replace("'", "''")

# 1. Database
if (-not (Test-Path -LiteralPath (Join-Path $root '.env'))) { throw 'Create .env from .env.example first (see docs/local-development.md).' }
Push-Location $root
try { Invoke-Step 'Starting the local PostgreSQL container' { docker compose -p teamflow-local up -d --wait } } finally { Pop-Location }

# 2. API (migrations are applied explicitly, only when the API is about to start)
$apiRunning = Test-Url "$ApiUrl/health/ready"
if ($apiRunning) { Write-Output "API already running at $ApiUrl." }
else {
    Push-Location (Join-Path $root 'apps/api')
    try {
        . (Join-Path $PSScriptRoot 'Use-LocalDatabase.ps1') | Out-Null
        Invoke-Step 'Restoring and building the API' { dotnet restore --locked-mode | Out-Null; if ($LASTEXITCODE -eq 0) { dotnet build --no-restore -v q } }
        Invoke-Step 'Applying pending database migrations' { dotnet tool restore | Out-Null; if ($LASTEXITCODE -eq 0) { dotnet ef database update --no-build } }
    } finally { Pop-Location }
    Start-Window 'TeamFlow API' ". '$quotedRoot/scripts/Use-LocalDatabase.ps1' | Out-Null; `$env:ASPNETCORE_ENVIRONMENT = 'Development'; Set-Location '$quotedRoot/apps/api'; dotnet run --no-build --no-launch-profile"
}

# 3. Web
if ($NoWeb) { Write-Output 'Web skipped (-NoWeb).' }
elseif (Test-Url $WebUrl) { Write-Output "Web already running at $WebUrl." }
else { Start-Window 'TeamFlow Web' "Set-Location '$quotedRoot/apps/web'; npm run dev" }

# 4. One runner per project that has been set up with Setup-Runner.ps1
if ($NoRunners) { Write-Output 'Runners skipped (-NoRunners).'; return }
$runnerRoot = Join-Path $root 'apps/runner/work/local'
$projects = @()
if (Test-Path -LiteralPath $runnerRoot) {
    $projects = @(Get-ChildItem -LiteralPath $runnerRoot -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'credential.json') })
}
if ($projects.Count -eq 0) { Write-Output 'No runner set up yet. Run scripts/Setup-Runner.ps1 -OrganizationId <id> -ProjectId <id>.'; return }
if (-not $DryRun) {
    for ($attempt = 0; $attempt -lt 45 -and -not (Test-Url "$ApiUrl/health/ready"); $attempt++) { Start-Sleep -Seconds 2 }
    if (-not (Test-Url "$ApiUrl/health/ready")) { throw "The API did not become ready at $ApiUrl." }
}
Push-Location (Join-Path $root 'apps/runner')
try { Invoke-Step 'Building the runner' { dotnet restore --locked-mode | Out-Null; if ($LASTEXITCODE -eq 0) { dotnet build --no-restore -v q } } } finally { Pop-Location }
foreach ($project in $projects) {
    $lockPath = Join-Path $project.FullName 'worker.lock'
    $held = $false
    if (Test-Path -LiteralPath $lockPath) {
        try { [IO.File]::Open($lockPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None).Dispose() } catch [IO.IOException] { $held = $true }
    }
    if ($held) { Write-Output "Runner for project $($project.Name) already running."; continue }
    Start-Window "TeamFlow Runner $($project.Name.Substring(0, 8))" "& '$quotedRoot/scripts/Start-LocalRunner.ps1' -ProjectId '$($project.Name)'"
}
Write-Output "Open $WebUrl. The board shows when each piece is ready."
