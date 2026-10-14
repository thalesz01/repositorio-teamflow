# Desenvolvimento até T10

## Estado funcional

API e web têm builds independentes. Cadastro/login local, equipes, membros, projetos, tarefas e Kanban estão implementados. Iniciar uma tarefa pronta persiste uma execução e a deixa queued. Não há runner, uso de IA, alteração de código ou PR automático ainda: T11 em diante cobre essas partes.

## Subir localmente

Na raiz do repositório, criar .env com senha local conforme .env.example, se ainda não existir:

```powershell
docker compose -p teamflow-local up -d --wait
. ./scripts/Use-LocalDatabase.ps1
cd apps/api
dotnet tool restore
dotnet restore --locked-mode
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef database update
dotnet run --no-launch-profile
```

Em outro terminal, apps/web:

```powershell
npm ci
npm run dev
```

Abra http://localhost:3000 e cadastre uma conta. Não há usuário ou senha de aplicação predefinidos. Crie equipe, projeto e tarefa; mova Entrada → Em análise → Pronta → Em execução. Cadastre os critérios antes de deixar pronta. A tarefa permanece na fila até o runner ser implementado. Proprietário adiciona membros já cadastrados via API; membros podem trabalhar nas tarefas, mas não administrar projetos.

## Validar backend

Em apps/api, com conexão local configurada no terminal:

```powershell
dotnet restore --locked-mode
dotnet build --no-restore
dotnet format TeamFlow.Api.csproj --verify-no-changes --no-restore
dotnet restore ../../tests/TeamFlow.Api.Tests/TeamFlow.Api.Tests.csproj --locked-mode
dotnet test ../../tests/TeamFlow.Api.Tests/TeamFlow.Api.Tests.csproj --no-restore
dotnet format ../../tests/TeamFlow.Api.Tests/TeamFlow.Api.Tests.csproj --verify-no-changes --no-restore
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef migrations has-pending-model-changes
```

Integração cria bancos temporários teamflow_test_<uuid> na instância configurada. A credencial de teste precisa de CREATE DATABASE. A fixture usa timeout de conexão/comando de 30 segundos para não herdar o limite curto do health check local. A limpeza remove somente o banco com nome gerado pela própria fixture; não remove o banco da aplicação. Não fornecer conexão de produção. Testes não chamam IA nem Git externos.

## Validar web e contratos

Em apps/web:

```powershell
npm ci
npm run generate:api
npm run lint
npm run typecheck
npm run build
npm audit --audit-level=high
```

API em Development serve /openapi/v1.json. Atualizar contracts/api/openapi.json e executar generate:api quando endpoints mudarem. Types pertencem ao consumidor; build não consulta a API. API_URL é lida ao gerar a configuração Next, inclusive rewrites: configure-a antes do build e mantenha o mesmo valor ao iniciar. O padrão é http://localhost:5080.

## Testes de navegador

API e banco devem estar ativos. Em apps/web:

```powershell
npx playwright install chromium
npm run build
npm start -- --port 13000
```

Em outro terminal, apps/web:

```powershell
npm run test:e2e
```

A API sob teste também usa o executável de fixture no lugar do Codex (`Codex__Executable=tests/CodexCliFixture/bin/Debug/net10.0/CodexCliFixture.exe` e `Codex__EnableRequests=true`), para que a verificação da IA seja real sem chamar modelo. A API sob teste precisa liberar a pasta usada pelos testes: inicie-a com `Local__BrowseRoots__0=<pasta>` e `Local__AllowRepositorySelection=true` e informe a mesma pasta em `E2E_FOLDER_ROOT` (padrão: `teamflow-e2e-folders` no diretório temporário do sistema). E2E_URL altera a URL do frontend; padrão http://localhost:13000. Os testes criam contas aleatórias e workspace local exclusivo; não apagam dados alheios. O cenário principal cobre cadastro, criação de tarefa com a pasta escolhida no seletor, execução (fila), correção com comentário em uma linha e conclusão; revisão e publicação usam respostas simuladas. Relatórios são ignorados no Git.

## CI

Há um workflow por aplicação em `.github/workflows` (`api.yml`, `runner.yml`, `web.yml`), cada um com filtro de caminhos e sem deploy conjunto. Eles reproduzem os comandos de `scripts/Test-App.ps1`: restore com lockfile, build, testes, `format --verify-no-changes`, EF sem alterações pendentes (API) e, no web, tipos gerados iguais ao contrato, typecheck, lint e build. O repositório ainda não tem remoto: os workflows só executam depois de publicado no GitHub. Playwright não roda no CI (exige API, banco e conta de fixtures).

Limites conhecidos, para não confundir CI verde com cobertura total:

- Três testes da API dependem de um `codex.exe` nativo (Windows, ADR 0005) e ficam fora do filtro do CI: `NativeCliReceivesStdinHasBoundedLifecycleAndDoesNotInventBillingAsync`, `LocalConfigurationChecksProjectAuthorityAndPinsCommittedBaseAsync` e `ConfirmedCliAnswerCanReplayAndRecoverWithUnknownBillingAsync`. Continuam obrigatórios na execução local com `scripts/Test-App.ps1 -App api`.
- Os workflows usam Ubuntu com Docker para o sandbox Linux. Em 2026-10-13 os passos de build, format, EF e 70 testes da API foram reproduzidos num container Linux local; os testes que precisam de Docker (sandbox) e a suíte do runner não foram executados no Linux, então o primeiro run real do CI deve ser conferido.
- As actions usam tags principais (`@v4`), não SHAs; fixar por SHA é um endurecimento pendente.

## Fronteiras de segurança

- Sessão em cookie HttpOnly/SameSite Strict, oito horas, sem JWT no browser. Security stamp verificado a cada requisição; associação ao tenant consultada no banco.
- Buscar /api/v1/auth/csrf e enviar token no header X-CSRF-Token para comandos, incluindo login/registro. Buscar novamente após troca de identidade.
- Login/registro têm limite por IP e bloqueio nativo de tentativas; não confiar em headers de proxy sem configuração de infraestrutura.
- APIs autenticadas não são cacheáveis; corpo limitado a 128 KiB nesta etapa.
- Desenvolvimento usa chaves de Data Protection em apps/api/work/keys, ignoradas pelo Git. Fora de Development/Testing, DataProtection:KeyPath é obrigatório. Antes de implantação, prover HTTPS, armazenamento e criptografia das chaves, gestão de segredos e verificação/recuperação de e-mail. Essas partes não estão declaradas prontas para produção.

## Execução durável

POST .../tasks/{id}/executions exige Idempotency-Key e a versão da tarefa. Mesma chave/payload retorna a execução anterior; payload diferente retorna conflito. Uma execução ativa por tarefa é protegida por índice único. Snapshot e limites são persistidos; nenhuma tarefa externa é disparada antes do registro transacional.

Limites do MVP: 30..3600 segundos, 1..5 tentativas, 1..200 passos e USD 0,01..100 com duas casas decimais. São tetos registrados; fiscalização de passos/custos de chamadas reais depende do harness. O tempo começa na primeira reivindicação, não enquanto aguarda na fila.

Claim e renovação são operações internas testadas, sem endpoints técnicos públicos ainda. FOR UPDATE SKIP LOCKED evita reivindicação concorrente; lease vencido pode ser retomado com token novo. Token antigo não renova posse. Tentativas esgotadas falham a execução; cancelamento de trabalho leased permanece solicitado até sua posse expirar. Checkpoints, ferramentas e reconciliação de Git/PR são tarefas posteriores.

O helper Use-LocalDatabase.ps1 usa timeout de conexão de 30 segundos para tolerar inicialização e carga local do Docker/PostgreSQL; esse limite não é o timeout de execução do agente.
