# ADR 0011 — Seleção de CLI, modelo e esforço por tarefa

Status: aceita em 2026-10-13 por pedido do responsável. Amplia ADR 0008 para Codex e Claude Code, ambos via executável nativo autenticado na máquina.

A criação de tarefa inclui IA, modelo e esforço de raciocínio opcional. A pasta usa apresentação discreta, sem card destacado. Credenciais continuam no login local do CLI; a aplicação não cria contas nem grava tokens de fornecedores.

O backend valida combinações e guarda um vínculo técnico imutável com escopo de organização/projeto, modelo e esforço. A tarefa referencia esse vínculo; a execução usa a seleção da tarefa, e correções sem override mantêm o mesmo perfil. Vínculos antigos continuam seguindo os defaults da máquina. Isso preserva FKs/histórico sem introduzir cadastros de perfis configuráveis por projeto. A migração TaskAgentSelection adiciona campos e FK composta sem remover dados.

Codex: catálogo do arquivo local models_cache.json, somente entradas listáveis; níveis low/medium/high/xhigh/max declarados pelo catálogo. Ausência/arquivo inválido oferece apenas automático. O acesso pode mudar depois do cache; é validado pelo CLI na chamada real. Esforço é passado por argumento estruturado --config model_reasoning_effort. Não oferecer ultra, que pode introduzir delegação automática fora do harness.

Claude: aliases default, sonnet, opus e haiku; esforço low/medium/high em sonnet/opus, automático em default/haiku. A escala não equivale à do Codex e políticas da conta podem limitar o esforço aplicado. O catálogo é conservador; não promete listar todos os modelos da conta. O CLI instalado é 2.1.296. O adaptador usa --restricted, --bare, --tools vazio, negação de ferramentas/MCP, configuração isolada, modo print e sem persistência de sessão. O prompt vai por stdin e o harness continua dono das leituras/escritas/testes. O instalador oficial foi solicitado pelo responsável; PATH do usuário inclui .local/bin. Login permanece pendente e é feito pelo usuário com claude auth login.

Claude:Executable/EnableRequests/MaxInputBytes/TimeoutSeconds pertencem à configuração local da máquina, ignorada pelo Git. Não passam para o container nem para o navegador. Instalação e auth status são verificados sem inferência. Respostas inválidas, timeout e processo ausente falham explicitamente. Stderr, prompts e credenciais não são exibidos. Custos continuam desconhecidos no transporte CLI, nunca zero por suposição.

Validação: PostgreSQL real para seleção, FK/escopo e snapshot; fixture de processo nativo para stdin, isolamento do ambiente, ferramentas desabilitadas e argumentos de modelo/esforço. Nenhuma inferência real do Claude foi executada: requer login do usuário e tarefa autorizada. Os testes de UI usam fixture de catálogo explicitamente identificada.

Referências oficiais consultadas:
- https://learn.chatgpt.com/docs/cli/reference
- https://learn.chatgpt.com/docs/config-file/config-advanced
- https://code.claude.com/docs/en/cli-reference
- https://code.claude.com/docs/en/model-config
- https://code.claude.com/docs/en/setup
