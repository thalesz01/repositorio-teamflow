# 0009 — Merge fast-forward feito pelo aplicativo

- Status: **Aceita** em 2026-10-13 pelo responsável ("Botão de merge no app"). Altera a ADR 0005, que mantinha o merge como ação manual no terminal.
- Contexto: depois da aprovação independente, concluir uma tarefa exigia voltar ao terminal, rodar `git merge --ff-only teamflow/<id>` e consultar de novo. É um passo mecânico, fácil de errar e que interrompe o fluxo.

## Decisão

O aplicativo oferece **Fazer merge** na etapa Aguardando merge e executa o fast-forward da branch base para o commit aprovado. Continuam valendo: aprovação independente do head testado, testes do runner confirmados, merge somente fast-forward e conclusão só depois de a consulta confirmar head, base e parent.

Salvaguardas obrigatórias:

- **Desligado por padrão.** `Local:EnableMerge=false`; o administrador liga explicitamente no servidor, como `Local:EnablePublication` e `Local:EnableReview`.
- **Só o commit aprovado.** A API exige estado `awaiting_merge`, aprovação registrada para exatamente o head publicado e, antes de agir, repete a consulta de revisão (head, base e parent). Se algo mudou, não faz nada.
- **Nunca sobrescreve trabalho local.** Se a branch base está em uso na pasta (checkout atual), a árvore de trabalho precisa estar limpa (arquivos rastreados sem alteração); caso contrário a operação é recusada com `merge_requires_clean_checkout` e nada é alterado. Quando a pasta está em outra branch, só a referência da branch base avança, com verificação do valor anterior.
- **Sem commit novo, sem force.** Apenas `git merge --ff-only` (ou `update-ref` com valor esperado). Hooks, configuração global e assinatura ficam desabilitados, como nos demais comandos Git do servidor.
- **Idempotente e rastreável.** Repetir depois de concluído devolve o estado atual. Quem pediu o merge e quando ficam registrados na execução (`MergeRequestedBy`, `MergeRequestedAt`).

## Consequências

- O produto passa a escrever na pasta do usuário em um único caso, explícito e opt-in: o fast-forward da branch base (a ADR 0005 afirmava que nunca alterava arquivos originais). A publicação da revisão continua escrevendo só objetos e a ref `teamflow/<id>`.
- Quem quiser manter o merge manual não liga a flag; o comportamento anterior continua válido.
- A regra de aprovação por outra pessoa **não muda**. Uma pessoa sozinha continua sem conseguir aprovar o próprio resultado.
- Novo código de erro estável por recusa: `merge_disabled`, `approval_required`, `merge_requires_clean_checkout`, `base_changed`, `branch_conflict`, `merge_failed`.
