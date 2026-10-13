# Memória compartilhada

Fontes pertencem a um projeto e têm tipo, chave única, versão, hash SHA-256, autores, datas e status. As operações HTTP exigem autenticação e associação à equipe; as mutações também exigem antiforgery. O painel permite cadastrar documentos/código autorizado/decisões, validar, tornar obsoleto, arquivar e restaurar.

Fontes novas e resoluções extraídas após merge confirmado entram como candidatas. A resolução liga tarefa, execução, PR e commit confirmado. Aprovar um PR não valida automaticamente o conhecimento: alguém da equipe revisa o conteúdo e altera seu status. A extração é idempotente por execução e participa da transação de confirmação do merge.

Cada alteração gera uma versão imutável; atualizar com versão antiga retorna conflito. GET da fonte e GET /versions/{version} permitem verificar referências. O contrato público usa DTOs próprios. A fonte é limitada a 32 KiB de UTF-8; conteúdo reconhecido como credencial é rejeitado em entrada manual e redigido na extração. Esta detecção conservadora não substitui a revisão das fontes autorizadas.

DELETE arquiva de forma reversível, preservando o histórico. Fontes arquivadas ficam ocultas também na leitura de versões e não podem ser reimportadas implicitamente; restauração explícita gera uma nova versão candidata. Indexação/busca devem conferir escopo, versão atual, status validado e ausência de arquivamento antes de enviar qualquer derivado ao modelo. Revogação de associação bloqueia o acesso pelas mesmas verificações de projeto. Nenhuma exclusão física automática foi aplicada; a proposta de retenção permanece pendente de autorização específica.

Validação: integração PostgreSQL cobre versionamento, conflito, tenant, arquivamento, restauração e caminhos/conteúdo excluídos; revisão cobre criação candidata vinculada ao merge. E2E de memória usa API local real, sem provedor de IA ou GitHub real.
