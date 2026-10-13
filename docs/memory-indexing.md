# Ingestão e indexação

Documentos e código são ingeridos explicitamente pelo cadastro de fontes da memória: uma chave relativa permitida e conteúdo revisado, até 32 KiB UTF-8. Não há varredura indiscriminada do checkout. Extensões de texto/código são permitidas; arquivos ocultos, credenciais, secrets, appsettings, dependências, saídas geradas e chaves privadas são excluídos. A validação de credenciais é repetida antes de indexar.

`POST /memory/{id}/indexes` recebe `version`, `profileId`, `idempotencyKey` e `maxCostUsd` (até US$ 5, mantido por compatibilidade: a indexação não tem custo). Apenas fontes atuais, ativas e validadas entram na fila. Repetir a chave com o mesmo pedido retorna o mesmo job; outro payload gera conflito. `GET` paginado no mesmo caminho mostra estado, falha, modelo, versão do chunker e consumo. Uma reconstrução explícita usa uma nova chave: a versão da fonte é independente do índice.

O worker serial usa leases PostgreSQL e `SKIP LOCKED`. Fontes são divididas por runes em trechos de até 2048 bytes, reconstruíveis na ordem, e cada trecho registra hash e índice/versão. O índice é sempre textual (`model=text-only`, `dimensions=0`, vetores vazios): não há chamada externa. Acesso e versão são conferidos antes de gravar; arquivamento, obsolescência, nova versão ou revogação do perfil suspendem a recuperação dos derivados. Índices incompletos nunca são retornados pela busca.

`Memory:EnableIndexWorker=true` liga o worker. Testing o desliga. Não existe provedor de embeddings: a coluna de vetores e a extensão pgvector permanecem no esquema para um provedor futuro (AGENTS.md §11), mas nada as preenche hoje. Um provedor novo exige decisão e ADR próprios.

Referência: [pgvector](https://github.com/pgvector/pgvector).
