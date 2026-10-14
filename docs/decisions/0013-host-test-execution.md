# 0013 — Build de teste na própria máquina (opt-in por pasta)

- Status: **Aceita** em 2026-10-13 pelo responsável ("Coloque para rodar o build na própria máquina"). Altera, para pastas escolhidas, a regra de que o comando de teste roda em um container (ADR 0003 e `AGENTS.md` §10).
- Contexto: o container de teste usa uma imagem com um SDK fixo. Tarefas como "ajustar o projeto para .NET 10" ou que dependem de pacotes NuGet não passavam: a imagem tinha o SDK 8 e nenhuma rede. A máquina do responsável já tem os SDKs, o cache de pacotes e acesso à internet.

## Decisão

1. **Por pasta, nunca por padrão.** Uma entrada de `folders` no `controller.json` com `"execution": "host"` faz o comando de teste dessa pasta rodar no computador, sem Docker. Qualquer outra pasta continua no container. Quem escolhe é o operador, com `Setup-Runner.ps1 -Folder <pasta> -RunOnHost -PolicyPath <arquivo>`, que avisa do risco.
2. **O comando continua do operador.** O executável e os argumentos vêm da política (`test.executable`, `test.arguments`, tempo máximo de 5 min), nunca do modelo nem do repositório. Para .NET: `dotnet build --nologo`.
3. **Cópia temporária.** O comando roda em uma cópia do checkout da execução em `%TEMP%`, fora do repositório do TeamFlow (assim não herda `global.json` nem `.editorconfig` daqui). A cópia exclui `.git` e links, tem no máximo 5000 arquivos e 100 MB e é apagada ao final; a saída de build não vai para o workspace da execução.
4. **Ambiente reduzido.** Só variáveis necessárias à toolchain (PATH, perfil do usuário, pastas de programas) são herdadas. O token do runner e as configurações da API não são passados. `TEMP` aponta para uma pasta descartável; nós do MSBuild não ficam reutilizáveis.
5. **Limites.** Tempo máximo com encerramento da árvore de processos, saída limitada a 16 KiB por fluxo e código 127 com mensagem clara quando o executável não existe. Tempo esgotado ou saída truncada continuam sendo falhas não recuperáveis para o harness.

## Consequências

- **Sem isolamento.** O comando e qualquer lógica de build escrita pelo agente (propriedades e alvos do `.csproj`, geradores de código, analisadores) rodam com as permissões do usuário, com rede e acesso aos arquivos dele. Isso só é aceitável para pastas pessoais e de confiança, nunca para código de terceiros ou ambiente multiusuário. Production continua recusando iniciar.
- O resultado deixa de ser reproduzível entre máquinas: depende do SDK instalado e dos pacotes da internet. O manifesto registrado na evidência passa a ser `host`.
- A política continua limitando os arquivos que o agente lê, edita e cria; o que muda é onde o teste executa.
- Docker segue sendo o padrão e o caminho recomendado quando a imagem atende.
