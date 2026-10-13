# Backlog de implementação

Última organização: 2026-10-11. Fonte de padrões: [AGENTS.md](AGENTS.md).

Este arquivo acompanha a implementação do produto; não é o Kanban que o produto oferecerá. T01–T17 concluídas; consultar datas e evidências nos registros abaixo. A documentação inicial existente não conta como implementação.

## Como manter este arquivo

- IDs são permanentes. Não renumerar nem apagar tarefas concluídas; acrescentar novos IDs.
- Checkbox `[ ]` significa não concluída; `[x]` significa concluída. Status permitidos: Pendente, Em andamento, Bloqueada, Concluída, Cancelada. Cancelada permanece com `[ ]` e razão.
- Antes de trabalhar, conferir Git, padrões e dependências. Escolher a primeira tarefa pendente com dependências concluídas, salvo prioridade explícita do usuário. Marcar status, responsável e início antes da implementação.
- Conclusão exige escopo entregue, todos os critérios atendidos e checks pertinentes aprovados. Se uma validação essencial não puder executar, manter Bloqueada ou Em andamento; não marcar concluída por estimativa.
- Ao concluir, alterar somente o checkbox e status da tarefa correspondente, atualizar o registro com data, resumo, arquivos e evidências reais. Atualizar dependentes apenas se o bloqueio realmente foi resolvido.
- Não substituir evidência por “testado”. Registrar comando, resultado e limite de testes com doubles. Registrar commit/PR quando existir; sua ausência não impede uma entrega local de ser marcada feita.
- Tarefas de decisão terminam com ADR aceito pelo responsável, não apenas recomendação do agente. Não presumir fornecedores aprovados.
- Uma tarefa de implementação pode concluir antes do merge do seu próprio PR se os critérios forem atendidos; isso não altera a regra de conclusão das tarefas gerenciadas pelo produto.
- Agentes editando simultaneamente devem reivindicar IDs distintos e preservar registros alheios. Nenhum lock distribuído é presumido por este Markdown. Conferir conflitos antes de salvar.
- Se escopo crescer, dividir em novos IDs e atualizar dependências/critério remanescente. Não marcar uma tarefa parcial como completa nem alterar critérios para esconder falhas.

Registro ao iniciar/concluir (substituir o campo Registro da tarefa; não duplicar o template inteiro por padrão):

```text
Responsável: identificação do agente/pessoa
Início: YYYY-MM-DD
Conclusão: YYYY-MM-DD ou —
Resumo: comportamento entregue
Arquivos: caminhos relativos relevantes
Validação: comando → resultado, ou evidência de decisão aceita
Limitações/bloqueio: motivo, impacto e ação necessária, ou nenhuma
Referência: commit/PR quando houver, ou entrega local sem commit
```

## Ordem e marcos

| Marco | Tarefas | Resultado |
| --- | --- | --- |
| Base | T01–T06 | Aplicações independentes, dados e acesso por equipe |
| Kanban | T07–T08 | Tarefas com estados e interface utilizável |
| Execução | T09–T18 | Decisões, runner, harness e recuperação observáveis |
| Revisão | T19–T20 | PR, feedback, CI e merge rastreáveis |
| Memória | T21–T24 | Conhecimento com fontes, recuperação e portabilidade |
| Piloto | T25 | Fluxo completo validado com limites explícitos |

Próximo passo: nenhum item obrigatório pendente. T01–T37 estão concluídas. Esta ordem não autoriza trabalho paralelo automaticamente.

## Tarefas

### [x] T01 — Criar a base independente da API

- Status: Concluída
- Dependências: —
- Componente: apps/api
- Escopo: Criar ASP.NET Core com Minimal APIs, nullable, configuração validada e health check; fixar SDK e dependências.
- Critérios de aceitação:
  - [x] Build e formatação passam em comando documentado
  - [x] API inicia sozinha e responde ao health check
  - [x] README local contém configuração e comandos reais.
- Registro:
  - Responsável: Codex
  - Início: 2026-10-10
  - Conclusão: 2026-10-10
  - Resumo: API ASP.NET Core independente, SDK 10.0.400 fixado, configuração de endereço validada e health check nativo.
  - Arquivos: apps/api/{TeamFlow.Api.csproj,global.json,Program.cs,ServerOptions.cs,appsettings.json,packages.lock.json,README.md}.
  - Validação: em apps/api, `dotnet restore --locked-mode` aprovado; `dotnet build --no-restore` aprovado com 0 avisos e 0 erros; `dotnet format TeamFlow.Api.csproj --verify-no-changes --no-restore` aprovado.
  - Validação HTTP: `dotnet run --no-build --no-launch-profile -- --Server:Url=http://localhost:15080` e `Invoke-WebRequest http://localhost:15080/health` retornaram 200 Healthy; serviço de verificação encerrado.
  - Validação negativa: `dotnet run --no-build --no-launch-profile -- --Server:Url=invalid` rejeitado com OptionsValidationException e código de saída não zero.
  - Limitações: health check cobre apenas o processo; não existem testes automatizados, banco ou autenticação nesta base. Nenhum teste foi declarado executado.
  - Referência: entrega local sem commit.

### [x] T02 — Criar a base independente da interface

- Status: Concluída
- Dependências: —
- Componente: apps/web
- Escopo: Criar Next.js/React com TypeScript strict, layout mínimo e configuração do endereço da API.
- Critérios de aceitação:
  - [x] Lint, typecheck e build passam
  - [x] interface inicia sem compilar backend
  - [x] configuração pública não expõe segredos.
- Registro:
  - Responsável: Codex
  - Início: 2026-10-10
  - Conclusão: 2026-10-10
  - Evidências: Base Next.js independente, configuração API_URL server-side, proxy e tipos gerados no consumidor. Arquivos: apps/web. Validação: npm ci, npm run lint, npm run typecheck e npm run build aprovados; npm audit com 0 vulnerabilidades; npm start -- --port 13000 respondeu HTTP 200 sem API. Sem testes automatizados de UI nesta base.
  - Referência: entrega local sem commit.

### [x] T03 — Preparar PostgreSQL e migrações

- Status: Concluída
- Dependências: T01
- Componente: apps/api; infraestrutura local
- Escopo: Adicionar EF Core/PostgreSQL, migração inicial e configuração local reproduzível; preparar suporte a pgvector sem modelo de memória antecipado.
- Critérios de aceitação:
  - [x] Migração funciona em banco vazio
  - [x] indisponibilidade do banco produz falha clara
  - [x] instruções de subir, migrar e verificar banco estão documentadas.
- Registro:
  - Responsável: Codex
  - Início: 2026-10-10
  - Conclusão: 2026-10-10
  - Evidências: EF Core/PostgreSQL e migração InitialDatabase com vector; Compose exclusivo fixado por digest, senha local ignorada e helper de configuração. Arquivos: apps/api/Data, apps/api/Migrations, compose.yaml, scripts/Use-LocalDatabase.ps1. Validação: dotnet ef database update aplicado em banco vazio; has-pending-model-changes sem divergência; pgvector 0.8.7 confirmado em PostgreSQL real; readiness 200 com banco e 503 após parada controlada.
  - Referência: entrega local sem commit.

### [x] T04 — Padronizar HTTP, erros e contratos

- Status: Concluída
- Dependências: T01
- Componente: apps/api; contracts/api
- Escopo: Configurar /api/v1, JSON, ProblemDetails, correlação e OpenAPI; definir estratégia simples para cliente web gerado.
- Critérios de aceitação:
  - [x] Erros de entrada e falhas internas têm resposta consistente sem dados sensíveis
  - [x] especificação pode ser gerada reproduzivelmente
  - [x] contrato inclui paginação e limites onde aplicável.
- Registro:
  - Responsável: Codex
  - Início: 2026-10-10
  - Conclusão: 2026-10-10
  - Evidências: ProblemDetails com code/traceId, correlação, paginação limitada, OpenAPI e geração TypeScript reproduzível. Arquivos: apps/api/Http, Program.cs, contracts/api, apps/web/scripts/generate-api.mjs, tests/TeamFlow.Api.Tests. Validação: build sem avisos/erros, format API/testes aprovados; 7 testes aprovados; OpenAPI exportado e tipos gerados; HTTP 404 contém código e traceId. Snapshot tem paths vazios até endpoints da T06; não há CRUD fictício.
  - Referência: entrega local sem commit.

### [x] T05 — Escolher autenticação e identidade

- Status: Concluída
- Dependências: T01, T02
- Componente: docs/decisions
- Escopo: Preparar opções enxutas de autenticação, sessão e associação a organizações; documentar recomendação e decisão do responsável.
- Critérios de aceitação:
  - [x] ADR aceito identifica mecanismo, armazenamento de sessão, autorização e credenciais locais de desenvolvimento
  - [x] não implementar provedor não escolhido.
- Registro: Codex; início e conclusão 2026-10-10. ADR 0001 aprovado explicitamente pelo usuário nesta conversa. Identity/cookies, antiforgery, autorização por membros e cadastro sem senha fixa definidos; entrega local sem commit.

### [x] T06 — Implementar organizações, membros e projetos

- Status: Concluída
- Dependências: T03, T04, T05
- Componente: apps/api
- Escopo: Implementar autenticação escolhida, organizações, associação de membros e CRUD mínimo de projetos com permissões explícitas.
- Critérios de aceitação:
  - [x] Usuário sem associação não acessa outro tenant
  - [x] payload não consegue trocar tenant autenticado
  - [x] testes de integração cobrem leitura, escrita e IDs de outro projeto.
- Registro: Codex; início/conclusão 2026-10-10. Identity, cookies/CSRF, organizações/membros e CRUD de projetos entregues. 9 testes aprovados; integração PostgreSQL em banco isolado cobre leituras/escritas cruzadas, papel member, revogação, concorrência de projeto e logout. Migração evolutiva aplicada. Entrega local sem commit.

### [x] T07 — Implementar tarefas e transições

- Status: Concluída
- Dependências: T06
- Componente: apps/api/Features/Tasks
- Escopo: Persistir descrição, critérios de aceitação, responsável, dependências e estados; implementar criação, consulta, edição e transições permitidas.
- Critérios de aceitação:
  - [x] Coleções são paginadas
  - [x] transições inválidas e escrita concorrente geram conflito
  - [x] testes cobrem permissões, dependências e proibição de conclusão manual sem merge.
- Registro: Codex; início/conclusão 2026-10-10. CRUD de tarefas, critérios, responsáveis, dependências, transições e versões entregues. 12 testes aprovados; PostgreSQL real cobre isolamento, paginação, ciclos, dependências de outro projeto, concorrência e conclusão manual proibida. Migração Tasks aplicada. Entrega local sem commit.

### [x] T08 — Implementar Kanban e detalhe de tarefa

- Status: Concluída
- Dependências: T02, T04, T07
- Componente: apps/web
- Escopo: Conectar interface aos contratos; criar, editar e movimentar tarefas, mostrando critérios e dependências.
- Critérios de aceitação:
  - [x] Ações têm loading, erro e reconciliação de estado
  - [x] teclado oferece alternativa ao arraste
  - [x] rejeição do backend não deixa cartão em estado incorreto.
- Registro:
  - Responsável: Codex; início/conclusão 2026-10-10.
  - Entrega: cadastro/login, equipes/projetos, Kanban, edição, responsáveis, dependências, arraste e seleção por teclado. Estado recarregado após sucesso ou rejeição; tipos gerados de OpenAPI, sem importar backend.
  - Arquivos: apps/web/features/workspace, app/workspace.css, api/schema.d.ts e tests/kanban.spec.ts.
  - Validação: lint, typecheck e build de produção aprovados; 2 testes Playwright aprovados contra API/PostgreSQL reais. Fluxo cobre conflito provocado por outro cliente, cartão mantido em Pronta após rejeição, execução na fila, cancelamento e logout; tela estreita sem overflow no formulário.
  - Limitação: sem execução real de IA; testes geram contas/workspaces locais exclusivos.
  - Referência: entrega local sem commit.

### [x] T09 — Escolher execução durável e contrato do runner

- Status: Concluída
- Dependências: T07
- Componente: docs/decisions; contracts/execution
- Escopo: Comparar mecanismo persistido simples e motor durável; definir despacho, leases, retomada, cancelamento, checkpoints e mensagens versionadas.
- Critérios de aceitação:
  - [x] ADR aceito inclui falhas entre persistência e despacho, concorrência e recuperação após restart
  - [x] contrato descreve autenticação entre serviços e idempotência
  - [x] não introduzir Temporal automaticamente.
- Registro: Codex; início/conclusão 2026-10-10. ADR 0002 aprovado explicitamente pelo usuário. Fila PostgreSQL transacional, idempotência, leases/fencing e recuperação definidos. Contrato em contracts/execution; identidade técnica e rotatividade especificadas, provisionamento na T11/T12. Nenhum Temporal adicionado. Entrega local sem commit.

### [x] T10 — Persistir e agendar execuções

- Status: Concluída
- Dependências: T09
- Componente: apps/api/Features/Executions; apps/worker se necessário
- Escopo: Implementar estratégia escolhida, estados da execução, agendamento persistido e validação de limites/dependências.
- Critérios de aceitação:
  - [x] Comando repetido não cria duplicata
  - [x] apenas uma execução ativa por tarefa sob concorrência
  - [x] restart não perde execução e worker não inicia trabalho sem registro persistido.
- Registro:
  - Responsável: Codex; início/conclusão 2026-10-10.
  - Entrega: execução e snapshot persistidos junto à transição da tarefa; chave de idempotência/payload; índice único de execução ativa; limites; claim SKIP LOCKED, lease, token de posse, retomada, renovação e cancelamento.
  - Arquivos: apps/api/Features/Executions, TaskAccess, AppDbContext, migração ExecutionQueue e tests/TeamFlow.Api.Tests/ExecutionTests.cs.
  - Validação: 17 testes aprovados com PostgreSQL real, incluindo repetição, concorrência de comandos e consumidores, dependências pendentes, reinício da API, recuperação de lease e rejeição de token antigo. Build sem avisos/erros; formatação API/testes aprovada; EF sem mudanças pendentes no modelo. Migração aplicada sobre T03/T06/T07 e em bancos vazios das fixtures.
  - Ajuste de teste: bootstrap PostgreSQL excedeu o timeout local de 3 segundos sob carga; somente a fixture passou a usar 30 segundos. Suíte completa reexecutada e aprovada; nenhum cenário ignorado. Bancos temporários removidos pela fixture (0 remanescentes verificados).
  - Limitação: nenhuma IA/worker externo é iniciado; o cliente técnico foi entregue na T12; o consumo automático depende do checkout/perfil/harness nas T14–T17. Limites de passos/custos são registrados; fiscalização de chamadas reais pertence ao harness.
  - Referência: entrega local sem commit.

### [x] T11 — Escolher sandbox e segredos

- Status: Concluída
- Dependências: T09
- Componente: docs/decisions
- Escopo: Definir executor local e fronteira de isolamento de produção, políticas de rede, recursos e armazenamento de credenciais.
- Critérios de aceitação:
  - [x] ADR aceito separa desenvolvimento de produção
  - [x] define autenticação API/runner, segredo por execução e limpeza
  - [x] nenhum recurso pago é criado sem autorização.
- Registro: Codex; início 2026-10-11. Proposta concreta em docs/decisions/0003-sandbox-and-secrets.md: Docker local, fronteira VM/microVM para produção, credencial técnica com escopo, Data Protection/DPAPI e limpeza. Aceito pelo responsável em 2026-10-11.

- Aceite: usuário autorizou a proposta recomendada em 2026-10-11; ADR aceito. Nenhuma conta externa ou recurso pago criado.

### [x] T12 — Criar runner independente e executor local

- Status: Concluída
- Dependências: T10, T11
- Componente: apps/runner; contracts/execution
- Escopo: Criar serviço C# com implementação local da fronteira de execução; validar paths e argumentos, limites e encerramento.
- Critérios de aceitação:
  - [x] Runner compila sem referências à API e não acessa seu banco
  - [x] testes cobrem timeout, cancelamento, path traversal e isolamento de diretórios
  - [x] ambiente local não é declarado seguro para produção multi-tenant.
- Registro:
  - Responsável: Codex; início/conclusão 2026-10-11.
  - Entrega: aplicação runner C# independente, ISandbox/Docker local, workspace exclusivo e paths sem traversal/reparse points; limites de rede, identidade, CPU/memória/PIDs, output, timeout e cancelamento. Cliente HTTP v1 sem referências à API ou banco.
  - API: credenciais técnicas de projeto, SHA-256, expiração, emissão única, rotação/revogação; sessão humana separada de Bearer; claim com escopo e admissão serializada contra revogação; heartbeat vinculado à credencial/worker/lease; DPAPI no key ring Windows local.
  - Validação: 8 testes runner aprovados em Docker real, incluindo junction Windows, preservação de diretórios vizinhos, argumentos literais, output limitado, timeout e cancelamento; 19 testes API aprovados em PostgreSQL real, incluindo escopo forjado, HTTPS, expiração, rotação, cancelamento e credencial sem acesso humano. Nenhum teste ignorado. Verificação CLI verify-sandbox com exitCode 0 e usuário 65532.
  - Migração RunnerCredentials aplicada sobre o banco existente e em bancos vazios das fixtures; EF sem mudanças pendentes. Formatação API/runner/testes verificada; OpenAPI e tipos TypeScript regenerados; lint/typecheck web aprovados. Frontend HTTP 200 e readiness API HTTP 200; 0 containers de sandbox e 0 bancos temporários remanescentes.
  - Ajustes de validação: fixture HTTPS precisava liberar apenas seu host de teste; Docker reporta container removido com mensagem lowercase. Cenários corrigidos e reexecutados, sem desabilitar proteções da aplicação. Migração local usou timeout de conexão 30 s neste terminal sob carga; configuração padrão preservada.
  - Limitação: não há polling automático, checkout ou chamada real de IA. Checkpoints, resultados, recuperação do controlador após crash e consumo pelo harness pertencem a T14–T17. Docker local não habilita produção multi-tenant.
  - Referência: entrega local sem commit.

### [x] T13 — Escolher integrações iniciais Git e IA

- Status: Concluída
- Dependências: T09
- Componente: docs/decisions
- Escopo: Selecionar um serviço Git e um provedor/modelo inicial; verificar mecanismos suportados, escopos, uso e limites.
- Critérios de aceitação:
  - [x] ADR aceito define integração e mecanismo de credenciais
  - [x] distingue API de assinatura pessoal e modelo de agente completo
  - [x] testes pagos e recursos externos permanecem opt-in.
- Registro: Codex; início 2026-10-11. Proposta em docs/decisions/0004-git-and-ai-integrations.md: GitHub App e OpenAI Responses/API key. Alternativa conta ChatGPT requer validação de elegibilidade. GitHub App + OpenAI API key aceitos pelo responsável em 2026-10-11; nenhuma conta ou chamada paga configurada.

- Aceite: usuário autorizou a proposta recomendada em 2026-10-11; ADR aceito. Nenhuma conta externa ou recurso pago criado.

### [x] T14 — Conectar repositório e preparar checkout

- Status: Concluída
- Dependências: T06, T12, T13
- Componente: apps/api/Features/Integrations; apps/runner
- Escopo: Persistir conexão Git autorizada e preparar checkout, commit base e branch por execução.
- Critérios de aceitação:
  - [x] Acesso exige permissão de projeto
  - [x] branch/base são registradas
  - [x] URLs e comandos são validados
  - [x] revogação, erro Git e limpeza têm comportamento definido.
- Registro:
  - Responsável: Codex; início/conclusão 2026-10-11.
  - Entrega: conexão GitHub App autorizada por projeto/instalação/repositório, chave privada protegida, identidade e commit verificados; uma conexão ativa por projeto; revogação apaga segredo e impede novos grants. Agendamento captura snapshot imutável de URL/base/branch/versão; checkout confere HEAD e devolve base/branch preparados.
  - Runner: cliente HTTP recebe token temporário mediante credencial/escopo/lease vigentes; revalidação após emissão cobre revogação durante I/O. Git isolado de configurações globais, hooks, submódulos e redirecionamentos; autenticação somente no ambiente efêmero do Git confiável, sem argv/URL/config persistida/container. Timeout/cancelamento e limpeza de arquivos read-only.
  - Validação: 11 testes runner aprovados, incluindo 3 novos testes de checkout com Git real em fixtures locais, URLs/commits inválidos, cancelamento e falha Git; 24 testes API aprovados, incluindo escopo autorizado, snapshot, token incorreto, revogação da conexão e revogação do runner durante emissão. Assinatura JWT RSA e restrição de token a um repositório read-only verificadas com HttpMessageHandler fictício. Zero chamadas GitHub reais.
  - Migração GitAndAgentProfiles aplicada sobre o banco existente e bancos vazios de testes; EF sem mudanças pendentes. Formatação verificada; contratos e documentação atualizados. 0 containers de sandbox, 0 bancos temporários e 0 diretórios de fixture remanescentes.
  - Limitação: dados reais da GitHub App devem ser configurados no servidor; commit fica fixo até reconectar. Checkout manual/programático disponível, sem polling automático; coordenação de heartbeat, resultados e recuperação pertence ao harness/T17.
  - Referência: entrega local sem commit.

### [x] T15 — Configurar provedor de IA e perfis

- Status: Concluída
- Dependências: T06, T11, T13
- Componente: backend; adaptador de IA
- Escopo: Implementar um adaptador e configuração de perfil, modelo, ferramentas e limites; proteger credenciais.
- Critérios de aceitação:
  - [x] Segredos não aparecem em API de leitura, logs ou browser
  - [x] capacidades do perfil são explícitas
  - [x] chamadas suportam timeout/cancelamento e limites
  - [x] testes padrão usam doubles claramente identificados.
- Registro:
  - Responsável: Codex; início/conclusão 2026-10-11.
  - Entrega: perfis OpenAI por projeto, modelo/preços explícitos, limites de entrada/saída/tempo, capacidades textGeneration e tools vazio; segredo importado da configuração autorizada do servidor e protegido por finalidade/tenant/ID. API de leitura e interface exibem somente metadata; revogação apaga segredo. Seleção de perfil na execução rejeita outro projeto e perfis revogados.
  - Adaptador: ITextModel/OpenAiTextModel com HTTP Responses, store=false, output tokens limitados, timeout/cancelamento, admissão por estimativa de orçamento, sem retries automáticos. Usage/custo desconhecidos permanecem null; custo informado é estimativa por preços configurados. Chamadas externas desativadas por padrão (OpenAI:EnableRequests).
  - Proteção: DPAPI local; certificado com chave privada em outros ambientes. Key ring sem criptografia é rejeitado antes de aceitar proteção durável; Linux Development sem certificado usa chaves de sessão separadas e não aceita segredos de integração.
  - Validação: 24 testes API aprovados com PostgreSQL, incluindo ciphertext sem chave em claro, isolamento de finalidade/tenant, metadata sem segredo, revogação, capacidade real, orçamento/contexto, erro de provedor sem body, resposta incomplete, timeout/cancelamento e usage ausente. Provedor padrão usa doubles explícitos; nenhuma chave real ou chamada paga usada.
  - Interface: configuração Git/perfis e seleção de perfil no Kanban, feedback quando credenciais não estão configuradas; nenhum campo de segredo. OpenAPI/tipos regenerados, lint/typecheck/build aprovados; 3 testes Playwright aprovados contra API/PostgreSQL reais. Frontend e readiness HTTP 200 em 127.0.0.1:3000 / localhost:5080.
  - Ajustes: limpeza read-only Windows e fixture remanescente corrigidas; teste de navegador passou a observar resposta final após redirect de slash Next.js. Edição de validadores foi restringida aos atributos Regex e as suítes foram repetidas no código final.
  - Limitação: modelo e preços precisam corresponder à conta configurada; faturamento real não foi verificado. Accounting cumulativo, ferramentas e ciclo de agente são tarefas T16/T17. Perfis são imutáveis; criar outro para atualizar configuração/credencial.
  - Referência: entrega local sem commit.

### [x] T16 — Implementar harness mínimo

- Status: Concluída
- Dependências: T10, T12, T14, T15
- Componente: componente backend de execução
- Escopo: Implementar ciclo investigar → editar → testar, ferramentas estruturadas e contrato de contexto portátil; registrar checkpoints e evidências.
- Critérios de aceitação:
  - [x] Repositório fixture recebe alteração e teste reproduzíveis
  - [x] falha de ferramenta não é sucesso
  - [x] quantidade de passos e recursos é limitada
  - [x] harness não depende do transporte HTTP ou UI.
- Registro: responsável Codex; início 2026-10-11.
  - Incremento: motor independente de HTTP/UI, ações read/write/test/finish, política de arquivos/comando, hashes, limites de passos/tempo/arquivo, contexto portátil e checkpoints locais fora do sandbox.
  - Validação: 17 testes runner aprovados; ciclo com modelo simulado e Docker real, falha de teste/hash, limite de passos, caminhos protegidos e cancelamento. Nenhuma chamada paga.
  - Incremento seguinte: checkpoints PostgreSQL por execução/tentativa/sequência, PUT idempotente e leitura técnica; posse revalidada com locks contra cancelamento/reclaim/revogação. Escopo protegido também por FK composta. Contratos portáteis v1 e adaptador RemoteAgentCheckpoint; limite de contexto 96 KiB, evidências anteriores imutáveis.
  - Fixture integrada: Git local com commit base, modelo simulado, edição e teste Docker real; quatro checkpoints pela API autenticada/TestServer e PostgreSQL; tarefa permanece em Execution, sem conclusão fictícia.
  - Verificações do incremento: 28 testes API e 18 runner aprovados; migração testada em banco vazio e aplicada ao banco existente, modelo sem diferenças pendentes, format verify aprovado nas aplicações e testes. Nenhuma chamada de IA real/paga.
  - Ambiente: API/frontend HTTP 200 ao final. Timeout local de conexão PostgreSQL aumentado para 30s após readiness transitório falhar durante os checks; scripts/Use-LocalDatabase.ps1 mantém o ajuste para próximos reinícios.
  - Conclusão 2026-10-11: controlador run-once conecta claim/checkout/heartbeat/modelo/checkpoints/resultado. Orçamento cumulativo reservado antes de chamadas; métricas conhecidas persistidas e desconhecidas permanecem null. Idempotência bloqueia chamadas pendentes/incertas. Sucesso exige evidências e não conclui a tarefa antes de PR/merge.
  - Proteções adicionais: checkout read-only durante testes, imagens fixadas por digest/recursos limitados, criação de arquivos somente nos caminhos aprovados com expectedHash=missing e CreateNew, evidências incluem comando e ambiente do teste.
  - Verificação final: 33 testes API e 23 runner aprovados; fixture Git/API repetida após enriquecimento da evidência. Novos casos cobrem orçamento cumulativo, consumo desconhecido, chamada pendente, lease vencido, resultado idempotente, nova execução após falha, coordenador e proteção de fontes. Doubles de IA explícitos; sem chamadas pagas.
  - Continuidade: T17 implementa recuperação/reconciliação e polling contínuo; sdk/container de build precisa ser preparado pelo operador com dependências offline. Não foi feito teste de IA real ou de SDK de projeto dentro de container.

### [x] T17 — Implementar recuperação e cancelamento

- Status: Concluída
- Dependências: T16
- Componente: execuções; harness; runner
- Escopo: Retomar checkpoints, reconciliar efeitos e aplicar cancelamento, budgets e encerramento de ambiente.
- Critérios de aceitação:
  - [x] Interrupção antes/depois de efeito externo não duplica resultado
  - [x] cancelamento chega ao runner
  - [x] execução esgotada tem estado e motivo persistidos
  - [x] mesma tarefa pode ter nova execução preservando histórico.
- Registro: responsável Codex; início 2026-10-11. Recuperação deve reutilizar ações conhecidas e hashes, bloquear consumo incerto, revalidar testes e preservar histórico sem duplicar efeitos.

  - Conclusão 2026-10-11: polling serial run, recuperação de checkpoints entre tentativas, replay de escritas por hashes/recibos confirmados e reexecução de testes no mesmo ambiente. Chamadas com resultado ou consumo incerto bloqueiam retomada automática; ações conhecidas não são cobradas nem chamadas novamente.
  - Encerramento: heartbeat cancela a chamada em espera e o workspace é removido antes do resultado. Limpeza após crash remove somente diretórios com marcador externo de propriedade vencido e containers com labels verificadas; ambientes ativos e diretórios desconhecidos são preservados. Read-only Windows tratado também nos diretórios.
  - Validação: 35 testes API e 27 runner aprovados; orçamento, resultado concorrente, cancelamento/deadline persistidos, retomada sem nova chamada do double, hashes, teardown Docker e preservação de histórico. Nenhuma chamada paga ou recurso GitHub real.
  - Limitação explícita: efeitos incertos exigem intervenção; recuperação não promete exactly-once. Cancelamento é cooperativo no heartbeat de 20s, com timeout da requisição de 10s. Docker local não habilita produção multi-tenant.
### [x] T18 — Exibir progresso e artefatos

- Status: Concluída
- Dependências: T08, T10, T16
- Componente: backend; apps/web
- Escopo: Expor estado, timeline, SSE e acesso autorizado a logs/relatórios; definir armazenamento inicial e retenção.
- Critérios de aceitação:
  - [x] Reconexão recupera eventos e estado sem duplicação visual
  - [x] usuário de outro tenant não acessa artefato
  - [x] outputs são limitados/redigidos
  - [x] custo desconhecido permanece desconhecido.
- Registro: Codex; início 2026-10-11. JSON/SSE com cursor sobre checkpoints duráveis, métricas com null para consumo desconhecido, relatórios autorizados sem código/prompts/output livre e limite de leitura de 30 dias. PostgreSQL é o armazenamento inicial; UI deduplica eventos por eventId e reconcilia o estado.
  - Verificação: 37 testes API e 3 Playwright aprovados após este incremento; inclui escopo de outro tenant, cursor, SSE, relatório limitado, expiração e custo desconhecido. Typecheck, lint e build aprovados; frontend atualizado em 127.0.0.1:3000.
  - Pendente: política de exclusão física. Revisão automática rejeitou aplicar um worker que apagaria checkpoints/actions após 30 dias sem autorização específica; proposta preparada fora do repositório, nenhuma exclusão aplicada. Metadados de custo/histórico seriam preservados e evidências necessárias à recuperação/publicação protegidas. Pergunta de autorização enviada; continuar demais tarefas sem contornar o bloqueio.

  - Política inicial definida 2026-10-11: PostgreSQL, leitura de relatórios/timeline por 30 dias, retenção física indefinida e arquivamento reversível de fontes. A implementação de purge proposta permanece não autorizada e não aplicada; o bloqueio foi resolvido dentro do escopo com a alternativa conservadora sem exclusão. Prazo de exclusão/backups/capacidade é gate documentado para produção em docs/pilot.md. Os critérios funcionais já verificados permanecem atendidos.

### [x] T19 — Publicar PR com evidências

- Status: Concluída
- Dependências: T14, T16, T17
- Componente: integração Git; execuções
- Escopo: Publicar commits e PR após validações exigidas; incluir comportamento, decisões, testes e pendências.
- Critérios de aceitação:
  - [x] Repetição localiza PR existente
  - [x] PR aponta commit/base registrados
  - [x] falha de testes não produz estado aprovado
  - [x] fixture testa erros e deduplicação sem chamadas pagas.
- Registro: Codex; início 2026-10-11. IGitPublisher/GitHubPublisher nativo, grant write restrito ao repositório e somente no servidor; checkout permanece read-only. Commit determinístico, base verificada, modos de arquivo preservados, branch existente conferida sem force e PR draft reconciliado por head/base/marker.
  - Validação da fronteira: 3 testes de transporte fictício aprovados; resposta perdida após criar PR, retry sem PR duplicado, base alterada, branch conflitante, symlink e hashes/paths inválidos sem rede. Nenhum PR externo criado.
  - Incremento: estado/lease/tentativas de publicação persistidos; no máximo 3 tentativas e 60s por publicação. Sucesso técnico exige checkpoint com escrita/teste e recibos confirmados; publicar muda a tarefa para Review, sem aprovação/Done. Worker serial opt-in GitHub:EnablePublication=false por padrão; Testing desativa polling. Migração PullRequestPublication aplicada ao banco local.
  - Conclusão 2026-10-11: 41 testes API aprovados, incluindo publicação integrada, falha após efeito/reconciliação, concorrência com lease, acesso de outro tenant e transição para Review sem Done. Migração aplicada ao banco existente e em bancos vazios das fixtures; EF sem diferenças pendentes. Contrato OpenAPI/tipos regenerados, painel de PR e erros em português, typecheck/lint/build aprovados.
  - Limites: testes Git/IA usam doubles explícitos. Nenhum PR real, chamada paga ou chave real utilizada. GitHub:EnablePublication continua false na visualização local. Produção depende da fronteira de sandbox e proteção de segredos aprovadas para o piloto. Mudanças na base param para revalidação, que pertence à T20; exclusão de artefatos T18 não foi aplicada.

### [x] T20 — Processar revisão, CI e merge

- Status: Concluída
- Dependências: T19
- Componente: integração Git; tarefas; apps/web
- Escopo: Receber webhooks verificados, processar CI e feedback; retomar ajustes e concluir apenas com merge confirmado.
- Critérios de aceitação:
  - [x] Webhooks duplicados/atrasados não corrompem estado
  - [x] aprovação sem merge não conclui
  - [x] mudança de base exige revalidação afetada
  - [x] feedback gera execução rastreável sem apagar tentativas.
- Registro: Codex; início 2026-10-11. Primeiro incremento: ingresso de webhooks via HMAC-SHA256 do corpo original, comparação constante, limite 128 KiB, escopo por repository/installation ativos e inbox PostgreSQL deduplicada por conexão/delivery. Payload/event divergentes no mesmo ID retornam conflito; signed ping/eventos não utilizados são ignorados.
  - Validação 2026-10-11: 43 testes API aprovados, incluindo 2 novos casos de assinatura (vetor oficial), corpo alterado/malformado, replay concorrente, payload conflitante, instalação não autorizada, revogação e limite de tamanho. Migração GitHubWebhookInbox aplicada ao banco existente e em bancos vazios das fixtures; EF sem mudanças pendentes. Formatação aplicada, OpenAPI/tipos regenerados e typecheck aprovado. 27 testes runner e 3 Playwright aprovados nesta entrega; frontend/readiness HTTP 200. Nenhuma chamada paga, PR externo ou webhook real criado.
  - Pendente: processar revisão/CI/merge com head/base verificados, tratar eventos atrasados e feedback com execução rastreável. Webhooks recebidos ficam pending e não mudam tarefas para Done. Não marcar T20 concluída por este incremento.

  - Incremento 2026-10-11: revisão independente, CI de App/head exigidos, invalidação por mudanças, merge confirmado com ancestralidade da base, proteção contra consultas atrasadas e execuções substituídas. WebhookWorker com leases/tentativas limitadas consulta fonte atual. Feedback idempotente cria execução vinculada com histórico/objetivo preservados. Painel de revisão/ajustes implementado.
  - Verificação intermediária: 50 testes API aprovados, 5 cenários integrados de revisão/merge/feedback/webhook e 2 cenários de transporte GitHub sem rede real; cenário de concorrência adicional e checks finais em execução. Migrações PullRequestReview, WebhookProcessing e ReviewFeedback aplicadas no banco existente e nas fixtures. Typecheck/lint/build web aprovados após correção dos limites do contrato.
  - Pendente para conclusão: finalizar cenário de concorrência e checks de interface/contratos/formatação. Política de merge está documentada em docs/pull-request-review.md; nenhuma aprovação/merge externo real foi realizado.
  - Conclusão 2026-10-11: 50 testes API aprovados na suíte ampla; depois dos ajustes finais, 6 testes de revisão aprovados, incluindo resposta Git atrasada sem sobrescrever CI mais nova. 3 Playwright aprovados; fluxo de revisão na UI usa transporte simulado explicitamente identificado, e políticas são verificadas separadamente na API/PostgreSQL. Typecheck, lint, build e formatação aprovados. Contratos regenerados e preview local atualizado.
  - Limitações: merge executado por pessoa autorizada no GitHub, com confirmação pelo aplicativo; rebase/ancestralidade diferente exige revalidação. Ajustes geram branch/PR novos vinculados, preservando o anterior; transferência automática do estado é T24. CI/consultas externas são opt-in; nenhum merge externo real realizado.
### [x] T21 — Persistir conhecimento e fontes

- Status: Concluída
- Dependências: T06, T20
- Componente: apps/api/Features/Memory
- Escopo: Criar itens de memória, fontes e status; extrair resolução candidata e permitir validação/obsolescência pela equipe.
- Critérios de aceitação:
  - [x] Fontes vinculam tarefa/PR/commit
  - [x] candidato não vira decisão validada automaticamente
  - [x] permissões cobrem CRUD
  - [x] excluir fonte ou revogar acesso trata seus derivados.
- Registro: Codex; início 2026-10-11. Fontes e versões autorizadas por projeto; resolução candidata com proveniência do merge; arquivamento reversível oculta também derivados, sem exclusão física automática.

  - Conclusão 2026-10-11: 55 testes API e 4 Playwright aprovados; fluxo de memória com API real, validação/arquivamento/restauração, DTOs/contrato regenerados. Typecheck, lint e build aprovados. Nenhuma chamada paga. docs/shared-memory.md descreve proveniência, versões e limites.

### [x] T22 — Ingerir e indexar contexto

- Status: Concluída
- Dependências: T14, T21
- Componente: memória; processamento em background
- Escopo: Ingerir documentos/código permitidos e memória; definir chunks, modelo de embedding, versionamento e reindexação.
- Critérios de aceitação:
  - [x] Indexação é repetível sem duplicar fontes
  - [x] embeddings podem ser reconstruídos
  - [x] arquivos/segredos excluídos não são enviados ao provedor
  - [x] reindexação e falha ficam observáveis.
- Registro: Codex; início 2026-10-11. Fila PostgreSQL, chunks/versionamento e adaptador de embeddings opt-in; fontes independentes dos derivados, orçamento e chamadas incertas persistidos.

  - Conclusão 2026-10-11: 57 testes API aprovados. Migração MemoryIndexing em banco existente e vazio; reindexação, idempotência, UTF-8, tenant, revogação e resposta perdida verificados com doubles. Contratos regenerados, TypeScript/lint/build e format aprovados. docs/memory-indexing.md documenta flags, tarifas, limites e ingestão explícita. Nenhuma chamada paga.

### [x] T23 — Recuperar contexto e planejar demandas

- Status: Concluída
- Dependências: T15, T16, T22
- Componente: memória; análise de tarefas
- Escopo: Implementar busca textual/vetorial filtrada, pacote com fontes e orçamento; analisar demanda e propor tarefa com critérios antes da execução.
- Critérios de aceitação:
  - [x] Busca não revela dados de outro tenant
  - [x] referências e versões são verificáveis
  - [x] conteúdo recuperado não autoriza ferramentas
  - [x] plano pode ser corrigido e informação essencial ausente bloqueia análise.
- Registro: Codex; início 2026-10-11. Busca textual/pgvector filtrada por escopo/versão/status e pacote citado limitado; propostas editáveis com bloqueio por informação ausente e chamadas idempotentes/orçadas.

  - Conclusão 2026-10-11: 58 testes API e 4 Playwright aprovados. Busca textual/pgvector em PostgreSQL real, referências/hashes, tenant, plano editado por pessoa, chave repetida sem chamada nova, perguntas sem consumo e invalidação por arquivamento. Migração DemandPlanning aplicada em banco existente/vazio; contrato, TypeScript, lint, build e formatação aprovados. Geração usa double explícito, nenhuma chamada paga.

### [x] T24 — Permitir trocar de agente e reutilizar memória

- Status: Concluída
- Dependências: T17, T23
- Componente: harness; apps/web; adaptadores
- Escopo: Selecionar perfil na tarefa, transferir estado portátil e recuperar histórico relevante em nova execução; demonstrar segunda implementação/adaptador compatível quando escolhido.
- Critérios de aceitação:
  - [x] Troca preserva commit, alterações, testes e pendências sem estado interno proprietário
  - [x] seleção respeita capacidades
  - [x] cenário mostra fonte anterior usada em nova tarefa, sem inventar melhoria quantitativa.
- Registro: Codex; início 2026-10-11. Contexto de fontes na execução, histórico portátil e transferência de escritas confirmadas na mesma base; seleção de perfil ativo e nova validação/PR sem herdar aprovação.

  - Conclusão 2026-10-11: 63 testes API e 28 runner aprovados; perfil substituído com recibos/estado portátil, hashes/base divergentes, teste histórico insuficiente, fontes invalidadas antes do claim. Contratos portáteis estendidos por campos opcionais, migração PortableAgentTransfer em banco existente/vazio, formatação e TypeScript/lint/build aprovados. Reutilização de fonte em análise/tarefa/agendamento verificada; T25 demonstra também o uso no modelo durante nova execução. Nenhum segundo fornecedor foi escolhido nem chamada paga executada.

### [x] T25 — Validar MVP de ponta a ponta e preparar piloto

- Status: Concluída
- Dependências: T18, T20, T24
- Componente: tests; documentação; configuração
- Escopo: Validar jornada completa em fixtures e smoke test real autorizado; organizar checks independentes e documentar operação/limites.
- Critérios de aceitação:
  - [x] Jornada criar → analisar → executar → PR → feedback → merge → reutilizar memória passa
  - [x] testes incluem isolamento e retomada
  - [x] produção permanece bloqueada até sandbox/segredos adequados
  - [x] CI por aplicação é configurada no serviço escolhido ou comandos locais equivalentes são documentados.
- Registro: Codex; início 2026-10-11. Jornada integrada com API/PostgreSQL/harness/Git/Docker reais e doubles explícitos de IA/GitHub; operação e gates de piloto em preparação. Smoke externo requer recursos/credenciais e orçamento autorizados.

  - Validação local concluída 2026-10-11: 66 testes API, 28 runner e 4 Playwright aprovados. A jornada completa usa API/TestServer, PostgreSQL, Git e Docker reais com doubles explícitos de IA/embeddings/GitHub. Inclui análise aceita, execução, PR reconciliado após resposta perdida, feedback idempotente com perfil substituído, nova execução/testes, aprovação independente, merge Git local confirmado, resolução candidata validada/indexada e fonte citada usada no modelo de nova execução.
  - Checks independentes: scripts/Test-App.ps1 -App api/runner/web executados com êxito; TypeScript, lint, build, format verify e EF sem alterações pendentes. Contratos/migrações/docs atualizados. UI de memória/análise também verificada em viewport estreita. Program bloqueia Production mesmo com flags de chamadas externas; teste específico aprovado. SDK resolvido pelo global.json de cada aplicação.
  - Ajustes da integração: ordenação por tarefa permanece monotônica na precisão de microssegundos do PostgreSQL, evitando empates de relógio entre execuções. Orçamento de contexto calcula o JSON real, preservando trecho que cabe em perfil de 4 KiB; teste de regressão aprovado. Detecção conservadora de conteúdo sensível cobre também JSON, Bearer e chaves OpenSSH.
  - Operação e proposta de piloto: docs/pilot.md. Nenhuma chamada paga, PR externo, merge GitHub, webhook externo ou recurso remoto criado. Flags continuam desabilitados no preview. Retenção física é indefinida no desenvolvimento; nenhum purge automático aplicado.
  - Continuidade 2026-10-12: pré-verificação read-only em scripts/Test-PilotReadiness.ps1 e template scripts/pilot.example.json; confere escopo/IDs, budgets, ambiente e metadata autorizada sem chamada paga/publicação. Recursos e teto do smoke real solicitados ao responsável; trabalho local preservado.
  - Verificação 2026-10-12: jornada MvpJourneyTests e ProductionGateTests reexecutadas, 2 testes aprovados. Pré-verificação validada contra IDs vazios, budgets acima do total, origem insegura, campo de credencial indevido e ausência de sessão; relatório não declara smoke externo executado. git diff --check sem erros de whitespace.
  - CI 2026-10-13: workflows por aplicação em .github/workflows (api, runner, web), descritos em docs/development.md#ci. Reproduzidos num container Linux local: build, format e EF da API/runner sem pendências e 70 testes da API aprovados com o filtro do CI; os testes dependentes de Docker e a suíte do runner não rodaram no Linux, e nenhum workflow executou no GitHub (sem remoto). Três testes de Codex nativo ficam fora do CI e seguem locais.
  - Pendente obrigatório do escopo atual (ADR 0005, 2026-10-12): smoke com Codex CLI real e Git local. Login, caminho autorizado e checkout da pasta indicada foram conferidos; ainda requer código registrado no Git, política/imagem/comando de testes, revisão independente e evidência de uma tarefa real até merge/reutilização. GitHub App/chave API/orçamento de fornecedor não são requisitos desse modo. Não marcar concluída somente por fixtures ou login status.
  - Smoke real concluído em 2026-10-13 (Codex CLI 0.162.0-alpha.2 com login ChatGPT, Git local sem remoto, Docker real), num projeto novo e isolado em `C:\Users\thale\teamflow-smoke\project` (add.sh com defeito, test.sh, 1 commit). Nenhum outro repositório foi lido ou alterado, em especial "Aplicacao de Licitacoes". Banco `teamflow_smoke` (todas as migrações em banco vazio), API na porta 15090, runner independente com sandbox sem rede. Fluxo: demanda → análise (Codex real, respondeu `needs_information` por não haver fontes; tarefa criada manualmente com 2 critérios) → execução 1 tentativa, ~25 s (Codex leu add.sh, escreveu a correção, teste `sh test.sh` aprovado no sandbox) → publicação local (head `6d490d5d3e50`; a primeira tentativa foi interrompida por mim com a API em curso e o lease de 3 min foi recuperado na reinicialização: tentativa 2, mesma ref) → diff local (`-echo $(($1 - $2))` / `+echo $(($1 + $2))`; árvore de trabalho e `main` intactas até o merge) → aprovação do próprio autor recusada (409 `independent_review_required`) → aprovação do revisor (`awaiting_merge`, tarefa em AwaitingMerge) → `git merge --ff-only teamflow/<id>` humano → consulta: `merged`, tarefa `done`, `sh test.sh` no projeto = ok → memória candidata `resolution/<id>` validada e indexada (`text-only`) → busca a cita → nova análise (Codex real) citou essa fonte no plano. Custo e tokens permanecem desconhecidos (`null`).
  - Limites desta evidência: projeto de brinquedo, uma tarefa; não mede qualidade do modelo nem substitui piloto em aplicação real; a análise real devolveu perguntas, não uma proposta pronta; busca textual usa AND de termos, então demandas longas podem não achar fontes (ver T37). A publicação foi disparada duas vezes por mim (worker + chamada manual): o 409 `publication_in_progress` é o comportamento esperado. Evidências locais: `C:\Users\thale\teamflow-smoke\{smoke.log,evidence.json,logs}`.
## Fora do primeiro MVP

Não iniciar como requisito implícito: múltiplos serviços Git, catálogo amplo de provedores, agentes concorrentes na mesma tarefa, workflows editáveis pelo usuário, marketplace, GraphRAG, Kubernetes, cobrança e relatórios avançados. Esses itens exigem demanda e tarefas próprias.

## Decisões ainda necessárias

T05 e T09 aceitas em docs/decisions. T11 e T13 aceitas: Docker local, GitHub App e OpenAI API key. Produção requer sandbox VM/microVM e proteção de segredos do ambiente. Hospedagem e CI externo dependem do ambiente do piloto; não criar recursos externos só para preencher o backlog.

## Mudança de escopo — execução local (2026-10-12)

Por decisão explícita do responsável, a T25 passa a exigir smoke de Codex CLI e Git local. A ausência de GitHub App/chave OpenAI deixa de bloquear esse novo piloto. Evidências anteriores com doubles continuam identificadas; não comprovam login/modelo real.

### [x] T26 — Adaptar configuração e workflow ao Codex CLI/Git local

- Status: Concluída
- Escopo: perfis Codex sem API key; comando não interativo; configuração de repositório por projeto; checkout isolado; publicação/diff/revisão/merge local; memória textual.
- Implementação 2026-10-12: endpoints locais/perfis, migração LocalCodexIntegrations, configuração ignorada de Development, adaptador stdin/resposta final, BillingMode persistido, clone separado e Git plumbing idempotente; telas e operação documentadas.
- Critérios: configuração autorizada, falha/timeout/cancelamento, recibo CLI recuperável sem custo inventado, publicação sem editar arquivos originais, revisão de head/base e merge confirmado, contratos/checks atualizados.
- Repositório indicado: Aplicacao de Licitacoes; Git inicializado em main e commit vazio autorizado. Código existente não foi adicionado ao histórico. Projeto/equipe solicitado: Teste.
- Conclusão 2026-10-12: 70 testes API, 29 runner e 4 Playwright aprovados. Após ajustes finais, cenários locais/API e checkout foram reexecutados (4 + 4 aprovados). Tipos, lint, build e format verificados; migração aplicada/EF sem diferenças. Equipe/projeto Teste configurados com repositório local e perfil Codex local, login ChatGPT/Docker e checkout real conferidos. Nenhuma chamada real de modelo nem arquivos de código adicionados ao Git. Smoke real continua na T25, requer baseline de código e política de testes; não foi declarado completo.
### [x] T27 — Renovar interface e experiência do workspace

- Status: Concluída
- Responsável: Codex; início 2026-10-12
- Componente: apps/web
- Escopo: identidade visual consistente, autenticação responsiva, quadro horizontal, busca de tarefas, indicadores reais e organização das ferramentas secundárias.
- Critérios: fluxos existentes preservados; navegação por teclado; telas estreitas sem overflow da página; TypeScript, lint, build e E2E aprovados; revisão visual desktop/mobile.

- Conclusão 2026-10-12: TypeScript, ESLint, build Next e 5 testes Playwright aprovados. Revisão visual desktop 1440 px e mobile 375 px; largura da página autenticada medida em 375 px, sem overflow. Busca por descrição/maiúsculas, retorno ao formulário com foco e fluxos existentes verificados. Nenhuma dependência adicionada, chamada de IA ou alteração no backend. Cache gerado do Next preservado em apps/web/work durante regeneração após erro do OneDrive.

### [x] T28 — Dividir workspace por atividade

- Status: Concluída
- Responsável: Codex; início 2026-10-12
- Componente: apps/web
- Escopo: navegação por Quadro, Demandas, Memória e Configurações; formulário de tarefa sob demanda com fechamento; rascunhos preservados ao alternar áreas; navegação responsiva.
- Critérios: nenhuma mistura de formulários entre áreas; fluxos existentes preservados; teclado/retorno de foco; rascunhos preservados; checks web e revisão desktop/mobile.

- Conclusão 2026-10-12: lint, TypeScript, build e 5 testes Playwright aprovados. Áreas isoladas visualmente; estado e rascunhos mantidos na navegação. Teste verifica troca de áreas, preservação de demanda, busca, criação sob demanda, Escape e foco restaurado. Revisão visual desktop 1440 px e mobile 375 px, sem overflow da página. Sem novas dependências ou mudança na API.

### [x] T29 — Escolher pastas locais e explicar perfis

- Status: Concluída
- Responsável: Codex; início 2026-10-12
- Escopo: navegador de diretórios autorizado por projeto; seleção sem upload/conexão automática; explicações do perfil e limites avançados.
- Critérios: isolamento, rejeição de escape/links, seleção/cancelamento com foco, contratos e checks atualizados.
- Conclusão 2026-10-12: build API, tipos/lint/build web, 2 testes API e 6 Playwright aprovados; formatação dos arquivos C# afetados verificada. Seletor navega somente raízes autorizadas por proprietário/projeto, bloqueia escapes e links, retorna diretórios (sem conteúdo de arquivos) com limite de 200. UI fixture verifica seleção sem conexão automática, cancelamento/Escape e retorno de foco. Documentos habilitado no projeto Teste após autorização explícita do responsável; conexão permanece restrita ao repositório configurado. OpenAPI/tipos atualizados; nenhuma chamada de IA.

### [x] T30 — Simplificar seleção e criação de equipes/projetos

- Status: Concluída
- Responsável: Codex; início 2026-10-12
- Escopo: barra de contexto unificada, seleção hierárquica, criação em diálogo com contexto da equipe, estados vazios, cancelamento e foco.
- Critérios: troca de equipe isola projetos; item criado é selecionado; erro preserva nome; teclado/mobile; checks web e revisão visual.
- Conclusão 2026-10-12: lint, TypeScript/build e 7 Playwright aprovados. Barra única com seletores hierárquicos e ações dedicadas; modais com contexto, foco, Esc/cancelamento e nome preservado em erro. Teste cria duas equipes e verifica que projetos não atravessam escopos. Revisão visual em 1440/1066/375 px e criação no desktop/mobile; página autenticada medida em 375 px no celular. Sem dependências adicionais ou alteração na API.

### [x] T31 — Liberar seleção local de repositórios no Teste 1

- Status: Concluída
- Responsável: Codex; início 2026-10-12
- Autorização: responsável pediu escolher qualquer pasta do computador, no contexto da equipe/projeto Teste 1.
- Escopo: unidades locais/removíveis, seleção de Git dentro do escopo autorizado, recuperação do seletor em erro; preservar restrição padrão de outros projetos.
- Conclusão 2026-10-12: flags BrowseAllDrives/AllowRepositorySelection habilitadas somente na equipe/projeto Teste 1 após pedido explícito de escolher qualquer pasta. Seletor preenche qualquer pasta navegada; conectar exige Git regular, branch com commit e proprietário autorizado. Outras equipes/projetos mantêm suas configurações. 2 testes API e 7 Playwright aprovados; tipos/lint/build web e formatação C# verificados. Testes cobrem raízes, escape, isolamento, escolha alternativa de Git, retry, pasta sem Git e cancelamento. API/frontend atualizados, sem conexão, alteração de arquivos ou chamada de IA automática.

### [x] T32 — Configurar pasta independente de Git e usar Codex da máquina

- Status: Concluída
- Responsável: Codex; início 2026-10-12
- Escopo autorizado: salvar pasta vazia/sem Git, inspecionar Git e branches locais, configuração de Codex global à máquina, nova UX de configurações.
- Critérios: nenhuma inicialização automática; pasta persistida; branches reais; execução bloqueada sem base; nenhuma credencial/perfil configurável por projeto; checks e docs.
- Conclusão 2026-10-12: pasta existente/vazia é persistida sem Git automático; inspeção lista branches locais reais e aceita salvar sem branch. Snapshot recusa execução sem base pronta. Codex usa login/configuração global da máquina, criação manual por projeto retorna 410; vínculo técnico interno preserva FKs/histórico sem configuração ou credenciais por projeto. Status usa login status, sem inferência. 72 testes API e 7 Playwright aprovados; teste ampliado de UI confirma salvamento sem Git, branch/versão e mobile. Tipos, lint, build e format afetados verificados. Revisão visual desktop/mobile 375 px sem overflow. Contratos atualizados; sem migração, nova dependência ou chamada real de modelo.

### [x] T33 — Executar tarefas sem exigir Git manual

- Status: Concluída
- Responsável: Codex; início 2026-10-12
- Escopo: preparação automática da base ao executar, preservação dos arquivos e revisão Git; retirar bloqueio/mensagem na UI.
- Conclusão 2026-10-12: preparação automática da base no agendamento para pasta vazia/com código; Git/branch/commit deixam de ser exigência manual. .gitignore respeitado, hashes sem filtros, links/dependências/caminhos comuns de credenciais excluídos e conteúdo original preservado. Lock e versão garantem reutilização da base. 74 testes API e 7 Playwright aprovados; após refinamento do filtro, 3 cenários de pasta reexecutados. Testes usam Git/PostgreSQL/checkout/publicação reais e validam fila idempotente, código preservado e diff; nenhuma chamada real de modelo ou pasta do usuário alterada nos testes. Tipos/lint/build/format afetados aprovados, API/frontend atualizados. Mensagem repository_not_ready removida; ADR 0007 e operação documentadas.

### [x] T34 — Operar o runner local no projeto Teste 1

- Status: Concluída
- Responsável: Codex; início 2026-10-12
- Autorização: configurar e iniciar runner para consumir a tarefa pendente.
- Escopo: provisionamento local restrito a Development/projeto autorizado; token protegido por DPAPI; comando de iniciar/reiniciar; política explícita de arquivos e testes Docker; evidência real de claim e Codex.
- Critérios: runner consumindo fila; sem token em logs/configuração aberta; revisão humana preservada; erros reais reportados; checks afetados e operação documentados.

- Conclusão 2026-10-12: provisionamento administrativo local Development com pasta autorizada e token DPAPI; runner contínuo iniciado para Teste 1, claim/heartbeat/checkpoints/Codex CLI reais confirmados. Consulta de arquivo ainda ausente corrigida sem ampliar escrita; workspace excluído do build; lock impede duplicação e runtime copiado permite rebuild durante operação. 3 testes API e 29 runner aprovados, regressão de criação conferida; build/format/diff verificados. Tentativas reais anteriores preservadas (consulta ausente e SDK incompatível). Replay isolado do código gerado passou em build e smoke HTTP CRUD com SDK oficial .NET 8; nova execução em andamento no runner com a política validada. Não declara tarefa aprovada, merge, memória reutilizada ou T25 completa. Início após reiniciar Windows continua manual conforme docs/runner-local.md.

### [x] T35 — Disponibilizar revisão diretamente no quadro

- Status: Concluída
- Responsável: Codex; início 2026-10-12
- Escopo: botão no cartão, publicação/revisão da execução mais recente no detalhe, histórico atualizado e tentativas antigas identificadas.
- Critérios: revisão acessível sem expandir progresso; nova execução substitui revisão antiga; checks web e preview atualizado.
- Conclusão 2026-10-12: revisão/publicação da execução mais recente exibida diretamente no detalhe, botão Revisar alterações no cartão de revisão/merge, polling do histórico a cada 5 segundos e identificação das tentativas antigas. Aprovação/merge permanecem inalterados. Tipos/lint/build aprovados; 6 cenários E2E passaram no conjunto inicial, teste de Kanban corrigido para o nome acessível Em revisão e seus 2 cenários passaram na repetição. Fixture verifica publicação/revisão sem abrir progresso, atualização para execução concluída, histórico antigo e botão no cartão. Preview atualizado na porta 3000.

### [x] T36 — Página visual de diff local

- Status: Concluída
- Responsável: Codex; início 2026-10-12
- Escopo: revisão dedicada, lista e filtro de arquivos, contagem real de linhas, numeração antes/depois, diffs acessíveis/responsivos e ações existentes de revisão.
- Critérios: autorização existente preservada; renderização segura; formatos de Git cobertos; testes de navegação/mobile/erros e preview atualizado.
- Conclusão 2026-10-12: rota dedicada /reviews com quatro IDs de escopo, consumo do diff local autenticado existente, navegação/filtro por arquivo, estatísticas reais, números antes/depois, modo somente alterações e ações de revisão existentes. Parser cobre múltiplos hunks, arquivo novo/removido, rename, Unicode Git octal, binários e ausência de newline. React escapa código; nenhuma autorização/merge foi alterada. Tipos/lint/build aprovados; 3 testes de diff aprovados após ajustar asserções opcionais/route announcer; 2 testes de Kanban passaram. Screenshots desktop/mobile revisados, largura 375 px sem overflow de página. Preview atualizado na porta 3000; link Abrir diff local agora abre a página visual.


### [x] T37 — Remover GitHub App, OpenAI API e webhooks

- Status: Concluída
- Responsável: Codex; início 2026-10-13
- Dependências: T25 (decisão do responsável em 2026-10-13: remover; ADR 0008 aceita)
- Escopo: remover o caminho GitHub/OpenAI do código, contratos, dados, testes e documentação; manter somente Codex CLI e Git local; nenhuma dependência de repositório Git remoto.
- Critérios: sem GitHub/OpenAI no código ativo; migração com atualização verificada; contratos e web regenerados; checks aprovados; documentação coerente.
- Conclusão 2026-10-13: removidos `GitHubProvider/Publisher/Review/Webhook`, `WebhookProcessor`, `OpenAiTextModel/EmbeddingModel`, `IntegrationSecrets`, endpoints de conexão GitHub, perfil com chave e webhook; `LocalGit` implementa `IGitPublisher`/`IGitReview`; `CodexTextModel` é o único `ITextModel`; `GitCheckout` aceita só a pasta local; busca e indexação só textuais (`semantic`/vetor saíram dos contratos). Migração `RemoveGitHubOpenAi` revoga conexões/perfis não locais e remove `GitHubDeliveries` e colunas de segredo/identificador; atualização testada num banco temporário com linhas legadas (github/openai revogados, local/codex intactos, colunas e tabela removidas). OpenAPI e tipos do web regenerados; UI sem texto de PR/CI/GitHub. Testes de provedores externos removidos; suítes reescritas com Git local e doubles de IA.
- Validação: `Test-App.ps1` api (64 testes, build 0 avisos, format, EF sem pendências), runner (31 testes, format), web (typecheck, lint, build); Playwright 10/10 numa cópia isolada do web com o Chrome instalado (o Chromium do Playwright 1.64 não está baixado nesta máquina e não foi baixado). Smoke real na T25.
- Limitações: CI no GitHub Actions não foi executado (sem remoto); os 3 testes de Codex nativo seguem fora do CI Linux. A busca textual usa `plainto_tsquery` (AND de termos) e perdeu o apoio vetorial: avaliar OR/`websearch_to_tsquery` com métrica antes de mudar. Colunas históricas (`Provider`, `RepositoryId`, preços, `BillingMode`, `Embedding`) foram mantidas de propósito (ADR 0008). Bancos de desenvolvimento existentes precisam de `dotnet ef database update` antes de subir a API nova.
