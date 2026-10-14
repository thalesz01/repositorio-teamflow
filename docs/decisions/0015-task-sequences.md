# 0015 — Tarefas em sequência

- Status: **Aceita** em 2026-10-14 pelo responsável ("uma tarefa é sequência da anterior; registrar essas tarefas em sequência e, ao executar uma, executar todas").
- Contexto: cada execução parte da ponta da branch base (ADR 0010) e só vira parte da base quando a pessoa conclui a revisão (ADR 0009). Uma tarefa que depende do resultado da anterior não enxergava o que a anterior criou enquanto ela não era concluída, e a pessoa precisava executar, revisar, concluir e só então executar a próxima.

## Decisão

1. **Registro.** `WorkItem.PreviousTaskId` liga uma tarefa à que ela continua (índice único parcial: no máximo uma sucessora por tarefa, sem bifurcação). Só vale entre tarefas de mudança da mesma pasta (mesma conexão local); a anterior não pode estar falha, cancelada ou bloqueada (`invalid_sequence`, `sequence_branch`). A API devolve `previousTaskId` e `sequenceRunning` no `TaskDto`.
2. **Executar a sequência.** `POST …/tasks/{id}/sequence/start` marca as etapas prontas da cadeia com `SequenceStartedBy` (quem iniciou) e agenda as que já podem rodar. `POST …/sequence/stop` remove a marca. A tela chama `start` quando a tarefa pertence a uma sequência.
3. **Avanço.** `TaskSequences.AdvanceAsync` (chamado ao iniciar e a cada 5 s por `SequenceWorker`; desligado no ambiente Testing) percorre as etapas marcadas e prontas: espera enquanto a anterior está em execução ou na fila da própria sequência; quando a anterior está em revisão (publicada), agenda a etapa sobre ela; quando está concluída, agenda normalmente; em qualquer outro caso interrompe a sequência (remove a marca e registra o motivo). Cada etapa usa a chave de idempotência fixa `sequence-<taskId>`, então repetir o passo é seguro e sobrevive a reinício (o estado está no PostgreSQL).
4. **Empilhamento.** A etapa seguinte usa como base o commit publicado pela anterior (`PublishedHeadCommit`), verificado contra a ponta de `refs/heads/teamflow/<execução anterior>` e contra a branch base (a base precisa ser ancestral). O snapshot guarda `StackedOn` (execução anterior). A publicação (`PublicationPlan.Stacked`), a revisão e o checkout aceitam que a ponta da branch base esteja atrás da base da etapa, desde que seja ancestral dela; qualquer outra mudança continua sendo `base_changed`.
5. **Ordem ao concluir.** O merge é fast-forward (ADR 0009): concluir a etapa 2 com a etapa 1 ainda pendente é recusado com `previous_step_pending`; concluída a 1, a 2 avança normalmente. Nada é aplicado automaticamente.
6. **Proteções.** Rodar sozinha uma tarefa que continua outra ainda não concluída é recusado (`previous_step_pending`), porque partiria da ponta da branch e perderia o trabalho da anterior. Corrigir uma etapa cuja sucessora já iniciou é recusado (`next_step_started`); a correção de uma etapa empilhada mantém a sua base. Skills não se aplicam a sequências.

## Consequências

- A sequência só enfileira; quem executa continua sendo o runner (uma execução por vez por projeto, ver [concurrent-execution.md](../concurrent-execution.md)). Se a etapa anterior for corrigida e republicada, as seguintes que ainda não começaram usam o novo commit; as já iniciadas bloqueiam a correção.
- Se a branch base avançar por fora para um commit que não é ancestral da base da etapa, a etapa seguinte falha com `base_changed` e a sequência para: é necessário repetir a etapa.
- Migração `TaskSequences` (duas colunas, uma chave estrangeira composta com o mesmo tenant e o índice único parcial); contratos aditivos.
