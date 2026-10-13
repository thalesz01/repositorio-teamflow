param(
    [Parameter(Mandatory)][Guid]$OrganizationId,
    [Parameter(Mandatory)][Guid]$ProjectId,
    [Parameter(Mandatory)][string]$RepositoryPath
)
$ErrorActionPreference = 'Stop'
if ($OrganizationId -eq [Guid]::Empty -or $ProjectId -eq [Guid]::Empty) { throw 'Informe os IDs reais da equipe e do projeto.' }
if (-not [IO.Path]::IsPathFullyQualified($RepositoryPath)) { throw 'Informe um caminho absoluto.' }
$repository = (Resolve-Path -LiteralPath $RepositoryPath).ProviderPath
if (-not (Test-Path -LiteralPath (Join-Path $repository '.git') -PathType Container)) { throw 'O caminho precisa conter um repositório Git inicializado.' }
$codex = Get-Command codex -CommandType Application -ErrorAction Stop | Select-Object -First 1
if ([IO.Path]::GetExtension($codex.Source) -ne '.exe') { throw 'Configure uma instalação nativa codex.exe.' }
$target = Join-Path (Split-Path $PSScriptRoot -Parent) 'apps/api/appsettings.Local.json'
$config = if (Test-Path -LiteralPath $target) { Get-Content -LiteralPath $target -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
if (-not $config.ContainsKey('Integrations')) { $config.Integrations = @{} }
$orgKey = $OrganizationId.ToString('D'); $projectKey = $ProjectId.ToString('D')
if (-not $config.Integrations.ContainsKey($orgKey)) { $config.Integrations[$orgKey] = @{} }
if (-not $config.Integrations[$orgKey].ContainsKey($projectKey)) { $config.Integrations[$orgKey][$projectKey] = @{} }
$config.Integrations[$orgKey][$projectKey].Codex = @{ Enabled = $true }
$config.Integrations[$orgKey][$projectKey].Local = @{ RepositoryPath = $repository }
$config.Memory = @{ EnableIndexWorker = $true }
$config.Local = @{ EnablePublication = $true; EnableReview = $true }
$config.Codex = @{ Executable = $codex.Source; EnableRequests = $true }
$config | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $target -Encoding utf8
Write-Output 'Configuração local salva. Reinicie a API e conecte o caminho e a branch na interface. Execute codex login no mesmo usuário da API se necessário.'
