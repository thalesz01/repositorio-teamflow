# 0008 — Remoção do caminho GitHub/OpenAI

- Status: **Aceita** em 2026-10-13 pelo responsável ("remover"), escolhendo a opção 2 da proposta de mesma data. Substitui a manutenção do legado prevista nas ADRs 0004 e 0005.
- Contexto: a ADR 0005 tornou Codex CLI e Git local o fluxo principal e manteve GitHub App e OpenAI API como integrações compatíveis. Manter dois caminhos aumentava código, testes e superfície (um endpoint anônimo de webhook, segredos persistidos) sem uso no piloto, e o responsável quer testes que não dependam de nenhum repositório Git remoto.

## Decisão

Remover GitHub App, OpenAI API (texto e embeddings) e webhooks. O produto passa a ter um único transporte de IA (Codex CLI) e um único de Git (pasta local).

### O que saiu

- Código: `GitHubProvider`, `GitHubPublisher`, `GitHubReview`, `GitHubWebhook`, `WebhookProcessor` (inbox, worker, assinatura), `OpenAiTextModel`, `OpenAiEmbeddingModel`, `IntegrationSecrets` e os endpoints `POST /webhooks/github`, `POST .../repository/` (conexão GitHub) e `POST .../profiles/` (perfil com chave de API).
- Configuração: `GitHub:*`, `OpenAI:*`, `Memory:EnableEmbeddingRequests`, `Memory:EmbeddingUsdPerMillionTokens` e as chaves por projeto de GitHub/OpenAI.
- Dados (migração `RemoveGitHubOpenAi`): conexões e perfis não locais são **revogados** antes de a tabela `GitHubDeliveries` e as colunas `AppId`, `InstallationId`, `Owner`, `Repository`, `ProtectedPrivateKey` e `ProtectedApiKey` serem apagadas. Segredos persistidos deixam de existir. Execuções, tarefas e memória históricas são preservadas, inclusive seus snapshots.
- Checkout do runner: o caminho HTTPS com token deixou de existir; `GitCheckout` só aceita a pasta local autorizada.
- Busca: sem provedor de embeddings, a busca é só textual e o parâmetro `semantic`/vetor foi removido dos contratos.

### O que permanece

- `ITextModel`, `IGitPublisher` e `IGitReview` continuam como fronteiras (AGENTS.md §4), agora com uma implementação cada (`CodexTextModel`, `LocalGit`) e doubles nos testes.
- Colunas de contrato que mantêm nomes históricos: `GitConnections.Provider`/`RepositoryId`, `pullRequest`/`number`/`ciPassed` na API, preços por milhão de tokens e `BillingMode`. O turno histórico `BillingMode=api` sem custo conhecido continua bloqueando nova tentativa.
- Coluna `MemoryChunks.Embedding` e extensão pgvector, vazias, à espera de um provedor futuro. Um provedor novo exigirá ADR.

## Consequências

- Atualizar o banco exige aplicar a migração (`dotnet ef database update`) antes de subir a API nova. A migração é irreversível quanto a segredos e identificadores GitHub/OpenAI apagados; os dados de revogação e o histórico são mantidos. O caminho de atualização foi verificado em banco temporário com linhas legadas (conexão e perfil revogados, locais intactos).
- Os testes de provedores externos (OpenAI, GitHub, webhook, embeddings) foram removidos. A jornada integrada continua cobrindo análise, execução, publicação, feedback, aprovação independente, merge fast-forward e reuso de memória, com Git real e doubles de IA.
- Contratos: OpenAPI e tipos do web foram regenerados; `semantic`, vetor de busca, `GitInput`, `ProfileInput` e `InstallationToken` (agora `CheckoutGrant`) saíram. Os consumidores no repositório foram atualizados.
- Nenhuma validação de CI externo é possível por esse caminho: a revisão é a verificação local de head, base e fast-forward mais a aprovação humana independente.
