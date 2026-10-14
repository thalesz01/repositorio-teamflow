# 0017 — `develop` como branch principal e uma branch de feature por tarefa

- Status: **Aceita** em 2026-10-14 pelo responsável ("sempre uma tarefa puxando da branch develop como feature").
- Contexto: o repositório passou a ter remoto (`thalesz01/repositorio-teamflow`). Até aqui o trabalho ia em branches avulsas a partir de `main`.

## Decisão

1. **`develop` é a branch principal de trabalho** e a branch padrão do repositório. Recebe somente o que foi revisado e passou nos checks da tarefa. `main` fica reservada para versões estáveis: só avança a partir de `develop`, por decisão explícita do responsável.
2. **Uma tarefa, uma branch.** Cada tarefa do `TASKS.md` começa de uma `develop` atualizada (`git switch develop`, `git pull --ff-only`, `git switch -c feature/T62-nome-curto`). Correções fora de uma tarefa usam `fix/nome-curto`; manutenção, `chore/nome-curto`. Nomes em inglês, minúsculos, com hífens.
3. **Volta por pull request para `develop`**, com o texto exigido no AGENTS.md (problema, comportamento final, validação, limitações). Revisão humana e CI conforme o projeto; a branch é apagada após o merge. Não se commita direto em `develop` depois desta estruturação inicial.
4. **Commits** seguem o AGENTS.md (pequenos, coesos, prefixos `feat`, `fix`, `refactor`, `test`, `docs`, `build`, `chore`).
5. **Proteção da `develop`** (exigir PR, revisão e checks) é configuração do GitHub, feita pelo responsável; este repositório não a aplica.

## Consequências

- As branches `teamflow/<executionId>` que o próprio produto cria para cada execução são outra coisa: continuam saindo da branch base escolhida na tarefa. Para um projeto que usa este fluxo, escolha `develop` como branch de partida ao criar a tarefa.
- O histórico local começou antes do remoto: o primeiro `develop` inclui um merge com o "Initial commit" do GitHub (apenas um `README.md` provisório, substituído pelo README do projeto).
