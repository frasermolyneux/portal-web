namespace XtremeIdiots.Portal.Web.Auth;

/// <summary>
/// Sentinel resource passed to <c>IAuthorizationService.AuthorizeAsync</c> when asking
/// "can this user potentially perform this action for ANY game type?"
/// <para>
/// Handlers recognise this type via pattern matching and check whether the user holds
/// any game-scoped role or additional permission that could satisfy the policy — without
/// requiring a concrete <c>GameType</c> or server ID.
/// </para>
/// <para>
/// Use this for UI gating (showing/hiding Create buttons on Index pages) and for
/// GET Create/Import actions where no resource exists yet. The real resource-scoped
/// authorization check still happens on POST with the actual resource.
/// </para>
/// <example>
/// Controller usage:
/// <code>
/// var result = await authorizationService.AuthorizeAsync(User, PotentialAccessProbe.Instance, AuthPolicies.MapRotations_Write);
/// if (!result.Succeeded) return Forbid();
/// </code>
/// View usage (tag helper):
/// <code>
/// &lt;a policy="@AuthPolicies.MapRotations_Write" policy-resource="@PotentialAccessProbe.Instance"&gt;Create&lt;/a&gt;
/// </code>
/// </example>
/// </summary>
public sealed class PotentialAccessProbe
{
    public readonly static PotentialAccessProbe Instance = new();

    private PotentialAccessProbe() { }
}
