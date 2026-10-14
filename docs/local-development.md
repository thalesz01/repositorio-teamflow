# Execução local com Codex CLI e Git

Decisão aceita em 2026-10-12 por solicitação do responsável. O fluxo usa `codex.exe` instalado na máquina e repositórios Git locais. Em 2026-10-13 a ADR 0008 removeu GitHub App, OpenAI API e webhooks: este é o único fluxo suportado e não depende de repositório Git remoto.

## O fluxo em quatro etapas (ADR 0010)

Não há mais equipe nem projeto na interface: cada pessoa trabalha no seu espaço (`POST /api/v1/workspace` escolhe o projeto próprio com a tarefa mais recente, ou cria "Pessoal/Tarefas"). O quadro tem quatro colunas:

1. **Criar** — "Nova tarefa": título, o que fazer, resultado esperado (opcional) e a **pasta do código**, escolhida no seletor (a última usada fica lembrada). A pasta e a branch de partida ficam gravadas na tarefa, que já nasce pronta. "Criar e executar" faz os dois passos.
2. **Executar** — o cartão mostra "Executar"; o agente trabalha em uma cópia isolada e a tarefa vai para "Executando". Falhou? "Tentar de novo".
3. **Corrigir** — "Revisar e concluir" abre as diferenças em um modal. Passe o mouse sobre uma linha, use **+** e escreva o que precisa mudar. "Corrigir (n)" cria uma nova execução com esses comentários (arquivo, linha, trecho e texto) mais uma observação geral opcional.
4. **Concluir** — "Concluir" aprova o commit testado e faz o merge fast-forward na pasta em um só comando (`POST .../review/complete`).

Para isso o operador liga, em `apps/api/appsettings.Local.json` (ignorado pelo Git), as chaves do servidor — por padrão tudo vem desligado:

```json
"Local": { "EnableReview": true, "EnablePublication": true, "EnableMerge": true, "SingleUser": true,
           "BrowseAllDrives": true, "AllowRepositorySelection": true }
```

`SingleUser` deixa quem pediu a execução aprovar o próprio commit (sem ele, outra pessoa precisa aprovar). `BrowseAllDrives` e `AllowRepositorySelection` valem para todo o servidor quando o projeto não tem uma seção própria `Integrations:{org}:{projeto}:Local`; a seção do projeto, quando existe, prevalece.

## Primeiros passos pela interface

O quadro mostra "Primeiros passos" enquanto algo falta: **Codex da máquina** e **runner** (ativo quando consultou a fila nos últimos 60 s). Cada item diz o que fazer; para o runner, a tela traz o comando pronto para copiar e some quando tudo está pronto. Na raiz do TeamFlow, no PowerShell (5.1 ou 7):

```powershell
./scripts/Setup-Runner.ps1 -OrganizationId <id-equipe> -ProjectId <id-projeto>   # uma vez
./scripts/Start-TeamFlow.ps1                                                   # banco, API, web e runner
```

`Setup-Runner.ps1` não adivinha a política do runner: na primeira execução grava `apps/runner/work/local/<projeto>/policy.json` (com a imagem do sandbox de um runner já configurado) para você preencher arquivos legíveis/editáveis e o comando de teste; rode de novo e ele emite a credencial. Use `-PolicyPath` para um arquivo pronto ou `-CopyPolicyFrom <outro-projeto>` para reaproveitar uma política, revisando-a. Para dar a **uma pasta** suas próprias ferramentas e testes, acrescente `-Folder 'C:\caminho'`: a política entra em `controller.json` (`folders`) e o runner a lê na próxima execução, sem reiniciar; pastas sem entrada usam a política padrão, que pode não servir a outro repositório (a execução falha de forma explícita). `Start-TeamFlow.ps1` abre cada peça em sua janela, ignora o que já responde e inicia o runner de cada projeto configurado; `-DryRun` só mostra o plano, `-NoWeb`/`-NoRunners` omitem partes. Merge pelo app: ver "Publicação, aprovação e merge".

## Configurar um projeto (modo antigo, pasta por projeto)

Tarefas criadas pelo fluxo atual levam a própria pasta e não precisam desta seção; ela vale para tarefas antigas que usam a conexão do projeto.

1. Crie ou escolha equipe/projeto no TeamFlow. Use os IDs desse projeto, sem reutilizar a configuração de outra equipe.
2. Instale Git e Codex CLI nativo. Execute `codex login` com o mesmo usuário do Windows que executa a API. A aplicação não copia nem armazena as credenciais do CLI.
3. O diretório deve ter `.git`, uma branch base e um commit. Mudanças não commitadas e arquivos não rastreados não entram na execução.
4. Na raiz do TeamFlow, execute:

```powershell
./scripts/Configure-LocalProject.ps1 -OrganizationId <id-equipe> -ProjectId <id-projeto> -RepositoryPath 'C:\projetos\aplicacao'
```

O comando descobre `codex.exe` e grava autorização de caminho e de uso do login Codex por equipe/projeto (`Codex:Enabled`) em `apps/api/appsettings.Local.json`, ignorado pelo Git. Reinicie a API. A configuração local só é carregada em Development, sem contaminar testes ou produção. Há uma conexão ativa por projeto; revogue a anterior antes de substituí-la.

5. Em Configurar integrações, conecte o caminho absoluto autorizado e a branch base. Crie um perfil Codex CLI e selecione-o para análise e execução. `default` usa o modelo padrão do executável; informe outro identificador se desejar selecionar um modelo explicitamente.

## Interações e contexto

O adaptador executa `codex exec` diretamente por `ProcessStartInfo`, sem `cmd /c`. É o equivalente não interativo do comando `codex` no CMD. O prompt inteiro segue por stdin, nunca por interpolação de shell; a resposta final é lida por `--output-last-message`. Argumentos estruturados, diretório temporário próprio, timeout/cancelamento com encerramento da árvore e saída limitada a 128 KiB. Stdout/stderr técnicos são drenados e não expostos.

O CLI funciona como gerador de decisões textuais/JSON para o harness existente. Ferramentas shell/unified_exec ficam desabilitadas, sandbox read-only, configuração pessoal/regras desabilitadas e sessão ephemeral. O contexto portátil persistido no TeamFlow acompanha cada chamada: tarefa, critérios, fontes, decisões, arquivos e testes. Não dependemos de um chat proprietário ou de resume do CLI.

A API e o executável devem estar na mesma máquina no modo atual. O runner continua independente e acessa a API por HTTP. O repositório local precisa ser acessível no mesmo caminho ao runner. O código é buscado pelo commit exato em workspace separado, sem mover a branch nem editar os arquivos originais. Symlinks/junctions de diretório são recusados; placeholders normais do OneDrive não são confundidos com links de diretório.

O CLI pode usar autenticação ChatGPT e modelos remotos. Executável local não significa inferência offline. Não adicionar `--oss`, Ollama ou LM Studio por suposição. A integração não lê tokens do CLI e não usa uma API key de fornecedor própria.

## Custos e memória

Não inventar preço zero: custo e tokens do CLI são desconhecidos/null. `ModelTurn.BillingMode=cli` distingue resposta confirmada sem tarifação de chamada com resultado incerto. Respostas confirmadas podem ser recuperadas; falhas/pending não podem ser repetidas automaticamente como se nada tivesse ocorrido. Reserva tarifada é zero porque não há preço configurado para esse transporte, não porque o serviço seja gratuito. Limites de tempo, etapas, tentativas e tamanho continuam obrigatórios. Um orçamento USD no contrato legado não controla a assinatura ou limites da conta do CLI.

Codex CLI não fornece embeddings nessa integração. O perfil cria índices textuais locais (`model=text-only`, `dimensions=0`, vetores vazios) sem chamar modelo. Fontes validadas, chunks/hashes e versões seguem as mesmas regras; recuperação semântica exclui índices textuais. A busca é sempre textual; não há provedor de embeddings.

## Publicação, aprovação e merge

`Local:EnablePublication` e `Local:EnableReview` habilitam o fluxo local; não existem flags GitHub. Após resultado e testes confirmados, o backend monta árvore/commit determinísticos e a ref `teamflow/<executionId>` no Git local. Usa índice temporário e plumbing Git; não altera índice original, checkout ou arquivos de trabalho. Repetição confirma o mesmo commit/branch; base alterada ou branch divergente interrompe a publicação.

O painel exibe um diff autorizado do projeto. Por compatibilidade, os contratos ainda usam `pullRequest`, `number=1` e `ciPassed`. No transporte local representam solicitação local por executionId e testes confirmados do runner; não são PR/CI do GitHub. Nunca apresentar isso como uma verificação externa.

Outra pessoa da equipe aprova o head exato. Feedback cria nova execução com histórico portátil, repetição de testes e nova aprovação. O merge é feito por pessoa autorizada no terminal (`git merge --ff-only teamflow/<executionId>` na branch base) ou, com `Local:EnableMerge=true` em `appsettings.Local.json` (padrão `false`, ADR 0009), pelo botão "Fazer merge agora" do painel, que só avança a base para o head já aprovado por outra pessoa e exige árvore rastreada limpa quando a base está em uso. Pelo fluxo de quatro etapas, "Concluir" faz aprovação e merge juntos; com `Local:SingleUser=true` quem criou a tarefa pode aprovar. Consulte novamente no painel: Done e memória candidata só aparecem após base/head/parent e aprovação serem confirmados. Apenas fast-forward exato é aceito atualmente; squash/rebase/outros merges exigem revalidação. Aprovar não faz merge automaticamente.

Após merge, desconecte/reconecte a configuração com o novo commit base antes de agendar trabalho seguinte. Isso invalida snapshots antigos e evita usar uma base desatualizada. Não editar bases de execuções já existentes.

## Validação

Fixtures de processo imitam o protocolo do CLI; não representam qualidade de modelo nem login real. Testes usam Git/PostgreSQL reais e verificam publicação idempotente, base/head, checkout separado, isolamento de projeto, timeout/falha e custo desconhecido. Smoke real com Codex/login/código commitado permanece na T25 e precisa de evidência própria.

Referência oficial consultada: [Codex non-interactive mode](https://learn.chatgpt.com/docs/non-interactive-mode). Opções também conferidas no `codex exec --help` instalado.

Pré-verificação local: `./scripts/Test-LocalReadiness.ps1 -OrganizationId <id> -ProjectId <id>`. Confere configuração, login, Docker e base Git, sem chamada de modelo; informa quando ainda não há arquivos rastreados. Usa main nesse roteiro inicial. Para outra branch, use a configuração validada pela API e confira seu commit explicitamente.

No CLI, MaxOutputTokens permanece apenas como campo legado de compatibilidade; o adaptador limita bytes da resposta (128 KiB), entrada, tempo e etapas. Não apresentá-lo como um limite de tokens efetivamente imposto ao Codex. `--no-daemon` mantém o processo dedicado à chamada para controlar seu encerramento.

Em pastas criadas por outro usuário do Windows, o Git pode pedir confiança explícita. A API e o runner aplicam `safe.directory` apenas ao caminho previamente autorizado, por comando, sem mudar a configuração global. Para comandos humanos nesse caso, use `git -c safe.directory="C:/caminho/autorizado" -C "C:/caminho/autorizado" ...`; continue revisando quais arquivos serão rastreados e não inclua credenciais por conveniência.
## Navegação de pastas no projeto

Explorar pastas do computador abre um seletor que usa a máquina onde a API está instalada. Sem seção própria do projeto, valem `Local:BrowseAllDrives`, `Local:BrowseRoots` e `Local:AllowRepositorySelection` do servidor. Na execução local, é o computador do usuário. Somente proprietários podem listar diretórios; não são lidos nem enviados arquivos. Integrations:{organizationId}:{projectId}:Local:BrowseRoots configura uma lista de caminhos absolutos autorizados. Sem BrowseRoots, o seletor oferece somente Local:RepositoryPath. A conexão continua exigindo o caminho exato de RepositoryPath e uma branch com commit; navegar não amplia autorização para executar código.

Não configurar raízes globais automaticamente. Autorizar cada escopo explicitamente, manter configuração local ignorada e evitar links/junctions. A listagem omite diretórios ocultos e links, bloqueia escapes e limita cada resposta a 200 subpastas. Caminhos indisponíveis retornam erro sem detalhes de arquivos. Para autorizar outro repositório, atualizar RepositoryPath do projeto conforme o procedimento existente.

O perfil Codex salva modelo e limites de interação; default usa o modelo configurado no CLI. Limite de entrada é o contexto serializado em bytes por interação; timeout é o tempo de resposta por interação, não o prazo total da tarefa. Limites ficam recolhidos em opções avançadas na UI.

### Selecionar repositórios sem caminho pré-definido

Por autorização explícita do responsável, o projeto Teste 1 usa Local:BrowseAllDrives=true e Local:AllowRepositorySelection=true na configuração local do seu escopo. BrowseAllDrives oferece unidades locais fixas/removíveis prontas, preservando bloqueio de links e permissões do sistema. AllowRepositorySelection permite conectar Git dentro dessas raízes sem RepositoryPath pré-definido; a conexão exige proprietário, repositório regular e branch com commit. Nenhuma flag é habilitada globalmente ou copiada para equipes/projetos novos. Manter configuração ignorada e restrita ao escopo autorizado.

Usar esta pasta somente preenche o caminho, inclusive em pasta sem Git. Conectar repositório faz a validação e grava a conexão separadamente. O seletor explica configuração ausente e oferece Tentar novamente depois da liberação/restart da API. Não inicializa Git nem altera arquivos automaticamente.

## Pasta do projeto e Codex da máquina — T32

Esta seção prevalece sobre a exigência anterior de Git para configurar a pasta e sobre perfis Codex editáveis por projeto. Escolher pasta → Verificar pasta → Salvar configuração. POST repository/local/inspect é leitura autenticada com antiforgery e autorização de proprietário. Retorna existência de Git, branches locais e branch atual. POST repository/local/folder salva pasta e branch opcional, com versionamento da configuração existente. Não inicializa Git, cria commits ou altera checkout. Git sem commit e pasta sem Git podem ser salvos; SnapshotAsync impede execução até existir uma base com commit. Mudanças posteriores no Git exigem Verificar pasta e salvar a base escolhida.

Codex:Executable, Codex:EnableRequests, Codex:Model (default), Codex:MaxInputBytes (32768) e Codex:TimeoutSeconds (120) pertencem à máquina. GET profiles/machine verifica instalação e codex login status sem prompts ou custos de inferência. POST profiles/machine cria/reutiliza somente vínculo técnico interno, com ID determinístico e configuração global; não recebe nome/modelo/credenciais do projeto. Os vínculos mantêm as foreign keys do histórico de execuções/memória e o isolamento de dados existentes. Perfis manuais Codex por projeto foram retirados; POST profiles/codex responde 410. GenerateAsync usa os parâmetros globais da máquina mesmo para vínculos históricos Codex. Credenciais permanecem no login local, nunca são copiadas para o banco ou enviadas à UI; processos recebem apenas o ambiente necessário ao CLI.

Pastas continuam sob autorização explícita por equipe/projeto. A configuração da máquina não amplia automaticamente os caminhos de arquivos permitidos. O legacy POST repository/local é preservado para consumidores que exigem Git pronto; a nova interface usa folder/inspect.

## Execução sem Git manual — T33

Esta seção e ADR 0007 prevalecem sobre o bloqueio de execução descrito na T32. Ao iniciar a primeira tarefa de uma pasta sem base pronta, o sistema prepara Git/branch/commit automaticamente e grava o snapshot resultante. Salvar a configuração não inicializa Git. Pastas vazias usam um commit vazio; pastas com código registram uma base sem modificar o conteúdo dos arquivos. Arquivos ignorados, links, dependências geradas e caminhos comuns de credenciais ficam fora da base. Hashes são criados sem filtros, hooks ou configuração Git global; índices temporários evitam executar filtros do projeto. Limites: 5000 arquivos, 200 MiB totais e caminhos relativos de até 240 caracteres.

A execução do runner continua isolada, com base fixa e revisão das alterações. Não há mais erro repository_not_ready nem instrução para iniciar Git manualmente. Verificar pasta também recarrega a versão da configuração após a preparação automática. Preparação repetida reutiliza a base existente; o agendamento permanece idempotente. Nenhuma tarefa é iniciada ao simplesmente salvar a pasta ou verificar o Codex.

## Escolha da IA por tarefa (ADR 0011)
Nova tarefa permite Codex/Claude, modelo e esforço. Codex usa o catálogo local; Claude usa aliases conservadores. Faça o login na máquina (codex login ou claude auth login), nunca informe tokens no formulário. Claude:Executable e Claude:EnableRequests pertencem ao appsettings.Local.json ignorado. O adaptador Claude exige versão compatível com --restricted/--bare (instalada 2.1.296), desabilita ferramentas/MCP e usa stdin. Os níveis dependem do modelo e das políticas da conta; o catálogo não garante acesso. A tarefa persiste o vínculo escolhido e as correções o mantêm. Sem inferência real do Claude nos checks desta mudança.

## Inicialização automática do runner local

Em Windows/Development, defina Local:AutoStartRunners como true no appsettings.Local.json da API. Ao iniciar a API, o supervisor abre em segundo plano os runners com credential.json e controller.json em apps/runner/work/local. O processo continua independente e usa HTTP, sem referência ao assembly do runner. Compile apps/runner antes de iniciar a API.

O supervisor verifica a cada 30 segundos, respeita worker.lock para não duplicar runners manuais e reinicia seus processos quando saem. Ao encerrar normalmente a API, encerra somente os processos que iniciou. Credenciais DPAPI continuam no script confiável; tokens e configurações secretas da API não são enviados ao processo. O recurso fica desligado por padrão e fora de Development/Windows.

A configuração existente de ferramentas, testes e sandbox é obrigatória. O supervisor não inventa políticas nem renova credenciais expiradas: use Setup-Runner.ps1/Provision-LocalRunner.ps1 conforme a documentação. Falhas de startup registram projeto e código de saída, sem expor saída de ferramentas.

Claude usa restricted e safe-mode para preservar o login da assinatura enquanto desativa customizações e ferramentas. Não usar bare: esse modo ignora OAuth e exige chave API/helper. Smoke real mínimo Opus/low verificado em 2026-10-13; uma resposta textual não equivale à validação da tarefa completa.

## Executar uma tarefa com uma skill (ADR 0012)

Abra a tarefa (coluna "Criadas") e use "Executar esta tarefa": escolha "Sem skill" ou uma skill e execute. A lista vem de `SKILL.md` em `.claude/skills` e `.agents/skills` da pasta da tarefa (skills do projeto) e em `~/.claude/skills` e `~/.codex/skills` (suas skills; desligue com `Local:UserSkills=false`). O texto é guardado na execução e enviado ao agente a cada turno como orientação; ele não amplia a política do runner. Correções herdam a skill da execução corrigida. Limite de 12 KiB por skill; só o `SKILL.md` é lido (sem scripts nem arquivos auxiliares).

## Tarefa de análise: perguntar sobre o código (ADR 0014)

Em "Nova tarefa" escolha **Analisar o projeto**, indique o repositório e escreva a pergunta, por exemplo "qual é a regra de desconto da rota POST /orders e onde ela é aplicada?". O agente só lê e busca nos arquivos permitidos, nunca escreve nem roda testes, e a tarefa vai direto para "Concluídas" com o botão **Ver resposta** (pergunta, resposta e copiar). Nada é publicado, revisado ou aplicado na pasta.

O que o agente consegue consultar é o que a política do runner permite ler para aquela pasta (até 100 arquivos de até 16 KiB). Para um repositório novo, configure a pasta: `./scripts/Setup-Runner.ps1 -OrganizationId <id> -ProjectId <id> -Folder 'C:\\caminho' -PolicyPath <arquivo>` com `readable` casando o código (por exemplo `src/**/*.cs`); `editable` e `create` podem ficar vazios para uma pasta só de consulta, mas o `test` continua obrigatório na política (não é executado). A resposta é gerada pela IA e pode estar errada: confira nos arquivos citados.

## Tarefas em sequência (ADR 0015)

Quando uma tarefa continua a anterior (por exemplo "criar a tabela" e depois "usar a tabela no cálculo"), crie a segunda em "Nova tarefa" escolhendo **Continuação de…**. Ela usa a mesma pasta e cada cartão mostra "Etapa n de m". **Executar sequência** em qualquer etapa roda todas as etapas prontas, uma depois da outra: cada etapa parte do commit publicado pela anterior, então enxerga o que ela criou, e começa assim que a anterior chega em "Corrigir". Se uma etapa falha ou é cancelada a sequência para (as seguintes continuam em "Criadas"); **Parar sequência** interrompe antes. Nada é aplicado na pasta sozinho: você revisa e conclui as etapas em ordem (concluir a segunda antes da primeira é recusado com `previous_step_pending`). Para corrigir uma etapa que já tem sucessora iniciada é preciso antes cancelar ou concluir a sucessora (`next_step_started`). Execuções simultâneas: veja [concurrent-execution.md](concurrent-execution.md).

## Consumo de tokens (ADR 0016)

O painel de progresso mostra os tokens que o CLI informou nas chamadas ao modelo: entrada (com a parte servida do cache do provedor entre parênteses) e saída. Valor que o CLI não informa aparece como "não informados", nunca como zero, e o custo em dinheiro segue desconhecido. Cada passo é uma chamada nova ao CLI com o contexto inteiro (o Codex gasta ~13,5 mil tokens de entrada só com o próprio prompt de sistema, em boa parte cacheados); por isso o prompt usa uma visão reduzida do contexto: testes resumidos, leituras repetidas colapsadas e, acima de 40 000 bytes de conteúdo, os arquivos mais antigos viram um marcador que o modelo pode reler. O registro completo continua no checkpoint.
