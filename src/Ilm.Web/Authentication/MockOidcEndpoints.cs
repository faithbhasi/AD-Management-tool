using System.Net;
using System.Text;
using Ilm.Infrastructure.Development;

namespace Ilm.Web.Authentication;

/// <summary>DEVELOPMENT ONLY endpoints for the mock OIDC provider. Mapped only when the mode is MockOidc.</summary>
public static class MockOidcEndpoints
{
    public static void MapMockOidc(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var group = app.MapGroup(MockOidcProvider.PathBase).AllowAnonymous().DisableAntiforgery();

        group.MapGet("/.well-known/openid-configuration", (MockOidcProvider p) => Results.Json(new
        {
            issuer = p.Issuer,
            authorization_endpoint = p.AuthorizationEndpoint,
            token_endpoint = p.TokenEndpoint,
            end_session_endpoint = p.EndSessionEndpoint,
            jwks_uri = p.Issuer + "/jwks",
            response_types_supported = new[] { "code" },
            code_challenge_methods_supported = new[] { "S256" },
            id_token_signing_alg_values_supported = new[] { "RS256" },
        }));

        group.MapGet("/authorize", (HttpContext context, MockOidcProvider p) =>
        {
            var query = context.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.Ordinal);
            var (_, error) = p.Authorize(query, FictionalOkta.Operators[0].Subject);
            if (error is not null && error != "unknown subject")
            {
                return Results.BadRequest(error);
            }

            var hidden = new StringBuilder();
            foreach (var (k, v) in query)
            {
                hidden.Append(CultureInvariant($"<input type=\"hidden\" name=\"{WebUtility.HtmlEncode(k)}\" value=\"{WebUtility.HtmlEncode(v)}\">"));
            }

            var rows = new StringBuilder();
            foreach (var op in FictionalOkta.Operators)
            {
                rows.Append(CultureInvariant($"<tr><td><strong>{WebUtility.HtmlEncode(op.DisplayName)}</strong><br><small>{WebUtility.HtmlEncode(op.Email)}</small></td><td>{WebUtility.HtmlEncode(op.Description)}</td>"));
                rows.Append(CultureInvariant($"<td><form method=\"post\" action=\"{MockOidcProvider.PathBase}/authorize\">{hidden}<input type=\"hidden\" name=\"subject\" value=\"{op.Subject}\"><button type=\"submit\" class=\"btn\">Sign in</button></form></td></tr>"));
            }

            var html = $"""
                <!doctype html><html lang="en"><head><meta charset="utf-8"><title>Mock Okta (development only)</title>
                <link rel="stylesheet" href="/css/site.css"></head><body><main class="container">
                <div class="banner warn">Development mock identity provider. Fictional operators only. Never enabled in production.</div>
                <h1>Choose a fictional operator</h1>
                <p>ILM authorises by immutable Okta group IDs resolved server-side. Group names in the token are ignored.</p>
                <table class="grid"><thead><tr><th>Operator</th><th>Purpose</th><th></th></tr></thead><tbody>{rows}</tbody></table>
                </main></body></html>
                """;
            return Results.Content(html, "text/html; charset=utf-8");
        });

        group.MapPost("/authorize", async (HttpContext context, MockOidcProvider p) =>
        {
            var form = await context.Request.ReadFormAsync();
            var values = form.ToDictionary(f => f.Key, f => f.Value.ToString(), StringComparer.Ordinal);
            var (redirect, error) = p.Authorize(values, values.GetValueOrDefault("subject") ?? string.Empty);
            return error is null ? Results.Redirect(redirect!) : Results.BadRequest(error);
        });

        group.MapPost("/token", async (HttpContext context, MockOidcProvider p) =>
        {
            var form = await context.Request.ReadFormAsync();
            var (json, error) = p.Token(form.ToDictionary(f => f.Key, f => f.Value.ToString(), StringComparer.Ordinal), context.Request.Headers.Authorization.ToString());
            return error is null ? Results.Content(json!, "application/json") : Results.BadRequest(new { error });
        });

        group.MapGet("/logout", (HttpContext context, MockOidcProvider p) =>
        {
            var target = context.Request.Query["post_logout_redirect_uri"].ToString();
            return target == p.Options.PostLogoutRedirectUri ? Results.Redirect(target + "?state=" + Uri.EscapeDataString(context.Request.Query["state"].ToString())) : Results.Redirect("/");
        });
    }

    private static string CultureInvariant(FormattableString value) => FormattableString.Invariant(value);
}
