# Rodar o TeamFlow em outra máquina — passo a passo

Guia para subir **todo o projeto localmente** (banco, API, web e runner) numa máquina nova. Ele reúne o que está espalhado em [local-development.md](local-development.md), [runner-local.md](runner-local.md) e [development.md](development.md); em caso de dúvida, esses documentos e as ADRs prevalecem.

> **Só Windows.** O adaptador do Codex exige um `codex.exe` nativo, a credencial do runner é cifrada com DPAPI do usuário do Windows e os scripts são PowerShell. Linux e macOS não são suportados neste modo.

## 1. O que instalar

| Item | Versão | Como conferir |
| --- | --- | --- |
| Windows PowerShell 5.1 ou PowerShell 7 | qualquer um dos dois | `$PSVersionTable.PSVersion` |
| Git | recente | `git --version` |
| .NET SDK | **exatamente 10.0.400** (`apps/*/global.json` não aceita outra versão) | `dotnet --list-sdks` |
| Node.js | 24.x (`package.json`: `>=24 <25`) | `node --version` |
| Docker Desktop | com **Linux containers**, aberto e rodando | `docker info` |
| Codex CLI (nativo) | logado com `codex login` | `codex login status` |
| Claude Code CLI | opcional, só se for escolher o Claude nas tarefas | `claude --version` |

Portas usadas (precisam estar livres): **5080** (API), **3000** (web) e **55432** (PostgreSQL, só em `127.0.0.1`).

O Codex ou o Claude usam a **sua** conta e **a sua** cota. O TeamFlow não guarda credenciais do provedor: ele chama o CLI já autenticado no mesmo usuário do Windows que roda a API.

## 2. Baixar o código

Todo trabalho parte da branch `develop` (ADR 0017):

```powershell
git clone https://github.com/thalesz01/repositorio-teamflow.git
cd repositorio-teamflow
git switch develop
```

Todos os comandos seguintes partem da raiz do repositório.

## 3. Criar o `.env` (senha do banco)

```powershell
Copy-Item .env.example .env
```

Abra o `.env` e troque o valor de `TEAMFLOW_DB_PASSWORD` por uma senha local de **16 a 128 caracteres**, só com letras, números, `_` e `-` (o script `Use-LocalDatabase.ps1` recusa outros formatos). O `.env` é ignorado pelo Git; nunca o envie.

## 4. Configurar a máquina (`appsettings.Local.json`)

Crie `apps/api/appsettings.Local.json` (também ignorado pelo Git). Ele só é lido em Development. Descubra o caminho do Codex (precisa ser o executável nativo `codex.exe`; um `codex.cmd` de instalação via npm não serve):

```powershell
(Get-ChildItem "$env:LOCALAPPDATA\OpenAI\Codex\bin" -Recurse -Filter codex.exe | Select-Object -First 1).FullName
```

Conteúdo mínimo (troque o caminho do Codex pelo encontrado, com `\\` no JSON):

```json
{
  "Codex": {
    "Executable": "C:\\Users\\<usuario>\\AppData\\Local\\OpenAI\\Codex\\bin\\<versao>\\codex.exe",
    "EnableRequests": true
  },
  "Local": {
    "EnableReview": true,
    "EnablePublication": true,
    "EnableMerge": true,
    "SingleUser": true,
    "BrowseAllDrives": true,
    "AllowRepositorySelection": true,
    "AutoStartRunners": true
  }
}
```

- `EnableReview`, `EnablePublication` e `EnableMerge` ligam revisão, publicação local e o botão **Concluir** (merge fast-forward na pasta). Tudo vem desligado por padrão.
- `SingleUser` deixa quem pediu a execução aprovar o próprio commit (uso pessoal).
- `BrowseAllDrives` e `AllowRepositorySelection` permitem escolher no seletor qualquer pasta do computador. Sem eles, o seletor só enxerga o que o operador autorizar.
- `AutoStartRunners` faz a API iniciar e reiniciar os runners configurados (verifica a cada 30 s).
- Para usar o Claude além do Codex, acrescente `"Claude": { "Executable": "C:\\Users\\<usuario>\\.local\\bin\\claude.exe", "EnableRequests": true }`.

## 5. Instalar as dependências do web

```powershell
cd apps/web
npm ci
cd ../..
```

As dependências .NET são restauradas pelo script do passo seguinte (com `--locked-mode`, então usam exatamente as versões do `packages.lock.json`).

## 6. Subir banco, API e web

```powershell
./scripts/Start-TeamFlow.ps1
```

O script, nesta ordem:
1. sobe o PostgreSQL (`docker compose -p teamflow-local up -d --wait`, imagem `pgvector/pgvector:pg17`, volume `postgres-data`);
2. restaura e compila a API, restaura a ferramenta `dotnet ef` e **aplica as migrações**;
3. abre uma janela para a API (http://localhost:5080) e outra para o web (http://localhost:3000);
4. inicia um runner por projeto já configurado (na primeira vez ainda não há nenhum, e ele avisa).

Peças que já respondem são deixadas em paz, então pode rodar de novo à vontade. `-DryRun` só mostra o plano; `-NoWeb` e `-NoRunners` omitem partes. Confirme a API: `Invoke-WebRequest http://localhost:5080/health/ready` deve devolver 200.

## 7. Criar a conta e achar os IDs

1. Abra http://localhost:3000 e **cadastre uma conta** (não existe usuário padrão).
2. O quadro mostra **Primeiros passos** enquanto faltar algo: "Codex da máquina" e "runner". No item do runner a tela traz o comando pronto, já com os IDs da sua equipe e do seu projeto, para copiar. Use-o no passo seguinte.

Se "Codex da máquina" não ficar verde: `codex login status` precisa funcionar **no mesmo usuário do Windows** que roda a API, e o caminho do passo 4 precisa estar certo.

## 8. Configurar o runner

O runner é um processo à parte que executa o agente numa cópia isolada do código e roda o comando de teste. Cada projeto tem o seu. A política (o que o agente pode ler, editar, criar e como testar) **é decisão sua; ela nunca é adivinhada**.

**8.1.** Com o banco de pé (passo 6), gere o modelo da política (troque pelos IDs mostrados na tela):

```powershell
./scripts/Setup-Runner.ps1 -OrganizationId <id-da-equipe> -ProjectId <id-do-projeto>
```

Na primeira execução o script só grava `apps/runner/work/local/<id-do-projeto>/policy.json` e para.

**8.2.** Edite esse arquivo. Exemplo para um projeto .NET:

```json
{
  "policy": {
    "readable": ["src/**/*.cs", "tests/**/*.cs"],
    "editable": ["src/**/*.cs", "tests/**/*.cs"],
    "create":   ["src/**/*.cs", "tests/**/*.cs"],
    "test": { "executable": "dotnet", "arguments": ["test", "--nologo"], "timeout": "00:05:00" }
  },
  "sandbox": { "image": "sha256:<id-da-imagem>", "cpus": 1, "memoryMb": 768, "pids": 128 }
}
```

- `readable`/`editable`: caminhos exatos ou globs (`src/**/*.cs`), no máximo 100 arquivos de até 16 KiB cada. `create` permite arquivos **novos** que casem com o padrão.
- `test`: o único comando que o runner executa. Ajuste ao projeto que será alterado.
- `sandbox.image`: o teste roda num container **sem rede**, então a imagem precisa ter o SDK e as dependências já instalados. Baixe a imagem antes (`docker pull ...`) e use o ID imutável: `docker image inspect <imagem> --format '{{.Id}}'`. Nunca use tag mutável como `latest`. Há um exemplo com dependências embutidas em `scripts/sandboxes/dotnet-sqlclient`.
- **Testar na própria máquina, sem container**, para uma pasta em que você confia: depois que o runner do projeto existe, `./scripts/Setup-Runner.ps1 -OrganizationId <id> -ProjectId <id> -Folder 'C:\caminho\do\repo' -RunOnHost -PolicyPath <arquivo>`, com um arquivo só com `policy` (sem `sandbox`). O teste roda **sem isolamento** (ADR 0013). O primeiro runner do projeto sempre exige `sandbox.image` na política padrão.

**8.3.** Rode o mesmo comando de novo. Agora ele compila a API (se ainda não estiver compilada), emite a credencial do runner (cifrada com o seu usuário do Windows, em `credential.json`) e diz o próximo passo. Para dar a **uma pasta** política e testes próprios, repita o comando com `-Folder 'C:\caminho'` e um `-PolicyPath`; o runner relê o arquivo a cada execução.

**8.4.** Inicie o runner. Com `AutoStartRunners` a API faz isso sozinha em até 30 s; ou rode de novo:

```powershell
./scripts/Start-TeamFlow.ps1
```

O quadro confirma quando o runner consultou a fila nos últimos 60 s. Log do runner: `apps/runner/work/local/<id-do-projeto>/runner.log`.

## 9. Criar a primeira tarefa

O fluxo tem quatro etapas (ADR 0010):

1. **Criar** — "Nova tarefa": título, o que fazer e a **pasta do código** (precisa ser um repositório Git com ao menos um commit; sem Git, o TeamFlow prepara a base na primeira execução). Escolha a IA, o modelo e o esforço.
2. **Executar** — o agente trabalha numa cópia isolada; a tarefa vai para "Executando".
3. **Corrigir** — "Revisar e concluir" abre o diff; comente linhas com **+** e use "Corrigir" para uma nova execução.
4. **Concluir** — aprova o commit testado e faz o merge fast-forward na sua pasta.

Também existem **tarefas de análise** (perguntas sobre o código, só leitura) e **sequências** (uma tarefa continua a anterior). Nada é aplicado na sua pasta antes de você concluir.

## 10. Conferir que está tudo certo (opcional)

```powershell
./scripts/Test-App.ps1 -App api -Restore      # build, testes (PostgreSQL real), format e migrações
./scripts/Test-App.ps1 -App runner -Restore   # exige Docker rodando
./scripts/Test-App.ps1 -App web -Restore      # tipos, lint e build
```

Os testes da API criam e apagam bancos temporários `teamflow_test_*` na mesma instância do Docker e usam um CLI de mentira (`tests/CodexCliFixture`): não chamam modelo. Três testes da API precisam de um `codex.exe` nativo. Para os testes de navegador (Playwright), veja a seção "Testes de navegador" em [development.md](development.md).

## 11. Parar e recomeçar

- Feche as janelas **TeamFlow API**, **TeamFlow Web** e **TeamFlow Runner**, ou use Ctrl+C em cada uma.
- Parar o banco mantendo os dados: `docker compose -p teamflow-local stop`.
- **Apagar todos os dados** (contas, tarefas, execuções): `docker compose -p teamflow-local down -v`. Os runners guardam estado à parte em `apps/runner/work/local/`; apague a pasta do projeto para refazer o runner.

## 12. Problemas comuns

| Sintoma | Causa e solução |
| --- | --- |
| `Create .env from .env.example first` | Falta o `.env` (passo 3). |
| `Use a local password of 16..128 letters...` | A senha do `.env` tem caracteres fora de letras, números, `_` e `-`, ou tamanho errado. Se já criou o volume com outra senha, apague-o (`down -v`). |
| `A compatible .NET SDK was not found` | Falta o SDK **10.0.400** exato (`rollForward` está desligado). |
| `docker: error during connect` | Docker Desktop fechado ou em modo Windows containers. |
| Porta 5080, 3000 ou 55432 em uso | Feche o processo que a ocupa; o script não troca de porta. |
| "Codex da máquina" não fica pronto | `codex login status` falhou, ou `Codex:Executable` não aponta para um `.exe` existente. Reinicie a API depois de editar o arquivo. |
| Tarefa fica em "Executando" sem passos | O runner não está rodando ou falhou: veja `runner.log` e confirme `Docker` e a imagem do `sandbox`. |
| A execução falha ao testar | A política não serve para aquele repositório (comando de teste, imagem sem dependências). Ajuste o `policy.json` da pasta. |
| `base_changed` ao concluir | A branch base mudou depois da execução; rode a tarefa de novo sobre a base atual. |
| Pasta não aparece no seletor | `Local:BrowseAllDrives`/`AllowRepositorySelection` desligados no `appsettings.Local.json`. |

## 13. O que fica só na sua máquina

Nada disto vai para o Git (está no `.gitignore`): `.env`, `apps/api/appsettings.Local.json`, `apps/runner/work/` (credenciais e logs dos runners), `apps/api/work/` (chaves de proteção de dados), `node_modules/`, `bin/` e `obj/`. Em outra máquina, repita os passos 3, 4 e 8.
