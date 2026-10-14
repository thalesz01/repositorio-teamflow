namespace TeamFlow.Api.Features.Tasks;

// "change" edits code and ends in review; "analysis" only reads the folder and ends with an answer (ADR 0014).
public enum TaskKind { Change, Analysis }

public enum TaskStage { Intake, Analysis, Ready, Execution, Review, AwaitingMerge, Done, Blocked, Failed, Cancelled }

public sealed class WorkItem
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? AnalysisId { get; set; }
    public Guid? AgentProfileId { get; set; }
    // The task this one continues (ADR 0015); at most one task continues a given task.
    public Guid? PreviousTaskId { get; set; }
    // Set while the step waits for its turn in a sequence that the person started; cleared when the sequence stops.
    public Guid? SequenceStartedBy { get; set; }
    public TaskKind Kind { get; set; } = TaskKind.Change;
    // Folder and base branch chosen when the task was created; null for tasks that use the project connection.
    public Guid? RepositoryConnectionId { get; set; }
    public string? FolderPath { get; set; }
    public string? BaseBranch { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string[] AcceptanceCriteria { get; set; } = [];
    public Guid? AssigneeId { get; set; }
    public Guid[] DependencyIds { get; set; } = [];
    public TaskStage Stage { get; set; } = TaskStage.Intake;
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
