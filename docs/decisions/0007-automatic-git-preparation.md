# 0007 — Git preparado automaticamente na primeira execução

- Status: aceita em 2026-10-12 por solicitação explícita do responsável.
- Substitui a exigência de Git manual para executar uma tarefa definida na ADR 0006/T32.

Salvar a pasta continua sem inicializar Git. Ao iniciar uma tarefa, se não houver base pronta, o sistema prepara Git, branch e commit inicial automaticamente. Uma branch existente com commit é reutilizada. A preparação ocorre antes de persistir o snapshot da execução, sob lock transacional para evitar bases concorrentes.

O commit inicial registra arquivos existentes com hashes sem filtros Git. Respeita o .gitignore, exclui links, dependências geradas e caminhos comuns de credenciais; limita quantidade, tamanho e caminhos ao escopo suportado. Não altera o conteúdo dos arquivos. Hooks, assinatura e configuração Git global ficam desabilitados durante a preparação. Pastas vazias recebem um commit vazio.

O runner continua executando em checkout isolado de um commit fixo; testes, diff, revisão independente e merge seguem o fluxo atual. Não esconder a preparação automática: a interface informa que a base será preparada ao iniciar a tarefa. Falhas de filesystem/Git ou autorização continuam sendo reportadas; ausência inicial de Git deixa de ser bloqueio.

Validação utiliza Git/PostgreSQL/checkout/publicação locais reais em pastas de teste vazias e com código. Nenhuma tarefa ou arquivo real do usuário é alterado como parte dos testes.
