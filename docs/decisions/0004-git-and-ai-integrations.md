# 0004 — Integrações iniciais Git e IA

Status: Aceito em 2026-10-11 pelo responsável, nesta conversa ("pode continuar"). Substituído pela ADR 0005 para o fluxo principal e removido pela ADR 0008 em 2026-10-13: GitHub App e OpenAI API não existem mais no código.

## Recomendação inicial

GitHub por GitHub App e OpenAI por Responses API com API key do projeto. Integrações são adaptadores independentes, com contratos portáteis e harness próprio. Um modelo configurável escolhido pelo administrador; não fixar nome/preço de modelo por suposição. Nada será publicado, instalado em conta externa ou chamado com cobrança sem autorização específica.

## GitHub

GitHub App instalada somente nos repositórios escolhidos, com permissões mínimas necessárias: metadata read, contents read/write, pull requests read/write e leitura de checks/status quando implementados. Não incluir administração do repositório ou workflows write por padrão. Guardar app/installation/repository IDs, validar a associação autorizada e o escopo de cada operação. Chave privada da App é protegida no backend conforme T11.

Gerar tokens de instalação restritos ao repositório e às permissões necessárias, com expiração de acordo com o provedor (atualmente uma hora). Checkout e push não colocam token em URL, logs ou argumentos. Revalidar revogação/permissões. Webhooks usam secret e validação de assinatura, deduplicação e reconciliação com estado atual. Merge permanece sujeito à aprovação humana/CI.

T14 implementa checkout, branch e commit base. T19 abre PR; T20 processa revisão/CI/merge. Configuração de GitHub App real dependerá dos dados da App autorizada; testes padrão usam fixtures e não criam PRs externos.

## OpenAI API

API key é fornecida por mecanismo seguro e usada somente no backend/harness. Responses API suporta requisições e ferramentas; o harness decide e valida operações. Contexto canônico, checkpoints e fontes continuam na plataforma. Limites de custo/tempo/passos são verificados antes de novas operações, com uso real registrado quando disponível. Testes padrão usam doubles; testes reais pagos são opt-in.

Não usar cookies do ChatGPT, extrair credenciais de apps locais ou tratar API key como acesso a histórico de conversas. Não compartilhar credenciais pessoais entre membros automaticamente. A conexão define proprietário, escopo de compartilhamento e revogação.

## Alternativa alinhada à ideia original: conta ChatGPT

Documentação oficial consultada em 2026-10-11 oferece Sign in with ChatGPT e uso de plano para integrações elegíveis. O quickstart descreve trial para parceiros comerciais selecionados e disponibilidade para parceiros open-source/clientes privados selecionados. Não foi estabelecida a elegibilidade deste projeto; não prometer uso de assinatura em um produto privado sem essa validação.

Esse caminho envolve cadastro de cliente, OAuth/OIDC com PKCE, consentimento para scopes de inferência, expiração/renovação e modelo disponível por conta. Scopes de identidade não autorizam inferência nem leitura de conversas. Autenticação do produto continua Identity; conectar o provedor é uma autorização separada. Se este caminho for prioridade, validar elegibilidade e o contrato oficial antes de T15; não implementar endpoints de OAuth presumidos.

## Outras integrações

GitLab, Anthropic e outros agentes podem ser adicionados após uma escolha explícita. Adaptador de modelo não equivale a runtime de agente completo; capacidades são registradas e verificadas. Não adicionar SDKs ou bibliotecas de todos os provedores antecipadamente.

## Fontes

- https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/differences-between-github-apps-and-oauth-apps
- https://developers.openai.com/api/reference/overview
- https://developers.openai.com/siwc/quickstart
- https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference

## Decisão

GitHub App e OpenAI Responses API com API key aprovados como integrações iniciais. Modelo permanece configurável. Nenhuma integração real ou chamada paga foi configurada.
