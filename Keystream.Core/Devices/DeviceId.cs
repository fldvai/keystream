namespace Keystream.Core.Devices;

/// <summary>Which identity namespace a <see cref="DeviceId"/> belongs to.
/// The audited codebase mixed these namespaces (the wizard enumerated Raw
/// Input paths while Mode A matched driver hardware ids), which made a saved
/// deck identity silently meaningless in the other mode. Kinds are therefore
/// part of the value itself and are NEVER compared across kinds.</summary>
public enum DeviceIdKind
{
    /// <summary>Hardware id string reported by the Interception driver
    /// (e.g. "HID\VID_413C&amp;PID_2003\7&amp;..."). The product's primary
    /// identity namespace since Gate 0 chose Mode A.</summary>
    InterceptionHardwareId,

    /// <summary>Raw Input device path (e.g. "\\?\HID#VID_...#...#{...}").
    /// Used for enumeration/display in the UI, NOT for blocking decisions.</summary>
    RawInputDevicePath,

    /// <summary>An identity string migrated from the legacy config whose
    /// namespace cannot be proven (the legacy wizard wrote Raw Input paths
    /// while parts of the app treated the value as a Mode A id). Such an id
    /// must never drive blocking; the user must re-identify the deck.</summary>
    LegacyUnverified,
}

/// <summary>
/// K2 - structured device identity. One namespace, exact-match semantics.
/// Normalization (trim + trailing-NUL strip) happens at construction;
/// comparison is same-kind + ordinal-ignore-case on the normalized value.
/// An empty value is invalid and never equals anything, including another
/// empty value. There is deliberately NO fuzzy/VID-PID fallback for
/// decisions: audit finding B1-1 showed a same-model main keyboard being
/// swallowed by exactly such a fallback.
///
/// Known Mode A caveat: an Interception hardware id may be IDENTICAL for
/// two physical keyboards of the same model (the instance segment is not
/// guaranteed unique). Runtime code that must distinguish identical twins
/// additionally needs the session-scoped device index; that pairing is a
/// Phase 2 runtime concern, recorded here so the limitation is explicit.
/// </summary>
public readonly record struct DeviceId
{
    public DeviceIdKind Kind { get; }
    public string Value { get; }

    private DeviceId(DeviceIdKind kind, string value)
    {
        Kind = kind;
        Value = Normalize(value);
    }

    public static string Normalize(string? value)
        => (value ?? "").Trim().TrimEnd('\0').Trim();

    public static DeviceId FromInterceptionHardwareId(string value) => new(DeviceIdKind.InterceptionHardwareId, value);
    public static DeviceId FromRawInputPath(string value) => new(DeviceIdKind.RawInputDevicePath, value);
    public static DeviceId FromLegacyUnverified(string value) => new(DeviceIdKind.LegacyUnverified, value);

    // A-02: default(DeviceId) punya Value null — IsValid harus tahan itu.
    public bool IsValid => Value is not null && Value.Length > 0;

    public bool Matches(DeviceId other)
        => IsValid && other.IsValid
           && Kind == other.Kind
           && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => IsValid ? $"{Kind}:{Value}" : $"{Kind}:(invalid)";
}
