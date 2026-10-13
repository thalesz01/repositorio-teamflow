# 0006 — Pasta independente de Git e Codex da máquina

- Status: aceita pelo responsável em 2026-10-12.
- Contexto: exigir Git para salvar a pasta impedia iniciar projetos vazios. Perfis Codex por projeto duplicavam uma autenticação que pertence à máquina.

Configuração de pasta e prontidão para execução são estados distintos. A pasta existente pode ser salva sem Git/commit. Inspeção lê Git e branches locais sem inicializar ou modificar arquivos; execução exige branch com commit. Autorização de caminhos e versionamento permanecem por equipe/projeto.

Codex usa instalação, login, modelo e limites da máquina. A UI não cria ou edita perfis Codex por projeto. Vínculos técnicos com ID determinístico preservam as foreign keys e o histórico existentes, sem copiar credenciais ou armazenar configuração independente. Limites e geração consultam parâmetros globais; o endpoint manual antigo retorna 410. Essa representação evita uma migração ampla do histórico para cumprir a mudança atual.

Verificar login executa somente codex login status; não chama modelo nem devolve tokens de autenticação. Testes usam fixture explícita do comando. Credenciais e ambiente do servidor não são herdados pelo processo; somente o ambiente local necessário ao CLI.
