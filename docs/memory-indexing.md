# Ingestão e indexação

Documentos e código são ingeridos explicitamente pelo cadastro de fontes da memória: uma chave relativa permitida e conteúdo revisado, até 32 KiB UTF-8. Não há varredura indiscriminada do checkout. Extensões de texto/código são permitidas; arquivos ocultos, credenciais, secrets, appsettings, dependências, outputs gerados e chaves privadas são excluídos. A validação de credenciais ocorre novamente antes de qualquer envio.

POST /memory/{id}/indexes recebe version, profileId, idempotencyKey e maxCostUsd (até US$ 5). Apenas fontes atuais, ativas e validadas entram na fila. Repetir a chave com o mesmo pedido retorna o mesmo job; outro payload gera conflito. GET paginado no mesmo caminho mostra estado, falha, modelo, dimensão, versão do chunker, reserva e consumo. Uma reconstrução explícita usa uma nova chave: versões das fontes permanecem independentes dos embeddings.

O worker serial usa leases PostgreSQL e SKIP LOCKED. Antes de uma chamada persiste estado calling e a reserva conservadora de orçamento; não mantém transação aberta durante HTTP. Chamada com resultado perdido, timeout ou crash permanece uncertain e nunca é repetida automaticamente. Falhas antes da chamada são failed. Custo desconhecido permanece null. O operador deve verificar o consumo no provedor antes de autorizar outra solicitação com nova chave.

Fontes são divididas por runes em trechos de até 2048 bytes, reconstruíveis na ordem. Cada trecho registra hash e índice/versão. OpenAiEmbeddingModel usa HTTP nativo /v1/embeddings, text-embedding-3-small, 1536 dimensões e valida cardinalidade, sequência, modelo, vetores finitos não nulos e usage. Vetores ficam em real[] PostgreSQL; T23 faz busca exata com cast para pgvector, sem biblioteca adicional ou índice aproximado prematuro.

Configuração obrigatória para processamento externo: OpenAI:EnableRequests=true, Memory:EnableEmbeddingRequests=true, Memory:EnableIndexWorker=true e Memory:EmbeddingUsdPerMillionTokens com tarifa verificada pelo operador. Perfil reutiliza a chave OpenAI protegida do projeto; nunca entrega a chave ao navegador. Todos os flags ficam false/ausentes por padrão. Testing desliga o worker; doubles verificam comportamento sem cobrança.

Acesso e versão são conferidos antes e depois do envio. Arquivamento, obsolescência, nova versão ou revogação da associação suspende a recuperação dos derivados, mesmo que chunks históricos permaneçam armazenados. Índices incompletos nunca são retornados pela busca.

Referências: [OpenAI embeddings](https://developers.openai.com/api/reference/resources/embeddings/methods/create), [pgvector](https://github.com/pgvector/pgvector). Nenhuma chamada real de embeddings foi executada na validação local.
