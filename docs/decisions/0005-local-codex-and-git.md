# ADR 0005 — Codex CLI e repositórios locais

- Status: Aceita em 2026-10-12; substitui a obrigatoriedade de GitHub App/OpenAI API da ADR 0004 para o fluxo principal.
- Decisão: Codex CLI nativo com autenticação local; Git local por projeto, base commitada e cópia por execução. HTTP mantém API/runner independentes.
- Revisão: solicitação local com diff e commit rastreáveis; outra pessoa aprova o head testado, merge fast-forward manual e confirmação local antes de Done.
- Memória: recuperação textual inicial, sem exigir embeddings externos. Contexto e checkpoints pertencem ao TeamFlow.
- Limites: CLI instalado não implica modelo offline. Cobrança desconhecida permanece null. Produção multi-tenant continua bloqueada; piloto local é Development.
- Operação e contrato: docs/local-development.md. Integrações anteriores permanecem compatíveis; novas telas priorizam Codex/local Git.