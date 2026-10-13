param([string]$Database = 'teamflow')

$ErrorActionPreference = 'Stop'
if (-not $env:ASPNETCORE_ENVIRONMENT) { $env:ASPNETCORE_ENVIRONMENT = 'Development' }
if ($Database -notmatch '^[a-z][a-z0-9_]*$') { throw 'Invalid database name.' }
$repoPath = Split-Path $PSScriptRoot -Parent
$envPath = Join-Path $repoPath '.env'
if (-not (Test-Path -LiteralPath $envPath)) { throw 'Create .env from .env.example with a local password first.' }
$passwordLine = Get-Content -LiteralPath $envPath | Where-Object { $_ -match '^TEAMFLOW_DB_PASSWORD=' } | Select-Object -First 1
if (-not $passwordLine) { throw 'TEAMFLOW_DB_PASSWORD is missing.' }
$databasePassword = ($passwordLine -split '=', 2)[1]
if ($databasePassword -notmatch '^[a-zA-Z0-9_-]{16,128}$') { throw 'Use a local password of 16..128 letters, digits, underscores or hyphens.' }
$env:ConnectionStrings__Database = "Host=localhost;Port=55432;Database=$Database;Username=teamflow;Password=$databasePassword;Timeout=30"
Write-Output 'Local database configured in this terminal. Connection string is not printed.'
