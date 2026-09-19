using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeBrix.Swarm.Queen.Authentication;

/// <summary>
/// Reads the access token off an incoming request and turns it into a role claim. One scheme serves
/// both hubs: the token says which role it is for, and each hub's policy insists on its own.
/// </summary>
/// <remarks>
/// The token arrives either as an <c>Authorization: Bearer</c> header, which is what an ordinary
/// HTTP request carries, or as the <c>access_token</c> query value, which is the only place a socket
/// handshake can put it.
/// </remarks>
internal sealed class SwarmTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private const string BearerPrefix = "Bearer ";
    private const string QueryTokenName = "access_token";

    private static readonly SwarmRole[] KnownRoles = [SwarmRole.Hive, SwarmRole.Worker];

    private readonly SwarmTokenAuthority _authority;

    public SwarmTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        SwarmTokenAuthority authority)
        : base(options, logger, encoder)
    {
        ArgumentNullException.ThrowIfNull(authority);
        _authority = authority;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ReadToken();

        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        foreach (var role in KnownRoles)
        {
            if (!_authority.TryReadToken(role, token, out var read))
            {
                continue;
            }

            var roleName = SwarmRoleNames.For(read.Role);

            var identity = new ClaimsIdentity(
                [
                    new Claim(SwarmAuthorization.RoleClaimType, roleName),
                    new Claim(ClaimTypes.NameIdentifier, read.TokenId.ToString("D")),
                    new Claim(ClaimTypes.Name, roleName)
                ],
                SwarmAuthorization.SchemeName);

            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity), SwarmAuthorization.SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        return Task.FromResult(
            AuthenticateResult.Fail("The access token was not accepted for any swarm role."));
    }

    private string ReadToken()
    {
        if (Request.Headers.TryGetValue("Authorization", out var header))
        {
            var value = header.ToString();

            if (value.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var bearer = value[BearerPrefix.Length..].Trim();

                if (bearer.Length > 0)
                {
                    return bearer;
                }
            }
        }

        return Request.Query[QueryTokenName].ToString();
    }
}
