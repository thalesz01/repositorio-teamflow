# 0002 — Agendamento durável do MVP

Status: Aceita em 2026-10-10 pelo responsável, que aprovou ambas as propostas nesta conversa.

## Contexto

Até T10 precisamos persistir e agendar execuções; runner e harness pertencem a tarefas posteriores. Não podemos perder comandos após restart ou criar execuções concorrentes para uma tarefa.

## Recomendação

Fila persistida no PostgreSQL. Criar execução e mudar tarefa para execution na mesma transação. A própria execução queued é o trabalho agendado, eliminando envio separado para broker. Chave de idempotência limitada a organização/tarefa/operação, com comparação do payload. Índice único parcial impede mais de uma execução ativa por tarefa.

Consumidores futuros reivindicam trabalho com FOR UPDATE SKIP LOCKED, prazo de lease e token de posse (fencing); atualização exige token vigente. Heartbeat renova lease. Lease vencido permite retomada com limite de tentativas e reconciliação dos efeitos externos. Cancelamento é persistido e impede nova reivindicação; checkpoint e operações externas têm identificadores para deduplicação. Sem transação aberta durante execução de ferramentas.

T10 implementa armazenamento e operações de agendamento/reivindicação testáveis, não um worker que finge executar IA. Runner T12 consumirá contrato HTTP versionado. Runner não acessa o banco do produto. Serviço usa HTTPS e identidade técnica com credencial rotativa e escopo de runner, distinta da sessão humana; armazenamento do segredo e sandbox dependem de T11. Sem endpoints de runner públicos antes dessas decisões.

## Alternativa

Temporal: fornece coordenação durável, retries e esperas, mas adiciona serviço e modelo de programação. Reavaliar quando workflows, volume ou recuperação justificarem essa operação. A fila simples exige implementar e testar explicitamente leases, idempotência e falhas.

## Consequências

Menos serviços no MVP, com concorrência transacional no banco existente. Não há garantia exactly-once: efeitos precisam de reconciliação. Esquema pertence ao backend; contrato de runner independe de entidades EF.

Referências: https://www.postgresql.org/docs/current/sql-select.html ; https://docs.temporal.io/

## Decisão

Proposta aprovada explicitamente pelo usuário: "Sim, aprovo". Implementar a recomendação descrita acima no escopo do MVP.
