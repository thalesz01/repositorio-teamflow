# Operar o runner local

API, frontend e runner são processos independentes. Ter Codex autenticado não inicia o consumidor da fila. `Queued` aguarda um runner; `Leased` com heartbeat indica trabalho recebido. Checkpoints e interações do modelo são as evidências de progresso.

## Atalho: um comando por etapa

`./scripts/Setup-Runner.ps1 -OrganizationId <uuid> -ProjectId <uuid>` prepara o runner de um projeto (política em `policy.json`, credencial DPAPI) e `./scripts/Start-TeamFlow.ps1` inicia banco, API, web e um runner por projeto configurado. Ambos funcionam no Windows PowerShell 5.1 e no 7. Detalhes em [local-development.md](local-development.md). O quadro indica se o runner está ativo (`GET .../runners/status`, visível a qualquer membro; online = consulta à fila nos últimos 60 s). Os passos abaixo são o roteiro manual equivalente.

## Criar arquivos novos (`create`)

`readable` e `editable` listam arquivos que já existem. Para o agente **adicionar** arquivos (por exemplo, um arquivo só com as rotas), a política aceita `create`: padrões glob como `**/*.cs`. Um arquivo novo só é aceito se o caminho casar com um padrão, a escrita usar `expectedHash=missing`, o arquivo ainda não existir e o caminho não passar por pasta oculta, `bin`, `obj` ou `node_modules`. No máximo 20 arquivos por execução; o agente pode reler e reescrever o que ele mesmo criou. Arquivos existentes continuam exigindo o caminho exato em `editable`. A API repete a checagem do padrão antes de aceitar a ação do modelo. Sem `create`, nada novo pode ser criado e o agente avisa isso no resumo da execução.

O agente encerra cada execução com um resumo do que fez e do que a política o impediu de fazer; ele aparece no modal de revisão ("O que o agente informou").

## Política por pasta

Cada tarefa leva a sua pasta. O `controller.json` aceita `folders`: um mapa de caminho absoluto para `{ policy, sandbox }`. O runner compara o caminho da pasta da execução reivindicada com essas chaves (sem diferenciar maiúsculas no Windows) e usa a política e a imagem do sandbox da entrada; sem entrada usa `policy` e `sandbox`. O arquivo é relido a cada execução, então basta salvar para valer. `./scripts/Setup-Runner.ps1 ... -Folder <caminho> -PolicyPath <arquivo>` faz isso. O caminho vem da execução agendada pela API, nunca do conteúdo do repositório.

## Configurar e iniciar
1. Compilar `apps/api/TeamFlow.Api.csproj` e `apps/runner/TeamFlow.Runner.csproj`.
2. Salvar uma pasta local autorizada no projeto. Preparação da base segue ADR 0007.
3. Preparar JSON com `policy` (arquivos readable/editable e comando test) e `sandbox` (imagem Docker por digest e limites). Usar `apps/runner/controller.example.json` apenas como exemplo, nunca como teste de uma aplicação diferente. Código e comandos do repositório não autorizam ferramentas.
   - Entradas readable/editable aceitam caminhos exatos ou padrões glob com `/` (`*`, `?`, `**`), por exemplo `src/**/*.cs`. Padrões são expandidos uma vez sobre a base do checkout, ignorando entradas ocultas, links, `bin`, `obj`, `node_modules` e arquivos acima de 16 KiB. O resultado continua limitado a 100 caminhos por lista; acima disso a execução falha com `runner_failed`. Criar arquivo novo exige o caminho exato em editable.
4. Baixar a imagem oficial escolhida antes de executar. Containers de teste usam `--pull never`, rede desabilitada, checkout somente leitura e usuário sem privilégios. Quando a compilação precisa escrever, copiar o código para `/tmp` dentro do container; não modificar o checkout montado.
5. Na raiz do TeamFlow, executar no PowerShell 7:

```powershell
./scripts/Provision-LocalRunner.ps1 -OrganizationId <uuid> -ProjectId <uuid> -PolicyPath <policy.json>
./scripts/Start-LocalRunner.ps1 -ProjectId <uuid>
```

O provisionamento é uma operação administrativa local explicitamente solicitada, disponível somente em Development. O operador já tem acesso ao banco/configuração do ambiente. A API emite uma credencial de sete dias para um projeto com pasta local autorizada; nenhuma autenticação HTTP é removida. A credencial normal de proprietário continua disponível pela API `/runners`. O comando da API não abre uma porta nem inicia workers e desabilita logs; sua resposta secreta é capturada pelo script, nunca exibida. Não executar o comando de emissão diretamente para imprimir um token.

O script cifra o token com DPAPI do usuário Windows atual em `apps/runner/work/local/<projectId>/credential.json`, ignorado pelo Git. O arquivo `controller.json` contém apenas configuração. O runner recebe o token por variável de ambiente; Docker não herda essa variável. Não colocar tokens em argumentos, configuração aberta, logs ou commits. Reutilizar a credencial ao reiniciar; expiração/revogação exige emissão/rotação pelo proprietário, não apagar registros antigos. Encerrar com Ctrl+C. Este roteiro inicia o processo da sessão, não instala serviço do Windows nem garante início após reinicialização.

## Teste 1

- Organização: `40e6d887-e6ea-48d8-8553-511bf366fdd0`.
- Projeto: `0c89c480-103d-49c2-9c3a-62a34db27396`.
- Política local: CRUD ASP.NET Core sem pacotes externos; arquivos `Crud.csproj`, `Program.cs`, `Models/Item.cs`, `Services/ItemStore.cs`, `README.md`.
- SDK oficial fixado por digest na configuração local. Teste real: restore offline, build e HTTP GET/POST/PUT/DELETE em `/items`, IDs numéricos, JSON `{name: ...}`, criação 201, exclusão 204 e consulta após exclusão 404. Esta política atende à tarefa atual; não representa descoberta automática de stack/testes de outros projetos.
- Uma consulta a arquivo permitido ainda ausente retorna evidência de ausência e orienta `expectedHash=missing`; não encerra uma tarefa de criação. Escrita em arquivo existente ainda exige hash correto. Caminhos não autorizados continuam rejeitados.
- A primeira tentativa real foi preservada como falha `tool_failed` devido à consulta de arquivo ausente. Uma nova execução foi agendada após a correção. O comando administrativo `--retry-local-execution <executionId>` exige Development, execução falha, pasta autorizada e acesso atual do solicitante original; usa as regras de transição e agendamento, preservando limites e histórico. Não repete consumo incerto de modelo automaticamente.

O processo permanece ativo consumindo apenas Teste 1. Aprovação e merge continuam humanos; iniciar o runner não conclui a tarefa nem confirma qualidade do modelo. Falhas de compilação/testes permanecem falhas reais.

O início mantém um lock por projeto para impedir processos duplicados e usa uma cópia do runtime ignorada pelo Git para não bloquear rebuilds. O sandbox da tarefa atual é SDK .NET 8, compatível com net8.0; o TeamFlow continua em .NET 10. O replay isolado passou em compilação e smoke HTTP; resultados da execução agendada devem ser confirmados separadamente no quadro.

## Correção automática e diagnósticos

Falhas ordinárias de teste (saída diferente de zero, sem timeout ou truncamento) são persistidas e devolvidas ao modelo. O agente pode corrigir e testar novamente dentro da política, prazo e passos já autorizados. Falhas de política, timeout, cancelamento e consumo incerto continuam interrompendo o trabalho. O último teste deve passar depois da última edição; falhas anteriores ficam no histórico e não bloqueiam uma correção validada. Aprovação/merge continuam sendo a ação humana final.

Saídas de teste são limitadas a 3000 caracteres por stream, com ocultação de padrões comuns de credenciais, e ficam no contexto e no relatório autorizado por projeto. Checkpoints têm limite de 96 KiB; contexto e diagnósticos não autorizam ferramentas. Relatórios antigos não possuem saídas que não foram armazenadas. Nenhuma regra de ocultação consegue reconhecer todo segredo arbitrário que um teste imprima: testes não devem imprimir credenciais.

Dependências externas precisam estar disponíveis no sandbox antes do teste sem rede. Exemplo reproduzível: scripts/sandboxes/dotnet-sqlclient contém imagem SDK 8 com Microsoft.Data.SqlClient 5.2.2, preparada para o projeto local que introduziu essa versão. Construa com docker build nessa pasta e use o ID sha256 imutável obtido por docker image inspect no sandbox.image do controller. Não executar código do projeto durante a preparação nem usar tags mutáveis como imagem da execução. Essa imagem não é uma política padrão para todos os projetos; outras dependências exigem preparação própria. A execução permanece sem rede, sem root e sem escrita na pasta original.

Falhas de interação com a IA são registradas no turno como FailureCode/FailureType, sem guardar resposta livre ou segredos no relatório. A resposta JSON em bloco Markdown é normalizada; formatos inválidos retornam invalid_model_response em vez de internal_error. GET .../executions/{id}/report oferece resultado completo mesmo sem checkpoint; o frontend exibe um botão direto por execução. Registros históricos sem diagnóstico continuam explícitos como desconhecidos; não se reconstrói uma causa por suposição.

Turnos de ações usam ModelActionSchema: tool, path, content e expectedHash, com enum de ferramentas e sem propriedades adicionais. Codex recebe --output-schema por arquivo temporário; Claude recebe --json-schema e usa structured_output do envelope JSON. A negação global de ferramentas '*' também bloqueia StructuredOutput, portanto o adaptador usa --tools vazio e nega explicitamente as ferramentas externas, além de restricted/safe-mode e MCP vazio. O CLI Claude tem até três turnos internos para produzir a resposta estruturada; passos/prazo do harness permanecem limitados. Chamadas de planejamento não recebem o schema de ação. Primeira reexecução real desse fluxo (ee3bc009) concluiu build e publicação local em 2026-10-13.

## Log do runner

Cada runner grava `runner.log` em `apps/runner/work/local/<projeto>/` (rotaciona em `runner.log.old` acima de 512 KiB): início e parada, o resultado reportado por execução e qualquer exceção inesperada, com tipo e mensagem. Não inclui prompts, conteúdo de arquivos nem tokens. Uma falha inesperada em uma execução não encerra mais o processo: ela é registrada e o runner volta a consultar a fila. Se uma execução ficar "Executando" sem passos, comece por esse arquivo.

## Testar na própria máquina (ADR 0013)

Para uma pasta de confiança que precisa do SDK ou da internet da máquina, use `./scripts/Setup-Runner.ps1 -OrganizationId <id> -ProjectId <id> -Folder 'C:\caminho' -RunOnHost -PolicyPath <arquivo>`, com um arquivo só com `policy` (sem `sandbox`), por exemplo `test.executable` = `dotnet` e `test.arguments` = `["build", "--nologo", "-v", "minimal"]`. A entrada fica com `"execution": "host"`. O comando roda numa cópia temporária do checkout, com ambiente reduzido, tempo máximo e saída limitada, mas **sem container**: o que o agente escrever no projeto é compilado e executado com as suas permissões. Remova `execution` (ou refaça sem `-RunOnHost`) para voltar ao Docker.
