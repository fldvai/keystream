using System;

namespace Keystream.Core.Localization;

/// <summary>Seam between Setup and the app for the wizard language choice (A13).
/// <para>When no main config.json exists, Setup writes the chosen language to the
/// HKCU uninstall-key registry value named <see cref="RegistryValueName"/>
/// instead of creating or editing config.json, so <c>ConfigStore.Load</c>
/// still returns FreshDefault and <c>CreateDefault</c> seeds the 7 default
/// Global cards. The app consumes the pending value once on FreshDefault only.</para>
/// <para>Normalization of the code is the caller's job (app:
/// <c>Localizer.NormalizeLanguage</c>; Setup: <c>SetupLocalizer.Normalize</c>)
/// — these functions deliberately pass values through unnormalized.</para>
/// <para>Depends on nothing but <c>System</c>: no Registry, no file I/O, no
/// Localizer reference, so this file compiles unchanged in Core, Setup,
/// and Uninstall.</para></summary>
public static class LanguageHandoff
{
    /// <summary>Registry value name under the HKCU uninstall key holding the
    /// pending wizard language choice.</summary>
    public const string RegistryValueName = "Language";

    /// <summary>What Setup does with the wizard language choice: merge it into
    /// the existing main config.json, or write it to the registry.</summary>
    public enum SetupAction { MergeIntoFile, WriteRegistry }

    /// <summary>An existing main config.json takes the merge path; with no main
    /// config.json, Setup writes the registry value so Load still returns
    /// FreshDefault.</summary>
    public static SetupAction DecideSetupAction(bool mainConfigExists) => mainConfigExists ? SetupAction.MergeIntoFile : SetupAction.WriteRegistry;

    /// <summary>Returns the pending value only on FreshDefault with a
    /// non-blank value; otherwise null. The value is passed through
    /// unnormalized — normalization is the caller's job.</summary>
    public static string? DecidePendingLanguage(bool isFreshDefault, string? pendingValue) => (isFreshDefault && !string.IsNullOrWhiteSpace(pendingValue)) ? pendingValue : null;
}
