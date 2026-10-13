# Validação do MVP e piloto local

O piloto usa somente Codex CLI e Git local (ADRs 0005 e 0008): nenhum repositório remoto, conta de fornecedor, chave de API ou orçamento pago é necessário. Passo a passo de configuração: [local-development.md](local-development.md) e [runner-local.md](runner-local.md).

## O que está implementado

API C# modular, Next.js e runner C# têm builds e configuração independentes. Cadastro, equipes e projetos, Kanban, fila durável, execução read/write/test com limites e checkpoints, recuperação, cancelamento, publicação local idempotente, revisão independente por head/base, merge fast-forward confirmado, fontes versionadas, índice textual reconstruível, análise editável e troca de perfil estão disponíveis.

A fixture `MvpJourneyTests` cria uma demanda, analisa, aceita a proposta, executa ferramentas, publica a revisão, pede ajustes com outro perfil, executa de novo, aprova por outra identidade, confirma o merge, valida e indexa a resolução e a usa numa nova análise e chamada de execução. API HTTP/TestServer, PostgreSQL, Git e Docker são reais; **o modelo e as fronteiras de publicação/revisão são doubles explícitos**, então a fixture não prova nada sobre o Codex real. As suítes adicionais cobrem tenants, autorização, concorrência, leases expirados, consumo incerto histórico, conflitos de hash/path, revogação, reconexão SSE e teclado. Fixtures temporárias pertencem ao teste; não usar bancos nem repositórios de outras aplicações.

## Checks locais (equivalentes ao CI)

Pré-requisitos: SDK/Node fixados nos manifests, Git, PostgreSQL com pgvector e Docker. As imagens do Compose e do sandbox têm digest fixado. A imagem Alpine não tem SDK de projetos: um projeto real precisa de imagem de build com dependências preparadas offline.

```powershell
. ./scripts/Use-LocalDatabase.ps1
./scripts/Test-App.ps1 -App api
./scripts/Test-App.ps1 -App runner
./scripts/Test-App.ps1 -App web
```

`-Restore` faz restore em modo locked/`npm ci`; `-E2E` (web) exige API e frontend locais e recusa URL remota porque cria contas e tarefas de fixture. Os workflows em `.github/workflows` repetem esses comandos (ver [development.md](development.md#ci)).

## Ambiente e operação

A API local usa `ASPNETCORE_ENVIRONMENT=Development`; Testing desativa os workers. Production recusa iniciar enquanto a fronteira VM/microVM e a proteção de segredos do ambiente não forem implementadas e aprovadas; não remover esse bloqueio para o piloto local. As flags `Codex:EnableRequests`, `Local:EnablePublication`, `Local:EnableReview` e `Memory:EnableIndexWorker` ficam desabilitadas por padrão.

Consumo do Codex é desconhecido (`null`), nunca zero; uma resposta confirmada pode ser recuperada, uma chamada pendente ou falha não é repetida como se nada tivesse ocorrido. Falha de teste não conclui a tarefa. O merge pertence a uma pessoa autorizada e o aplicativo confirma head, base e aprovação independente. Mudança de base na transferência exige reconciliação e testes novos.

Armazenamento: PostgreSQL. Relatórios e timeline têm limite de leitura de 30 dias, com conteúdo redigido e limitado; fontes, versões e histórico têm retenção física indefinida e nenhum purge automático foi autorizado. Arquivar uma fonte é reversível. Antes de produção é preciso decidir retenção e backups.

## Smoke com Codex real (T25)

Procedimento usado em 2026-10-13, num projeto novo e isolado, sem tocar em nenhum outro repositório:

1. Pasta nova fora do OneDrive com Git local sem remoto (`git remote -v` vazio): `add.sh` com um defeito (subtrai em vez de somar), `test.sh` que falha, `README.md`; um commit na `main`.
2. Banco novo (`teamflow_smoke`), porta própria e chaves do Data Protection próprias; todas as migrações aplicadas num banco vazio.
3. Duas contas (autor e revisor), equipe, projeto, pasta autorizada por `Integrations:<org>:<projeto>:Local:RepositoryPath`, vínculo do Codex da máquina e credencial de runner.
4. Runner independente com sandbox Docker (imagem por digest, sem rede), política `readable=[add.sh, test.sh]`, `editable=[add.sh]` e teste `sh test.sh`.
5. Demanda → análise (Codex) → tarefa → execução (Codex + testes no sandbox) → publicação local → diff → aprovação do revisor (a do autor é recusada) → `git merge --ff-only` humano → confirmação → memória candidata validada e indexada → busca.

Resultado e evidências: registro da T25 em [TASKS.md](../TASKS.md). O smoke não mede a qualidade do modelo, não valida um projeto real nem a produção.
