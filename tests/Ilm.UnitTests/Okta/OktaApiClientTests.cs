using System.Net;
using Ilm.Domain.Common;
using Ilm.Infrastructure.Okta.Http;
using Microsoft.Extensions.Options;

namespace Ilm.UnitTests.Okta;

public sealed class OktaApiClientTests
{
    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeTokens : IOktaAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult("service-app-access-token");
    }

    private static (OktaApiClient Client, FakeHandler Handler) Create(HttpStatusCode status = HttpStatusCode.OK, string body = "{}")
    {
        var handler = new FakeHandler(status, body);
        var client = new OktaApiClient(new FakeFactory(handler), new FakeTokens(), Options.Create(new OktaOptions { OrgUrl = "https://okta.example.test" }));
        return (client, handler);
    }

    [Fact]
    public async Task Uses_oauth_bearer_token_never_ssws()
    {
        var (client, handler) = Create(HttpStatusCode.NoContent, string.Empty);
        await client.RevokeSessionsAsync("00uPersonAlex0000001", revokeOAuthTokens: true, CancellationToken.None);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/api/v1/users/00uPersonAlex0000001/sessions?oauthTokens=true", request.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Lifecycle_endpoints_are_the_supported_api()
    {
        var (client, handler) = Create();
        await client.DeactivateAsync("00uPersonAlex0000001", CancellationToken.None);
        await client.SuspendAsync("00uPersonAlex0000001", CancellationToken.None);
        Assert.Equal("/api/v1/users/00uPersonAlex0000001/lifecycle/deactivate?sendEmail=false", handler.Requests[0].RequestUri!.PathAndQuery);
        Assert.Equal("/api/v1/users/00uPersonAlex0000001/lifecycle/suspend", handler.Requests[1].RequestUri!.PathAndQuery);
    }

    [Theory]
    [InlineData("../admin")]
    [InlineData("00u123")]
    [InlineData("00uPersonAlex0000001/../../x")]
    public async Task Invalid_ids_never_reach_a_url(string id)
    {
        var (client, _) = Create();
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetUserAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task Search_values_with_quotes_are_rejected()
    {
        var (client, _) = Create();
        await Assert.ThrowsAsync<ArgumentException>(() => client.SearchUsersAsync(new("x\" or profile.login sw \"", null, null), CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, SafeErrorCategory.NotFound)]
    [InlineData(HttpStatusCode.Forbidden, SafeErrorCategory.NotAuthorised)]
    [InlineData(HttpStatusCode.TooManyRequests, SafeErrorCategory.Timeout)]
    [InlineData(HttpStatusCode.ServiceUnavailable, SafeErrorCategory.ConnectorUnavailable)]
    public async Task Errors_map_to_safe_categories(HttpStatusCode status, SafeErrorCategory expected)
    {
        var (client, _) = Create(status, "{\"errorCode\":\"E0000007\"}");
        var result = await client.GetUserAsync("00uPersonAlex0000001", CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.ErrorCategory);
    }

    [Fact]
    public void Options_have_no_static_token_or_secret_fields() =>
        Assert.DoesNotContain(typeof(OktaOptions).GetProperties(), p => p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Ssws", StringComparison.OrdinalIgnoreCase));
}
