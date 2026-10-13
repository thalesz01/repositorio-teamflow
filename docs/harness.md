# Harness mínimo — T16

O motor em `apps/runner/AgentHarness.cs` é independente de HTTP, UI, entidades da API e fornecedor de IA. `IAgentModel` fornece uma ação estruturada por passo; `IAgentCheckpoint` recebe contexto portátil após cada efeito. Não contém raciocínio interno do modelo.

O contexto v1 registra objetivo, critérios, commit base, evidências de ferramentas e pendências. As ações atuais são `read`, `write`, `test` e `finish`. Política confiável do controlador define arquivos legíveis/editáveis e o comando de teste. O modelo não escolhe comandos, credenciais ou caminhos adicionais. Arquivos ocultos, incluindo `.git`, são rejeitados; o workspace também rejeita traversal e links. Arquivos de teste precisam permanecer fora da lista editável quando forem a evidência de validação.

Leitura e escrita são limitadas a 16 KiB por arquivo; escrita exige o hash SHA-256 atual. São permitidos até 200 passos, duração até uma hora e timeout de teste até cinco minutos. O Docker mantém os limites de rede, identidade e recursos existentes. Outputs de testes não entram no contexto: somente exit code, timeout e truncamento. Conteúdo de arquivos lidos é dado não confiável, não autorização.

O resultado `Validated` indica edição seguida de teste bem-sucedido, sem ferramentas falhas ou pendências. Não comprova todos os critérios de negócio nem significa tarefa concluída, revisão aprovada ou merge. Falha de ferramenta encerra este ciclo conservadoramente; timeout e cancelamento propagam como exceções e precisam ser registrados pelo coordenador futuro.

`FileAgentCheckpoint` grava JSON com limite de 4 MiB e substituição por rename no diretório privado do controlador, fora do mount do repositório. Esse armazenamento local é uma fronteira inicial: não implementa recuperação, lease fencing, envio ao PostgreSQL, retenção nem durabilidade após falha de energia. O controlador deve manter esse diretório enquanto precisar das evidências; `ExecutionWorkspace.Dispose` remove seus próprios arquivos.

## Validação reproduzível

Em `apps/runner`, executar `dotnet test ../../tests/TeamFlow.Runner.Tests --no-restore`. Docker Desktop Linux precisa estar disponível e a imagem documentada no README instalada. HarnessTests modifica um arquivo fixture e verifica o resultado em Docker real usando modelo simulado. HarnessIntegrationTests cria um repositório Git local, registra o commit base, executa leitura/edição/teste e persiste quatro checkpoints pela API autenticada com PostgreSQL real. HTTP desse teste usa TestServer; CheckpointContractTests verifica o adaptador HTTP do runner em loopback. Nenhum desses testes usa IA real ou repositório remoto.

## Pendências históricas do primeiro incremento

- Adaptador de modelo autenticado por lease, com orçamento cumulativo e tratamento de consumo desconhecido antes de habilitar chamadas pagas.
- Coordenador checkout → heartbeat → harness → persistência de checkpoint/resultado, com isolamento por execução e interrupção ao perder autorização.
- Validação de respostas do modelo no futuro adaptador HTTP; formato de ações/contexto v1 já especificado.
- Registro terminal da execução e coordenação da fila; fixture Git local com teste e checkpoints já está validada.

A fila existente ainda não inicia esse motor automaticamente. T17 mantém a responsabilidade por retomada, reconciliação de efeitos e cancelamento durável.

## Checkpoints duráveis — incremento T16

`RemoteAgentCheckpoint` implementa a mesma fronteira do motor e envia o contexto à API via `RunnerClient`, sem referência de projeto à API. O cliente verifica tentativa, sequência e hash SHA-256 da confirmação. O controlador precisa renovar o lease durante trabalho longo; o timestamp original do envelope não substitui a autorização atual do servidor.

`PUT /internal/v1/executions/{id}/checkpoint/` recebe `{leaseToken, context}`. A sequência é o tamanho de `context.evidence`: zero inicialmente, depois exatamente um novo passo. A tentativa e o tenant derivam da posse autenticada. O backend valida objetivo (`title + "\n" + description` do snapshot), critérios e commit base do agendamento. Evidências anteriores e pendências são imutáveis nesta versão; depois de uma ferramenta falha, novos passos são rejeitados. Reenvio de conteúdo idêntico na mesma tentativa/sequência retorna a confirmação original; divergência ou sequência pulada retorna `checkpoint_conflict` (409).

`POST /internal/v1/executions/{id}/checkpoint/read` recebe `{leaseToken}` e retorna o checkpoint mais recente por tentativa/sequência, ou 204. Pode retornar evidência de uma tentativa anterior, identificada por `attempt`; isso permite futura reconciliação, mas ainda não retoma o motor automaticamente. Token antigo, cancelamento e prazo vencido retornam `lease_lost` (409); credencial revogada retorna 401. Sessão humana não acessa esses endpoints.

PostgreSQL mantém histórico por execução/tentativa/sequência e chave estrangeira composta que protege escopo. Locks curtos serializam escrita com cancelamento/reclaim/revogação; não há I/O externo dentro da transação. Cada contexto serializado é limitado a 96 KiB e cada detalhe a 16 KiB UTF-8; exceder o limite de contexto retorna 413, sem truncamento silencioso. Contextos volumosos precisarão de referências a artefatos na T18.

Contratos v1: `contracts/execution/portable-context.schema.json` e `agent-action.schema.json`, além de OpenAPI para os envelopes HTTP. O JSON Schema descreve formato; a validação de sequência, posse, bytes e snapshot é feita pelo backend. Os dados são evidências fornecidas pelo runner autorizado; o backend não reexecuta testes para atestar seu conteúdo.

Retenção inicial: checkpoints permanecem com o histórico da execução, sem expiração automática. Não são memória compartilhada nem ficam expostos ao navegador. Conteúdo de arquivo lido pode ser persistido; a política confiável deve selecionar somente fontes necessárias e não incluir arquivos de credenciais. Gestão de artefatos/retenção e visualização pertencem à T18; encerramento e retomada pertencem à T17.

As pendências desse primeiro incremento foram atendidas no ciclo conectado descrito abaixo. Retomada e consumo contínuo ficam na T17. A fila não dispara chamadas pagas nem o harness automaticamente.

## Ciclo conectado — T16

`run-once <controller-config.json>` reivindica uma tarefa, clona o commit fixado, renova a posse a cada 20s, executa o harness e grava o resultado. A política vem exclusivamente do operador: arquivos legíveis/editáveis e comando de teste. O token técnico vem de `TEAMFLOW_RUNNER_TOKEN`, nunca de argumento ou do JSON. `controller.example.json` contém somente placeholders; configurar IDs, worker emitido e diretório absoluto antes de executar. Não iniciar o controlador contra filas desconhecidas como smoke test.

O modelo é chamado pelo backend; sua chave nunca chega ao runner. `POST /internal/v1/executions/{id}/model-turn` recebe operação estável, sequência do checkpoint e política de caminhos. O backend monta o prompt, valida a resposta estruturada, reserva orçamento antes da chamada e persiste consumo/ação. Operações repetidas conhecidas retornam o resultado salvo. Operação pendente/incerta ou consumo ausente bloqueia chamadas seguintes; custo desconhecido permanece null. Preços são os configurados no perfil e representam estimativas, não garantia de faturamento. Nenhuma transação permanece aberta durante a chamada externa.

`POST /internal/v1/executions/{id}/result` aceita resultado idempotente. Sucesso exige checkpoint atual com edição seguida de teste, sem falhas/pendências nem orçamento incerto/excedido. Uma execução Succeeded não conclui a tarefa: ela permanece Execution até publicação e revisão do PR. Falha normal marca tarefa Failed; cancelamento e prazo exigem estado correspondente no servidor. Reenvio conhecido do resultado é permitido após o prazo, com a mesma identidade técnica ainda ativa; operação diferente é conflito.

O controlador interrompe trabalho ao perder heartbeat, atingir prazo ou receber Ctrl+C; tarefas locais e cleanup são aguardados. Retomada/reconciliação após crash e consumo contínuo pertencem à T17.

Testes do harness montam o checkout somente para leitura, impedindo o código de teste de alterar fontes enquanto as valida. Escritas deliberadas vêm da ferramenta do controlador. Arquivos novos exigem caminho previamente autorizado e expectedHash=missing; criação usa CreateNew e não sobrescreve arquivo existente. Para arquivos existentes, SHA-256 continua obrigatório. Arquivos de teste/validação confiáveis não devem ser editáveis pela política do modelo.

`sandbox` opcional no JSON configura imagem fixada por digest e limites: memoryMb 256..2048, cpus 1..4, pids 128..512. O default permanece Alpine para fixtures. Imagens de SDK precisam ser preparadas pelo operador com dependências disponíveis offline, sem segredos; o runtime nunca faz pull. Testes devem escrever outputs em /tmp, pois fontes e raiz são read-only e a rede permanece desligada. Imagem, comando e recursos não são escolhidos pelo modelo. Não foi validado um build .NET dentro de imagem de SDK; builds do produto foram verificados com o SDK local. Produção multi-tenant continua exigindo a decisão de VM/microVM do ADR 0003.

Os testes usam doubles explícitos de modelo/transporte para orçamento/coordenador, mais Git, PostgreSQL e Docker reais nas fixtures integradas. Nenhuma chamada paga ou PR externo foi feito nesta implementação.
