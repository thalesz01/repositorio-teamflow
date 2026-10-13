param(
    [Parameter(Mandatory)][string]$Config,
    [Microsoft.PowerShell.Commands.WebRequestSession]$Session
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$spec = Get-Content -LiteralPath $Config -Raw | ConvertFrom-Json
$allowed = @('origin', 'organizationId', 'projectId', 'repositoryUrl', 'agentProfileId', 'replacementProfileId', 'maxTotalCostUsd', 'budgets')
if (@($spec.PSObject.Properties.Name | Where-Object { $_ -notin $allowed }).Count -gt 0) { throw 'Pilot config has unsupported fields. Do not store credentials in this file.' }
foreach ($field in $allowed) { if ($field -notin $spec.PSObject.Properties.Name) { throw "Pilot config requires $field." } }
$origin = [Uri]$spec.origin
if (-not $origin.IsAbsoluteUri -or ($origin.Scheme -ne 'https' -and -not ($origin.Scheme -eq 'http' -and $origin.IsLoopback)) -or
    $origin.AbsolutePath -ne '/' -or $origin.UserInfo -or $origin.Query -or $origin.Fragment) { throw 'Use an HTTPS API origin or HTTP loopback, without credentials/path/query.' }
foreach ($field in @('organizationId', 'projectId', 'agentProfileId', 'replacementProfileId')) {
    $id = [Guid]::Empty
    if (-not [Guid]::TryParse([string]$spec.$field, [ref]$id) -or $id -eq [Guid]::Empty) { throw "Provide a nonempty UUID for $field." }
}
if ($spec.agentProfileId -eq $spec.replacementProfileId) { throw 'Use two distinct profiles for the transfer scenario.' }
$repository = [Uri]$spec.repositoryUrl
if (-not $repository.IsAbsoluteUri -or $repository.Scheme -ne 'https' -or $repository.Host -ne 'github.com' -or
    $repository.UserInfo -or $repository.Query -or $repository.Fragment -or $repository.AbsolutePath -notmatch '^/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?$') {
    throw 'Use the dedicated GitHub repository HTTPS URL without credentials.'
}
$phases = @('analysis', 'execution', 'feedback', 'indexing', 'reuseAnalysis', 'reuseExecution')
if (@($spec.budgets.PSObject.Properties.Name | Where-Object { $_ -notin $phases }).Count -gt 0) { throw 'Unsupported pilot budget phase.' }
$total = [decimal]$spec.maxTotalCostUsd
if ($total -le 0 -or $total -gt 100) { throw 'Provide an explicit total budget up to USD 100.' }
[decimal]$sum = 0
foreach ($phase in $phases) {
    if ($phase -notin $spec.budgets.PSObject.Properties.Name) { throw "Budget is required for $phase." }
    $amount = [decimal]$spec.budgets.$phase
    if ($amount -lt 0.01 -or $amount -gt 5 -or [decimal]::Round($amount, 2) -ne $amount) { throw "Budget for $phase must be USD 0.01..5 with two decimal places." }
    $sum += $amount
}
if ($sum -gt $total) { throw 'The sum of phase ceilings exceeds the authorized total. No request was made.' }
$checks = [Collections.Generic.List[object]]::new()
function Add-Check([string]$Name, [bool]$Passed, [string]$Detail) { $checks.Add([pscustomobject]@{ name = $Name; passed = $Passed; detail = $Detail }) }
foreach ($tool in @('dotnet', 'node', 'git', 'docker')) {
    Add-Check $tool ([bool](Get-Command $tool -CommandType Application -ErrorAction SilentlyContinue)) 'Executable presence only; Test-App verifies builds and tests.'
}
if ((Get-Command docker -CommandType Application -ErrorAction SilentlyContinue)) {
    $engine = & docker info --format '{{.ServerVersion}}' 2>$null
    Add-Check 'dockerEngine' ($LASTEXITCODE -eq 0) 'Docker engine must be reachable; this does not validate a production sandbox.'
}
try {
    $health = Invoke-WebRequest -Uri ([Uri]::new($origin, 'health/ready')) -MaximumRedirection 0 -TimeoutSec 10
    Add-Check 'databaseReadiness' ($health.StatusCode -eq 200) 'API readiness response; no external provider called.'
} catch { Add-Check 'databaseReadiness' $false 'API/database is unavailable or rejected the readiness request.' }
if ($null -eq $Session) { Add-Check 'projectSession' $false 'Supply an authenticated WebRequestSession to check project metadata.' }
else {
    $projectPath = "api/v1/organizations/$($spec.organizationId)/projects/$($spec.projectId)"
    try {
        $connection = Invoke-RestMethod -Uri ([Uri]::new($origin, "$projectPath/repository/")) -WebSession $Session -MaximumRedirection 0 -TimeoutSec 10
        $expected = $repository.AbsoluteUri.TrimEnd('/')
        $actual = ([string]$connection.remoteUrl).TrimEnd('/')
        if ($expected.EndsWith('.git', [StringComparison]::OrdinalIgnoreCase)) { $expected = $expected.Substring(0, $expected.Length - 4) }
        if ($actual.EndsWith('.git', [StringComparison]::OrdinalIgnoreCase)) { $actual = $actual.Substring(0, $actual.Length - 4) }
        Add-Check 'repository' ($actual -eq $expected -and $connection.baseCommit -match '^[a-fA-F0-9]{40}$') 'Connected repository/base must match the dedicated pilot target.'
        $profiles = @()
        for ($page = 1; $page -le 10; $page++) {
            $result = Invoke-RestMethod -Uri ([Uri]::new($origin, "$projectPath/profiles/?page=$page&pageSize=100")) -WebSession $Session -MaximumRedirection 0 -TimeoutSec 10
            $profiles += @($result.items)
            if ($profiles.Count -ge $result.total -or @($result.items).Count -eq 0) { break }
        }
        foreach ($field in @('agentProfileId', 'replacementProfileId')) {
            $profileMatch = @($profiles | Where-Object { $_.id -eq $spec.$field -and $null -eq $_.revokedAt -and 'textGeneration' -in $_.capabilities })
            Add-Check $field ($profileMatch.Count -eq 1) 'Active project profile with text generation; credential/provider validity is not tested.'
        }
    } catch { Add-Check 'projectMetadata' $false 'Project metadata could not be checked with this session. No credentials or response body are reported.' }
}
[pscustomobject]@{
    scope = 'read-only-environment-and-project-metadata'
    metadataChecksPassed = @($checks | Where-Object { -not $_.passed }).Count -eq 0
    plannedMaximumUsd = $sum
    authorizedTotalUsd = $total
    checks = $checks.ToArray()
    externalSmoke = 'not-run'
    manualRequirements = @('Provider credentials/flags and prices', 'Trusted CI App/checks', 'Two independent human identities', 'Approved test image/paths/command', 'Provider spending limits and reconciliation of unknown consumption')
} | ConvertTo-Json -Depth 6
