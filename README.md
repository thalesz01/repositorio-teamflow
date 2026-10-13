# TeamFlow

Plataforma de desenvolvimento em equipe com agentes de IA, Kanban e memória compartilhada. Backend C# / ASP.NET Core modular, Next.js / React, PostgreSQL com pgvector e runner/harness C# independente.

Leia [AGENTS.md](AGENTS.md) e [TASKS.md](TASKS.md) antes de alterar o projeto. O backlog registra critérios e evidências reais; builds/configurações das aplicações permanecem próprios.

O fluxo implementado vai de demanda/análise editável até fila, execução/testes, PR, feedback, revisão independente/CI, merge confirmado e resolução candidata. Fontes podem ser validadas, indexadas e recuperadas em novas tarefas; ajustes permitem trocar o perfil preservando estado portátil e alterações confirmadas na mesma base. Histórico de teste não substitui validação nova.

- [API e banco](apps/api/README.md)
- [Frontend](apps/web/README.md)
- [Runner e ferramentas isoladas](apps/runner/README.md)
- [Integrações e credenciais](docs/integrations.md)
- [Memória](docs/shared-memory.md), [indexação](docs/memory-indexing.md), [análise](docs/context-planning.md) e [troca de perfil](docs/agent-transfer.md)
- [Validação e piloto](docs/pilot.md)

A visualização local e fixtures não fazem chamadas pagas nem criam PRs externos. Provedores externos são opt-in. Os testes integrados usam PostgreSQL, Git e Docker reais com doubles explícitos de IA/GitHub. T25 ainda exige smoke externo autorizado com recursos/credenciais/CI e orçamento definidos; consulte o backlog. Não há ambiente de produção instalado.

Production recusa inicialização até implementar sandbox VM/microVM e proteção de segredos aprovadas. Retenção inicial é PostgreSQL com leitura de relatórios limitada a 30 dias e retenção física indefinida; arquivamento de fontes é reversível. Nenhum purge automático foi autorizado/aplicado.


## Modo local aceito em 2026-10-12

O fluxo principal utiliza Codex CLI e repositórios Git locais; siga docs/local-development.md e ADR 0005 (caminhos relativos à raiz do monorepo). As instruções anteriores de GitHub/OpenAI API descrevem integrações alternativas preservadas. Configuração de Development por projeto, clone separado, revisão de diff e merge fast-forward confirmado. Embeddings externos não são necessários: existe indexação textual local.
