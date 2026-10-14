# 0016 — Consumo de tokens dos CLIs: medir e enviar menos

- Status: **Aceita** em 2026-10-14 pelo responsável ("diminuir a quantidade de tokens utilizadas em cada interação com o CLI"; itens 1 a 3 do plano).
- Contexto: cada passo do agente é uma chamada nova ao CLI (`codex exec --ephemeral`, `claude --print --no-session-persistence`) que recebe instruções, política, skill e o contexto inteiro, inclusive o conteúdo de todos os arquivos lidos até ali. O consumo era desconhecido (`null`), então nenhuma melhora podia ser verificada.

## Medições (2026-10-14, chamadas reais com o prompt "responda OK")

- Codex 0.162: ~13,5 mil tokens de entrada **por chamada**, mesmo para um prompt de uma palavra (prompt de sistema e ferramentas do CLI). Numa segunda chamada idêntica, 11 648 vieram do cache: o cache do provedor funciona entre processos efêmeros quando o começo do prompt é igual.
- Claude (sem ferramentas): ~2,3 mil tokens de entrada, a maior parte lida do cache (535) ou gravada nele (1 732).
- Consequência: o custo fixo por passo é alto e o contexto cresce a cada passo, então reduzir o **número de passos** e **não mudar o começo do prompt** pesam mais do que cortar texto.

## Decisão

1. **Medir.** O adaptador do Codex passa a usar `--json` e soma `input_tokens`, `cached_input_tokens` e `output_tokens` dos eventos `turn.completed`; o do Claude lê `usage` do envelope JSON (entrada = `input_tokens` + escrita + leitura de cache). Os valores vão para `ModelTurn` (`InputTokens` inclui a parte em cache; nova coluna `CachedInputTokens`, migração `ModelTurnCachedTokens`) e para o progresso da execução (`inputTokens`, `cachedInputTokens`, `outputTokens`), mostrados no painel de progresso. Número que o CLI não informa continua `null`. O **custo em dinheiro continua desconhecido**: o `total_cost_usd` do Claude é uma estimativa de tarifa de API e não vira custo da assinatura. Chamadas do Claude sem esquema de resposta (texto) não informam tokens.
2. **Visão menor do contexto no prompt (`PromptContext`).** O checkpoint persistido continua completo e portável; só o texto enviado ao modelo muda: evidência de teste vira "código de saída + diagnóstico" sem repetir comando, limites e manifesto do sandbox (o diagnóstico do teste mais recente fica com até 3000 caracteres por fluxo, os anteriores com 300); uma leitura repetida do mesmo arquivo mantém só a mais recente; o JSON sai com UTF-8 direto, sem `\u00XX` para acentos, `<`, `>` e `&`.
3. **Omitir o conteúdo mais antigo só ao passar de um limite.** Quando o conteúdo das evidências passa de 40 000 bytes, o mais antigo é trocado por um marcador ("leia o arquivo de novo se precisar") até caber em 20 000 bytes; o hash e o caminho permanecem. O intervalo entre os dois limites faz o texto mudar raramente; entre as mudanças o contexto só cresce no fim e o cache do provedor continua casando com o começo.
4. O limite de entrada do perfil (`MaxInputBytes`) passa a valer sobre o prompt já reduzido.

## Consequências

- Leituras não são truncadas: uma escrita substitui o arquivo inteiro, então o modelo precisa do conteúdo completo de um arquivo que vai editar. Um arquivo omitido pode ser lido outra vez (custa um passo).
- Um modelo que ainda precisa de um arquivo omitido gasta um passo para ler de novo; os limites (40 000/20 000) são constantes em `PromptContext` e podem ser ajustados com dados reais. Número de passos, orçamento por execução e leitura de vários arquivos por vez ficam para uma próxima decisão.
- Não houve execução completa de agente com modelo real para medir a economia; o teste automatizado com uma execução sintética de 10 passos enviou ~24% menos caracteres que o contexto armazenado (sem atingir o limite de 40 000 bytes de omissão).
