using TeamFlow.Api.Features.Memory;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Features.Runners;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Features.Tasks;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Features.Auth;
using TeamFlow.Api.Features.Organizations;
using TeamFlow.Api.Features.Projects;

namespace TeamFlow.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityUserContext<AppUser, Guid>(options)
{
    public DbSet<DemandAnalysis> DemandAnalyses => Set<DemandAnalysis>();
    public DbSet<MemoryIndex> MemoryIndexes => Set<MemoryIndex>();
    public DbSet<MemoryChunk> MemoryChunks => Set<MemoryChunk>();
    public DbSet<MemoryItem> MemoryItems => Set<MemoryItem>();
    public DbSet<MemoryRevision> MemoryRevisions => Set<MemoryRevision>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<Execution> Executions => Set<Execution>();
    public DbSet<ExecutionCheckpoint> ExecutionCheckpoints => Set<ExecutionCheckpoint>();
    public DbSet<ModelTurn> ModelTurns => Set<ModelTurn>();
    public DbSet<RunnerCredential> RunnerCredentials => Set<RunnerCredential>();
    public DbSet<GitConnection> GitConnections => Set<GitConnection>();
    public DbSet<AgentProfile> AgentProfiles => Set<AgentProfile>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.HasPostgresExtension("vector");
        model.Entity<DemandAnalysis>().HasAlternateKey(a => new { a.Id, a.OrganizationId, a.ProjectId });
        model.Entity<DemandAnalysis>().HasOne<Project>().WithMany().HasForeignKey(a => new { a.ProjectId, a.OrganizationId })
            .HasPrincipalKey(p => new { p.Id, p.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<DemandAnalysis>().HasIndex(a => new { a.OrganizationId, a.ProjectId, a.IdempotencyKey }).IsUnique();
        model.Entity<DemandAnalysis>().Property(a => a.IdempotencyKey).HasMaxLength(128);
        model.Entity<DemandAnalysis>().Property(a => a.RequestHash).HasMaxLength(64);
        model.Entity<DemandAnalysis>().Property(a => a.State).HasMaxLength(24);
        model.Entity<DemandAnalysis>().Property(a => a.FailureCode).HasMaxLength(64);
        model.Entity<DemandAnalysis>().Property(a => a.SourceBundle).HasColumnType("jsonb");
        model.Entity<DemandAnalysis>().Property(a => a.Proposal).HasColumnType("jsonb");
        model.Entity<DemandAnalysis>().Property(a => a.MaxCostUsd).HasPrecision(18, 8);
        model.Entity<DemandAnalysis>().Property(a => a.ReservedUsd).HasPrecision(18, 8);
        model.Entity<DemandAnalysis>().Property(a => a.EstimatedCostUsd).HasPrecision(20, 12);
        model.Entity<WorkItem>().HasOne<DemandAnalysis>().WithMany().HasForeignKey(t => new { t.AnalysisId, t.OrganizationId, t.ProjectId })
            .HasPrincipalKey(a => new { a.Id, a.OrganizationId, a.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<MemoryIndex>().HasOne<MemoryItem>().WithMany().HasForeignKey(i => new { i.ItemId, i.OrganizationId, i.ProjectId })
            .HasPrincipalKey(m => new { m.Id, m.OrganizationId, m.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<MemoryIndex>().HasOne<AgentProfile>().WithMany().HasForeignKey(i => new { i.ProfileId, i.OrganizationId, i.ProjectId })
            .HasPrincipalKey(p => new { p.Id, p.OrganizationId, p.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<MemoryIndex>().HasIndex(i => new { i.OrganizationId, i.ProjectId, i.ItemId, i.IdempotencyKey }).IsUnique();
        model.Entity<MemoryIndex>().Property(i => i.IdempotencyKey).HasMaxLength(128);
        model.Entity<MemoryIndex>().Property(i => i.RequestHash).HasMaxLength(64);
        model.Entity<MemoryIndex>().Property(i => i.State).HasMaxLength(16);
        model.Entity<MemoryIndex>().Property(i => i.FailureCode).HasMaxLength(64);
        model.Entity<MemoryIndex>().Property(i => i.Model).HasMaxLength(120);
        model.Entity<MemoryIndex>().Property(i => i.Chunker).HasMaxLength(32);
        model.Entity<MemoryIndex>().Property(i => i.MaxCostUsd).HasPrecision(18, 8);
        model.Entity<MemoryIndex>().Property(i => i.ReservedUsd).HasPrecision(18, 8);
        model.Entity<MemoryIndex>().Property(i => i.EstimatedCostUsd).HasPrecision(20, 12);
        model.Entity<MemoryChunk>().HasKey(c => new { c.IndexId, c.Sequence });
        model.Entity<MemoryChunk>().HasOne<MemoryIndex>().WithMany().HasForeignKey(c => c.IndexId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<MemoryChunk>().Property(c => c.ContentHash).HasMaxLength(64);
        model.Entity<MemoryChunk>().Property(c => c.Embedding).HasColumnType("real[]");
        model.Entity<MemoryItem>().HasAlternateKey(m => new { m.Id, m.OrganizationId, m.ProjectId });
        model.Entity<MemoryItem>().HasOne<Project>().WithMany().HasForeignKey(m => new { m.ProjectId, m.OrganizationId })
            .HasPrincipalKey(p => new { p.Id, p.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<MemoryItem>().HasOne<Execution>().WithMany().HasForeignKey(m => new { m.ExecutionId, m.OrganizationId, m.ProjectId })
            .HasPrincipalKey(e => new { e.Id, e.OrganizationId, e.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<MemoryItem>().HasIndex(m => new { m.OrganizationId, m.ProjectId, m.Key }).IsUnique();
        model.Entity<MemoryItem>().HasIndex(m => m.ExecutionId).IsUnique().HasFilter("\"ExecutionId\" IS NOT NULL");
        model.Entity<MemoryItem>().Property(m => m.Key).HasMaxLength(240);
        model.Entity<MemoryItem>().Property(m => m.Title).HasMaxLength(160);
        model.Entity<MemoryItem>().Property(m => m.ContentHash).HasMaxLength(64);
        model.Entity<MemoryItem>().Property(m => m.Kind).HasConversion<string>().HasMaxLength(16);
        model.Entity<MemoryItem>().Property(m => m.Status).HasConversion<string>().HasMaxLength(16);
        model.Entity<MemoryItem>().Property(m => m.Commit).HasMaxLength(40);
        model.Entity<MemoryItem>().Property(m => m.PullRequestUrl).HasMaxLength(512);
        model.Entity<MemoryItem>().Property(m => m.Version).IsConcurrencyToken();
        model.Entity<MemoryRevision>().HasKey(r => new { r.ItemId, r.Version });
        model.Entity<MemoryRevision>().HasOne<MemoryItem>().WithMany().HasForeignKey(r => new { r.ItemId, r.OrganizationId, r.ProjectId })
            .HasPrincipalKey(m => new { m.Id, m.OrganizationId, m.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<MemoryRevision>().Property(r => r.Title).HasMaxLength(160);
        model.Entity<MemoryRevision>().Property(r => r.ContentHash).HasMaxLength(64);
        model.Entity<MemoryRevision>().Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
        model.Entity<GitConnection>().HasAlternateKey(g => new { g.Id, g.OrganizationId, g.ProjectId });
        model.Entity<ModelTurn>().HasKey(t => new { t.ExecutionId, t.OperationId });
        model.Entity<ModelTurn>().HasIndex(t => new { t.ExecutionId, t.Attempt, t.Sequence }).IsUnique();
        model.Entity<ModelTurn>().HasOne<Execution>().WithMany().HasForeignKey(t => new { t.ExecutionId, t.OrganizationId, t.ProjectId })
            .HasPrincipalKey(e => new { e.Id, e.OrganizationId, e.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ModelTurn>().Property(t => t.ReservedUsd).HasPrecision(18, 8);
        model.Entity<ModelTurn>().Property(t => t.EstimatedCostUsd).HasPrecision(20, 12);
        model.Entity<ModelTurn>().Property(t => t.RequestHash).HasMaxLength(64);
        model.Entity<ModelTurn>().Property(t => t.BillingMode).HasConversion<LowercaseEnumConverter<BillingMode>>().HasMaxLength(8).HasDefaultValue(BillingMode.Api);
        model.Entity<AgentProfile>().Property(p => p.ReasoningEffort).HasMaxLength(16);
        model.Entity<WorkItem>().HasOne<AgentProfile>().WithMany().HasForeignKey(t => new { t.AgentProfileId, t.OrganizationId, t.ProjectId }).HasPrincipalKey(p => new { p.Id, p.OrganizationId, p.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<AgentProfile>().Property(p => p.Provider).HasMaxLength(16).HasDefaultValue("codex");
        model.Entity<GitConnection>().Property(g => g.Provider).HasMaxLength(16).HasDefaultValue("local");
        model.Entity<GitConnection>().Property(g => g.LocalPath).HasMaxLength(1024);
        model.Entity<ModelTurn>().Property(t => t.State).HasConversion<LowercaseEnumConverter<ModelTurnState>>().HasMaxLength(16);
        model.Entity<ModelTurn>().Property(t => t.FailureCode).HasMaxLength(64);
        model.Entity<ModelTurn>().Property(t => t.FailureType).HasMaxLength(128);
        model.Entity<ModelTurn>().Property(t => t.Action).HasColumnType("jsonb");
        model.Entity<Execution>().Property(e => e.ReviewFeedback).HasMaxLength(6000);
        model.Entity<Execution>().HasOne<Execution>().WithMany().HasForeignKey(e => new { e.ParentExecutionId, e.OrganizationId, e.ProjectId })
            .HasPrincipalKey(e => new { e.Id, e.OrganizationId, e.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Execution>().Property(e => e.SkillName).HasMaxLength(80);
        model.Entity<Execution>().Property(e => e.ReviewState).HasMaxLength(32);
        model.Entity<Execution>().Property(e => e.ReviewHeadCommit).HasMaxLength(40);
        model.Entity<Execution>().Property(e => e.ReviewApprovedHead).HasMaxLength(40);
        model.Entity<Execution>().Property(e => e.ConfirmedMergeCommit).HasMaxLength(40);
        model.Entity<Execution>().Property(e => e.PublicationState).HasMaxLength(16);
        model.Entity<Execution>().Property(e => e.PublicationFailureCode).HasMaxLength(64);
        model.Entity<Execution>().Property(e => e.PullRequestUrl).HasMaxLength(512);
        model.Entity<Execution>().Property(e => e.PublishedHeadCommit).HasMaxLength(40);
        model.Entity<Execution>().Property(e => e.ResultHash).HasMaxLength(64);
        model.Entity<Execution>().Property(e => e.ResultCode).HasMaxLength(64);
        model.Entity<ExecutionCheckpoint>().HasKey(c => new { c.ExecutionId, c.Attempt, c.Sequence });
        model.Entity<Execution>().HasAlternateKey(e => new { e.Id, e.OrganizationId, e.ProjectId });
        model.Entity<ExecutionCheckpoint>().HasOne<Execution>().WithMany().HasForeignKey(c => new { c.ExecutionId, c.OrganizationId, c.ProjectId })
            .HasPrincipalKey(e => new { e.Id, e.OrganizationId, e.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ExecutionCheckpoint>().Property(c => c.Context).HasColumnType("jsonb");
        model.Entity<ExecutionCheckpoint>().Property(c => c.ContentHash).HasMaxLength(64);
        model.Entity<ExecutionCheckpoint>().HasIndex(c => new { c.OrganizationId, c.ProjectId, c.ExecutionId });
        model.Entity<GitConnection>().HasOne<Project>().WithMany().HasForeignKey(g => new { g.ProjectId, g.OrganizationId })
            .HasPrincipalKey(p => new { p.Id, p.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<GitConnection>().HasIndex(g => new { g.OrganizationId, g.ProjectId }).IsUnique().HasFilter("\"RevokedAt\" IS NULL AND NOT \"ForTasks\"");
        model.Entity<GitConnection>().HasIndex(g => new { g.OrganizationId, g.ProjectId, g.LocalPath, g.BaseBranch }).IsUnique().HasFilter("\"RevokedAt\" IS NULL AND \"ForTasks\"");
        model.Entity<WorkItem>().HasOne<GitConnection>().WithMany().HasForeignKey(t => new { t.RepositoryConnectionId, t.OrganizationId, t.ProjectId })
            .HasPrincipalKey(g => new { g.Id, g.OrganizationId, g.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<WorkItem>().Property(t => t.Kind).HasConversion<LowercaseEnumConverter<TaskKind>>().HasMaxLength(16).HasDefaultValue(TaskKind.Change);
        model.Entity<WorkItem>().HasOne<WorkItem>().WithMany().HasForeignKey(t => new { t.PreviousTaskId, t.OrganizationId, t.ProjectId })
            .HasPrincipalKey(t => new { t.Id, t.OrganizationId, t.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<WorkItem>().HasIndex(t => t.PreviousTaskId).IsUnique().HasFilter("\"PreviousTaskId\" IS NOT NULL");
        model.Entity<WorkItem>().Property(t => t.FolderPath).HasMaxLength(1024);
        model.Entity<WorkItem>().Property(t => t.BaseBranch).HasMaxLength(120);
        model.Entity<GitConnection>().Property(g => g.BaseBranch).HasMaxLength(120);
        model.Entity<GitConnection>().Property(g => g.BaseCommit).HasMaxLength(40);
        model.Entity<GitConnection>().Property(g => g.Version).IsConcurrencyToken();
        model.Entity<AgentProfile>().HasOne<Project>().WithMany().HasForeignKey(p => new { p.ProjectId, p.OrganizationId })
            .HasPrincipalKey(p => new { p.Id, p.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<AgentProfile>().Property(p => p.Name).HasMaxLength(120);
        model.Entity<AgentProfile>().Property(p => p.Model).HasMaxLength(120);
        model.Entity<AgentProfile>().Property(p => p.InputUsdPerMillionTokens).HasPrecision(16, 6);
        model.Entity<AgentProfile>().Property(p => p.OutputUsdPerMillionTokens).HasPrecision(16, 6);
        model.Entity<Execution>().Property(e => e.MemoryContext).HasColumnType("jsonb");
        model.Entity<Execution>().Property(e => e.TransferSnapshot).HasColumnType("jsonb");
        model.Entity<Execution>().Property(e => e.RepositorySnapshot).HasColumnType("jsonb");
        model.Entity<Execution>().HasOne<AgentProfile>().WithMany().HasForeignKey(e => e.AgentProfileId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<RunnerCredential>().Property(r => r.TokenHash).HasMaxLength(64);
        model.Entity<RunnerCredential>().HasOne<Project>().WithMany().HasForeignKey(r => new { r.ProjectId, r.OrganizationId })
            .HasPrincipalKey(p => new { p.Id, p.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Execution>().HasOne<RunnerCredential>().WithMany().HasForeignKey(e => e.RunnerCredentialId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Execution>().Property(e => e.State).HasConversion<string>().HasMaxLength(16);
        model.Entity<Execution>().Property(e => e.IdempotencyKey).HasMaxLength(128);
        model.Entity<Execution>().Property(e => e.RequestHash).HasMaxLength(64);
        model.Entity<Execution>().Property(e => e.TaskSnapshot).HasColumnType("jsonb");
        model.Entity<Execution>().Property(e => e.FailureCode).HasMaxLength(64);
        model.Entity<Execution>().Property(e => e.MaxCostUsd).HasPrecision(10, 2);
        model.Entity<Execution>().HasIndex(e => new { e.OrganizationId, e.TaskId, e.IdempotencyKey }).IsUnique();
        model.Entity<Execution>().HasIndex(e => e.TaskId).IsUnique().HasFilter("\"State\" IN ('Queued', 'Leased')");
        model.Entity<Execution>().HasIndex(e => new { e.State, e.CreatedAt });
        model.Entity<Execution>().HasOne<WorkItem>().WithMany().HasForeignKey(e => new { e.TaskId, e.OrganizationId, e.ProjectId })
            .HasPrincipalKey(t => new { t.Id, t.OrganizationId, t.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Execution>().HasOne<AppUser>().WithMany().HasForeignKey(e => e.RequestedBy).OnDelete(DeleteBehavior.Restrict);
        model.Entity<WorkItem>().Property(t => t.Title).HasMaxLength(160);
        model.Entity<WorkItem>().Property(t => t.Description).HasMaxLength(10000);
        model.Entity<WorkItem>().Property(t => t.Stage).HasConversion<string>().HasMaxLength(24);
        model.Entity<WorkItem>().Property(t => t.Version).IsConcurrencyToken();
        model.Entity<WorkItem>().HasAlternateKey(t => new { t.Id, t.OrganizationId, t.ProjectId });
        model.Entity<WorkItem>().HasOne<Project>().WithMany().HasForeignKey(t => new { t.ProjectId, t.OrganizationId })
            .HasPrincipalKey(p => new { p.Id, p.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<WorkItem>().HasOne<AppUser>().WithMany().HasForeignKey(t => t.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Organization>().Property(o => o.Name).HasMaxLength(120);
        model.Entity<Membership>().HasKey(m => new { m.OrganizationId, m.UserId });
        model.Entity<Membership>().Property(m => m.Role).HasConversion<LowercaseEnumConverter<MemberRole>>().HasMaxLength(16);
        model.Entity<Membership>().HasOne<Organization>().WithMany().HasForeignKey(m => m.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Membership>().HasOne<AppUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Project>().Property(p => p.Name).HasMaxLength(120);
        model.Entity<Project>().Property(p => p.Version).IsConcurrencyToken();
        model.Entity<Project>().HasAlternateKey(p => new { p.Id, p.OrganizationId });
        model.Entity<Project>().HasOne<Organization>().WithMany().HasForeignKey(p => p.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}
