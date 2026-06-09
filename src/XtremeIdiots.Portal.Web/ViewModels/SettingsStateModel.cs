namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// Canonical UI state models used by settings view models.
/// </summary>
public enum SettingsStateModel
{
    /// <summary>
    /// Concrete boolean value used for global defaults.
    /// </summary>
    ConcreteBool,

    /// <summary>
    /// Tri-state override used by server-level settings (inherit, enabled, disabled).
    /// </summary>
    TriStateOverride,

    /// <summary>
    /// Child list rows such as message collections.
    /// </summary>
    ChildList
}

/// <summary>
/// Tri-state override helper where null means inherit global behavior.
/// </summary>
public sealed record TriStateOverrideValue
{
    public bool? Value { get; init; }

    public bool IsInherit => Value is null;

    public bool IsEnabled => Value is true;

    public bool IsDisabled => Value is false;

    public static TriStateOverrideValue Inherit()
    {
        return new TriStateOverrideValue { Value = null };
    }

    public static TriStateOverrideValue Enabled()
    {
        return new TriStateOverrideValue { Value = true };
    }

    public static TriStateOverrideValue Disabled()
    {
        return new TriStateOverrideValue { Value = false };
    }

    public static TriStateOverrideValue From(bool? value)
    {
        return new TriStateOverrideValue { Value = value };
    }
}