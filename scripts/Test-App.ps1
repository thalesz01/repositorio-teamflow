param(
    [Parameter(Mandatory)][ValidateSet('api', 'runner', 'web')][string]$App,
    [switch]$Restore,
    [switch]$E2E
)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repoPath
function Invoke-Checked([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable check failed with exit code $LASTEXITCODE." }
}
if ($App -eq 'web') {
    Push-Location './apps/web'
    try {
        if ($Restore) { Invoke-Checked 'npm' @('ci') }
        Invoke-Checked 'npm' @('run', 'typecheck')
        Invoke-Checked 'npm' @('run', 'lint')
        Invoke-Checked 'npm' @('run', 'build')
        if ($E2E) {
            if (-not $env:E2E_URL) { $env:E2E_URL = 'http://127.0.0.1:3000' }
            $address = [Uri]$env:E2E_URL
            if (-not $address.IsLoopback) { throw 'E2E creates fixture accounts and tasks; use only a local preview.' }
            Invoke-Checked 'npm' @('run', 'test:e2e')
        }
    } finally { Pop-Location }
    return
}
if ($App -eq 'api' -and -not $env:ConnectionStrings__Database) { . './scripts/Use-LocalDatabase.ps1' }
if ($App -eq 'api' -and -not $env:ASPNETCORE_ENVIRONMENT) { $env:ASPNETCORE_ENVIRONMENT = 'Development' }
$tests = if ($App -eq 'api') { '../../tests/TeamFlow.Api.Tests' } else { '../../tests/TeamFlow.Runner.Tests' }
# Resolve this application's global.json rather than a machine-wide SDK from the monorepo root.
Push-Location "./apps/$App"
try {
    if ($Restore) {
        Invoke-Checked 'dotnet' @('restore', '.', '--locked-mode')
        Invoke-Checked 'dotnet' @('restore', $tests, '--locked-mode')
    }
    Invoke-Checked 'dotnet' @('build', '.', '--no-restore')
    Invoke-Checked 'dotnet' @('test', $tests, '--no-restore')
    Invoke-Checked 'dotnet' @('format', '.', '--no-restore', '--verify-no-changes')
    Invoke-Checked 'dotnet' @('format', $tests, '--no-restore', '--verify-no-changes')
    if ($App -eq 'api') { Invoke-Checked 'dotnet' @('tool', 'run', 'dotnet-ef', 'migrations', 'has-pending-model-changes', '--no-build', '--project', '.') }
} finally { Pop-Location }
