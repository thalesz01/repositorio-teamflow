using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Tasks;

public static class TaskRules
{
    public static TaskStage[] AllowedTransitions(TaskStage stage) => stage switch
    {
        TaskStage.Intake => [TaskStage.Analysis, TaskStage.Cancelled],
        TaskStage.Analysis => [TaskStage.Ready, TaskStage.Blocked, TaskStage.Cancelled],
        TaskStage.Ready => [TaskStage.Analysis, TaskStage.Execution, TaskStage.Cancelled],
        TaskStage.Blocked => [TaskStage.Analysis, TaskStage.Cancelled],
        TaskStage.Failed => [TaskStage.Ready, TaskStage.Cancelled],
        _ => []
    };

    public static void RequireVersion(WorkItem task, Guid expected)
    {
        if (task.Version != expected)
            throw new ApiException(409, "version_conflict", "Task has changed. Reload before editing.");
    }

    public static void RequireTransition(WorkItem task, TaskStage target)
    {
        if (target == TaskStage.Execution)
            throw new ApiException(409, "execution_command_required", "Use the execution command to start work.");
        if (!AllowedTransitions(task.Stage).Contains(target))
            throw new ApiException(409, "invalid_transition", "This task transition is not allowed.");
        if (target == TaskStage.Ready && task.AcceptanceCriteria.Length == 0)
            throw new ApiException(409, "criteria_required", "Acceptance criteria are required before execution.");
    }
}
