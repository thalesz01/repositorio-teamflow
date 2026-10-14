# Execução de várias tarefas ao mesmo tempo — análise

Data: 2026-10-14. Pergunta: o projeto consegue executar várias tarefas ao mesmo tempo? Resposta curta: **o servidor e o armazenamento aguentam, mas hoje o runner executa uma tarefa por vez por projeto**; e no fluxo pessoal ("Tarefas", um projeto por pessoa) isso significa uma execução de cada vez. Nenhuma execução real com Codex/Claude foi feita para esta análise: as evidências abaixo são leitura do código e testes automatizados com PostgreSQL e Git reais.

## O que já funciona em paralelo

| Ponto | Evidência |
| --- | --- |
| Muitas tarefas na fila | O índice único permite uma execução ativa (`Queued`/`Leased`) **por tarefa**, não por projeto. `ExecutionScheduler.ScheduleAsync` só rejeita uma segunda execução da mesma tarefa (`execution_active`). |
| Dois runners não pegam a mesma execução | `ExecutionScheduler.ClaimAsync` usa `SELECT … FOR UPDATE SKIP LOCKED … LIMIT 1` dentro de transação. Teste existente `CompetingWorkersCannotClaimTheSameLeaseAsync`: duas chamadas `ClaimAsync` simultâneas disputam uma execução e exatamente uma recebe o lease. |
| Isolamento entre execuções | Cada execução tem seu próprio diretório de trabalho (`ExecutionWorkspace`), container ou processo do sandbox e branch própria `refs/heads/teamflow/<executionId>`. A publicação grava só objetos e essa referência (`update-ref` com `verify` da ponta da base), nunca o checkout. |
| Publicações simultâneas na mesma pasta | Teste novo `IndependentExecutionsOnTheSameFolderPublishInParallelButOnlyTheFirstMergesAsync` (`LocalMergeTests`): duas execuções saídas da mesma base publicam ao mesmo tempo com `Task.WhenAll`; as duas têm sucesso, commits diferentes, e a branch base continua intocada. |
| Projetos diferentes | Cada projeto tem o seu runner (credencial própria, `apps/runner/work/local/<projectId>`), então projetos diferentes executam em paralelo de verdade. |

## O que impede o paralelismo hoje

1. **Um runner por projeto, uma execução por vez.** `RunnerWorker.RunAsync` é um laço sequencial (`RunOnceAsync` reivindica, executa e devolve o resultado antes de reivindicar outra) e `Start-LocalRunner.ps1` mantém `worker.lock` aberto com acesso exclusivo, o que impede um segundo processo para o mesmo projeto (o `LocalRunnerSupervisor` e o `Start-TeamFlow.ps1` só iniciam um runner quando o lock está livre). Todas as tarefas do projeto, mesmo de pastas diferentes, esperam na fila em ordem de criação (`ORDER BY "CreatedAt"`). No fluxo pessoal (ADR 0010) cada pessoa tem um único projeto, então a concorrência prática é 1.
2. **Não há limite configurável de concorrência.** O AGENTS.md pede limitar concorrência; hoje o limite é implícito (número de runners). Não existe um parâmetro "N execuções simultâneas" nem orçamento compartilhado entre execuções.
3. **Mudanças independentes na mesma pasta colidem na conclusão.** Duas tarefas executadas ao mesmo tempo partem do mesmo commit. Quando a pessoa conclui a primeira (fast-forward, ADR 0009), a segunda deixa de ser aplicável: o teste acima confirma `base_changed` na segunda conclusão, e o remédio é executar a tarefa de novo sobre a nova base. Não há rebase automático nem detecção prévia de arquivos em comum. Para tarefas que dependem uma da outra, use [sequência](decisions/0015-task-sequences.md).
4. **Recursos da máquina.** Com `execution: host` os testes rodam direto na máquina do usuário; builds simultâneos disputam CPU, portas e caches do SDK. Com Docker a disputa é de CPU e memória. Cada passo do agente chama o CLI do modelo (Codex ou Claude); o consumo de cota/limites do provedor cresce com a concorrência e não é medido aqui.

## Opções, se o objetivo for paralelismo real

- **A. Vários runners por projeto (menor mudança).** Trocar `worker.lock` por um lock por "vaga" (`worker-1.lock`, `worker-2.lock`…) e permitir N credenciais de runner. A fila já suporta isso (`SKIP LOCKED`). Falta: configuração de N, supervisão de N processos, e um limite global por projeto/usuário.
- **B. Laço concorrente dentro do runner.** `RunnerWorker` reivindicaria até N execuções em paralelo; exige revisar o estado compartilhado do processo (log, sandbox) e é mais invasivo que A.
- **C. Política por pasta.** Permitir concorrência só entre pastas diferentes (nunca na mesma), o que elimina a colisão do item 3 sem rebase.

Recomendação: A com limite padrão 1, exposto por configuração, e a restrição C (uma execução ativa por pasta) até existir um rebase/detecção de conflito. Isso exige decisão do responsável (mudança de fronteira do runner) e não foi implementado.

## Limitações desta análise

- Não foi executado o cenário completo com dois runners no mesmo projeto, porque o `worker.lock` o impede por desenho; a segurança de duas reivindicações simultâneas está coberta apenas no nível da API/PostgreSQL.
- O comportamento de Codex/Claude sob carga (limites de taxa, tempo de resposta) não foi medido.
