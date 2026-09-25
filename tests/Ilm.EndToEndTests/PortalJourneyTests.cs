using System.Net;
using System.Text.RegularExpressions;
using Ilm.Application.Abstractions;
using Ilm.Domain.Leaver;
using Ilm.Infrastructure.Development;
using Ilm.Infrastructure.Directory.Mock;
using Ilm.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ilm.EndToEndTests;

/// <summary>End-to-end journeys through HTTP, the OIDC flow, Razor Pages, services, the database and the mocks.</summary>
public sealed class PortalJourneyTests : IDisposable
{
    private readonly IlmWebFactory factory = new();

    public void Dispose() => factory.Dispose();

    private static string Match(string html, string pattern) =>
        Regex.Match(html, pattern, RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value;

    private async Task<string> PersonId(HttpClient client, string name)
    {
        var html = await (await client.GetAsync("/People?q=" + Uri.EscapeDataString(name))).Content.ReadAsStringAsync();
        return Match(html, "/People/Details/([0-9a-f-]{36})");
    }

    private async Task<LeaverState> StateOf(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IIlmDbContext>().LeaverRequests.SingleAsync(r => r.Id == id)).State;
    }

    private async Task<Guid> RequestLeaver(HttpClient operatorClient, string person, string ticket)
    {
        var id = await PersonId(operatorClient, person);
        var response = await IlmWebFactory.PostFormAsync(operatorClient, $"/Leavers/New?personId={id}", $"/Leavers/New?personId={id}", new()
        {
            ["Reason"] = "End-to-end test leaver", ["TicketReference"] = ticket, ["Urgency"] = "Urgent", ["IdempotencyKey"] = Guid.NewGuid().ToString("N"),
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Guid.Parse(Match(response.Headers.Location!.ToString(), "/Leavers/Details/([0-9a-f-]{36})"));
    }

    [Fact]
    public async Task Okta_mastered_leaver_goes_from_request_to_safely_contained_over_http()
    {
        var olivia = await factory.SignInAsync(FictionalIds.OperatorOlivia);
        var adrian = await factory.SignInAsync(FictionalIds.ApproverAdrian);
        var id = await RequestLeaver(olivia, "Alex Example", "CHG-40001");
        Assert.Equal(LeaverState.AwaitingApproval, await StateOf(id));

        await IlmWebFactory.PostFormAsync(adrian, $"/Leavers/Details/{id}", $"/Leavers/Details/{id}?handler=Approve", new() { ["comment"] = "approved" });
        Assert.Equal(LeaverState.Approved, await StateOf(id));

        await IlmWebFactory.PostFormAsync(olivia, $"/Leavers/Details/{id}", $"/Leavers/Details/{id}?handler=Start", new());
        Assert.Equal(LeaverState.SafelyContained, await StateOf(id));

        var page = await (await olivia.GetAsync($"/Leavers/Details/{id}")).Content.ReadAsStringAsync();
        Assert.Contains("SafelyContained", page, StringComparison.Ordinal);
        Assert.Contains("VerifyOnly", page, StringComparison.Ordinal);

        var audrey = await factory.SignInAsync(FictionalIds.AuditorAudrey);
        var audit = await (await audrey.GetAsync($"/Audit?OperationId={id}")).Content.ReadAsStringAsync();
        Assert.Contains("ContainmentActionExecuted", audit, StringComparison.Ordinal);
        Assert.Contains("LeaverStateChanged", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manual_containment_is_completed_through_a_task_and_verified_by_observation()
    {
        var olivia = await factory.SignInAsync(FictionalIds.OperatorOlivia);
        var adrian = await factory.SignInAsync(FictionalIds.ApproverAdrian);
        var id = await RequestLeaver(olivia, "Dana Fictional", "CHG-40002");
        await IlmWebFactory.PostFormAsync(adrian, $"/Leavers/Details/{id}", $"/Leavers/Details/{id}?handler=Approve", new());
        await IlmWebFactory.PostFormAsync(olivia, $"/Leavers/Details/{id}", $"/Leavers/Details/{id}?handler=Start", new());
        Assert.Equal(LeaverState.ManualContainmentRequired, await StateOf(id));

        var details = await (await olivia.GetAsync($"/Leavers/Details/{id}")).Content.ReadAsStringAsync();
        var taskId = Match(details, "/Tasks/Details/([0-9a-f-]{36})\">Contain");
        Assert.False(string.IsNullOrEmpty(taskId));
        var taskPage = await (await olivia.GetAsync($"/Tasks/Details/{taskId}")).Content.ReadAsStringAsync();
        Assert.Contains("Manual containment: Dana Fictional", taskPage, StringComparison.Ordinal);
        Assert.Contains("Do not", taskPage, StringComparison.Ordinal);

        // Recording completion without actually disabling does not close containment.
        await IlmWebFactory.PostFormAsync(olivia, $"/Tasks/Details/{taskId}", $"/Tasks/Details/{taskId}?handler=Complete", new() { ["evidence"] = "CHG-40002 claimed" });
        Assert.Equal(LeaverState.ManualContainmentRequired, await StateOf(id));

        var store = factory.Services.GetRequiredService<InMemoryDirectoryStore>();
        foreach (var sam in new[] { "dana.fictional", "adm-dana.fictional" })
        {
            store.Forest("corp").Objects[FictionalIds.For($"corp:user:{sam}")].UserAccountControl |= 2;
        }

        await IlmWebFactory.PostFormAsync(olivia, $"/Leavers/Details/{id}", $"/Leavers/Details/{id}?handler=Reverify", new());
        Assert.Equal(LeaverState.SafelyContained, await StateOf(id));
    }

    [Theory]
    [InlineData(FictionalIds.OperatorOlivia, new[] { "/", "/People", "/Users?q=a", "/Computers?q=WS", "/Directory", "/Leavers", "/Approvals", "/Tasks", "/Alerts", "/Reconciliation", "/Account/Me" })]
    [InlineData(FictionalIds.AuditorAudrey, new[] { "/Audit", "/Admin", "/Admin/Health", "/Approvals" })]
    [InlineData(FictionalIds.ConfigCasey, new[] { "/Admin", "/Admin/Configuration", "/Admin/Flags", "/Admin/Protection", "/Admin/Feasibility", "/Admin/Health" })]
    [InlineData(FictionalIds.SecuritySasha, new[] { "/Audit", "/Admin/Configuration", "/People", "/Approvals" })]
    public async Task Pages_render_for_their_roles(string subject, string[] urls)
    {
        var client = await factory.SignInAsync(subject);
        foreach (var url in urls)
        {
            var response = await client.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url} returned {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Detail_pages_render_with_protection_and_authority()
    {
        var olivia = await factory.SignInAsync(FictionalIds.OperatorOlivia);
        var person = await (await olivia.GetAsync($"/People/Details/{await PersonId(olivia, "Harper Synthetic")}")).Content.ReadAsStringAsync();
        Assert.Contains("Provisional", person, StringComparison.Ordinal);
        Assert.Contains("AccountEnabledState", person, StringComparison.Ordinal);

        var user = await (await olivia.GetAsync($"/Users/Details?connector=corp&id={FictionalIds.For("corp:user:lee.nested")}")).Content.ReadAsStringAsync();
        Assert.Contains("Protected", user, StringComparison.Ordinal);
        Assert.Contains("Tier0Group", user, StringComparison.Ordinal);

        var computer = await (await olivia.GetAsync($"/Computers/Details?connector=corp&id={FictionalIds.For("corp:computer:WS-0002")}")).Content.ReadAsStringAsync();
        Assert.Contains("more than 90 days", computer, StringComparison.Ordinal);
        Assert.Contains("ComputerDisableAndMove", computer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Feasibility_mock_run_through_the_ui_is_no_go()
    {
        var casey = await factory.SignInAsync(FictionalIds.ConfigCasey);
        var run = await IlmWebFactory.PostFormAsync(casey, "/Admin/Feasibility", "/Admin/Feasibility?handler=RunMock", new());
        Assert.Equal(HttpStatusCode.Redirect, run.StatusCode);
        var page = await (await casey.GetAsync(run.Headers.Location)).Content.ReadAsStringAsync();
        Assert.Contains("No-Go", page, StringComparison.Ordinal);
        Assert.Contains("Mock run", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reconciliation_runs_and_audit_verifies_and_exports()
    {
        var olivia = await factory.SignInAsync(FictionalIds.OperatorOlivia);
        await IlmWebFactory.PostFormAsync(olivia, "/Reconciliation", "/Reconciliation?handler=Run", new());
        var recon = await (await olivia.GetAsync("/Reconciliation")).Content.ReadAsStringAsync();
        Assert.Contains("manual", recon, StringComparison.Ordinal);

        var audrey = await factory.SignInAsync(FictionalIds.AuditorAudrey);
        var verify = await IlmWebFactory.PostFormAsync(audrey, "/Audit", "/Audit?handler=Verify", new());
        Assert.Contains("Chain valid", await verify.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var csv = await audrey.GetAsync("/Audit?handler=Export");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Sign_out_clears_the_session()
    {
        var olivia = await factory.SignInAsync(FictionalIds.OperatorOlivia);
        var response = await IlmWebFactory.PostFormAsync(olivia, "/", "/Account/SignOut", new());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var after = await olivia.GetAsync("/People");
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
    }
}
