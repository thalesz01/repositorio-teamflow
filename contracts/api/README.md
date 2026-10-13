# Contrato HTTP

Fonte: OpenAPI gerado pelo backend em `/openapi/v1.json` no ambiente Development. Endpoints de produto usam `/api/v1`, JSON camelCase, IDs UUID, enums string e timestamps UTC. Listas usam page (1..100000), pageSize (1..100, padrão 20), items e total. Limites pertencem ao backend.

Erros usam ProblemDetails com code estável e traceId. X-Correlation-Id identifica a requisição; não aceitar IDs externos ilimitados. Clientes não recebem stack trace.

O cliente TypeScript será gerado com openapi-typescript no consumidor apps/web, sem importar classes da API. Não editar manualmente tipos gerados. Snapshot é versionado com os endpoints de produto; verificar geração novamente a cada mudança de contrato.

## Geração reproduzível

Com API em Development, exportar /openapi/v1.json para contracts/api/openapi.json. Em apps/web executar npm run generate:api para usar o snapshot, ou npm run generate:api -- http://localhost:5080/openapi/v1.json para gerar de uma API disponível. O gerador e sua versão ficam no consumidor. Tipos gerados são versionados; build web não consulta a API nem o diretório contracts. O snapshot atual descreve autenticação, organizações, membros, projetos, tarefas e execuções; regenerar após alterar endpoints.
