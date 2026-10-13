# TeamFlow API

ASP.NET Core / .NET 10 com EF Core e PostgreSQL. Build independente do frontend e runner. SDK 10.0.400 fixado localmente, pacotes e ferramentas com versões explícitas e lockfiles versionados.

## Banco local

Na raiz do repositório, copie `.env.example` para `.env` e defina TEAMFLOW_DB_PASSWORD com 16..128 letras, dígitos, underscores ou hífens. Nunca versionar `.env`. O banco local exclusivo usa porta 55432 e imagem pgvector fixada por digest.

```powershell
docker compose -p teamflow-local up -d --wait
. ./scripts/Use-LocalDatabase.ps1
cd apps/api
dotnet tool restore
dotnet restore --locked-mode
dotnet ef database update
dotnet ef migrations has-pending-model-changes
```

O helper só configura a variável no terminal atual; não imprime credenciais. Não é necessário usar Compose: uma instância PostgreSQL com extensão vector disponível pode ser configurada por `ConnectionStrings__Database`. Não usar banco de outro projeto para testes. Migrações são aplicadas por comando explícito, nunca automaticamente em toda inicialização.

## Verificar

Neste diretório:

```powershell
dotnet build --no-restore
dotnet format TeamFlow.Api.csproj --verify-no-changes --no-restore
dotnet restore ../../tests/TeamFlow.Api.Tests/TeamFlow.Api.Tests.csproj --locked-mode
dotnet test ../../tests/TeamFlow.Api.Tests/TeamFlow.Api.Tests.csproj --no-restore
dotnet format ../../tests/TeamFlow.Api.Tests/TeamFlow.Api.Tests.csproj --verify-no-changes --no-restore
```

A suíte inclui testes HTTP e integração com PostgreSQL real para equipes, tarefas, concorrência e fila. Configure a conexão local antes de dotnet test. A fixture cria e remove apenas seu banco temporário; a credencial de teste precisa de CREATE DATABASE. Veja docs/development.md na raiz para os detalhes.

## Executar

Configure a conexão no terminal, então:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --no-build --no-launch-profile
```

Em outro terminal:

```powershell
Invoke-RestMethod http://localhost:5080/health
Invoke-RestMethod http://localhost:5080/health/ready
```

`/health` verifica o processo e retorna 200 Healthy. `/health/ready` verifica acesso ao banco e retorna 503 quando indisponível; não verifica migrações ou integrações futuras. Pare com Ctrl+C.

## Configuração

Variáveis de ambiente ou argumentos substituem appsettings.json. A conexão é obrigatória; ausência gera erro claro na inicialização, sem imprimir seu valor. Não há loader de `.env` no backend: o helper de desenvolvimento configura a variável nativa.

| Chave | Uso |
| --- | --- |
| `ConnectionStrings__Database` | Conexão PostgreSQL, fornecida fora dos arquivos versionados |
| `Server__Url` | Endereço HTTP(S), padrão http://localhost:5080; sem path, query, fragmento ou credenciais |
| `AllowedHosts` | Padrão localhost, 127.0.0.1 e [::1] |
| `ASPNETCORE_ENVIRONMENT` | Development para disponibilizar OpenAPI localmente |

Server:Url é a fonte de configuração do endereço; não usar ASPNETCORE_URLS, --urls ou endpoints Kestrel em paralelo. HTTPS exige certificado válido configurado nativamente. HTTP padrão é apenas local; hospedagem não definida.

```powershell
dotnet run --no-build --no-launch-profile -- --Server:Url=http://localhost:5081
dotnet run --no-build --no-launch-profile -- --Server:Url=invalid
```

O último comando deve encerrar com código não zero por OptionsValidationException.

## OpenAPI e cliente

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --no-build --no-launch-profile
```

Documento em http://localhost:5080/openapi/v1.json. O documento descreve endpoints de produto à medida que forem implementados; health checks são operacionais e não aparecem nele. Os tipos TypeScript são gerados no consumidor com openapi-typescript, sem compartilhar entidades EF. Estratégia em contracts/api/README.md.

Erros HTTP usam ProblemDetails, code e traceId; X-Correlation-Id identifica a requisição. APIs de produto serão /api/v1, com paginação limitada e JSON camelCase. Identity, organizações/membros, CRUD de projetos, tarefas e execuções estão implementados. Endpoints técnicos exigem a autenticação específica de runner descrita abaixo.

## Encerrar banco

Na raiz, `docker compose -p teamflow-local stop` preserva os dados. Não remover volumes como parte de testes ou encerramento rotineiro.

## Runner técnico (T12)

Proprietário emite, consulta metadata, rotaciona e revoga credenciais por projeto em `/api/v1/organizations/{organizationId}/projects/{projectId}/runners`. Sessão humana e CSRF obrigatórios. Token aleatório retornado uma vez, apenas SHA-256 persistido, validade de 1..30 dias. Paginação e escopo seguem os endpoints de produto.

O protocolo `/internal/v1/executions/claim` e `/{id}/heartbeat` exige Bearer de runner. Cookies humanos não autenticam o protocolo; Bearer não autentica a API humana. Claim limitado ao projeto e serializado contra revogação. Heartbeat exige a mesma credencial, worker, lease token e posse vigente. Revogação/expiração/cancelamento impedem renovação.

HTTP técnico só no loopback de Development/Testing; produção exige HTTPS. Aplicar migração RunnerCredentials antes de iniciar. O consumo automático é responsabilidade do runner independente, nos modos run/run-once; a API mantém fila, leases, budgets, checkpoints e resultados.

No Windows, novas chaves Data Protection usam DPAPI do usuário do serviço. O diretório padrão passou a `work/protected-keys` para não reutilizar chaves antigas sem proteção; sessões locais antigas precisam de novo login. Não apagar esse diretório na limpeza de execuções. Outros ambientes precisam configurar proteção de chaves antes de armazenar segredos duráveis de integrações.
## Integrações (T14/T15)

Migração GitAndAgentProfiles adiciona conexões de repositório, perfis protegidos e snapshots de execução. Consulte [guia de integrações](../../docs/integrations.md) para configuração por projeto, permissões, proteção de chaves e limites do adaptador. Testes reais pagos permanecem opt-in; testes padrão usam doubles.

## Checkpoints (T16)

Aplicar a migração ExecutionCheckpoints. Escrita/leitura exigem Bearer técnico e lease vigente; detalhes em [harness](../../docs/harness.md). A suíte de integração também coordena runner e API em fixture Git local com Docker real; instalar previamente a imagem fixada no README do runner. As aplicações continuam independentes.

## Progresso e publicação

Progresso JSON/SSE e relatórios autorizados: [documentação](../../docs/execution-progress.md). Publicação local opt-in e comando manual: [documentação](../../docs/pull-request-publication.md). Defina Local__EnablePublication=true somente na máquina do piloto; a permissão afeta todas as execuções elegíveis do servidor. Padrão false. Polling de publicação usa leases e no máximo três tentativas. Revisão independente e merge fast-forward confirmado: docs/pull-request-review.md.
## Validação integrada e piloto

Consultar docs/pilot.md na raiz. O comando scripts/Test-App.ps1 -App api executa os checks desta aplicação; -Restore restaura dependências fixadas. O smoke com provedores reais depende de recursos e orçamento autorizados. Produção permanece bloqueada; Docker local não é a fronteira de produção multi-tenant.


## Modo local aceito em 2026-10-12

O fluxo utiliza Codex CLI e repositórios Git locais; siga docs/local-development.md e as ADRs 0005 e 0008 (caminhos relativos à raiz do monorepo). GitHub, OpenAI API e webhooks foram removidos. Configuração de Development por projeto, clone separado, revisão de diff e merge fast-forward confirmado. Embeddings externos não são necessários: existe indexação textual local.
