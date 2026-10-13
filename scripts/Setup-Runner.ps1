# Sets up the runner of one project: its credential (encrypted for this Windows user) and its tool/test policy.
# The policy is the project's decision, so it is never guessed: pass -PolicyPath, copy a configured project's policy
# with -CopyPolicyFrom, or edit the template this script writes on the first run and run it again.
# Works on Windows PowerShell 5.1 and PowerShell 7.
param(
    [Parameter(Mandatory)][Guid]$OrganizationId,
    [Parameter(Mandatory)][Guid]$ProjectId,
    [string]$PolicyPath,
    [Guid]$CopyPolicyFrom
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$runnerRoot = Join-Path $root 'apps/runner/work/local'
$state = Join-Path $runnerRoot $ProjectId.ToString()
if (Test-Path -LiteralPath (Join-Path $state 'credential.json')) {
    Write-Output "Project $ProjectId already has a runner. Start it with ./scripts/Start-TeamFlow.ps1 (policy: $state/controller.json)."
    return
}
New-Item -ItemType Directory -Force $state | Out-Null

function Test-Policy([string]$Path) {
    $candidate = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    return [bool]($candidate.policy.test.executable -and $candidate.sandbox.image)
}

$draft = Join-Path $state 'policy.json'
if ($PolicyPath) { $source = $PolicyPath }
elseif ($CopyPolicyFrom) {
    $source = Join-Path $runnerRoot "$CopyPolicyFrom/controller.json"
    if (-not (Test-Path -LiteralPath $source)) { throw "Project $CopyPolicyFrom has no controller.json to copy." }
    Write-Warning 'The copied policy keeps the other project''s readable/editable files and test command; adjust it if this project differs.'
}
elseif (Test-Path -LiteralPath $draft) { $source = $draft }
else {
    # Reuse the pinned sandbox image of any configured runner so the template needs no digest hunting.
    $image = $null
    foreach ($other in @(Get-ChildItem -LiteralPath $runnerRoot -Directory -ErrorAction SilentlyContinue)) {
        $path = Join-Path $other.FullName 'controller.json'
        if (Test-Path -LiteralPath $path) { $image = (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json).sandbox; break }
    }
    if (-not $image) { $image = [ordered]@{ image = ''; cpus = 1; memoryMb = 768; pids = 128 } }
    [ordered]@{
        policy = [ordered]@{
            readable = @(); editable = @()
            test = [ordered]@{ executable = ''; arguments = @(); timeout = '00:02:00' }
        }
        sandbox = $image
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $draft -Encoding utf8
    Write-Output "Policy template written to $draft"
    Write-Output 'Fill in policy.readable, policy.editable (exact paths or globs like src/**/*.cs) and policy.test.executable/arguments,'
    Write-Output 'then run this script again. See docs/runner-local.md. The runner never runs commands the project did not list.'
    return
}
if (-not (Test-Policy $source)) { throw "Policy in $source is incomplete: it needs policy.test.executable and sandbox.image." }

$apiDll = Join-Path $root 'apps/api/bin/Debug/net10.0/TeamFlow.Api.dll'
if (-not (Test-Path -LiteralPath $apiDll)) {
    Push-Location (Join-Path $root 'apps/api')
    try { dotnet restore --locked-mode | Out-Null; dotnet build --no-restore -v q; if ($LASTEXITCODE -ne 0) { throw 'API build failed.' } } finally { Pop-Location }
}
& (Join-Path $PSScriptRoot 'Provision-LocalRunner.ps1') -OrganizationId $OrganizationId -ProjectId $ProjectId -PolicyPath $source
Write-Output 'Next: ./scripts/Start-TeamFlow.ps1'
