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

A visualização local e as fixtures não chamam modelos nem criam repositórios remotos: Git é sempre local. Os testes integrados usam PostgreSQL, Git e Docker reais com doubles explícitos de IA. Chamadas ao Codex real são opt-in. A T25 ainda exige o smoke com Codex real; consulte o backlog. Não há ambiente de produção instalado.

Production recusa inicialização até implementar sandbox VM/microVM e proteção de segredos aprovadas. Retenção inicial é PostgreSQL com leitura de relatórios limitada a 30 dias e retenção física indefinida; arquivamento de fontes é reversível. Nenhum purge automático foi autorizado/aplicado.


## Início rápido local

Com `.env` criado, Docker, Git e Codex instalados: `./scripts/Start-TeamFlow.ps1` inicia banco, API, web e os runners; o quadro mostra "Primeiros passos" até Codex e runner estarem prontos; o fluxo tem quatro etapas — criar (com a pasta do código), executar, corrigir (comentando linhas do diff) e concluir (ADR 0010) (ver [docs/local-development.md](docs/local-development.md)). O merge pode ser feito no app quando o operador liga `Local:EnableMerge` (ADR 0009).

## Modo local aceito em 2026-10-12
O fluxo utiliza Codex CLI e repositórios Git locais; siga docs/local-development.md e as ADRs 0005 e 0008 (caminhos relativos à raiz do monorepo). GitHub, OpenAI API e webhooks foram removidos. Configuração de Development por projeto, clone separado, revisão de diff e merge fast-forward confirmado. Embeddings externos não são necessários: existe indexação textual local.
