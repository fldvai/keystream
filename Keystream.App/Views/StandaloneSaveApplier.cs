using System;
using System.Collections.Generic;
using System.Linq;
using Keystream.Core.Config;

namespace Keystream.App.Views;

/// <summary>§119-fix (F-C2/F-C3) - the standalone panel's Stop-time
/// save decision, factored out of
/// <see cref="StandalonePanelController"/> so the two rules are
/// provable without WPF: (1) a macro deleted during the session
/// must NEVER be resurrected - <see cref="BuildCopy"/> returns null
/// and the caller discards the staged steps; (2) a failed save must
/// roll the library back AND report failure - <see cref="TrySave"/>
/// returns false and the caller RETAINS the working steps for a
/// retry, instead of nulling them before the save. WPF-FREE: only
/// <see cref="MacroConfig"/>/<see cref="ActionStep"/> data and a
/// save delegate, no Dispatcher, no hooks. There is deliberately
/// NO balance/cap check here: old macros may be unbalanced and
/// there is no step limit (§119 decision, final) - Validate is
/// not consulted and no step is ever refused by this helper.</summary>
public static class StandaloneSaveApplier
{
    /// <summary>Builds the macro to save from the working list. The
    /// saved entry is re-resolved by the caller at Stop time and
    /// passed in as <paramref name="saved"/>: null means the macro
    /// was deleted during the session, so there is nothing to save
    /// INTO - returns null and the caller must discard the staged
    /// steps (never create a fresh entry from the id/name, which
    /// is exactly the resurrection F-C2 describes). Otherwise
    /// returns a copy of the saved entry whose steps ARE the
    /// working list (same list reference, as the controller's
    /// original inline code did - the caller clears its own field
    /// only after a successful save).</summary>
    public static MacroConfig? BuildCopy(
        MacroConfig? saved,
        List<ActionStep> working)
    {
        if (saved is null) return null;
        var copy = saved.Copy();
        copy.Steps = working;
        return copy;
    }

    /// <summary>Replaces/adds <paramref name="copy"/> in
    /// <paramref name="macros"/> and calls <paramref name="save"/>.
    /// True only when the save succeeded. On a save failure the
    /// library is restored from a deep backup FIRST (the modal's
    /// rollback pattern) and false is returned with the save
    /// error - the caller must then RETAIN its working steps for
    /// the next attempt; the library no longer references them
    /// (the backup copies replaced the list contents), so
    /// retaining is safe. Never throws for a save failure: the
    /// save delegate's exception is captured in
    /// <paramref name="error"/>.</summary>
    public static bool TrySave(
        List<MacroConfig> macros,
        MacroConfig copy,
        Action save,
        out string? error)
    {
        var backup = macros.Select(m => m.Copy()).ToList();
        int idx = macros.FindIndex(m => m.Id == copy.Id);
        if (idx >= 0) macros[idx] = copy;
        else macros.Add(copy);
        try
        {
            save();
        }
        catch (Exception ex)
        {
            macros.Clear();
            macros.AddRange(backup);
            error = ex.Message;
            return false;
        }
        error = null;
        return true;
    }
}
