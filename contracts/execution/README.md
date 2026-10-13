# Contrato do runner — v1

Status: fila/leases, autenticação técnica, checkout, harness e recuperação implementados até T17; publicação de PR implementada na T19. Schemas v1 pertencem a contracts/execution. Progresso humano usa checkpoints e SSE; revisão/CI/merge estão implementados na T20; memória e transferência portátil estão implementadas na T24.

## Fronteira

Runner é cliente técnico do backend, sem banco e sem sessão humana. Tráfego HTTPS fora de localhost, credencial rotativa com escopo de runner e autorização do backend para cada execução. A credencial não concede acesso aos endpoints de usuários. Credenciais técnicas por projeto, hash, expiração, rotação e revogação estão implementadas; sessão humana não autoriza os endpoints técnicos.

| Operação definida | Semântica |
| --- | --- |
| POST /internal/v1/executions/claim | Reivindica uma execução persistida disponível; 204 se não houver trabalho |
| POST /internal/v1/executions/{id}/heartbeat | Renova lease vigente com token de posse; devolve cancelamento solicitado |
| PUT /internal/v1/executions/{id}/checkpoint | Persiste checkpoint versionado usando token de posse e sequência esperada |
| GET /api/v1/organizations/{org}/projects/{project}/tasks/{task}/executions/{id}/events | Progresso SSE autorizado para sessão humana, derivado dos checkpoints |
| POST /internal/v1/executions/{id}/result | Registra resultado terminal ou bloqueio com operationId; repetição consistente retorna o resultado anterior |

Todas as operações validam posse vigente. Token de lease antigo não pode escrever depois de nova reivindicação. organizationId/projectId vêm do registro validado pelo backend, nunca são aceitos como prova de autorização. Não registrar token de posse.

## Mensagem de execução

Campos obrigatórios: schemaVersion, executionId, taskId, organizationId, projectId, workerId, attempt, leaseToken, leaseExpiresAt, deadlineAt, snapshot e limits. Repositório, commit base e perfil serão preenchidos pelas integrações antes da execução de código. Campos opcionais: branch, checkpoint anterior e referências de contexto. Segredos são referências de curta duração, nunca incluídos em logs ou checkpoints. O schema v1 inclui envelope, snapshot e limites. Repositório e perfil podem ser null até suas integrações; o runner não deve executar código ou IA sem configuração válida.

Limites: duração, tentativas, ferramentas, passos e orçamento; unidades explícitas. Tokens/custo não disponíveis são null. Campos novos compatíveis são opcionais; valores desconhecidos de estado não devem virar sucesso por padrão.

## Checkpoint portátil

Inclui versão, sequência, fase, commit/base/branch, hashes relevantes, artefatos, fontes consultadas, decisões com evidências, alterações, testes e pendências. Não contém raciocínio interno proprietário nem dados de outro tenant. Artefatos volumosos são referências autorizadas, não blobs ilimitados.

## Recuperação

Claim e lease são transacionais. Consumidor não trabalha antes da confirmação do claim. Em resposta perdida, recuperar posse pelo identificador idempotente, sem criar outra execução. Heartbeat perdido requer verificar posse antes de novos efeitos. Lease vencido permite outra tentativa, com reconciliação de operações já efetuadas. Cancelamento impede novo trabalho e exige encerramento cooperativo; limites do sandbox tratam processo não cooperativo na T12/T17.

Não há exactly-once. Commit, upload e PR usam operationId e consulta do efeito anterior. Novos resultados exigem posse vigente, exceto cancelamento/deadline confirmados pelo servidor. Resultado já confirmado admite replay consistente pela mesma posse registrada e credencial ativa após expiração. Atualização de tarefa e resultado de execução são persistidos de forma consistente pelo backend.

## Implementação T12

Claim e heartbeat estão implementados; T16 acrescenta escrita/leitura de checkpoints. Resultados e operações de modelo estão implementados na T16; recuperação está implementada na T17. Progresso público deriva dos checkpoints na T18. Autenticação Bearer separada de sessão humana; escopo deriva da credencial, não do payload. Claim retorna 204 ou envelope conforme execution-message.schema.json. `repository` e `agentProfileId` são opcionais/null quando não configurados. T14 inclui snapshot de conexão/versão/URL/base/branch; T15 inclui o perfil escolhido, sempre limitado ao projeto. RunnerClient valida versão, escopo, posse, prazos e limites sem importar a aplicação backend.
## Checkout T14

POST /internal/v1/executions/{id}/repository-token recebe leaseToken e devolve token temporário com expiresAt somente ao runner autenticado. Revalida escopo, credencial ativa, posse e conexão após a emissão. Token não integra o envelope nem o snapshot. Base e branch são fixadas no agendamento e verificadas pelo checkout; resultados/checkpoints são registrados pelo harness no protocolo durável.

## Checkpoints T16

PUT /internal/v1/executions/{id}/checkpoint/ e POST /internal/v1/executions/{id}/checkpoint/read estão implementados, com Bearer + leaseToken vigente. Contexto v1 em portable-context.schema.json; ações do modelo em agent-action.schema.json. Sequência inicial zero e incremento de um; conteúdo anterior imutável; tentativas preservadas. Contexto limitado a 96 KiB. Consulte [harness](../../docs/harness.md) para idempotência, erros, retenção e limites. Há retomada com recibos confirmados e registro terminal; efeitos incertos bloqueiam o retry automático.

## Modelo, resultado e recuperação T16–T17

POST `/internal/v1/executions/{id}/model-turn`: leaseToken, operationId, sequence e política de paths; reserva orçamento antes do I/O, não repete chamadas pendentes/incertas. POST `/result`: leaseToken, operationId, checkpointSequence, validated e reason; validação exige escrita seguida de teste, sem falhas ou pendências. Resultado consistente pode ser relido após expiração pela mesma credencial ativa/posse registrada. Sucesso técnico não conclui tarefa nem equivale a aprovação de PR.

POST `/recovery`: leaseToken; responde checkpoint e writes (step/path/content/expectedHash/hash) autorizados. Checkpoint inicial de uma nova tentativa herda exatamente o último contexto confirmado. Conteúdo pertence ao controlador, não a logs públicos. Revogação ou consumo incerto impedem continuar.
## Memória e transferência compatíveis (T24)

Envelope v1 admite memory e transfer opcionais. PortableContext/CheckpointContext admitem memory e history opcionais, omitidos quando ausentes para preservar hashes antigos. context-bundle.schema.json e portable-transfer.schema.json registram formatos e limites. Fontes são dados não confiáveis e revalidadas; histórico anterior não autoriza ferramentas nem substitui testes atuais. Carregue os schemas no registry pelos IDs canônicos antes de resolver referências; não buscar o domínio fictício na rede.
