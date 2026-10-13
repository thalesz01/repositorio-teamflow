param([Parameter(Mandatory)][Guid]$ProjectId)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$state = Join-Path $repoRoot "apps/runner/work/local/$ProjectId"
$lock = [IO.File]::Open((Join-Path $state 'worker.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
$credential = Get-Content -LiteralPath (Join-Path $state 'credential.json') -Raw | ConvertFrom-Json
$controllerPath = Join-Path $state 'controller.json'
$controller = Get-Content -LiteralPath $controllerPath -Raw | ConvertFrom-Json
if ([Guid]$credential.projectId -ne $ProjectId -or $controller.projectId -ne $credential.projectId -or
    $controller.organizationId -ne $credential.organizationId -or $controller.workerId -ne $credential.workerId -or
    [DateTimeOffset]$credential.expiresAt -le [DateTimeOffset]::UtcNow) { throw 'Invalid or expired runner credential/configuration.' }
if ($controller.origin -ne 'http://localhost:5080/') { throw 'Local runner must use the configured loopback API.' }
Invoke-RestMethod ($controller.origin + 'health/ready') | Out-Null
& docker image inspect $controller.sandbox.image --format '{{.Id}}' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Sandbox image unavailable.' }
$secret = ConvertTo-SecureString $credential.protectedToken
$previousToken = $env:TEAMFLOW_RUNNER_TOKEN
try {
    $env:TEAMFLOW_RUNNER_TOKEN = [System.Net.NetworkCredential]::new('', $secret).Password
    $runtime = Join-Path $state ('runtime/' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $runtime | Out-Null
    Copy-Item (Join-Path $repoRoot 'apps/runner/bin/Debug/net10.0/*') -Destination $runtime -Recurse
    Write-Output "Runner active for project $ProjectId. Ctrl+C stops the worker."
    & dotnet (Join-Path $runtime 'TeamFlow.Runner.dll') run $controllerPath
    if ($LASTEXITCODE -ne 0) { throw 'Runner stopped with an error.' }
} finally { $env:TEAMFLOW_RUNNER_TOKEN = $previousToken; $secret.Dispose() }
} finally { $lock.Dispose() }
