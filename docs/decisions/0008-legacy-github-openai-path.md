# 0008 — Destino do caminho legado GitHub/OpenAI

- Status: **Proposta** em 2026-10-13. Depende de aceite do responsável; nenhuma mudança de código acompanha esta proposta.
- Contexto: a ADR 0005 tornou Codex CLI e Git local o fluxo principal e manteve GitHub App e OpenAI API como integrações anteriores compatíveis. A análise do projeto levantou se esse caminho deveria sair do registro padrão da API.

## Situação verificada

- `Program.cs` registra `OpenAiTextModel`, `OpenAiEmbeddingModel`, `GitHubProvider`, `GitHubPublisher`, `GitHubReview`, o worker de webhooks e `POST /webhooks/github`, junto com os equivalentes locais.
- O uso em tempo de execução já é opt-in por configuração, com padrão desligado: `OpenAI:EnableRequests`, `Memory:EnableEmbeddingRequests`, `GitHub:EnablePublication`, `GitHub:EnableReview` e, no webhook, `GitHub:WebhookSecret`. Sem o segredo, `/webhooks/github` responde 503 `webhook_not_configured` antes de qualquer efeito.
- `ITextModel`, `IGitPublisher` e `IGitReview` são as fronteiras de provedor de IA e de Git que o AGENTS.md pede. `ProfileAccess`, `RepositoryAccess`, `PublicationAccess` e `ReviewAccess` escolhem entre local e remoto por dado persistido (perfil, `LocalPath`), não por registro no contêiner. Registro condicional exigiria resolver o provedor por requisição e acrescentaria um segundo mecanismo de seleção.
- Tamanho do legado: cerca de 680 linhas em `Features/Integrations` (GitHub*, OpenAi*, Webhook*), migrações e colunas já aplicadas, e testes (`ProviderTests`, `PublicationProviderTests`, `ReviewProviderTests`, `WebhookTests`, `EmbeddingProviderTests`) que usam doubles.

## Opções

1. **Manter como está, documentar o estado (recomendada).** Sem código novo. O custo atual é de leitura e manutenção, não de exposição: o caminho é inerte sem configuração explícita. Preserva dados e execuções anteriores e a opção de voltar ao GitHub no piloto.
2. **Remover.** Apaga o código, as colunas e os testes do legado por nova migração e quebra a compatibilidade das ADRs 0004 e 0005. Só se justifica se GitHub/OpenAI forem descartados como direção do produto. Exige plano para registros existentes, pois não se altera migração aplicada.
3. **Registro condicional por configuração.** Rejeitada: não reduz exposição que já esteja ativa e duplica a seleção de provedor.

## Decisão proposta

Adotar a opção 1 e registrar que:

- GitHub App, OpenAI API e webhooks continuam suportados somente por configuração explícita de servidor; o fluxo local não os requer.
- Não há mudança no `Program.cs`, nas migrações ou nas interfaces.
- Reavaliar a remoção (opção 2) quando o piloto local (T25) terminar e se decidir que GitHub/OpenAI não fazem parte da direção do produto.

## Consequências

- O legado continua sendo mantido e testado com doubles; chamadas reais seguem opt-in e não são exercidas no CI padrão.
- A superfície anônima permanece `POST /webhooks/github`, que valida assinatura HMAC, tamanho e entrega antes de aceitar. Antes de qualquer implantação, o endpoint deve ser exposto por proxy apenas se o webhook for utilizado.
