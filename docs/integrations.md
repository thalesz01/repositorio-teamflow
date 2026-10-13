# Integrações Git e IA

T14/T15 implementam GitHub App, checkout por commit, perfis OpenAI e adaptador de texto Responses API. Configuração e credenciais são limitadas a organização/projeto. Nenhuma conta externa, chamada paga ou PR foi criado pela implementação.

## Configuração do servidor

O administrador configura somente o projeto autorizado, por secret manager ou variáveis do processo da API. Use os UUIDs reais da organização e do projeto na chave de configuração:

```text
Integrations__<organizationId>__<projectId>__GitHub__AppId
Integrations__<organizationId>__<projectId>__GitHub__InstallationId
Integrations__<organizationId>__<projectId>__GitHub__RepositoryId
Integrations__<organizationId>__<projectId>__GitHub__PrivateKeyPem
Integrations__<organizationId>__<projectId>__OpenAI__ApiKey
```

Não colocar valores no Git, em comandos com segredos literais ou no frontend. Instalação/repositório solicitados pelo proprietário precisam coincidir com a autorização do servidor. A GitHub App deve estar instalada no repositório; nesta etapa, tokens solicitam apenas contents read e metadata read.

A interface permite conectar o repositório, criar/revogar perfis e escolher um perfil para novas execuções. A criação importa a credencial do servidor para persistência protegida por Data Protection com finalidade, organização, projeto e ID específicos. A API de leitura retorna somente metadata. Revogar remove o segredo persistido e mantém metadata histórica; novas execuções precisam de outro perfil/conexão. Reiniciar a API após alterar variáveis de ambiente.

Windows usa DPAPI no key ring local. Outros sistemas precisam de `DataProtection:CertificatePath` apontando a PFX com chave privada e `DataProtection:CertificatePassword` fornecida pelo mecanismo de segredos. Sem proteção configurada, importação de segredos falha. Diretórios com chaves XML sem criptografia são rejeitados ao configurar proteção; usar diretório protegido novo ou migrar as chaves existentes antes de iniciar. Linux Development sem certificado usa session-keys apenas para sessão e não aceita segredos duráveis. Preservar/guardar o key ring e o certificado fora de workspaces de execução e fazer backup conforme a política do ambiente. Testing usa chaves efêmeras e credenciais fictícias.

## Checkout Git

`POST /api/v1/organizations/{organizationId}/projects/{projectId}/repository/` exige proprietário/CSRF e GitInput com installationId, repositoryId, owner, repository e baseBranch. A API verifica acesso à instalação, identidade do repositório e SHA da branch. `GET /` consulta metadata ativa; `DELETE /` revoga a conexão. Há no máximo uma conexão ativa por projeto.

O commit é fixado ao conectar. Não acompanha automaticamente movimentos da branch: desconectar/reconectar resolve uma base atualizada. Cada agendamento captura connectionId/version, URL, branch base, SHA e branch `teamflow/<executionId>` em snapshot imutável, sem segredos. Uma conexão revogada/substituída não entrega novos tokens para snapshots antigos.

`POST /internal/v1/executions/{id}/repository-token`, corpo `{ "leaseToken": "..." }`, exige credencial técnica, escopo e lease vigente; revalida posse e conexão depois de emitir o token. Token de leitura restrito a um repositório, temporário, retornado somente ao runner confiável; nunca para endpoints humanos.

RunnerClient.PrepareRepositoryAsync obtém a credencial e passa a IRepositoryCheckout. GitCheckout aceita somente HTTPS github.com, SHA de 40 caracteres e branch gerada da execução. Faz init/fetch/checkout com argumentos estruturados; desabilita configuração global/sistema, hooks, submódulos, symlinks, helpers de credenciais e redirecionamentos. Autorização vai somente ao ambiente efêmero do Git confiável, sem URL, argumentos, arquivo de configuração ou container. Confere o HEAD e retorna base/branch realmente preparados. Saída de erro Git não é propagada para evitar exposição de dados/segredos.

Timeout de checkout: 120 s, também limitado pelo prazo da execução e validade do token. Cancelamento mata a árvore de processos. O chamador possui/dispose o workspace exclusivo mesmo na falha; arquivos Git read-only são removidos somente nesse diretório. Transporte local existe apenas para fixtures da assembly de testes. Lease heartbeat/cancelamento contínuo durante checkout será coordenado pelo harness/T17; não há polling automático nesta etapa.

## Perfis e Responses API

`POST /api/v1/organizations/{organizationId}/projects/{projectId}/profiles/` exige proprietário/CSRF; recebe nome, modelo, limites e preços configurados por milhão de tokens. `GET /` paginado retorna metadata; `DELETE /{id}` revoga. Perfis são imutáveis: alteração de modelo/limites/credencial exige novo perfil. Seleção em ExecutionInput.agentProfileId verifica perfil ativo do mesmo projeto; IDs de outro projeto falham.

Modelo e preços não têm valor presumido. Preços precisam refletir a conta e ser atualizados criando outro perfil. Capacidade atual: textGeneration; tools vazio. Esse adaptador não é um agente completo. O harness e ferramentas estruturadas pertencem à T16.

`ITextModel` usa POST https://api.openai.com/v1/responses, max_output_tokens e store=false. Contexto máximo em bytes, saída máxima em tokens, timeout e cancelamento são aplicados. Chamadas externas ficam bloqueadas até o administrador configurar `OpenAI__EnableRequests=true`; testes padrão usam HttpMessageHandler fictício, não fazem rede e não requerem chave real.

A admissão compara orçamento disponível com estimativa conservadora de entrada/saída pelos preços configurados. Custo retornado é estimativa baseada no usage informado, não uma fatura nem garantia de limite de cobrança do provedor. Usage ausente permanece null, inclusive custo; nunca vira zero. Accounting cumulativo/reserva e reação a uso desconhecido serão coordenados pelo harness/T17. Configurar também os limites do projeto no provedor. Requisições não têm retries automáticos, evitando duplicação de cobrança após falha incerta. Falhas/incomplete/timeouts não retornam sucesso e bodies de erro do provedor não são expostos.

## Validação

Com PostgreSQL e Docker locais disponíveis, comandos dos READMEs de API/runner executam as suítes. Fixtures Git usam repositórios locais descartáveis e Git real. GitHub/OpenAI usam doubles explicitamente nomeados FixtureGitProvider/FixtureHttpClients. Testes externos reais continuam opt-in e não são parte da suíte padrão. Testes Playwright verificam a UI e falha clara sem credenciais, mantendo o Kanban utilizável.

## Referências

- GitHub JWT/App: https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-json-web-token-jwt-for-a-github-app
- Tokens restritos: https://docs.github.com/en/rest/apps/apps#create-an-installation-access-token-for-an-app
- Referência base: https://docs.github.com/en/rest/git/refs#get-a-reference
- Configuração Git por processo: https://git-scm.com/docs/git-config#_environment
- Responses/texto: https://developers.openai.com/api/docs/guides/text
- Schema Responses: https://developers.openai.com/api/reference/python/resources/responses/methods/create
