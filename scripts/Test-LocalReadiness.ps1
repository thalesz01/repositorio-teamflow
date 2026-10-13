param([Parameter(Mandatory)][Guid]$OrganizationId, [Parameter(Mandatory)][Guid]$ProjectId)
$ErrorActionPreference = 'Stop'
if ($OrganizationId -eq [Guid]::Empty -or $ProjectId -eq [Guid]::Empty) { throw 'Informe equipe/projeto reais.' }
$root = Split-Path $PSScriptRoot -Parent
$config = Get-Content -LiteralPath (Join-Path $root 'apps/api/appsettings.Local.json') -Raw | ConvertFrom-Json -AsHashtable
$project = $config.Integrations[$OrganizationId.ToString('D')][$ProjectId.ToString('D')]
$path = $project.Local.RepositoryPath
$executable = $config.Codex.Executable
if (-not $project.Codex.Enabled -or -not [IO.Path]::IsPathFullyQualified($path) -or -not [IO.Path]::IsPathFullyQualified($executable) -or [IO.Path]::GetExtension($executable) -ne '.exe') { throw 'Configuração local incompleta.' }
$null = & $executable login status 2>&1
$loginAvailable = $LASTEXITCODE -eq 0
$commit = (& git -c ('safe.directory=' + $path.Replace('\','/')) -C $path rev-parse --verify 'refs/heads/main^{commit}').Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-f0-9]{40}$') { throw 'A branch main não tem um commit válido.' }
$tracked = @(& git -c ('safe.directory=' + $path.Replace('\','/')) -C $path ls-files)
if ($LASTEXITCODE -ne 0) { throw 'Falha na leitura do índice Git.' }
$null = & docker info --format '{{.ServerVersion}}' 2>&1
$dockerAvailable = $LASTEXITCODE -eq 0
[ordered]@{
    scope = 'read-only-local-configuration'; organizationId = $OrganizationId; projectId = $ProjectId
    codexLoginAvailable = $loginAvailable; dockerAvailable = $dockerAvailable; baseCommit = $commit
    trackedFileCount = $tracked.Count; modelSmoke = 'not-run'; filesAdded = $false
    requiresCodeBaseline = $tracked.Count -eq 0
} | ConvertTo-Json
