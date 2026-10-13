# Publicação da revisão (Git local)

A API publica somente execuções `Succeeded` com resultado `tests_passed`, checkpoint correspondente, escrita seguida de teste aprovado, nenhuma falha/pendência e recibos de modelo confirmados. Os arquivos são reconstruídos dos recibos e conferidos contra o hash final do checkpoint. Isso é evidência do controlador; não substitui a revisão por outra pessoa.

`Local:EnablePublication` é `false` quando ausente. Quando `true`, `PublicationWorker` consulta execuções elegíveis a cada cinco segundos e usa o mesmo fluxo do comando manual. O serviço roda na API, sobre a fila PostgreSQL existente. Testing desativa o polling.

`GET/POST /api/v1/organizations/{org}/projects/{project}/tasks/{task}/executions/{execution}/publication` exige sessão e acesso ao projeto; `POST` exige CSRF. A consulta devolve estado, tentativas, código de falha e o recibo. O painel mostra o link do diff local e o commit.

A publicação reserva um lease de três minutos numa transação curta, sem manter transação durante o I/O. Limites: três tentativas persistidas e 60 s por tentativa. Outro publicador retorna conflito; depois de uma queda o lease vencido permite reconciliar e, esgotadas as tentativas, é necessária intervenção do operador.

`LocalGit` monta árvore e commit determinísticos (data, base, mensagem e árvore fixos) com índice temporário e plumbing Git, e cria a ref `refs/heads/teamflow/<executionId>` com verificação da base. Não altera índice, checkout nem arquivos do usuário. Até 100 arquivos de texto de 16 KiB; links, caminhos protegidos e modos especiais são rejeitados; remoção de arquivos não faz parte do escopo.

Repetir a publicação encontra o mesmo commit e a mesma ref. Base alterada (`base_changed`) ou ref divergente (`branch_conflict`) interrompe sem sobrescrever. Depois do recibo, a API revalida a conexão e grava head e link junto à transição Execution → Review. Por compatibilidade, os contratos mantêm os nomes `pullRequest`/`number` (sempre `1`): representam a solicitação de revisão local, não um PR externo.

Aprovação e confirmação de merge: [pull-request-review.md](pull-request-review.md).
