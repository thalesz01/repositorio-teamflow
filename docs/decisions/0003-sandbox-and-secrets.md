# 0003 — Sandbox e segredos

Status: Aceito em 2026-10-11 pelo responsável, nesta conversa ("pode continuar").

## Contexto

T10 persiste trabalho e leases. T12 precisa de um runner independente que execute comandos sem conceder ao código do projeto acesso à API, ao banco ou às credenciais do controlador. O ambiente atual tem Docker Desktop. Não há fornecedor de produção escolhido.

## Recomendação para desenvolvimento

Runner C# é o controlador confiável e usa Docker CLI por argumentos estruturados. Cada execução recebe diretório próprio e container descartável. Container usa imagem explicitamente permitida e fixada por digest, usuário sem privilégios, root filesystem read-only, capabilities removidas, no-new-privileges, limites de memória/CPU/PIDs e timeout. Somente o workspace daquela execução é montado como gravável. Não montar raiz do host, socket Docker, arquivos de configuração do runner ou diretórios de outras execuções.

Rede desabilitada por padrão. Checkout e download de dependências, quando necessários na T14/T16, são etapas controladas e separadas, com destinos permitidos; não liberar rede genérica como fallback para um teste falhar. Código não recebe acesso ao endpoint interno do backend. O executor local atende desenvolvimento confiável e não é declarado isolamento suficiente para serviço multi-tenant público.

Paths são normalizados e devem permanecer dentro do workspace exclusivo; rejeitar traversal, paths absolutos e links/reparse points que escapem do diretório. Docker flags e mounts são definidos pelo controlador, nunca pelo texto da tarefa. Comandos usam executável + lista de argumentos; scripts do projeto podem rodar dentro do container, não no host.

## Fronteira de produção

Cada execução terá fronteira de VM/microVM, ou serviço de sandbox que forneça isolamento equivalente, em infraestrutura separada da API/banco. O SandboxProvider permite substituir a implementação local sem alterar domínio ou contratos. Fornecedor e provisionamento ficam pendentes até implantação; nenhum recurso pago será criado. Produção multi-tenant permanece não habilitada até existir essa implementação e sua validação.

## Credenciais técnicas

- Proprietário autoriza um runner limitado a uma organização/projeto. A API gera token aleatório de 32 bytes, devolvido uma vez; armazena apenas hash SHA-256, identificador, escopo, validade e revogação. Comparação resistente a timing e rate limit em autenticação inválida.
- Token técnico autentica somente /internal/v1; cookies de pessoas não autenticam runner e token técnico não autentica endpoints humanos. Emissão, rotação e revogação são operações humanas autorizadas com CSRF.
- Runner obtém token por configuração local ignorada ou secret manager do ambiente. Nunca grava token em logs, snapshots, argumentos de processos ou mounts do container. Transporte HTTP permitido somente no loopback do desenvolvimento; HTTPS obrigatório fora dele.
- Cada claim associa workerId e token de posse. Toda escrita verifica token técnico, escopo, workerId, lease vigente e token de posse. Token de posse não substitui autorização técnica.
- Rotação revoga credencial anterior e interrompe novos comandos daquele runner; perda de posse exige parar trabalho e reconciliar efeitos antes de retomar.

## Credenciais Git e IA

Segredos de integração são protegidos no backend, separados por organização/projeto e finalidade. Para MVP, usar ASP.NET Core Data Protection com key ring fora dos dados protegidos. No Windows local, proteger o key ring com DPAPI vinculado ao usuário do serviço; testes usam chaves efêmeras. Em outros ambientes, configurar explicitamente proteção por certificado/secret manager antes de aceitar segredos duráveis. Chaves não entram no Git nem em diretórios montados no executor.

Esse mecanismo exige backup e retenção das chaves para recuperar credenciais antigas; não apagar key ring durante limpeza. Secret manager de produção é decisão de implantação, com auditoria e acesso mínimo. Credenciais de IA permanecem no harness confiável; código do repositório recebe resultados das ferramentas, não a chave do provedor.

## Segredo por execução e limpeza

Credenciais temporárias são entregues apenas à operação autorizada, com expiração e escopo mínimos. GitHub tokens de instalação podem ser gerados por execução quando essa integração for aprovada. Nenhuma credencial é passada por URL ou linha de comando. No término, remover somente container e workspace cujo identificador e path foram verificados; revogar material temporário, preservar artefatos permitidos e redigir outputs. Limpeza após cancelamento, timeout e restart deve ser testada. Não limpar repositório do usuário, volumes compartilhados ou diretórios calculados sem validar alvo absoluto.

## Alternativa

VM também no desenvolvimento: aproxima a fronteira de produção, mas exige provisionamento e manutenção adicionais. O contrato permanece o mesmo. Não adotar Kubernetes ou criar novo serviço de segredos apenas para esta etapa.

## Critérios de implementação

T12 valida timeout, cancelamento, isolamento de diretórios, traversal/reparse points, argumentos, rede e proteção de credenciais; seu build não depende da API e não acessa banco. Não consumir automaticamente tarefas antigas na fila antes de configurar o runner e suas operações permitidas.

## Fontes

- https://docs.docker.com/engine/security/
- https://docs.docker.com/engine/security/rootless/
- https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-encryption-at-rest

## Decisão

Docker local e política de segredos aprovados. Produção permanece condicionada à implementação de isolamento VM/microVM e proteção de segredos do ambiente; nenhum recurso externo foi criado.
