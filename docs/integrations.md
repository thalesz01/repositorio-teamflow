# Integrações Git e IA

Desde a [ADR 0008](decisions/0008-legacy-github-openai-path.md) as únicas integrações são Codex CLI e Git local. GitHub App, OpenAI API (texto e embeddings) e webhooks foram removidos; não há credencial de fornecedor, chave de API nem endpoint público de terceiros. Operação e passo a passo: [local-development.md](local-development.md).

## Fronteiras

| Fronteira | Contrato | Implementação |
| --- | --- | --- |
| Provedor de IA | `ITextModel.GenerateAsync(profile, prompt, budgetUsd, ct)` | `CodexTextModel` (processo `codex exec`, prompt por stdin) |
| Publicação Git | `IGitPublisher.PublishAsync(connection, plan, ct)` | `LocalGit` (plumbing Git: objetos e ref da execução) |
| Revisão Git | `IGitReview.ReadAsync(connection, snapshot, expectedHead, ct)` | `LocalGit` (head, base, parent e fast-forward) |
| Checkout no runner | `IRepositoryCheckout` | `GitCheckout` (somente pasta local autorizada) |

As interfaces existem porque são fronteiras reais (AGENTS.md §4); os testes as substituem por doubles nomeados, nunca por uma chamada real. A busca de memória é textual: não existe provedor de embeddings.

## Configuração do servidor

Chaves de processo ou `appsettings.Local.json` (ignorado pelo Git, carregado só em Development):

| Chave | Efeito |
| --- | --- |
| `Codex:Executable`, `Codex:EnableRequests`, `Codex:Model`, `Codex:MaxInputBytes`, `Codex:TimeoutSeconds` | CLI da máquina; sem `EnableRequests` nenhuma chamada de modelo ocorre |
| `Local:EnablePublication`, `Local:EnableReview` | habilitam publicação e revisão locais (padrão `false`) |
| `Integrations:<org>:<projeto>:Local:RepositoryPath`, `BrowseRoots`, `BrowseAllDrives`, `AllowRepositorySelection` | autorização de pastas por equipe/projeto |
| `Memory:EnableIndexWorker` | worker do índice textual |

Nenhuma delas contém segredo. O Data Protection continua existindo apenas para cookies e sessão (ver [development.md](development.md)).

## Conexão e checkout

`/api/v1/organizations/{org}/projects/{project}/repository/local/{inspect,folder}` verifica e salva a pasta; `GET` lê a conexão ativa; `DELETE` revoga. Há uma conexão ativa por projeto. Cada agendamento captura em snapshot imutável o `connectionId`/versão, a pasta, a branch base, o commit e a branch `teamflow/<executionId>`.

`POST /internal/v1/executions/{id}/repository-token` (`leaseToken`) devolve o `CheckoutGrant` literal `local`, depois de revalidar escopo, lease, conexão e que a branch base ainda aponta o commit do snapshot (`base_changed` caso contrário). O runner recusa qualquer origem que não seja a pasta local absoluta autorizada, sem links, e busca o commit exato por `file` em workspace próprio, com hooks, helpers de credencial e configuração global desabilitados.

## Perfis

Existe um vínculo técnico por projeto com o Codex da máquina (`POST profiles/machine`). `GET profiles/` pagina os vínculos e `DELETE profiles/{id}` revoga. Seleção em `ExecutionInput.agentProfileId` exige perfil ativo do mesmo projeto. Modelo, limites de entrada e tempo vêm da configuração da máquina. Preços ficam em zero porque o CLI não expõe tarifa: custo e tokens desconhecidos permanecem `null` (`BillingMode=cli`). Turnos históricos com `BillingMode=api` e custo desconhecido continuam bloqueando nova tentativa automática.

## Validação

As suítes da API e do runner usam PostgreSQL, Git e Docker reais, e um executável de teste que imita o protocolo do CLI (`tests/CodexCliFixture`). Isso valida protocolo, limites e falhas, não qualidade de modelo nem login real: o smoke com Codex real fica na T25. Comandos em [apps/api/README.md](../apps/api/README.md) e [apps/runner/README.md](../apps/runner/README.md).
