namespace TeamFlow.Api.Features.Tasks;

public enum TaskStage { Intake, Analysis, Ready, Execution, Review, AwaitingMerge, Done, Blocked, Failed, Cancelled }

public sealed class WorkItem
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? AnalysisId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string[] AcceptanceCriteria { get; set; } = [];
    public Guid? AssigneeId { get; set; }
    public Guid[] DependencyIds { get; set; } = [];
    public TaskStage Stage { get; set; } = TaskStage.Intake;
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
