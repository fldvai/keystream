using System;

namespace Keystream.App.Views;

/// <summary>
/// §130 - pure decision logic for the Reset Data confirmation, deliberately
/// WPF-FREE: strings only, no WPF namespace reference of any kind, so this
/// file can be linked directly into the plain net8.0 wb-harness (same
/// discipline as TextBoxRevertLogic.cs). The WPF half (ResetConfirmWindow:
/// TextBox + OK button) calls into here on every TextChanged - including
/// paste, which fires TextChanged - and re-checks in OnConfirm, never
/// trusting the button's enabled state alone.
/// The match is EXACT: ordinal comparison against "DELETE", no trim, no
/// case or spacing tolerance. "delete", "DELETE ", " DELETE" and
/// "DELETE\n" are all rejected.
/// </summary>
public static class ResetConfirmLogic
{
    /// <summary>True only when the input is exactly "DELETE".</summary>
    public static bool IsConfirmed(string? text)
        => string.Equals(text, "DELETE", StringComparison.Ordinal);
}
