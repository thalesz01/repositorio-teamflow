import type { components } from "../../api/schema";

export type Schema = components["schemas"];
export type TaskItem = Schema["TaskDto"];
export type Stage = Schema["TaskStage"];

const messages: Record<string, string> = {
  codex_machine_unavailable: "Verifique a instalação, o login e a habilitação do Codex nesta máquina.",
  context_source_changed: "Uma fonte foi alterada, arquivada ou revogada. Analise a demanda novamente.",
  analysis_not_complete: "Resolva as perguntas e revise a análise antes de criar a tarefa.",
  memory_not_validated: "Valide a fonte antes de solicitar indexação.",
  index_key_conflict: "Esta solicitação de indexação já possui outros parâmetros.",
  analysis_key_conflict: "Esta análise já possui outros parâmetros.",
  codex_not_configured: "O administrador precisa configurar o executável do Codex nesta máquina.",
  codex_failed: "O Codex não terminou a execução. Confira o login e a instalação local.",
  embedding_not_supported: "Use a busca textual de memória com o perfil Codex CLI.",
  provider_disabled: "O operador precisa configurar e habilitar as chamadas externas neste servidor.",
  budget_exceeded: "O orçamento é menor que a reserva estimada. Revise os limites configurados.",
  context_limit: "O contexto ultrapassa o limite do perfil. Reduza a demanda ou ajuste o perfil.",
  memory_version_conflict: "A fonte mudou. Reabra a memória antes de salvar uma nova versão.",
  invalid_memory_content: "Informe título e conteúdo limitado a 32 KiB.",
  invalid_memory_source: "Use uma chave relativa permitida; arquivos de credenciais são excluídos.",
  sensitive_memory_content: "Remova ou oculte credenciais antes de guardar esta fonte.",
  memory_archived: "Restaure a fonte arquivada explicitamente antes de reutilizá-la.",
  review_conflict: "A revisão mudou durante a consulta. Atualize o estado antes de tentar novamente.",
  review_disabled: "O administrador precisa habilitar a consulta externa de revisão.",
  independent_review_required: "Outra pessoa da equipe precisa revisar esta execução.",
  ci_policy_required: "Configure a GitHub App de CI confiável e os checks exigidos no servidor.",
  ci_required: "Aprove antes do merge, com os checks de CI exigidos aprovados.",
  review_task_changed: "Uma nova execução ou etapa substituiu esta revisão. Atualize o quadro.",
  invalid_review_feedback: "Informe até 2000 caracteres de ajustes para uma tarefa em revisão.",
  feedback_parent_superseded: "Solicite ajustes na execução mais recente, antes do merge.",
  publication_disabled: "O administrador precisa habilitar a publicação externa neste servidor.",
  validation_required: "A execução precisa terminar com alterações e testes aprovados antes de publicar.",
  publication_in_progress: "Esta execução já está sendo publicada. Aguarde a atualização.",
  publication_attempt_limit: "O limite de tentativas foi atingido. O administrador precisa reconciliar o PR.",
  publication_evidence_missing: "As evidências confirmadas desta execução estão incompletas.",
  base_changed: "A branch base mudou. As alterações precisam ser revalidadas antes de publicar.",
  branch_conflict: "A branch da execução foi alterada externamente. É necessário reconciliá-la.",
  integration_not_configured: "O administrador precisa configurar a integração local deste projeto no servidor.",
  repository_not_authorized: "Este repositório não foi autorizado para o projeto.",
  repository_connected: "Desconecte o repositório atual antes de substituí-lo.",
  invalid_repository: "Confira a conta, o repositório, os IDs e a branch base.",
  invalid_profile: "Confira o modelo, os limites e os preços do perfil.",
  profile_not_found: "O perfil selecionado não está mais disponível para este projeto.",
  git_access_denied: "Confira a instalação GitHub App e as permissões do repositório.",
  secret_unavailable: "A credencial precisa ser restaurada pelo administrador.",
  invalid_credentials: "E-mail ou senha inválidos. Confira também se a conta está temporariamente bloqueada.",
  registration_failed: "Não foi possível cadastrar. Confira o e-mail e os requisitos da senha.",
  invalid_csrf: "A sessão de segurança mudou. Tente novamente.",
  version_conflict: "A tarefa foi alterada por outra pessoa. Carregamos a versão atual; revise antes de salvar.",
  dependencies_pending: "Conclua as dependências antes de executar esta tarefa.",
  dependency_cycle: "As dependências não podem formar um ciclo.",
  owner_required: "Somente o proprietário pode realizar esta ação.",
  invalid_transition: "Essa movimentação não está disponível para a tarefa.",
  criteria_required: "Preencha os critérios de aceitação antes de deixar a tarefa pronta.",
  task_not_ready: "A tarefa precisa estar pronta e ter critérios de aceitação.",
  invalid_assignee: "O responsável precisa pertencer a esta equipe.",
};

export class ApiError extends Error {
  constructor(public readonly status: number, public readonly code: string, message: string) { super(message); }
}

export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const headers = new Headers(options.headers);
  if (options.body) headers.set("Content-Type", "application/json");
  if (options.method && !["GET", "HEAD", "OPTIONS"].includes(options.method)) {
    const token = await api<Schema["CsrfDto"]>("/auth/csrf", { signal: options.signal });
    headers.set("X-CSRF-Token", token.token);
  }
  const response = await fetch(`/api/v1${path}`, { ...options, headers, credentials: "same-origin", cache: "no-store" });
  if (!response.ok) {
    const problem: { code?: string; title?: string } = await response.json().catch(() => ({}));
    const code = problem.code ?? `http_${response.status}`;
    throw new ApiError(response.status, code, messages[code] ?? problem.title ?? `Não foi possível concluir (${response.status}).`);
  }
  return response.status === 204 ? undefined as T : await response.json() as T;
}

export async function listAll<T>(path: string, signal?: AbortSignal): Promise<T[]> {
  const items: T[] = [];
  for (let page = 1; ; page++) {
    const result = await api<Pick<Schema["PageResultOfTaskDto"], "page" | "pageSize" | "total"> & { items: T[] }>(
      `${path}?page=${page}&pageSize=100`, { signal });
    items.push(...result.items);
    if (items.length >= Number(result.total) || result.items.length === 0) return items;
  }
}

export const errorMessage = (error: unknown) => error instanceof Error ? error.message
  : typeof error === "object" && error !== null && "code" in error && typeof error.code === "string"
    ? messages[error.code] ?? "Não foi possível concluir a ação. Revise a configuração e o estado da solicitação."
    : "Não foi possível concluir a ação.";
export const stageLabels: Record<Stage, string> = {
  intake: "Entrada", analysis: "Em análise", ready: "Pronta", execution: "Em execução", review: "Em revisão",
  awaitingMerge: "Aguardando merge", done: "Concluída", blocked: "Bloqueada", failed: "Falhou", cancelled: "Cancelada",
};
