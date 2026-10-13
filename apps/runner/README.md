# Runner local

Aplicação C# independente: não referencia a API, não acessa PostgreSQL e usa somente bibliotecas do SDK .NET 10.0.400. O executor implementa `ISandbox`; o cliente HTTP possui seus próprios DTOs do contrato v1.

## Executar e verificar

Pré-requisitos: .NET fixado em global.json, Docker Desktop com Linux containers e a imagem local fixada. Na raiz deste componente:

```powershell
docker pull alpine@sha256:5291449c3df73caf6ed85e649dec1b9e818b39a5d8c871e97afc13e9cd5e8fa8
dotnet restore --locked-mode
dotnet build --no-restore
dotnet run --no-build -- verify-sandbox
dotnet test ../../tests/TeamFlow.Runner.Tests/TeamFlow.Runner.Tests.csproj
dotnet format --verify-no-changes --no-restore
dotnet format ../../tests/TeamFlow.Runner.Tests/TeamFlow.Runner.Tests.csproj --verify-no-changes --no-restore
```

`verify-sandbox` executa uma verificação controlada, imprime resultado JSON e remove seu workspace. Não consome a fila. O cliente `RunnerClient` oferece claim, heartbeat, modelo, checkpoints e resultado; ausência de repositório ou perfil não autoriza execução de tarefa nem sucesso fictício.

## Fronteira local

Workspace absoluto e exclusivo por execução; paths relativos rejeitam traversal, streams NTFS e links/reparse points. Limpeza valida a propriedade do diretório e remove links sem segui-los. Comandos usam executável e argumentos, sem shell no host. Shells do projeto só executam dentro do container.

Container sem rede, sem capabilities, sem privilégios adicionais, usuário 65532, filesystem raiz read-only, limite de 256 MiB, uma CPU, 128 PIDs e tmpfs de 64 MiB. Apenas o workspace é montado. Output limitado a 16 KiB por stream; timeout e cancelamento encerram o processo e removem o container identificado por nome aleatório e label de propriedade. Nenhuma credencial ou variável do controlador é enviada ao container.

Essa imagem Alpine valida a infraestrutura; não contém SDKs de projetos. Imagens de build e instalação controlada de dependências pertencem à integração/harness. Docker local atende desenvolvimento confiável. Produção multi-tenant requer a fronteira VM/microVM aprovada no ADR 0003.

## Protocolo e credenciais

O proprietário emite credencial em `POST /api/v1/organizations/{organizationId}/projects/{projectId}/runners/`, corpo `{ "validityDays": 7 }`, com sessão humana e CSRF. Token retornado somente na emissão/rotação; consultas retornam metadata. `POST /{id}/rotate` revoga a anterior; `DELETE /{id}` revoga. Validade máxima: 30 dias.

O token deve vir de configuração ignorada/secret manager e ser entregue ao cliente em memória. Não colocá-lo em argumentos, logs ou containers. HTTP só no loopback local; demais origens exigem HTTPS. Redirecionamentos e cookies estão desabilitados no cliente.

`POST /internal/v1/executions/claim` com Bearer e `{ "leaseSeconds": 30 }` retorna 204 ou envelope v1, limitado ao projeto da credencial. `POST /internal/v1/executions/{id}/heartbeat` com `{ "leaseToken": "...", "leaseSeconds": 30 }` retorna sucesso ou conflito/perda de posse. Credenciais expiradas/revogadas falham com 401. Perda de posse exige parar o executor. Checkpoints e resultados estão implementados na T16. Polling contínuo e recuperação após crash estão implementados na T17.

## Checkout (T14)

Git instalado é necessário para GitCheckout (validado com Git 2.53.0 no Windows). RunnerClient.PrepareRepositoryAsync recebe grant temporário autenticado, prepara commit/branch do snapshot e devolve o resultado verificado. Credenciais não são escritas no repositório nem entregues ao sandbox. Consulte [integrações](../../docs/integrations.md). O workspace deve ser descartado pelo chamador na falha e após preservar artefatos permitidos. run-once mantém heartbeat contínuo durante uma execução. O modo run consome a fila continuamente.

O motor mínimo e as pendências de integração estão documentados em [harness](../../docs/harness.md). O controlador run consome a fila com a política de ferramentas configurada pelo operador.

## Executar uma tarefa (T16)

Copiar controller.example.json para configuração ignorada fora do checkout, preencher IDs/worker/root/política e fornecer TEAMFLOW_RUNNER_TOKEN no ambiente. Executar `dotnet run --no-build -- run-once <arquivo>`. Isso consome uma tarefa real do projeto; não usar como check neutro. Perfil e repositório devem estar configurados; chamadas de modelo continuam desativadas no backend até OpenAI:EnableRequests ser habilitado pelo operador. Consulte docs/harness.md para orçamento, limites e imagens de SDK.

## Recuperação e operação contínua (T17)

`dotnet run --no-build -- run <arquivo>` executa tarefas serialmente, aguardando dois segundos quando a fila está vazia. Ctrl+C encerra cooperativamente. Antes do loop, a limpeza remove somente workspaces identificados por marcador externo com mais de 70 minutos e containers vencidos com proprietário/root conferidos.

Após reclaim, `/recovery` entrega o checkpoint e escritas confirmadas. O runner verifica objetivo/base, restaura hashes, revalida testes e continua os passos restantes. Respostas conhecidas são reutilizadas sem nova chamada paga; consumo incerto bloqueia recuperação. Heartbeat a cada 20s cancela o trabalho ao perder posse. Falha de limpeza é reportada como falha, nunca sucesso.
## Validação integrada e piloto

Consultar docs/pilot.md na raiz. O comando scripts/Test-App.ps1 -App runner executa os checks desta aplicação; -Restore restaura dependências fixadas. O smoke com provedores reais depende de recursos e orçamento autorizados. Produção permanece bloqueada; Docker local não é a fronteira de produção multi-tenant.


## Modo local aceito em 2026-10-12

O fluxo principal utiliza Codex CLI e repositórios Git locais; siga docs/local-development.md e ADR 0005 (caminhos relativos à raiz do monorepo). As instruções anteriores de GitHub/OpenAI API descrevem integrações alternativas preservadas. Configuração de Development por projeto, clone separado, revisão de diff e merge fast-forward confirmado. Embeddings externos não são necessários: existe indexação textual local.

Operação local com credencial protegida e comandos de início: [docs/runner-local.md](../../docs/runner-local.md).
