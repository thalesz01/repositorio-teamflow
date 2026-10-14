# 0012 — Skills escolhidas por execução

- Status: **Aceita** em 2026-10-13 por pedido do responsável ("poder utilizar uma skill para executar a tarefa", também no projeto).
- Contexto: o harness roda o Codex e o Claude Code em modo restrito (sem ferramentas, sem MCP, sem configuração do usuário), então o mecanismo nativo de skills dos CLIs não está disponível. As skills do usuário e do projeto são arquivos `SKILL.md` de instruções.

## Decisão

1. **Fontes.** O servidor lista `SKILL.md` de, no máximo, quatro pastas conhecidas: `.claude/skills` e `.agents/skills` dentro da pasta da tarefa (origem "projeto") e `.claude/skills` e `.codex/skills` do perfil do usuário da máquina (origem "pessoal", desligável com `Local:UserSkills=false`). Nada fora dessas raízes é lido: nome de pasta restrito a `[A-Za-z0-9._-]`, sem links, arquivo regular, UTF-8 válido, entre 1 byte e 12 KiB, no máximo 100 por raiz.
2. **Escolha.** Na tarefa aberta (colunas "Criadas"), "Executar esta tarefa" oferece "Sem skill" ou uma skill da lista; `ExecutionInput.skill` leva o identificador (`origem:pasta`), nunca um caminho. O servidor resolve o identificador de novo; identificador inválido ou arquivo que mudou de tamanho/encoding falha com `invalid_skill`.
3. **Instantâneo.** O texto lido, o nome e o hash SHA-256 ficam na execução (`SkillSnapshot`, `SkillName`), como as demais entradas do contexto portátil. A execução não relê o arquivo; trocar de IA ou repetir a execução usa o mesmo texto. Uma correção (execução filha) herda a skill da execução que ela corrige, salvo nova escolha.
4. **Uso.** A cada chamada ao modelo o prompt traz o texto da skill depois da política do controlador e antes do contexto, marcado como orientação escolhida pela pessoa. A política de ferramentas continua soberana: a skill não amplia arquivos legíveis/editáveis, comandos de teste nem padrões de criação.
5. **Conteúdo do projeto não é autoridade.** Uma skill da pasta da tarefa é conteúdo do repositório. Ela só é enviada se a pessoa a escolher explicitamente, vendo nome, descrição e o começo do texto; o enquadramento do prompt e a política do runner limitam o dano de um texto malicioso.

## Consequências

- O prompt de cada turno cresce com o tamanho da skill (até 12 KiB), reduzindo o espaço do contexto dentro do limite de entrada do perfil (32 KiB por padrão); skills grandes podem causar `context_limit`.
- A skill vale para a execução inteira, não por etapa. Não há skills com scripts ou arquivos auxiliares: só o `SKILL.md`.
- Migração `ExecutionSkill` (duas colunas opcionais). Códigos novos: `invalid_skill`.
