# Publicação de pull requests

A API publica somente execuções Succeeded com resultado tests_passed, checkpoint correspondente, escrita seguida de teste aprovado, nenhuma falha/pendência e recibos de modelo com consumo conhecido. Arquivos são reconstruídos dos recibos confirmados e conferidos contra o hash final do checkpoint. Isso é evidência do controlador; não substitui revisão humana ou CI.

`GitHub:EnablePublication` é false quando ausente. Quando true, PublicationWorker consulta execuções elegíveis a cada cinco segundos e chama o mesmo fluxo do comando manual. O serviço roda na API, usa a fila PostgreSQL existente e não cria outro deploy. Testing desativa polling; testes usam provedores explicitamente fictícios.

GET/POST `/api/v1/organizations/{org}/projects/{project}/tasks/{task}/executions/{execution}/publication` exige sessão e acesso ao projeto; POST exige CSRF. Consulta retorna estado, tentativas, código de falha e recibo público, sem segredo. POST publica ou reconcilia a execução. O painel mostra o link/commit do PR confirmado; sucesso técnico sozinho não é revisão aprovada.

Publicação reserva lease de três minutos numa transação curta, com lock da execução; não mantém transação durante I/O. Limite: três tentativas persistidas, no máximo 60s por tentativa. Execução de outro publicador retorna conflito. Após crash, lease vencido permite reconciliar; após esgotar o limite é necessária intervenção do operador, sem reinício automático ilimitado.

O token de publicação fica somente no servidor e tem permissions contents:write, metadata:read e pull_requests:write, restritas ao repository_id autorizado. O token de checkout continua contents:read e não ganha acesso de escrita. A GitHub App precisa das permissões correspondentes; nenhuma App, instalação ou chave real foi criada pelo desenvolvimento.

GitHubPublisher verifica identidade, base atual e modo dos arquivos, usa base_tree para preservar o restante do repositório e cria commit determinístico com data/base/mensagem/tree fixos. Arquivos executáveis conservam 100755; links/submódulos/paths protegidos são rejeitados. Árvores truncadas/maiores que o limite do cliente falham explicitamente. Suporta até 100 arquivos de texto, 16 KiB cada; remoção de arquivos e edição de workflows não fazem parte desta implementação.

Branch `teamflow/{executionId:N}` existente precisa apontar o mesmo commit; nunca há force push. Antes de abrir PR draft, busca PRs por head/base e verifica número/URL/repositório/commit/marcador da execução. Repetição após resposta perdida encontra o mesmo PR. Base alterada interrompe publicação para revalidação; divergência externa não é sobrescrita.

Depois do recibo confirmado, a API revalida a conexão e persiste o link/head junto à transição Execution → Review. PR externo criado seguido de falha local é reconciliado em nova tentativa, não declarado perdido nem recriado cegamente. Aprovação, CI e confirmação de merge pertencem à T20. Chamadas externas continuam opt-in e nenhum teste padrão abre PR real.

Referências oficiais verificadas em 2026-10-11: [Git trees](https://docs.github.com/en/rest/git/trees?apiVersion=2026-03-10), [pull requests](https://docs.github.com/en/rest/pulls/pulls?apiVersion=2026-03-10).