using System.Security.Claims;
using Ilm.Application.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Ilm.Web.Authentication;

/// <summary>
/// Okta OIDC: authorization code + PKCE, confidential client, exact issuer/audience/signature validation,
/// state and nonce validation, secure cookies, exact redirect URI allowlist. No password form exists.
/// </summary>
public static class OidcSetup
{
    public static IServiceCollection AddIlmAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection(IlmAuthenticationOptions.SectionName).Get<IlmAuthenticationOptions>() ?? new IlmAuthenticationOptions();
        services.AddSingleton(options);
        MockOidcProvider? mock = null;
        if (options.Mode == AuthenticationMode.MockOidc)
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException("The mock OIDC provider can only be used in the Development or Testing environment.");
            }

            mock = new MockOidcProvider(options);
            services.AddSingleton(mock);
        }

        services.AddAuthentication(o =>
            {
                o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(o =>
            {
                o.Cookie.Name = "__Host-ilm";
                o.Cookie.HttpOnly = true;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.Path = "/";
                o.ExpireTimeSpan = TimeSpan.FromMinutes(options.SessionMinutes);
                o.SlidingExpiration = true;
                o.LoginPath = "/Account/SignIn";
                o.AccessDeniedPath = "/Account/AccessDenied";
            })
            .AddOpenIdConnect(o =>
            {
                o.Authority = options.Issuer;
                o.ClientId = options.ClientId;
                o.ClientSecret = mock?.ClientSecret ?? Environment.GetEnvironmentVariable(options.ClientSecretEnvironmentVariable);
                o.ResponseType = OpenIdConnectResponseType.Code;
                o.ResponseMode = OpenIdConnectResponseMode.Query;
                o.UsePkce = true;
                o.SaveTokens = false;
                o.GetClaimsFromUserInfoEndpoint = false;
                o.MapInboundClaims = false;
                o.CallbackPath = options.CallbackPath;
                o.SignedOutCallbackPath = options.SignedOutCallbackPath;
                o.SignedOutRedirectUri = "/";
                o.Scope.Clear();
                o.Scope.Add("openid");
                o.Scope.Add("profile");
                o.Scope.Add("email");
                o.RequireHttpsMetadata = true;
                o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                o.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.ClientId,
                    ValidateLifetime = true,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromMinutes(2),
                    NameClaimType = IlmClaimTypes.Name,
                };

                if (mock is not null)
                {
                    o.Configuration = mock.CreateConfiguration();
                    o.BackchannelHttpHandler = new MockOidcBackchannelHandler(mock);
                }

                o.Events = new OpenIdConnectEvents
                {
                    OnRedirectToIdentityProvider = context =>
                    {
                        EnsureAllowedRedirect(options, options.RedirectUri);
                        context.ProtocolMessage.RedirectUri = options.RedirectUri;
                        return Task.CompletedTask;
                    },
                    OnRedirectToIdentityProviderForSignOut = context =>
                    {
                        context.ProtocolMessage.PostLogoutRedirectUri = options.PostLogoutRedirectUri;
                        return Task.CompletedTask;
                    },
                    OnAuthorizationCodeReceived = context =>
                    {
                        if (context.TokenEndpointRequest is not null)
                        {
                            context.TokenEndpointRequest.RedirectUri = options.RedirectUri;
                        }

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        var token = context.SecurityToken ?? throw new SecurityTokenException("No validated token.");
                        if (!string.Equals(token.Issuer, options.Issuer, StringComparison.Ordinal))
                        {
                            throw new SecurityTokenInvalidIssuerException("Issuer mismatch.");
                        }

                        string subject = token.Subject ?? throw new SecurityTokenException("The token has no subject.");
                        string name = token.Payload.TryGetValue("name", out var n) && n is string ns ? ns : subject;
                        string? email = token.Payload.TryGetValue("email", out var e) && e is string es ? es : null;
                        var users = context.HttpContext.RequestServices.GetRequiredService<AppUserService>();
                        var user = await users.SignInAsync(token.Issuer, subject, name, email, context.HttpContext.RequestAborted);

                        // Keep only the stable identity. Group claims and everything else from the token are discarded.
                        var identity = new ClaimsIdentity(
                            [
                                new Claim(IlmClaimTypes.Subject, subject),
                                new Claim(IlmClaimTypes.Issuer, token.Issuer),
                                new Claim(IlmClaimTypes.Name, user.DisplayName),
                                new Claim(IlmClaimTypes.AppUserId, user.Id.ToString("D")),
                            ],
                            CookieAuthenticationDefaults.AuthenticationScheme,
                            IlmClaimTypes.Name,
                            IlmClaimTypes.Role);
                        if (email is not null)
                        {
                            identity.AddClaim(new Claim(IlmClaimTypes.Email, email));
                        }

                        context.Principal = new ClaimsPrincipal(identity);
                    },
                    OnRemoteFailure = context =>
                    {
                        context.HandleResponse();
                        context.Response.Redirect("/Account/SignInFailed");
                        return Task.CompletedTask;
                    },
                };
            });

        services.AddScoped<IClaimsTransformation, IlmClaimsTransformation>();
        services.AddScoped<CurrentActorAccessor>();
        return services;
    }

    public static void EnsureAllowedRedirect(IlmAuthenticationOptions options, string redirectUri)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.AllowedRedirectUris.Contains(redirectUri, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The redirect URI is not in the exact allowlist.");
        }
    }
}
