using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ilm.Persistence.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ilm");

            migrationBuilder.CreateTable(
                name: "Alerts",
                schema: "ilm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Severity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcknowledgedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcknowledgedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Approvals",
                schema: "ilm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubjectId = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    RequiredRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByLabel = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedByLabel = table.Column<string>(type: "text", nullable: true),
                    DecidedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InvalidationReason = table.Column<string>(type: "text", nullable: true),
                    ConfigurationVersion = table.Column<long>(type: "bigint", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Approvals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppUsers",
                schema: "ilm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Issuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LastSeenEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSignInUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MigratedToUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditForwardingCheckpoints",
                schema: "ilm",
                columns: table => new
                {
                    SinkName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LastForwardedSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastForwardedHash = table.Column<string>(type: "text", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditForwardingCheckpoints", x => x.SinkName);
                });

            migrationBuilder.CreateTable(
                name: "AuditRecords",
                schema: "ilm",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PreviousHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Mac = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MacKeyId = table.Column<string>(type: "text", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: true),
                    ActorIssuer = table.Column<string>(type: "text", nullable: true),
                    ActorSubject = table.Column<string>(type: "text", nullable: true),
                    AppUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EffectiveRoles = table.Column<string>(type: "text", nullable: true),
                    Action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TargetStableId = table.Column<string>(type: "text", nullable: true),
                    Domain = table.Column<string>(type: "text", nullable: true),
                    Forest = table.Column<string>(type: "text", nullable: true),
                    OuGuid = table.Column<string>(type: "text", nullable: true),
                    AuthorityDecision = table.Column<string>(type: "text", nullable: true),
                    ProtectionDecision = table.Column<string>(type: "text", nullable: true),
                    ScopeDecision = table.Column<string>(type: "text", nullable: true),
                    Approval = table.Column<string>(type: "text", nullable: true),
                    ConfigurationVersion = table.Column<long>(type: "bigint", nullable: true),
                    BeforeValues = table.Column<string>(type: "text", nullable: true),
                    RequestedValues = table.Column<string>(type: "text", nullable: true),
                    AppliedValues = table.Column<string>(type: "text", nullable: true),
                    SelectedConnector = table.Column<string>(type: "text", nullable: true),
                    SelectedDomainController = table.Column<string>(type: "text", nullable: true),
                    AttemptedActions = table.Column<string>(type: "text", nullable: true),
                    VerifiedActions = table.Column<string>(type: "text", nullable: true),
                    WorkflowState = table.Column<string>(type: "text", nullable: true),
                    Result = table.Column<string>(type: "text", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    ExceptionCategory = table.Column<string>(type: "text", nullable: true),
                    ReconciliationResults = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditRecords", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "AuthorityRules",
                schema: "ilm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Population = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BusinessEntity = table.Column<string>(type: "text", nullable: true),
                    MigrationWave = table.Column<string>(type: "text", nullable: true),
                    AccountType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LifecycleAction = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttributeSet = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthoritativeSystem = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContainmentOwner = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProvisioningStrategy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedConfigurationVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorityRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConfigurationVersions",
                schema: "ilm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DocumentJson = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    ValidationIssuesJson = table.Column<string>(type: "text", nullable: true),
                    ProposedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposedByLabel = table.Column<string>(type: "text", nullable: false),
                    ProposedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovalId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActivatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupersedesVersion = table.Column<long>(type: "bigint", nullable: true),
                    RollbackOfVersion = table.Column<long>(type: "bigint", nullable: true),
                    IsBootstrap = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigurationVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalIdentities",
                schema: "ilm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    System = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ForestOrTenantId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ConnectorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    StableObjectId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ObjectGuid = table.Column<Guid>(type: "uuid", nullable: true),
                    ObjectSid = table.Column<string>(type: "character varying(184)", maxLength: 184, nullable: true),
                    DistinguishedName = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    SamAccountName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UserPrincipalName = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Mail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    OktaIssuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    OktaSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EntraObjectId = table.Column<string>(type: "text", nullable: true),
                    SourceAnchor = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastReconciledUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AccountType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Population = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BusinessEntity = table.Column<string>(type: "text", nullable: true),
                    MigrationWave = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIdentities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeasibilityRuns",
                schema: "ilm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Environment = table.Column<string>(type: "text", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResultsJson = table.Column<string>(type: "text", nullable: false),
                    ReportMarkdown = table.Column<string>(type: "text", nullable: false),
                    ReportSha256 = table.Column<string>(type: "text", nullable: false),
                    Recommendation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovalId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeasibilityRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdentityLinks",
                schema: "ilm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceIdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetIdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkMethod = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvidenceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvidenceReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Confidence = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IssuerMigrations",
                schema: "ilm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FromUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    ProposedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovalId = table.Column<Guid>(type: "uuid", nullable: true),
                    Applied = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AppliedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssuerMigrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeaverRequests",
                schema: "ilm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Urgency = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    TicketReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EffectiveUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "text", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByLabel = table.Column<string>(type: "text", nullable: false),
                    ApprovalId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanJson = table.Column<string>(type: "text", nullable: true),
                    PlanHash = table.Column<string>(type: "text", nullable: true),
                    ConfigurationVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ContainmentStartedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SafelyContainedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SlaDueUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "uuid", nullable: false),
                    LastErrorCategory = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaverRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LockLeases",
                schema: "ilm",
                columns: table => new
                {
                    LockKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Owner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AcquiredUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LockLeases", x => x.LockKey);
                });

            migrationBuilder.CreateTable(
                name: "ManualTasks",
                schema: "ilm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LeaverRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContainmentActionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Severity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    RunbookMarkdown = table.Column<string>(type: "text", nullable: false),
                    TargetsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SlaDueUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SlaBreached = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssignedRole = table.Column<string>(type: "text", nullable: false),
                    VerificationRequired = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletionEvidence = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualTasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MigrationStates",
                schema: "ilm",
                columns: table => new
                {
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    MigrationWave = table.Column<string>(type: "text", nullable: true),
                    LegacyIdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetIdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthenticationAuthority = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProvisioningAuthority = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AccountEnabledStateOwner = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OktaIdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    CloudIdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CutoverDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RollbackDeadline = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationStates", x => x.PersonId);
                });

            migrationBuilder.CreateTable(
                name: "Persons",
                schema: "ilm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonIdentifier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PersonIdentifierSource = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmployeeIdentifier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LifecycleStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BusinessEntity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Department = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ManagerPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Persons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationFindings",
                schema: "ilm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: false),
                    DetectedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationFindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationRuns",
                schema: "ilm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IdentitiesChecked = table.Column<int>(type: "integer", nullable: false),
                    FindingsCount = table.Column<int>(type: "integer", nullable: false),
                    Trigger = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContainmentActions",
                schema: "ilm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaverRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Step = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Method = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    System = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionSystem = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TargetIdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetStableId = table.Column<string>(type: "text", nullable: false),
                    TargetLabel = table.Column<string>(type: "text", nullable: false),
                    ConnectorId = table.Column<string>(type: "text", nullable: true),
                    ForestId = table.Column<string>(type: "text", nullable: true),
                    Mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SelectedDomainController = table.Column<string>(type: "text", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ErrorCategory = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Evidence = table.Column<string>(type: "text", nullable: true),
                    AuthorityDecision = table.Column<string>(type: "text", nullable: true),
                    ProtectionDecision = table.Column<string>(type: "text", nullable: true),
                    AttemptedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ManualTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedExternalState = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContainmentActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContainmentActions_LeaverRequests_LeaverRequestId",
                        column: x => x.LeaverRequestId,
                        principalSchema: "ilm",
                        principalTable: "LeaverRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaverTransitions",
                schema: "ilm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaverRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    Approver = table.Column<string>(type: "text", nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    TicketReference = table.Column<string>(type: "text", nullable: false),
                    PreviousState = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NextState = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorityDecision = table.Column<string>(type: "text", nullable: false),
                    ProtectionDecision = table.Column<string>(type: "text", nullable: false),
                    ConfigurationVersion = table.Column<long>(type: "bigint", nullable: false),
                    ActionsAttempted = table.Column<string>(type: "text", nullable: false),
                    ActionsVerified = table.Column<string>(type: "text", nullable: false),
                    UnresolvedActions = table.Column<string>(type: "text", nullable: false),
                    SafeErrorCategory = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaverTransitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaverTransitions_LeaverRequests_LeaverRequestId",
                        column: x => x.LeaverRequestId,
                        principalSchema: "ilm",
                        principalTable: "LeaverRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_CreatedUtc",
                schema: "ilm",
                table: "Alerts",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_Status",
                schema: "ilm",
                table: "Approvals",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_SubjectType_SubjectId",
                schema: "ilm",
                table: "Approvals",
                columns: new[] { "SubjectType", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_Issuer_Subject",
                schema: "ilm",
                table: "AppUsers",
                columns: new[] { "Issuer", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_Action",
                schema: "ilm",
                table: "AuditRecords",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_OperationId",
                schema: "ilm",
                table: "AuditRecords",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_Sequence",
                schema: "ilm",
                table: "AuditRecords",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_TimestampUtc",
                schema: "ilm",
                table: "AuditRecords",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorityRules_ApprovedConfigurationVersion",
                schema: "ilm",
                table: "AuthorityRules",
                column: "ApprovedConfigurationVersion");

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationVersions_Status",
                schema: "ilm",
                table: "ConfigurationVersions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationVersions_Version",
                schema: "ilm",
                table: "ConfigurationVersions",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContainmentActions_IdempotencyKey",
                schema: "ilm",
                table: "ContainmentActions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContainmentActions_LeaverRequestId",
                schema: "ilm",
                table: "ContainmentActions",
                column: "LeaverRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_ObjectGuid",
                schema: "ilm",
                table: "ExternalIdentities",
                column: "ObjectGuid");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_PersonId",
                schema: "ilm",
                table: "ExternalIdentities",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_System_ForestOrTenantId_StableObjectId",
                schema: "ilm",
                table: "ExternalIdentities",
                columns: new[] { "System", "ForestOrTenantId", "StableObjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdentityLinks_PersonId",
                schema: "ilm",
                table: "IdentityLinks",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityLinks_TargetIdentityId",
                schema: "ilm",
                table: "IdentityLinks",
                column: "TargetIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaverRequests_IdempotencyKey",
                schema: "ilm",
                table: "LeaverRequests",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaverRequests_State",
                schema: "ilm",
                table: "LeaverRequests",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "UX_LeaverRequests_OneActivePerPerson",
                schema: "ilm",
                table: "LeaverRequests",
                column: "PersonId",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_LeaverTransitions_LeaverRequestId_TimestampUtc",
                schema: "ilm",
                table: "LeaverTransitions",
                columns: new[] { "LeaverRequestId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ManualTasks_LeaverRequestId",
                schema: "ilm",
                table: "ManualTasks",
                column: "LeaverRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ManualTasks_Status",
                schema: "ilm",
                table: "ManualTasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_PersonIdentifier",
                schema: "ilm",
                table: "Persons",
                column: "PersonIdentifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationFindings_RunId",
                schema: "ilm",
                table: "ReconciliationFindings",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Alerts",
                schema: "ilm");

            migrationBuilder.DropTable(
                name: "Approvals",
                schema: "ilm");

            migrationBuilder.DropTable(
                name: "AppUsers",
                schema: "ilm");

            migrationBuilder.DropTable(
                name: "AuditForwardingCheckpoints",
                schema: "ilm");

            migrationBuilder.DropTable(
                name: "AuditRecords",
                schema: "ilm");

            migrationBuilder.DropTable(
                name: "AuthorityRules",
                schema: "ilm");

            migrationBuilder.DropTable(
                name: "ConfigurationVersions",
                schema: "ilm");

            migrationBuilder.DropTable(
                name: "ContainmentActions",
                schema: "ilm");

            migrationBuilder.DropTable(
                name: "ExternalIdentities",
                schema: "ilm");

            migrationBuilder.DropTable(
                name: "FeasibilityRuns",
                schema: "ilm");

            migrationBuilder.DropTable(
                name: "IdentityLinks",
                schema: "ilm");

            migrationBuilder.DropTable(
                name: "IssuerMigrations",
                schema: "ilm");

            migrationBuilder.DropTable(
                name: "LeaverTransitions",
                schema: "ilm");

            migrationBuilder.DropTable(
                name: "LockLeases",
                schema: "ilm");

            migrationBuilder.DropTable(
                name: "ManualTasks",
                schema: "ilm");

            migrationBuilder.DropTable(
                name: "MigrationStates",
                schema: "ilm");

            migrationBuilder.DropTable(
                name: "Persons",
                schema: "ilm");

            migrationBuilder.DropTable(
                name: "ReconciliationFindings",
                schema: "ilm");

            migrationBuilder.DropTable(
                name: "ReconciliationRuns",
                schema: "ilm");

            migrationBuilder.DropTable(
                name: "LeaverRequests",
                schema: "ilm");
        }
    }
}
