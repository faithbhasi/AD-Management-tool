using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ilm.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AcknowledgedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AcknowledgedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Approvals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SubjectId = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    RequiredRole = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestedByLabel = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecidedByLabel = table.Column<string>(type: "TEXT", nullable: true),
                    DecidedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Comment = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    InvalidationReason = table.Column<string>(type: "TEXT", nullable: true),
                    ConfigurationVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Approvals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Issuer = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    LastSeenEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSignInUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MigratedToUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditForwardingCheckpoints",
                columns: table => new
                {
                    SinkName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    LastForwardedSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    LastForwardedHash = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditForwardingCheckpoints", x => x.SinkName);
                });

            migrationBuilder.CreateTable(
                name: "AuditRecords",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PreviousHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Mac = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MacKeyId = table.Column<string>(type: "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "TEXT", nullable: true),
                    ActorIssuer = table.Column<string>(type: "TEXT", nullable: true),
                    ActorSubject = table.Column<string>(type: "TEXT", nullable: true),
                    AppUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EffectiveRoles = table.Column<string>(type: "TEXT", nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TargetStableId = table.Column<string>(type: "TEXT", nullable: true),
                    Domain = table.Column<string>(type: "TEXT", nullable: true),
                    Forest = table.Column<string>(type: "TEXT", nullable: true),
                    OuGuid = table.Column<string>(type: "TEXT", nullable: true),
                    AuthorityDecision = table.Column<string>(type: "TEXT", nullable: true),
                    ProtectionDecision = table.Column<string>(type: "TEXT", nullable: true),
                    ScopeDecision = table.Column<string>(type: "TEXT", nullable: true),
                    Approval = table.Column<string>(type: "TEXT", nullable: true),
                    ConfigurationVersion = table.Column<long>(type: "INTEGER", nullable: true),
                    BeforeValues = table.Column<string>(type: "TEXT", nullable: true),
                    RequestedValues = table.Column<string>(type: "TEXT", nullable: true),
                    AppliedValues = table.Column<string>(type: "TEXT", nullable: true),
                    SelectedConnector = table.Column<string>(type: "TEXT", nullable: true),
                    SelectedDomainController = table.Column<string>(type: "TEXT", nullable: true),
                    AttemptedActions = table.Column<string>(type: "TEXT", nullable: true),
                    VerifiedActions = table.Column<string>(type: "TEXT", nullable: true),
                    WorkflowState = table.Column<string>(type: "TEXT", nullable: true),
                    Result = table.Column<string>(type: "TEXT", nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: true),
                    ExceptionCategory = table.Column<string>(type: "TEXT", nullable: true),
                    ReconciliationResults = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditRecords", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "AuthorityRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Population = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    BusinessEntity = table.Column<string>(type: "TEXT", nullable: true),
                    MigrationWave = table.Column<string>(type: "TEXT", nullable: true),
                    AccountType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LifecycleAction = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AttributeSet = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AuthoritativeSystem = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ContainmentOwner = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ProvisioningStrategy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EffectiveUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ApprovedConfigurationVersion = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorityRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConfigurationVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DocumentJson = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    ValidationIssuesJson = table.Column<string>(type: "TEXT", nullable: true),
                    ProposedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProposedByLabel = table.Column<string>(type: "TEXT", nullable: false),
                    ProposedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ApprovalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ActivatedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SupersedesVersion = table.Column<long>(type: "INTEGER", nullable: true),
                    RollbackOfVersion = table.Column<long>(type: "INTEGER", nullable: true),
                    IsBootstrap = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigurationVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: true),
                    System = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ForestOrTenantId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ConnectorId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    StableObjectId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ObjectGuid = table.Column<Guid>(type: "TEXT", nullable: true),
                    ObjectSid = table.Column<string>(type: "TEXT", maxLength: 184, nullable: true),
                    DistinguishedName = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    SamAccountName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    UserPrincipalName = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Mail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    OktaIssuer = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    OktaSubject = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EntraObjectId = table.Column<string>(type: "TEXT", nullable: true),
                    SourceAnchor = table.Column<string>(type: "TEXT", nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LastReconciledUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AccountType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Population = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    BusinessEntity = table.Column<string>(type: "TEXT", nullable: true),
                    MigrationWave = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIdentities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeasibilityRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Environment = table.Column<string>(type: "TEXT", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResultsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ReportMarkdown = table.Column<string>(type: "TEXT", nullable: false),
                    ReportSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    Recommendation = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StartedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApprovalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeasibilityRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdentityLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceIdentityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetIdentityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LinkMethod = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EvidenceType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EvidenceReference = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Confidence = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EffectiveFrom = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EffectiveUntil = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IssuerMigrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    ProposedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApprovalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Applied = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AppliedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssuerMigrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeaverRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Urgency = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    TicketReference = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EffectiveUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestedByLabel = table.Column<string>(type: "TEXT", nullable: false),
                    ApprovalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PlanJson = table.Column<string>(type: "TEXT", nullable: true),
                    PlanHash = table.Column<string>(type: "TEXT", nullable: true),
                    ConfigurationVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ContainmentStartedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SafelyContainedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SlaDueUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastErrorCategory = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaverRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LockLeases",
                columns: table => new
                {
                    LockKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    AcquiredUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LockLeases", x => x.LockKey);
                });

            migrationBuilder.CreateTable(
                name: "ManualTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LeaverRequestId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ContainmentActionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    RunbookMarkdown = table.Column<string>(type: "TEXT", nullable: false),
                    TargetsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SlaDueUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SlaBreached = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AssignedRole = table.Column<string>(type: "TEXT", nullable: false),
                    VerificationRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    CompletedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletionEvidence = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualTasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MigrationStates",
                columns: table => new
                {
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MigrationWave = table.Column<string>(type: "TEXT", nullable: true),
                    LegacyIdentityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetIdentityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AuthenticationAuthority = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ProvisioningAuthority = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AccountEnabledStateOwner = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OktaIdentityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CloudIdentityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CutoverDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RollbackDeadline = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationStates", x => x.PersonId);
                });

            migrationBuilder.CreateTable(
                name: "Persons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonIdentifier = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PersonIdentifierSource = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EmployeeIdentifier = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    LifecycleStatus = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BusinessEntity = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Department = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ManagerPersonId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Persons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationFindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdentityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Detail = table.Column<string>(type: "TEXT", nullable: false),
                    DetectedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationFindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IdentitiesChecked = table.Column<int>(type: "INTEGER", nullable: false),
                    FindingsCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContainmentActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeaverRequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Step = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    System = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SessionSystem = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TargetIdentityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetStableId = table.Column<string>(type: "TEXT", nullable: false),
                    TargetLabel = table.Column<string>(type: "TEXT", nullable: false),
                    ConnectorId = table.Column<string>(type: "TEXT", nullable: true),
                    ForestId = table.Column<string>(type: "TEXT", nullable: true),
                    Mandatory = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SelectedDomainController = table.Column<string>(type: "TEXT", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorCategory = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Evidence = table.Column<string>(type: "TEXT", nullable: true),
                    AuthorityDecision = table.Column<string>(type: "TEXT", nullable: true),
                    ProtectionDecision = table.Column<string>(type: "TEXT", nullable: true),
                    AttemptedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VerifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ManualTaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ChangedExternalState = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContainmentActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContainmentActions_LeaverRequests_LeaverRequestId",
                        column: x => x.LeaverRequestId,
                        principalTable: "LeaverRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaverTransitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeaverRequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", nullable: false),
                    Approver = table.Column<string>(type: "TEXT", nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    TicketReference = table.Column<string>(type: "TEXT", nullable: false),
                    PreviousState = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    NextState = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AuthorityDecision = table.Column<string>(type: "TEXT", nullable: false),
                    ProtectionDecision = table.Column<string>(type: "TEXT", nullable: false),
                    ConfigurationVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    ActionsAttempted = table.Column<string>(type: "TEXT", nullable: false),
                    ActionsVerified = table.Column<string>(type: "TEXT", nullable: false),
                    UnresolvedActions = table.Column<string>(type: "TEXT", nullable: false),
                    SafeErrorCategory = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaverTransitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaverTransitions_LeaverRequests_LeaverRequestId",
                        column: x => x.LeaverRequestId,
                        principalTable: "LeaverRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_CreatedUtc",
                table: "Alerts",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_Status",
                table: "Approvals",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_SubjectType_SubjectId",
                table: "Approvals",
                columns: new[] { "SubjectType", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_Issuer_Subject",
                table: "AppUsers",
                columns: new[] { "Issuer", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_Action",
                table: "AuditRecords",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_OperationId",
                table: "AuditRecords",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_Sequence",
                table: "AuditRecords",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_TimestampUtc",
                table: "AuditRecords",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorityRules_ApprovedConfigurationVersion",
                table: "AuthorityRules",
                column: "ApprovedConfigurationVersion");

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationVersions_Status",
                table: "ConfigurationVersions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationVersions_Version",
                table: "ConfigurationVersions",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContainmentActions_IdempotencyKey",
                table: "ContainmentActions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContainmentActions_LeaverRequestId",
                table: "ContainmentActions",
                column: "LeaverRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_ObjectGuid",
                table: "ExternalIdentities",
                column: "ObjectGuid");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_PersonId",
                table: "ExternalIdentities",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_System_ForestOrTenantId_StableObjectId",
                table: "ExternalIdentities",
                columns: new[] { "System", "ForestOrTenantId", "StableObjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdentityLinks_PersonId",
                table: "IdentityLinks",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityLinks_TargetIdentityId",
                table: "IdentityLinks",
                column: "TargetIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaverRequests_IdempotencyKey",
                table: "LeaverRequests",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaverRequests_State",
                table: "LeaverRequests",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "UX_LeaverRequests_OneActivePerPerson",
                table: "LeaverRequests",
                column: "PersonId",
                unique: true,
                filter: "\"IsActive\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_LeaverTransitions_LeaverRequestId_TimestampUtc",
                table: "LeaverTransitions",
                columns: new[] { "LeaverRequestId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ManualTasks_LeaverRequestId",
                table: "ManualTasks",
                column: "LeaverRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ManualTasks_Status",
                table: "ManualTasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_PersonIdentifier",
                table: "Persons",
                column: "PersonIdentifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationFindings_RunId",
                table: "ReconciliationFindings",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Alerts");

            migrationBuilder.DropTable(
                name: "Approvals");

            migrationBuilder.DropTable(
                name: "AppUsers");

            migrationBuilder.DropTable(
                name: "AuditForwardingCheckpoints");

            migrationBuilder.DropTable(
                name: "AuditRecords");

            migrationBuilder.DropTable(
                name: "AuthorityRules");

            migrationBuilder.DropTable(
                name: "ConfigurationVersions");

            migrationBuilder.DropTable(
                name: "ContainmentActions");

            migrationBuilder.DropTable(
                name: "ExternalIdentities");

            migrationBuilder.DropTable(
                name: "FeasibilityRuns");

            migrationBuilder.DropTable(
                name: "IdentityLinks");

            migrationBuilder.DropTable(
                name: "IssuerMigrations");

            migrationBuilder.DropTable(
                name: "LeaverTransitions");

            migrationBuilder.DropTable(
                name: "LockLeases");

            migrationBuilder.DropTable(
                name: "ManualTasks");

            migrationBuilder.DropTable(
                name: "MigrationStates");

            migrationBuilder.DropTable(
                name: "Persons");

            migrationBuilder.DropTable(
                name: "ReconciliationFindings");

            migrationBuilder.DropTable(
                name: "ReconciliationRuns");

            migrationBuilder.DropTable(
                name: "LeaverRequests");
        }
    }
}
