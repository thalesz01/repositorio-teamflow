# 0010 — Fluxo em quatro etapas, espaço pessoal e pasta por tarefa

- Status: **Aceita** em 2026-10-13 pelo responsável ("Criar tarefa, Executar, Corrigir e concluir"; "não quero mais segmentar por equipe e projeto"; "Quem criou a tarefa"; "Esconder da interface"; "Qualquer pasta Git local"). Altera a ADR 0009 (aprovação independente) e a configuração de pasta por projeto das ADR 0005 e 0006.
- Contexto: o quadro tinha sete etapas, a pasta do código era configurada por projeto em outra tela, e era preciso escolher equipe e projeto antes de criar uma tarefa. Uma pessoa sozinha precisava de dois usuários para concluir.

## Decisão

1. **Quatro etapas visíveis**: *Criada*, *Executando*, *Corrigir* (revisão) e *Concluída*. As etapas internas (`intake`, `analysis`, `ready`, `execution`, `review`, `awaitingMerge`, `done`, falha/cancelamento) e as regras de transição continuam no backend; a interface apenas as agrupa. Criar a tarefa já a deixa `ready`.
2. **Pasta na tarefa.** A tarefa guarda a pasta Git e a branch base escolhidas ao criá-la. Cada pasta/branch vira uma conexão `ForTasks` reutilizável do projeto; a conexão única "do projeto" continua existindo para tarefas e testes antigos. O commit base é atualizado a partir da branch no início de cada execução; execuções em andamento mantêm o seu próprio commit base e são recusadas com `base_changed` se a branch avançar antes da publicação ou do merge.
3. **Espaço pessoal, sem equipe/projeto na interface.** `POST /api/v1/workspace` devolve (e cria, se preciso) o espaço de quem está autenticado: o projeto de sua propriedade com a tarefa mais recente, ou "Pessoal/Tarefas". O modelo e o isolamento por `organizationId`/`projectId` não mudam; só deixam de aparecer na interface. Outros projetos continuam existindo e acessíveis pela API.
4. **Concluir por quem criou a tarefa.** Com `Local:SingleUser=true` (padrão `false`), quem pediu a execução pode aprovar o próprio commit. **Concluir** executa, em um comando (`POST .../review/complete`), a aprovação do head testado e o merge fast-forward da ADR 0009, com as mesmas salvaguardas (`Local:EnableMerge`, árvore limpa, base e parent inalterados, só o commit aprovado). Com `SingleUser=false` a regra de outra pessoa continua valendo.
5. **Corrigir com comentários no código.** Na revisão, a pessoa comenta linhas do diff; ao enviar, os comentários viram o texto de ajustes da nova execução (arquivo, linha e trecho), que herda o histórico como hoje. O limite do texto de ajustes passa de 2000 para 6000 caracteres.
6. **Pastas livres.** `Local:BrowseAllDrives` e `Local:AllowRepositorySelection` valem para todo o servidor quando o projeto não tem configuração própria; a configuração por projeto, quando existe, prevalece. Continuam proibidos links e diretórios inacessíveis, e só o proprietário navega.
7. **Runner por pasta.** O `controller.json` do runner aceita `folders` (caminho → política de ferramentas e sandbox) e usa `policy` como padrão. A política continua sendo uma decisão do operador; o repositório não autoriza ferramentas.

## Consequências

- `SingleUser` remove a segunda pessoa apenas onde o operador o liga; ela deixa de provar independência de revisão. O registro de aprovação e merge (quem, quando, head) continua gravado.
- Tarefas antigas continuam funcionando pela conexão do projeto. Colunas novas são opcionais; migração aditiva (a exceção é o limite do texto de ajustes, ampliado).
- Duas tarefas na mesma pasta competem pela mesma branch base: a segunda a concluir pode receber `base_changed` e precisa de nova execução.
- Sem política de pasta no runner, vale a política padrão do `controller.json`, que pode não servir ao novo repositório; a execução falha de forma explícita (`tool_failed`), não aprova nada por engano.
- Códigos novos: `invalid_stage`.
