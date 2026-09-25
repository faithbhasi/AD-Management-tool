using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ilm.Application.Okta;
using Ilm.Domain.Common;
using Microsoft.Extensions.Options;

namespace Ilm.Infrastructure.Okta.Http;

/// <summary>
/// Okta management API client using supported endpoints only, authenticated with a scoped OAuth 2.0
/// service-app token (never SSWS). IDs are validated before they reach a URL path.
/// UNVERIFIED against a real Okta org in this repository.
/// </summary>
public sealed partial class OktaApiClient(IHttpClientFactory httpClients, IOktaAccessTokenProvider tokens, IOptions<OktaOptions> options) :
    IOktaUserClient, IOktaLifecycleClient, IOktaSessionClient, IOktaGroupClient, IOktaApplicationClient, IOktaSystemLogClient, IOktaFeasibilityCleanup
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<OktaResult<OktaUser>> CreateUserAsync(OktaCreateUserRequest request, bool activate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new
        {
            profile = new
            {
                login = request.Login,
                email = request.Email,
                firstName = request.FirstName,
                lastName = request.LastName,
                department = request.Department,
                managerId = request.ManagerId,
                employeeNumber = request.EmployeeNumber,
            },
        };
        return await SendAsync<UserDto, OktaUser>(HttpMethod.Post, $"/api/v1/users?activate={(activate ? "true" : "false")}", body, Map, cancellationToken);
    }

    public async Task<OktaResult<IReadOnlyList<OktaUser>>> SearchUsersAsync(OktaUserSearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        var clauses = new List<string>();
        if (search.Login is not null)
        {
            clauses.Add($"profile.login eq \"{SafeSearchValue(search.Login)}\"");
        }

        if (search.Email is not null)
        {
            clauses.Add($"profile.email eq \"{SafeSearchValue(search.Email)}\"");
        }

        if (search.EmployeeNumber is not null)
        {
            clauses.Add($"profile.employeeNumber eq \"{SafeSearchValue(search.EmployeeNumber)}\"");
        }

        if (clauses.Count == 0)
        {
            return OktaResult<IReadOnlyList<OktaUser>>.Fail(400, SafeErrorCategory.ValidationFailed);
        }

        var query = Uri.EscapeDataString(string.Join(" or ", clauses));
        return await SendAsync<List<UserDto>, IReadOnlyList<OktaUser>>(HttpMethod.Get, $"/api/v1/users?search={query}", null, list => list.Select(Map).ToList(), cancellationToken);
    }

    public Task<OktaResult<OktaUser>> GetUserAsync(string userId, CancellationToken cancellationToken) =>
        SendAsync<UserDto, OktaUser>(HttpMethod.Get, $"/api/v1/users/{Id(userId)}", null, Map, cancellationToken);

    public Task<OktaResult<OktaUser>> UpdateProfileAsync(string userId, IReadOnlyDictionary<string, string> profile, CancellationToken cancellationToken) =>
        SendAsync<UserDto, OktaUser>(HttpMethod.Post, $"/api/v1/users/{Id(userId)}", new { profile }, Map, cancellationToken);

    public Task<OktaResult<IReadOnlyList<string>>> GetFactorTypesAsync(string userId, CancellationToken cancellationToken) =>
        SendAsync<List<FactorDto>, IReadOnlyList<string>>(HttpMethod.Get, $"/api/v1/users/{Id(userId)}/factors", null, f => f.Select(x => $"{x.FactorType}:{x.Provider}").ToList(), cancellationToken);

    public Task<OktaResult<IReadOnlyList<string>>> GetAssignedApplicationIdsAsync(string userId, CancellationToken cancellationToken) =>
        SendAsync<List<AppLinkDto>, IReadOnlyList<string>>(HttpMethod.Get, $"/api/v1/users/{Id(userId)}/appLinks", null, a => a.Select(x => x.AppInstanceId).Distinct(StringComparer.Ordinal).ToList(), cancellationToken);

    public Task<OktaResult> ActivateAsync(string userId, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, $"/api/v1/users/{Id(userId)}/lifecycle/activate?sendEmail=false", cancellationToken);

    public Task<OktaResult> SuspendAsync(string userId, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, $"/api/v1/users/{Id(userId)}/lifecycle/suspend", cancellationToken);

    public Task<OktaResult> UnsuspendAsync(string userId, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, $"/api/v1/users/{Id(userId)}/lifecycle/unsuspend", cancellationToken);

    public Task<OktaResult> DeactivateAsync(string userId, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, $"/api/v1/users/{Id(userId)}/lifecycle/deactivate?sendEmail=false", cancellationToken);

    public async Task<OktaResult<string>> GetLifecycleStateAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(userId, cancellationToken);
        return user.Succeeded ? OktaResult<string>.Ok(user.Value!.Status) : OktaResult<string>.Fail(user.StatusCode, user.ErrorCategory, user.OktaErrorCode);
    }

    public Task<OktaResult> RevokeSessionsAsync(string userId, bool revokeOAuthTokens, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, $"/api/v1/users/{Id(userId)}/sessions?oauthTokens={(revokeOAuthTokens ? "true" : "false")}", cancellationToken);

    public Task<OktaResult> AssignUserToGroupAsync(string groupId, string userId, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Put, $"/api/v1/groups/{Id(groupId)}/users/{Id(userId)}", cancellationToken);

    public Task<OktaResult> RemoveUserFromGroupAsync(string groupId, string userId, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, $"/api/v1/groups/{Id(groupId)}/users/{Id(userId)}", cancellationToken);

    public Task<OktaResult<IReadOnlyList<string>>> GetUserGroupIdsAsync(string userId, CancellationToken cancellationToken) =>
        SendAsync<List<IdDto>, IReadOnlyList<string>>(HttpMethod.Get, $"/api/v1/users/{Id(userId)}/groups", null, g => g.Select(x => x.Id).ToList(), cancellationToken);

    public async Task<OktaResult> AssignUserToApplicationAsync(string appId, string userId, CancellationToken cancellationToken)
    {
        var result = await SendAsync<JsonElement, bool>(HttpMethod.Post, $"/api/v1/apps/{Id(appId)}/users", new { id = Id(userId), scope = "USER" }, _ => true, cancellationToken);
        return result.Succeeded ? OktaResult.Ok(result.StatusCode) : OktaResult.Fail(result.StatusCode, result.ErrorCategory, result.OktaErrorCode);
    }

    public async Task<OktaResult<IReadOnlyList<string>>> GetAssignmentRolesAsync(string appId, string userId, CancellationToken cancellationToken)
    {
        var result = await SendAsync<AppUserDto, IReadOnlyList<string>>(HttpMethod.Get, $"/api/v1/apps/{Id(appId)}/users/{Id(userId)}", null, ExtractRoles, cancellationToken);
        return result.StatusCode == 404 ? OktaResult<IReadOnlyList<string>>.Ok([]) : result;
    }

    public Task<OktaResult<IReadOnlyList<OktaLogEvent>>> QueryAsync(DateTime sinceUtc, string? targetUserId, string? eventTypePrefix, CancellationToken cancellationToken)
    {
        var filters = new List<string>();
        if (targetUserId is not null)
        {
            filters.Add($"target.id eq \"{Id(targetUserId)}\"");
        }

        if (eventTypePrefix is not null)
        {
            filters.Add($"eventType sw \"{SafeSearchValue(eventTypePrefix)}\"");
        }

        var url = $"/api/v1/logs?since={Uri.EscapeDataString(sinceUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))}&limit=200";
        if (filters.Count > 0)
        {
            url += "&filter=" + Uri.EscapeDataString(string.Join(" and ", filters));
        }

        return SendAsync<List<LogDto>, IReadOnlyList<OktaLogEvent>>(HttpMethod.Get, url, null, l => l.Select(Map).ToList(), cancellationToken);
    }

    public async Task<OktaResult> DeleteSyntheticUserAsync(string userId, string requiredLoginPrefix, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(userId, cancellationToken);
        if (!user.Succeeded)
        {
            return OktaResult.Fail(user.StatusCode, user.ErrorCategory, user.OktaErrorCode);
        }

        if (string.IsNullOrWhiteSpace(requiredLoginPrefix) || !user.Value!.Login.StartsWith(requiredLoginPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return OktaResult.Fail(403, SafeErrorCategory.NotAuthorised, "NOT_SYNTHETIC");
        }

        return await SendAsync(HttpMethod.Delete, $"/api/v1/users/{Id(userId)}", cancellationToken);
    }

    internal static string Id(string id) =>
        OktaIdPattern().IsMatch(id) ? id : throw new ArgumentException("Invalid Okta object ID.", nameof(id));

    internal static string SafeSearchValue(string value) =>
        value.IndexOfAny(['"', '\\', '\r', '\n']) >= 0 ? throw new ArgumentException("Search value contains forbidden characters.", nameof(value)) : value;

    internal static SafeErrorCategory Classify(HttpStatusCode status, string? errorCode) => status switch
    {
        HttpStatusCode.BadRequest when errorCode == "E0000001" => SafeErrorCategory.ValidationFailed,
        HttpStatusCode.BadRequest => SafeErrorCategory.ValidationFailed,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => SafeErrorCategory.NotAuthorised,
        HttpStatusCode.NotFound => SafeErrorCategory.NotFound,
        HttpStatusCode.Conflict => SafeErrorCategory.DuplicateObject,
        HttpStatusCode.TooManyRequests => SafeErrorCategory.Timeout,
        >= HttpStatusCode.InternalServerError => SafeErrorCategory.ConnectorUnavailable,
        _ => SafeErrorCategory.ExternalServiceError,
    };

    private async Task<OktaResult> SendAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        var result = await SendAsync<JsonElement, bool>(method, path, null, _ => true, cancellationToken, allowEmpty: true);
        return result.Succeeded ? OktaResult.Ok(result.StatusCode) : OktaResult.Fail(result.StatusCode, result.ErrorCategory, result.OktaErrorCode);
    }

    private async Task<OktaResult<TOut>> SendAsync<TDto, TOut>(HttpMethod method, string path, object? body, Func<TDto, TOut> map, CancellationToken cancellationToken, bool allowEmpty = false)
    {
        var client = httpClients.CreateClient("okta");
        client.BaseAddress ??= new Uri(options.Value.OrgUrl.TrimEnd('/'));
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAccessTokenAsync(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var status = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
        {
            string? code = null;
            try
            {
                code = (await response.Content.ReadFromJsonAsync<ErrorDto>(Json, cancellationToken))?.ErrorCode;
            }
            catch (JsonException)
            {
                code = null;
            }

            return OktaResult<TOut>.Fail(status, Classify(response.StatusCode, code), code);
        }

        if (allowEmpty && (response.Content.Headers.ContentLength is 0 || response.StatusCode == HttpStatusCode.NoContent))
        {
            return OktaResult<TOut>.Ok(map(default!), status);
        }

        var dto = await response.Content.ReadFromJsonAsync<TDto>(Json, cancellationToken);
        return OktaResult<TOut>.Ok(map(dto!), status);
    }

    private IReadOnlyList<string> ExtractRoles(AppUserDto dto)
    {
        if (dto.Profile is not { } profile || !profile.TryGetProperty(options.Value.AppAssignmentRoleAttribute, out var roles))
        {
            return [];
        }

        return roles.ValueKind switch
        {
            JsonValueKind.Array => roles.EnumerateArray().Select(r => r.GetString()).OfType<string>().ToList(),
            JsonValueKind.String => [roles.GetString()!],
            _ => [],
        };
    }

    private static OktaUser Map(UserDto u) => new(
        u.Id,
        u.Status,
        u.Profile?.Login ?? string.Empty,
        u.Profile?.Email,
        u.Profile?.FirstName,
        u.Profile?.LastName,
        u.Profile?.Department,
        u.Profile?.ManagerId,
        u.Created,
        u.StatusChanged);

    private static OktaLogEvent Map(LogDto l) => new(
        l.Uuid,
        l.Published,
        l.EventType,
        l.Outcome?.Result ?? "UNKNOWN",
        l.Actor?.Id,
        l.Actor?.Type,
        l.Target?.FirstOrDefault(t => t.Type == "User")?.Id,
        l.Transaction?.Id,
        l.DisplayMessage);

    [GeneratedRegex("^[A-Za-z0-9]{20}$", RegexOptions.CultureInvariant, 100)]
    private static partial Regex OktaIdPattern();

    private sealed record UserDto(string Id, string Status, DateTime? Created, DateTime? StatusChanged, ProfileDto? Profile);

    private sealed record ProfileDto(string? Login, string? Email, string? FirstName, string? LastName, string? Department, string? ManagerId);

    private sealed record IdDto(string Id);

    private sealed record FactorDto(string FactorType, string Provider);

    private sealed record AppLinkDto(string AppInstanceId);

    private sealed record AppUserDto(string Id, JsonElement? Profile);

    private sealed record ErrorDto(string? ErrorCode, string? ErrorSummary);

    private sealed record LogDto(string Uuid, DateTime Published, string EventType, string? DisplayMessage, LogOutcome? Outcome, LogActor? Actor, List<LogTarget>? Target, LogTransaction? Transaction);

    private sealed record LogOutcome(string? Result);

    private sealed record LogActor(string? Id, string? Type);

    private sealed record LogTarget(string? Id, string? Type);

    private sealed record LogTransaction(string? Id);
}
