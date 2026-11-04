using System.Windows;
using Keystream.App.Views;
using Keystream.Core.Config;
using Keystream.Core.Localization;

namespace Keystream.App.Services;

/// <summary>What to do with an imported Global section.</summary>
public enum GlobalImportChoice
{
    /// <summary>Replace the importer's Global bindings with the imported ones.</summary>
    Replace,
    /// <summary>Append only bindings whose key identity is not already present.</summary>
    Merge,
    /// <summary>Leave the importer's Global untouched (the default, also on cancel/close).</summary>
    Skip,
}

/// <summary>Outcome of applying one Global import choice.</summary>
public sealed record GlobalImportOutcome(int Appended, int SkippedCollisions);

/// <summary>§171 Task 6 - the ONE shared prompt + apply path for an
/// imported Global section, used by both import call sites
/// (MainViewModel + SettingsViewModel). The prompt appears only when
/// the package actually carries a non-empty Global section, after
/// profile validation and before anything is applied. Global is
/// single and shared by all profiles, so it is never overwritten
/// silently: Replace/Merge/Skip, default Skip (cancel/close also
/// means Skip). The importer's Global page keeps its Id/Name/
/// TemplateId - only the binding collection is ever replaced or
/// appended to.</summary>
public static class GlobalImport
{
    /// <summary>Shows the one three-choice AppDialog (custom labels
    /// Ganti/Gabung/Lewati). Cancel, X, or close = Skip.</summary>
    public static GlobalImportChoice Ask(Window? owner, int bindingCount)
    {
        var result = AppDialog.ShowThreeChoices(owner,
            Localizer.Format("S1825", bindingCount),
            Localizer.Get("S1824"),
            Localizer.Get("S1826"),
            Localizer.Get("S1827"),
            Localizer.Get("S1828"),
            AppDialogIcon.Question);
        return result switch
        {
            AppDialogResult.Yes => GlobalImportChoice.Replace,
            AppDialogResult.No => GlobalImportChoice.Merge,
            _ => GlobalImportChoice.Skip,
        };
    }

    /// <summary>Applies the choice to the importer's Global page.
    /// Merge skips bindings whose (ScanCode, IsE0, ChordMask) identity
    /// is already present - including identities appended earlier in
    /// the same merge, so an imported page can never duplicate itself
    /// into Global.</summary>
    public static GlobalImportOutcome Apply(PageConfig current, PageConfig imported, GlobalImportChoice choice)
    {
        switch (choice)
        {
            case GlobalImportChoice.Replace:
                int replaced = imported.Bindings.Count;
                current.Bindings = new List<BindingConfig>(imported.Bindings);
                return new GlobalImportOutcome(replaced, 0);
            case GlobalImportChoice.Merge:
            {
                var seen = new HashSet<(ushort ScanCode, bool IsE0, byte ChordMask)>();
                foreach (var b in current.Bindings)
                    seen.Add((b.ScanCode, b.IsE0, b.ChordMask));
                int appended = 0, skipped = 0;
                foreach (var b in imported.Bindings)
                {
                    if (!seen.Add((b.ScanCode, b.IsE0, b.ChordMask)))
                    {
                        skipped++;
                        continue;
                    }
                    current.Bindings.Add(b);
                    appended++;
                }
                return new GlobalImportOutcome(appended, skipped);
            }
            default:
                return new GlobalImportOutcome(0, 0);
        }
    }

    /// <summary>One report fragment for the import result text
    /// (appended by the caller after the profile report). Empty when
    /// there was no Global section at all.</summary>
    public static string OutcomeText(GlobalImportChoice choice, GlobalImportOutcome outcome, int globalDropped)
    {
        string text = choice switch
        {
            GlobalImportChoice.Replace => Localizer.Format("S1829", outcome.Appended),
            GlobalImportChoice.Merge => Localizer.Format("S1830", outcome.Appended, outcome.SkippedCollisions),
            _ => Localizer.Get("S1831"),
        };
        if (globalDropped > 0)
            text += " " + Localizer.Format("S1832", globalDropped);
        return text;
    }
}
