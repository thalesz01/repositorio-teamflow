# TeamFlow Web

Aplicação Next.js independente. Node 24.11.0, versões explícitas e package-lock versionado. Executar neste diretório:

```powershell
npm ci
npm run lint
npm run typecheck
npm run build
npm run dev
```

Abra http://localhost:3000. A base inicia e compila sem API. `API_URL` é configuração somente do servidor Next e define o proxy `/api/*`; padrão http://localhost:5080. Não coloque segredos nesse endereço nem use NEXT_PUBLIC para credenciais. Testes de navegador estão disponíveis em npm run test:e2e; veja docs/development.md na raiz.

Para visualização local com build otimizado, `npm run build` e `npm start`. `.env.local` pode substituir API_URL e não é versionado. O Next lê seus arquivos de ambiente nativamente. A aplicação usa a mesma origem via proxy e mantém suas dependências e build independentes do backend.

Lint usa ESLint, typescript-eslint e regras React Hooks. A configuração não inclui eslint-config-next: suas dependências de glob apresentaram alerta de segurança; esta base evita essa dependência. Navegação interna usa Link do Next. Dependências verificadas com npm audit.

Tipos HTTP: npm run generate:api usa contracts/api/openapi.json; para um checkout separado, npm run generate:api -- http://localhost:5080/openapi/v1.json. O build usa api/schema.d.ts já gerado, sem dependência de outro app. Não editar esse arquivo manualmente.

Kanban conectado à API, com cadastro/login, seleção/criação de equipe e projeto, criação/edição de tarefas, responsáveis, dependências, arraste e teclado. Movimentar para execução agenda trabalho para o runner configurado; o painel acompanha estado, PR, revisão e feedback. Memória e análise editável estão disponíveis no quadro. Configurar API_URL antes do build, pois os rewrites são gerados nessa etapa.

## Configuração de integrações

No quadro, selecione o perfil para novas execuções. Proprietário pode abrir Configurar integrações para escolher a pasta do projeto e vincular o Codex da máquina. Autorização de pastas e do CLI é configurada pelo administrador no backend, nunca pelo bundle/localStorage. Consulte [guia](../../docs/integrations.md). O teste integrations.spec.ts cobre a falha clara sem configuração e a ausência de campos de segredo.

O histórico de execuções oferece Ver progresso (checkpoints, SSE, estado/custo e relatório limitado), com deduplicação de IDs na reconexão. Execuções técnicas concluídas mostram o painel de PR e link do recibo confirmado; publicar depende da configuração externa no servidor. Custos desconhecidos permanecem desconhecidos. Encerrar o painel fecha stream/timers e cancela as consultas.
## Validação integrada e piloto

Consultar docs/pilot.md na raiz. O comando scripts/Test-App.ps1 -App web executa os checks desta aplicação; -Restore restaura dependências fixadas. O smoke com provedores reais depende de recursos e orçamento autorizados. Produção permanece bloqueada; Docker local não é a fronteira de produção multi-tenant.

## Interface do workspace

**Sistema de design.** Tokens CSS em `app/globals.css` (neutros, marca, cores semânticas e uma cor por etapa do fluxo), tipografia de sistema, escala de 4 px, componentes base (botões, campos, selos, cartões, alertas, esqueletos) e tema claro/escuro automático por `prefers-color-scheme`. Layout em `app/app.css`; leitor de diff em `features/review/review.css`. Ícones e marca ficam em `features/workspace/ui.tsx`. Não há fonte, imagem ou biblioteca de UI externa.

**Estrutura.** Barra lateral única com marca, seletores de Equipe e Projeto (com os botões Adicionar equipe/projeto), navegação (Quadro, Demandas, Memória, Configurações) e conta. Abaixo de 1024 px a barra vira um menu fora da tela, aberto por um botão no topo, com Escape e fundo clicável para fechar. Usuários sem equipe veem o mesmo layout com o convite para criar a primeira.

**Quadro.** Cabeçalho com busca, atualização e Nova tarefa; resumo do projeto em uma linha; colunas por etapa com rolagem própria. Etapas vazias recolhem em uma faixa vertical (e reabrem ao arrastar um cartão); Bloqueada, Falhou e Cancelada só aparecem quando têm tarefas. O cartão mostra título, descrição, critérios, dependências e responsável; a ação principal depende da etapa (Executar em Pronta, Revisar alterações em revisão/merge). Arrastar continua disponível, e o mesmo movimento existe por formulário no painel da tarefa, para teclado e leitor de tela.

**Painel da tarefa.** Um diálogo modal nativo à direita (foco preso, Escape e clique fora fecham, o foco volta ao cartão) com as abas Detalhes e Execuções. Detalhes tem o formulário (dependências como lista de marcação) e Mover para; Execuções mostra a revisão pronta, o histórico, o cancelamento e o progresso em linha do tempo. Nova tarefa abre o mesmo painel e, depois de salva, ele passa para a tarefa criada.

**Telas.** Demandas (analisar e consultar contexto), Memória (lista de fontes, edição e indexação) e Configurações (pasta e Codex da máquina) usam os mesmos cartões e selos de estado. Todas permanecem montadas para preservar rascunhos. Atualizar a página volta ao quadro; a navegação local ainda não gera URLs.

**Acessibilidade e responsividade.** Foco visível, link para pular ao conteúdo, rótulos associados (dicas ficam fora do `label` e ligadas por `aria-describedby`), estado nunca só por cor, alvos de 44 px em ponteiro grosso e `prefers-reduced-motion` respeitado. Verificado de 375 px a 1440 px, sem rolagem horizontal da página; só o quadro rola na horizontal. Não introduzir métricas, controles ou estados de conexão fictícios.

**Testes de navegador.** `npm run test:e2e` roda contra um build de produção (o servidor de desenvolvimento dispara efeitos duas vezes e quebra mocks de “primeira chamada”). Sem o Chromium do Playwright instalado, use `E2E_BROWSER_CHANNEL=chrome` (ou `msedge`). Para rodar um servidor de desenvolvimento ao lado de um preview, defina `NEXT_DIST_DIR` com outra pasta (`.next-*` é ignorada pelo Git).

### Seleção e criação de equipe/projeto

Os seletores nativos mantêm suporte a teclado e filtram projetos pela equipe selecionada. Adicionar equipe/projeto abre diálogos modais nativos; criar projeto explicita a equipe de destino. Foco inicial no nome, Escape/Cancelar restaura o foco, erros preservam o preenchimento e o sucesso seleciona o item criado. Criar projeto é restrito ao proprietário. `context-switcher.spec.ts` valida cancelamento, foco, falha de transporte e troca entre equipes; `workspace-design.spec.ts` valida busca, retorno à criação e ausência de overflow.

### Configurações atuais (T32)

Dois cartões: Pasta do projeto e Codex da máquina. A pasta pode estar vazia ou sem Git. Após escolha/Verificar pasta, a inspeção mostra o estado Git e um seletor de branches locais quando disponíveis. Salvar configuração não inicializa nem altera Git. O cartão Codex é informativo, usa a instalação/login da máquina e não oferece criação de perfis por projeto; Verificar Codex consulta novamente o estado. Os tipos derivados de OpenAPI e testes directory-picker/integrations refletem esse fluxo e substituem as instruções anteriores de criação manual de perfis.

A ausência de Git não bloqueia tarefas (T33/ADR 0007). A configuração informa preparação automática da base ao iniciar a tarefa; não pede Git ou commit manual. Salvar a pasta permanece sem inicialização. Verificar pasta recarrega a versão/branches depois da primeira execução.

Revisão local visual: Abrir diff local abre /reviews/<organizationId>/<projectId>/<taskId>/<executionId>. A página consome os endpoints autorizados existentes, mostra arquivos/linhas/commits, mantém ações de revisão e não aplica merge automaticamente. Testes em tests/diff-review.spec.ts cobrem parser, navegação, mobile, renderização segura e acesso negado.
