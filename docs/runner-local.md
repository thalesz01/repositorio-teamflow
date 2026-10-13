# Operar o runner local

API, frontend e runner são processos independentes. Ter Codex autenticado não inicia o consumidor da fila. `Queued` aguarda um runner; `Leased` com heartbeat indica trabalho recebido. Checkpoints e interações do modelo são as evidências de progresso.

## Configurar e iniciar

1. Compilar `apps/api/TeamFlow.Api.csproj` e `apps/runner/TeamFlow.Runner.csproj`.
2. Salvar uma pasta local autorizada no projeto. Preparação da base segue ADR 0007.
3. Preparar JSON com `policy` (arquivos readable/editable e comando test) e `sandbox` (imagem Docker por digest e limites). Usar `apps/runner/controller.example.json` apenas como exemplo, nunca como teste de uma aplicação diferente. Código e comandos do repositório não autorizam ferramentas.
   - Entradas readable/editable aceitam caminhos exatos ou padrões glob com `/` (`*`, `?`, `**`), por exemplo `src/**/*.cs`. Padrões são expandidos uma vez sobre a base do checkout, ignorando entradas ocultas, links, `bin`, `obj`, `node_modules` e arquivos acima de 16 KiB. O resultado continua limitado a 100 caminhos por lista; acima disso a execução falha com `runner_failed`. Criar arquivo novo exige o caminho exato em editable.
4. Baixar a imagem oficial escolhida antes de executar. Containers de teste usam `--pull never`, rede desabilitada, checkout somente leitura e usuário sem privilégios. Quando a compilação precisa escrever, copiar o código para `/tmp` dentro do container; não modificar o checkout montado.
5. Na raiz do TeamFlow, executar no PowerShell 7:

```powershell
./scripts/Provision-LocalRunner.ps1 -OrganizationId <uuid> -ProjectId <uuid> -PolicyPath <policy.json>
./scripts/Start-LocalRunner.ps1 -ProjectId <uuid>
```

O provisionamento é uma operação administrativa local explicitamente solicitada, disponível somente em Development. O operador já tem acesso ao banco/configuração do ambiente. A API emite uma credencial de sete dias para um projeto com pasta local autorizada; nenhuma autenticação HTTP é removida. A credencial normal de proprietário continua disponível pela API `/runners`. O comando da API não abre uma porta nem inicia workers e desabilita logs; sua resposta secreta é capturada pelo script, nunca exibida. Não executar o comando de emissão diretamente para imprimir um token.

O script cifra o token com DPAPI do usuário Windows atual em `apps/runner/work/local/<projectId>/credential.json`, ignorado pelo Git. O arquivo `controller.json` contém apenas configuração. O runner recebe o token por variável de ambiente; Docker não herda essa variável. Não colocar tokens em argumentos, configuração aberta, logs ou commits. Reutilizar a credencial ao reiniciar; expiração/revogação exige emissão/rotação pelo proprietário, não apagar registros antigos. Encerrar com Ctrl+C. Este roteiro inicia o processo da sessão, não instala serviço do Windows nem garante início após reinicialização.

## Teste 1

- Organização: `40e6d887-e6ea-48d8-8553-511bf366fdd0`.
- Projeto: `0c89c480-103d-49c2-9c3a-62a34db27396`.
- Política local: CRUD ASP.NET Core sem pacotes externos; arquivos `Crud.csproj`, `Program.cs`, `Models/Item.cs`, `Services/ItemStore.cs`, `README.md`.
- SDK oficial fixado por digest na configuração local. Teste real: restore offline, build e HTTP GET/POST/PUT/DELETE em `/items`, IDs numéricos, JSON `{name: ...}`, criação 201, exclusão 204 e consulta após exclusão 404. Esta política atende à tarefa atual; não representa descoberta automática de stack/testes de outros projetos.
- Uma consulta a arquivo permitido ainda ausente retorna evidência de ausência e orienta `expectedHash=missing`; não encerra uma tarefa de criação. Escrita em arquivo existente ainda exige hash correto. Caminhos não autorizados continuam rejeitados.
- A primeira tentativa real foi preservada como falha `tool_failed` devido à consulta de arquivo ausente. Uma nova execução foi agendada após a correção. O comando administrativo `--retry-local-execution <executionId>` exige Development, execução falha, pasta autorizada e acesso atual do solicitante original; usa as regras de transição e agendamento, preservando limites e histórico. Não repete consumo incerto de modelo automaticamente.

O processo permanece ativo consumindo apenas Teste 1. Aprovação e merge continuam humanos; iniciar o runner não conclui a tarefa nem confirma qualidade do modelo. Falhas de compilação/testes permanecem falhas reais.

O início mantém um lock por projeto para impedir processos duplicados e usa uma cópia do runtime ignorada pelo Git para não bloquear rebuilds. O sandbox da tarefa atual é SDK .NET 8, compatível com net8.0; o TeamFlow continua em .NET 10. O replay isolado passou em compilação e smoke HTTP; resultados da execução agendada devem ser confirmados separadamente no quadro.
