# Atualização do piloto em 2026-10-12

O fluxo principal agora usa Codex CLI e Git local. Siga docs/local-development.md e ADR 0005. O roteiro de provedores abaixo é histórico/alternativo e não é requisito do novo piloto. O preflight anterior Test-PilotReadiness exige GitHub e não valida o modo local.

# Validação do MVP e piloto

## O que está implementado

API C# modular, Next.js e runner C# têm builds/configuração independentes. Cadastro/equipes/projetos, Kanban, fila durável, execução read/write/test com budgets/checkpoints, recuperação, cancelamento, publicação idempotente, revisão independente com CI/head/base, merge confirmado, fontes versionadas, indexação reconstruível, busca textual/vetorial, análise editável e troca de perfil estão disponíveis.

A fixture MvpJourneyTests cria uma demanda, analisa, aceita a proposta, executa ferramentas, publica um PR simulado, solicita ajustes com outro perfil, executa novamente, aprova por uma identidade independente, confirma merge, valida/indexa a resolução e usa sua versão em uma nova análise e chamada de execução. API HTTP/TestServer, PostgreSQL, Git e Docker são reais. IA, embeddings e GitHub usam doubles explicitamente registrados; nenhuma chamada paga ou PR externo é produzido. A fixture exercita também recuperação sem nova chamada e repetição após resposta perdida da publicação.

As suítes adicionais cobrem tenants, autorização, assinatura de webhook, concorrência, leases expirados, resposta Git atrasada, orçamento/consumo incerto, conflitos de hashes/paths, revogação, reconexão SSE e interação por teclado. Fixtures temporárias são de propriedade do teste; não usar bancos/repositórios de outras aplicações.

## Checks locais equivalentes a CI

Pré-requisitos: SDK/Node fixados nos manifests, Git, PostgreSQL com pgvector e Docker local. As imagens do Compose/sandbox têm digest fixado. A imagem de teste Alpine não possui SDK de projetos; um piloto de aplicação precisa de imagem de build com dependências preparadas offline pelo operador.

Na raiz, após configurar o banco local e restaurar dependências:

```powershell
. ./scripts/Use-LocalDatabase.ps1
./scripts/Test-App.ps1 -App api
./scripts/Test-App.ps1 -App runner
./scripts/Test-App.ps1 -App web
$env:E2E_URL = 'http://127.0.0.1:3000'
./scripts/Test-App.ps1 -App web -E2E
```

Use -Restore para restore locked-mode .NET ou npm ci antes dos checks. O script interrompe na primeira falha. Build de cada aplicação não depende do build das demais; a suíte integrada da API pode coordenar o runner explicitamente. E2E requer API/frontend já em execução e recusa URL remota porque cria contas/tarefas de fixture. Não foi escolhido serviço externo de CI nem criado recurso remoto só para preencher o backlog.

## Ambiente e operação

API local usa ASPNETCORE_ENVIRONMENT=Development; Testing desativa workers das fixtures. Production recusa inicialização enquanto a fronteira VM/microVM e proteção de segredos do ambiente não forem implementadas/aprovadas. Compilar o frontend em modo otimizado não habilita backend de produção. Não remover esse bloqueio para executar o piloto local.

Operador configura GitHub App/repositório, chave OpenAI do projeto, perfis e credencial técnica conforme docs/integrations.md. Segredos ficam fora do Git/navegador/containers e da documentação. Flags de chamadas externas, publicação, revisão, embeddings e index worker ficam desabilitados por padrão. Ativar somente no projeto/ambiente autorizado, com limite do provedor e orçamento por operação. Há worker de publicação/revisão/indexação e modo serial run do runner; nenhum polling em memória substitui o estado persistido.

Consumo desconhecido é null; chamadas incertas exigem reconciliação no provedor antes de novo pedido. Falha de CI/teste não conclui tarefa. Merge pertence à pessoa autorizada no GitHub; aplicativo confirma head/CI/ancestralidade e aprovação independente. Mudança de base para transferência exige reconciliação e testes novos. Arquivar fonte ou revogar associação suspende seus derivados.

Armazenamento inicial: PostgreSQL. Relatórios/timeline têm limite de leitura de 30 dias e conteúdo redigido/limitado. Fontes/versões e histórico têm retenção física indefinida nesta versão local; exclusão automática está desabilitada. Arquivamento é reversível. A proposta de purge após 30 dias não foi aplicada porque a revisão automática exigiu autorização específica para perda irreversível; não presumir autorização do pedido geral de implementar. Antes de produção, decidir retenção/backups e capacidade de armazenamento. Código de limpeza de sandbox afeta somente diretórios/containers de propriedade do runner, como documentado na T17.

## Smoke externo ainda necessário

O teste com provedores reais precisa de repositório de teste dedicado, instalação GitHub App autorizada, perfil/modelo escolhido, segredos configurados no servidor, CI App/checks confiáveis, duas identidades humanas para revisão, imagem/política de teste e teto total de custo. Nunca enviar chaves pelo chat. Esses recursos não foram fornecidos/configurados para esta validação.

Proposta de piloto: uma tarefa pequena em arquivos de fixture explicitamente autorizados; uma execução com testes e PR draft; feedback vinculado com troca de perfil; nova validação/PR; aprovação independente e merge humano; resolução candidata revisada/indexada; nova tarefa que cita a resolução. Registrar IDs, base/head, testes, custo conhecido/indefinido, versões citadas, cancelamento e resultado de cada etapa. Nenhuma aprovação, merge ou habilitação paga ocorre só por executar os checks locais.

T25 pode ser concluída integralmente após registrar esse smoke autorizado. O aplicativo está preparado para o piloto local; não há declaração de produção pronta ou teste externo já realizado.

## Pré-verificação reproduzível do piloto

Copie scripts/pilot.example.json para work/pilot.json (ignorado pelo Git), preencha IDs/URL e ajuste os tetos de cada etapa ao total autorizado. Os valores do exemplo são demonstrativos, não autorização de gasto nem previsão de cobrança. Não incluir credenciais. O script recusa IDs vazios, perfis iguais, campos adicionais, URLs com credenciais e soma de tetos maior que o total antes de qualquer requisição.

```powershell
./scripts/Test-PilotReadiness.ps1 -Config ./work/pilot.json
# Com uma WebRequestSession humana já autenticada no próprio ambiente:
./scripts/Test-PilotReadiness.ps1 -Config ./work/pilot.json -Session $pilotSession
```

O comando verifica executáveis, Docker, health/ready e, com sessão, metadata autorizada de repositório/base e perfis ativos. Não lê segredos, não faz POST, não chama provedores, não inicia o runner e não publica PRs. Sem sessão, o relatório aponta a verificação de projeto como pendente. Não segue redirects. O campo metadataChecksPassed refere-se apenas aos checks executados; externalSmoke permanece not-run. Flags/credenciais/tarifas, CI, revisão humana, imagem/política e limites do provedor ainda exigem conferência operacional.

O teto total desse arquivo organiza o roteiro; a API aplica budgets por operação, e não possui um budget agregado de piloto. Repetir uma etapa com nova chave exige recalcular o total autorizado, principalmente se houve consumo incerto. Progresso/idempotência da execução permanecem registrados no banco. Não usar essa pré-verificação como evidência de teste real de IA/GitHub ou como motivo para marcar T25 concluída.
