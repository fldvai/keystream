using System;

namespace Keystream.App.Views;

/// <summary>
/// §110 - pure decision logic for TextBox revert, deliberately
/// WPF-FREE: strings only, no WPF namespace reference of any
/// kind, so this file can be linked directly into the plain
/// net8.0 wb-harness (same discipline as MouseRecordPlanner.cs).
/// The WPF half (attached properties + class handlers) lives in
/// TextBoxRevert.cs and calls into here; do not add WPF types,
/// dependency-object parameters, or resource lookups to this file.
/// </summary>
public static class TextBoxRevertLogic
{
    /// <summary>True iff the current text differs ordinally from the focus-entry snapshot.</summary>
    public static bool NeedsEscapeRestore(string? currentText, string? snapshot)
        => !string.Equals(currentText, snapshot, StringComparison.Ordinal);

    /// <summary>The text an Esc restore writes back: the snapshot, or "" when there is none.</summary>
    public static string GetEscapeRestoreText(string? snapshot) => snapshot ?? "";
}
