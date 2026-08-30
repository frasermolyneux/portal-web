using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Authentication;

internal static class TestPrincipalProfiles
{
    /// <summary>
    /// Well-known game server id used by credential coverage scenarios. Direct per-server credential
    /// grants (see <see cref="CredentialFileTransportReader"/> and <see cref="CredentialRconReader"/>)
    /// are keyed to this id, so the backing scenario must expose a server with the same id.
    /// </summary>
    public const string CredentialServerId = "cccccccc-cccc-cccc-cccc-cccccccccccc";

    /// <summary>
    /// Well-known game server id used by map rotation deployment scenarios. The
    /// <see cref="MapRotationDeployer"/> profile has a <c>MapRotations.Deploy</c> grant scoped
    /// to this exact server, so the backing scenario must expose a server with the same id.
    /// </summary>
    public const string MapRotationDeployServerId = "dddddddd-dddd-dddd-dddd-dddddddddddd";

    public const string GameAdmin = "game-admin";
    public const string GameServerWriterWithoutRcon = "game-server-writer-without-rcon";
    public const string HeadAdmin = "head-admin";
    public const string HeadAdminCod5 = "head-admin-cod5";
    public const string Cod4xLifecycleManager = "cod4x-lifecycle-manager";
    public const string MapRotationDeployer = "map-rotation-deployer";
    public const string CredentialFileTransportReader = "credential-file-transport-reader";
    public const string CredentialRconReader = "credential-rcon-reader";
    public const string LiveServerMap = "live-server-map";
    public const string LiveServerBan = "live-server-ban";
    public const string LiveServerKick = "live-server-kick";
    public const string LiveServerKickWithoutAdminActions = "live-server-kick-without-admin-actions";
    public const string LiveServerRestart = "live-server-restart";
    public const string LiveServerSay = "live-server-say";
    public const string Moderator = "moderator";
    public const string SeniorAdmin = "senior-admin";

    public static ClaimsPrincipal Create(string profile)
    {
        var claims = CreateIdentityClaims();

        if (profile == Cod4xLifecycleManager)
        {
            claims.Add(new Claim(AdditionalPermission.GameServers_Admin_Read, GameType.CallOfDuty4x.ToString()));
            claims.Add(new Claim(AdditionalPermission.GameServers_Admin_CoD4xPluginLifecycle, GameType.CallOfDuty4x.ToString()));
            claims.Add(new Claim(AdditionalPermission.ChatLog_ReadServer, GameType.CallOfDuty4x.ToString()));
        }
        else if (profile == MapRotationDeployer)
        {
            // COD5 HeadAdmin with direct COD4 map rotation permissions and server-scoped deploy grant.
            // Exercises the COD4/COD4x equivalence path: the permitted server is COD4x but the
            // rotation is COD4 — the equivalence logic must include the server.
            claims.Add(new Claim(UserProfileClaimType.HeadAdmin, GameType.CallOfDuty5.ToString()));
            claims.Add(new Claim(AuthPolicies.MapRotations_Read, GameType.CallOfDuty4.ToString()));
            claims.Add(new Claim(AuthPolicies.MapRotations_Write, GameType.CallOfDuty4.ToString()));
            claims.Add(new Claim(AuthPolicies.MapRotations_Deploy, MapRotationDeployServerId));
        }
        else if (profile == CredentialFileTransportReader)
        {
            // A Moderator (no credential access by role) plus a direct per-server file transport grant.
            // Exercises the "direct assignment" path: the server appears with file transport columns only.
            claims.Add(new Claim(UserProfileClaimType.Moderator, GameType.CallOfDuty4.ToString()));
            claims.Add(new Claim(AuthPolicies.GameServers_Credentials_FileTransport_Read, CredentialServerId));
        }
        else if (profile == CredentialRconReader)
        {
            // A Moderator (no credential access by role) plus a direct per-server RCON grant.
            // Exercises the "direct assignment" path: the server appears with the RCON column only.
            claims.Add(new Claim(UserProfileClaimType.Moderator, GameType.CallOfDuty4.ToString()));
            claims.Add(new Claim(AdditionalPermission.GameServers_Credentials_Rcon_Read, CredentialServerId));
        }
        else if (profile == GameServerWriterWithoutRcon)
        {
            claims.Add(new Claim(AdditionalPermission.GameServers_Read, GameType.CallOfDuty4.ToString()));
            claims.Add(new Claim(AdditionalPermission.GameServers_Write, GameType.CallOfDuty4.ToString()));
        }
        else if (profile is LiveServerBan or LiveServerKick or LiveServerKickWithoutAdminActions or LiveServerMap or LiveServerRestart or LiveServerSay)
        {
            claims.Add(new Claim(AdditionalPermission.GameServers_Admin_Read, GameType.CallOfDuty4.ToString()));
            claims.Add(new Claim(AdditionalPermission.GameServers_Admin_Rcon, GameType.CallOfDuty4.ToString()));
            claims.Add(new Claim(AdditionalPermission.ChatLog_ReadServer, GameType.CallOfDuty4.ToString()));

            if (profile is LiveServerBan or LiveServerKick)
                claims.Add(new Claim(AdditionalPermission.AdminActions_Create, GameType.CallOfDuty4.ToString()));

            claims.Add(new Claim(profile switch
            {
                LiveServerBan => AdditionalPermission.GameServers_Admin_Rcon_Ban,
                LiveServerKick => AdditionalPermission.GameServers_Admin_Rcon_Kick,
                LiveServerKickWithoutAdminActions => AdditionalPermission.GameServers_Admin_Rcon_Kick,
                LiveServerMap => AdditionalPermission.GameServers_Admin_Rcon_Map,
                LiveServerRestart => AdditionalPermission.GameServers_Admin_Rcon_Restart,
                LiveServerSay => AdditionalPermission.GameServers_Admin_Rcon_Say,
                _ => throw new InvalidOperationException($"Unsupported live server profile '{profile}'."),
            }, GameType.CallOfDuty4.ToString()));
        }
        else
        {
            claims.Add(profile switch
            {
                GameAdmin => new Claim(UserProfileClaimType.GameAdmin, GameType.CallOfDuty4.ToString()),
                HeadAdmin => new Claim(UserProfileClaimType.HeadAdmin, GameType.CallOfDuty4.ToString()),
                HeadAdminCod5 => new Claim(UserProfileClaimType.HeadAdmin, GameType.CallOfDuty5.ToString()),
                Moderator => new Claim(UserProfileClaimType.Moderator, GameType.CallOfDuty4.ToString()),
                SeniorAdmin => new Claim(UserProfileClaimType.SeniorAdmin, bool.TrueString),
                _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown test principal profile."),
            });
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme));
    }

    private static List<Claim> CreateIdentityClaims()
    {
        return
        [
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Name, "Portal Test User"),
            new Claim(ClaimTypes.Email, "portal-test@example.invalid"),
            new Claim(UserProfileClaimType.XtremeIdiotsId, "12345"),
            new Claim(UserProfileClaimType.UserProfileId, "11111111-1111-1111-1111-111111111111"),
            new Claim(UserProfileClaimType.PhotoUrl, "/images/noimage.jpg"),
        ];
    }
}
