# 0014 — Tarefas de análise (perguntas sobre o código)

- Status: **Aceita** em 2026-10-13 pelo responsável ("criar uma tarefa para análise": escolher o repositório e descrever uma pergunta para saber a regra de algo específico, por exemplo de uma rota de uma API).
- Contexto: todas as tarefas eram mudanças de código: o agente escreve, testa, publica uma revisão e a pessoa conclui. Muitas vezes a pessoa só precisa entender o que o código faz.

## Decisão

1. **Tipo da tarefa.** `WorkItem.Kind` é `change` (padrão, igual a hoje) ou `analysis`. Uma análise exige a pasta (`folder_required`) e nasce pronta. A criação aceita `kind` em `TaskInput`; qualquer outro valor falha com `invalid_kind`.
2. **Somente leitura, por construção.** Para uma análise o runner entrega ao agente `editable` vazio, nenhum padrão `create` e não executa o comando de teste (`testsAllowed = false`); o harness roda em `HarnessMode.Analysis`. A API repete a regra: ações `write` e `test` são recusadas (`invalid_model_action`). Nada é publicado, revisado ou mesclado e a pasta nunca é alterada.
3. **Ferramenta `search`.** Nova ação de leitura: procura um texto literal (2 a 200 caracteres, sem diferenciar maiúsculas) apenas nos arquivos legíveis pela política e devolve até 40 linhas `arquivo:linha: trecho`. Vale também para tarefas de mudança. Sem ela o agente teria de ler os arquivos um a um dentro do limite de passos.
4. **Resposta como resultado.** A análise termina com `finish` cujo `content` é a resposta. O resultado `analysis_answered` só é aceito se: a tarefa é uma análise; houve ao menos uma leitura ou busca bem-sucedida; não houve escrita nem teste; e a última ação do modelo foi um `finish` com conteúdo. Então a execução fica `Succeeded` e a tarefa vai direto a `Done`. Leituras recusadas ou com erro não encerram a análise, porque o agente pode se corrigir; terminar sem consultar nada ou com resposta vazia vira `validation_missing`.
5. **Onde a pessoa vê.** O cartão da análise leva o selo "Análise", o botão "Analisar" e, concluída, "Ver resposta", que abre pergunta e resposta em texto simples com "Copiar resposta" e o aviso de que a resposta da IA pode errar. O texto vem do progresso da execução (`agentSummary`, até 12 000 caracteres).
6. **Limite de entrada.** O limite de entrada do modelo para IAs escolhidas na tarefa deixou de ficar congelado no perfil: vem de `Codex:MaxInputBytes` / `Claude:MaxInputBytes` da máquina, com padrão de 96 KiB (antes 32 KiB), porque uma análise lê vários arquivos.

## Consequências

- O alcance de uma análise é o da política do runner para a pasta (`readable`, no máximo 100 arquivos de até 16 KiB cada). Uma pasta sem política própria usa a política padrão do projeto, que pode não listar os arquivos do repositório: configure a pasta com `Setup-Runner.ps1 -Folder`. A leitura envia trechos dos arquivos ao provedor da IA escolhida, como nas demais tarefas.
- A resposta é texto livre do modelo, não é verificada e não vira memória do projeto.
- Uma análise concluída não volta a "Criadas": uma nova pergunta é uma nova tarefa.
- Migração `TaskKind` (coluna com padrão `change`); contratos aditivos.
