using TeamFlow.Api.Features.Integrations;

namespace TeamFlow.Api.Features.Tasks;

public sealed record TaskInput(string Title, string Description, string[] AcceptanceCriteria, Guid? AssigneeId, Guid[] DependencyIds, Guid? AnalysisId = null,
    string? FolderPath = null, string? BaseBranch = null, TaskStage? Stage = null, AgentChoice? Ai = null, string? Kind = null, Guid? PreviousTaskId = null);
public sealed record TaskUpdate(string Title, string Description, string[] AcceptanceCriteria, Guid? AssigneeId, Guid[] DependencyIds, Guid Version);
public sealed record TransitionInput(TaskStage Stage, Guid Version);
public sealed record TaskDto(Guid Id, Guid OrganizationId, Guid ProjectId, string Title, string Description,
    string[] AcceptanceCriteria, Guid? AssigneeId, Guid[] DependencyIds, TaskStage Stage, Guid Version, DateTimeOffset CreatedAt, TaskStage[] AllowedTransitions, Guid? AnalysisId = null,
    string? FolderPath = null, string? BaseBranch = null, Guid? AgentProfileId = null, string Kind = "change", Guid? PreviousTaskId = null, bool SequenceRunning = false)
{
    public static TaskDto From(WorkItem task) => new(task.Id, task.OrganizationId, task.ProjectId, task.Title, task.Description,
        task.AcceptanceCriteria, task.AssigneeId, task.DependencyIds, task.Stage, task.Version, task.CreatedAt, TaskRules.AllowedTransitions(task.Stage), task.AnalysisId,
        task.FolderPath, task.BaseBranch, task.AgentProfileId, task.Kind, task.PreviousTaskId, task.SequenceStartedBy != null && task.Stage == TaskStage.Ready);
}
