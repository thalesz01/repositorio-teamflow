param(
    [Parameter(Mandatory)][Guid]$OrganizationId,
    [Parameter(Mandatory)][Guid]$ProjectId,
    [Parameter(Mandatory)][string]$PolicyPath
)
$ErrorActionPreference = 'Stop'
# $IsWindows only exists in PowerShell 7; Windows PowerShell 5.1 is always Windows.
if ($PSVersionTable.PSEdition -eq 'Core' -and -not $IsWindows) { throw 'Local credentials use Windows DPAPI.' }
$repoRoot = Split-Path $PSScriptRoot -Parent
$policy = Get-Content -LiteralPath $PolicyPath -Raw | ConvertFrom-Json
if (-not $policy.policy.test.executable -or -not $policy.sandbox.image) { throw 'Explicit tool/test policy and sandbox image required.' }
$state = Join-Path $repoRoot "apps/runner/work/local/$ProjectId"
New-Item -ItemType Directory -Force $state | Out-Null
$credentialPath = Join-Path $state 'credential.json'
if (Test-Path -LiteralPath $credentialPath) { throw 'Credential already exists. Reuse Start-LocalRunner.ps1 or rotate through the owner API.' }
. (Join-Path $PSScriptRoot 'Use-LocalDatabase.ps1')
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    Push-Location (Join-Path $repoRoot 'apps/api')
    try {
        $issuedJson = & dotnet 'bin/Debug/net10.0/TeamFlow.Api.dll' '--provision-local-runner' $OrganizationId $ProjectId
        if ($LASTEXITCODE -ne 0) { throw 'Runner provisioning failed.' }
        $issued = ($issuedJson -join "`n") | ConvertFrom-Json
    } finally { Pop-Location }
    $protectedToken = ConvertFrom-SecureString (ConvertTo-SecureString $issued.token -AsPlainText -Force)
    @{ organizationId = $OrganizationId; projectId = $ProjectId; workerId = $issued.credential.workerId
       expiresAt = $issued.credential.expiresAt; protectedToken = $protectedToken } |
        ConvertTo-Json | Set-Content -LiteralPath $credentialPath -Encoding utf8
    @{ origin = 'http://localhost:5080/'; organizationId = $OrganizationId; projectId = $ProjectId
       workerId = $issued.credential.workerId; workspaceRoot = (Join-Path $state 'workspaces')
       policy = $policy.policy; sandbox = $policy.sandbox } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $state 'controller.json') -Encoding utf8
    Write-Output "Runner provisioned for project $ProjectId; token encrypted for this Windows user."
} finally {
    $issued = $null; $issuedJson = $null
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
}
