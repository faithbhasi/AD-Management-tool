namespace Ilm.Web.Infrastructure;

/// <summary>Strict security headers. The UI uses no inline script, so script-src stays 'self'.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, Authentication.IlmAuthenticationOptions auth)
{
    private readonly string formAction = BuildFormAction(auth);

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var headers = context.Response.Headers;
        headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; object-src 'none'; " +
            $"base-uri 'none'; frame-ancestors 'none'; form-action {formAction}";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cache-Control"] = "no-store";
        return next(context);
    }

    private static string BuildFormAction(Authentication.IlmAuthenticationOptions auth)
    {
        // Sign-out posts back to ILM, which then redirects to the IdP's end-session endpoint.
        return Uri.TryCreate(auth.Issuer, UriKind.Absolute, out var issuer)
            ? $"'self' {issuer.GetLeftPart(UriPartial.Authority)}"
            : "'self'";
    }
}
