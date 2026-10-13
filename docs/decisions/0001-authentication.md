# 0001 — Autenticação do MVP

Status: Aceita em 2026-10-10 pelo responsável, que aprovou ambas as propostas nesta conversa.

## Contexto

Precisamos identificar pessoas e isolar organizações sem criar um serviço de identidade. O backend é ASP.NET Core e o cliente é um navegador.

## Recomendação

ASP.NET Core Identity com EF Core/PostgreSQL, contas locais e cookies de sessão HttpOnly. Não emitir JWT para o navegador. Proteger comandos com antiforgery, inclusive login/registro, e limitar tentativas de login. Em produção, cookies Secure, HTTPS e chaves de Data Protection persistidas fora do código. No desenvolvimento, HTTP somente em localhost. Cookie não persistente, prazo de oito horas e validação de security stamp para revogação. A interface usa proxy de mesma origem para a API; não guardar tokens em localStorage.

Organizações possuem membros owner/member; owner administra membros e projetos, member pode trabalhar nas tarefas. Verificar associação no banco a cada operação; organização selecionada nunca é autorização por si só. Não criar usuário/senha fixa: cadastro cria conta com senha definida pelo usuário; testes criam usuários isolados. Antes de piloto público, acrescentar recuperação de conta/verificação de e-mail e definir provedor de envio em tarefas próprias.

## Alternativa

OIDC com provedor externo: reduz gestão de senhas e atende SSO, mas adiciona configuração e operação externa. Pode substituir/adicionar login no futuro preservando IDs locais e associação a organizações.

## Consequências

Identity cuida de hashing, bloqueio e cookies; precisamos operar banco e chaves, proteger CSRF e manter gestão de contas. Sem SSO nesta etapa. Nenhum recurso pago ou conta externa será criado.

Referências: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity ; https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery

## Decisão

Proposta aprovada explicitamente pelo usuário: "Sim, aprovo". Implementar a recomendação descrita acima no escopo do MVP.
