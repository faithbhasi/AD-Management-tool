using Ilm.Application.Directory;
using Ilm.Domain.Common;

namespace Ilm.Application.Provisioning;

/// <summary>Input for the (initially disabled) create saga.</summary>
public sealed record JoinerRequest(
    string ConnectorId,
    string TemplateId,
    AccountType AccountType,
    NewUserRequest User,
    IReadOnlyList<Guid> MandatoryGroupGuids);
