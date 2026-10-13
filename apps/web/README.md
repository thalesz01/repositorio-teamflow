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

No quadro, selecione o perfil para novas execuções. Proprietário pode abrir Configurar integrações para conectar GitHub e criar/revogar perfis OpenAI. Credenciais e autorização são configuradas pelo administrador no backend, nunca pelo bundle/localStorage. Campos de preço são explícitos, sem valor presumido. Consulte [guia](../../docs/integrations.md). O teste integrations.spec.ts cobre falha sem credenciais e ausência de campos de segredo.

O histórico de execuções oferece Ver progresso (checkpoints, SSE, estado/custo e relatório limitado), com deduplicação de IDs na reconexão. Execuções técnicas concluídas mostram o painel de PR e link do recibo confirmado; publicar depende da configuração externa no servidor. Custos desconhecidos permanecem desconhecidos. Encerrar o painel fecha stream/timers e cancela as consultas.
## Validação integrada e piloto

Consultar docs/pilot.md na raiz. O comando scripts/Test-App.ps1 -App web executa os checks desta aplicação; -Restore restaura dependências fixadas. O smoke com provedores reais depende de recursos e orçamento autorizados. Produção permanece bloqueada; Docker local não é a fronteira de produção multi-tenant.

## Interface do workspace

Design com tokens CSS locais, contraste legível, foco visível e link para pular ao conteúdo. Quadro horizontal com arraste e alternativa por formulário/teclado; busca local por título ou descrição antes da paginação. Indicadores refletem o projeto completo. Equipes e projetos possuem criação recolhida quando já configurados. Ferramentas de análise, memória e integração ficam abaixo do fluxo principal. No modo local, Configure integrações conecta Git local e perfis Codex CLI usando a autenticação instalada; o guia local e ADR 0005 prevalecem sobre a configuração remota legada descrita acima.

Responsividade verificada em 375 px; somente as colunas do quadro rolam horizontalmente. Respeitar prefers-reduced-motion e manter labels acessíveis. Não introduzir métricas, controles ou estados de conexão fictícios. workspace-design.spec.ts cobre busca, retorno à criação e ausência de overflow da página autenticada.

### Áreas do projeto

Navegação por Quadro, Demandas, Memória e Configurações. Apenas a área ativa é exibida; os componentes permanecem montados para preservar rascunhos e a seleção do perfil. Ao trocar de projeto, o estado é reiniciado pelo escopo do componente. O quadro exibe criação/detalhe somente sob demanda, com Fechar detalhe e Escape restaurando o foco ao botão que abriu o painel. Desktop usa menu lateral; celular apresenta as quatro opções em duas linhas. Repositório e perfil de IA possuem blocos separados nas configurações. Atualização da página volta ao quadro; esta navegação local ainda não gera URLs individuais.

### Seleção e criação de equipe/projeto

A barra de contexto reúne Equipe → Projeto e conta. Os seletores nativos mantêm suporte a teclado e filtram projetos pela equipe selecionada. Ações Adicionar equipe/Adicionar projeto abrem diálogos modais nativos; criar projeto explicita a equipe de destino. Foco inicial no nome, cancelamento/Escape restaura o foco, erros preservam o preenchimento e sucesso seleciona o item criado. Novos usuários recebem estado vazio com próximo passo explícito; criação de projeto continua restrita ao proprietário. No celular, equipe e projeto ficam em linhas próprias para melhorar a leitura dos nomes. context-switcher.spec.ts valida cancelamento, foco, falha de transporte e troca entre duas equipes.

### Configurações atuais (T32)

Dois cartões: Pasta do projeto e Codex da máquina. A pasta pode estar vazia ou sem Git. Após escolha/Verificar pasta, a inspeção mostra o estado Git e um seletor de branches locais quando disponíveis. Salvar configuração não inicializa nem altera Git. O cartão Codex é informativo, usa a instalação/login da máquina e não oferece criação de perfis por projeto; Verificar Codex consulta novamente o estado. Os tipos derivados de OpenAPI e testes directory-picker/integrations refletem esse fluxo e substituem as instruções anteriores de criação manual de perfis.

A ausência de Git não bloqueia tarefas (T33/ADR 0007). A configuração informa preparação automática da base ao iniciar a tarefa; não pede Git ou commit manual. Salvar a pasta permanece sem inicialização. Verificar pasta recarrega a versão/branches depois da primeira execução.

Revisão local visual: Abrir diff local abre /reviews/<organizationId>/<projectId>/<taskId>/<executionId>. A página consome os endpoints autorizados existentes, mostra arquivos/linhas/commits, mantém ações de revisão e não aplica merge automaticamente. Testes em tests/diff-review.spec.ts cobrem parser, navegação, mobile, renderização segura e acesso negado.
