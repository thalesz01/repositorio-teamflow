# Sets up the runner of one project: its credential (encrypted for this Windows user) and its tool/test policy.
# The policy is the operator's decision, so it is never guessed: pass -PolicyPath, copy another policy with
# -CopyPolicyFrom, or edit the template this script writes on the first run and run it again.
# With -RunOnHost (only with -Folder) the test command runs on this computer instead of in Docker; see docs/decisions/0013.
# With -Folder the policy applies only to tasks in that folder (the runner reads it again for each execution, no restart);
# without it, the policy is the default for every folder that has no entry of its own.
# Works on Windows PowerShell 5.1 and PowerShell 7.
param(
    [Parameter(Mandatory)][Guid]$OrganizationId,
    [Parameter(Mandatory)][Guid]$ProjectId,
    [string]$Folder,
    [string]$PolicyPath,
    [Guid]$CopyPolicyFrom,
    [switch]$RunOnHost
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$runnerRoot = Join-Path $root 'apps/runner/work/local'
$state = Join-Path $runnerRoot $ProjectId.ToString()
$hasRunner = Test-Path -LiteralPath (Join-Path $state 'credential.json')
if ($hasRunner -and -not $Folder) {
    Write-Output "Project $ProjectId already has a runner. Start it with ./scripts/Start-TeamFlow.ps1 (policy: $state/controller.json)."
    Write-Output 'To give one folder its own tools and tests, run again with -Folder <path>.'
    return
}
if ($RunOnHost -and -not $Folder) { throw '-RunOnHost applies to one folder: pass -Folder as well.' }
if ($Folder) {
    # IsPathFullyQualified does not exist in Windows PowerShell 5.1 (.NET Framework).
    if ($Folder -notmatch '^([A-Za-z]:[\\/]|\\\\)') { throw '-Folder needs an absolute path.' }
    $Folder = [IO.Path]::GetFullPath($Folder).TrimEnd('\', '/')
}
New-Item -ItemType Directory -Force $state | Out-Null

function Test-Policy([string]$Path) {
    $candidate = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    # A host policy runs the test command on this computer, so it needs no sandbox image.
    return [bool]($candidate.policy.test.executable -and ($RunOnHost -or $candidate.sandbox.image))
}

$draftName = if ($Folder) { 'policy-' + ((Split-Path $Folder -Leaf) -replace '[^A-Za-z0-9._-]', '_') + '.json' } else { 'policy.json' }
$draft = Join-Path $state $draftName
if ($PolicyPath) { $source = $PolicyPath }
elseif ($CopyPolicyFrom) {
    $source = Join-Path $runnerRoot "$CopyPolicyFrom/controller.json"
    if (-not (Test-Path -LiteralPath $source)) { throw "Project $CopyPolicyFrom has no controller.json to copy." }
    Write-Warning 'The copied policy keeps the other project''s readable/editable files and test command; adjust it if this folder differs.'
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

if (-not $hasRunner) {
    $apiDll = Join-Path $root 'apps/api/bin/Debug/net10.0/TeamFlow.Api.dll'
    if (-not (Test-Path -LiteralPath $apiDll)) {
        Push-Location (Join-Path $root 'apps/api')
        try { dotnet restore --locked-mode | Out-Null; dotnet build --no-restore -v q; if ($LASTEXITCODE -ne 0) { throw 'API build failed.' } } finally { Pop-Location }
    }
    & (Join-Path $PSScriptRoot 'Provision-LocalRunner.ps1') -OrganizationId $OrganizationId -ProjectId $ProjectId -PolicyPath $source
}
if ($Folder) {
    $controllerPath = Join-Path $state 'controller.json'
    $controller = Get-Content -LiteralPath $controllerPath -Raw | ConvertFrom-Json
    $candidate = Get-Content -LiteralPath $source -Raw | ConvertFrom-Json
    $folders = [ordered]@{}
    if ($controller.PSObject.Properties['folders']) { foreach ($entry in $controller.folders.PSObject.Properties) { $folders[$entry.Name] = $entry.Value } }
    $entry = [ordered]@{ policy = $candidate.policy; sandbox = $candidate.sandbox }
    if ($RunOnHost) {
        $entry = [ordered]@{ policy = $candidate.policy; execution = 'host' }
        Write-Warning 'The test command will run directly on this computer, without a container (docs/decisions/0013). Use it only for folders you trust.'
    }
    $folders[$Folder] = $entry
    if ($controller.PSObject.Properties['folders']) { $controller.folders = $folders } else { $controller | Add-Member -NotePropertyName folders -NotePropertyValue $folders }
    $controller | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $controllerPath -Encoding utf8
    Write-Output "Folder policy saved for $Folder. A running runner picks it up on its next execution."
}
Write-Output 'Next: ./scripts/Start-TeamFlow.ps1'
