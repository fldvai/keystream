namespace Keystream.App.ViewModels;

/// <summary>§159 - the single home for the binding editor's file/folder
/// dialog specifications (title/description key + filter key + kind).
/// Host-free by design: plain data, no <c>Localizer</c> call, no WPF,
/// no Win32 - the window resolves the keys at dialog construction,
/// exactly as the main-editor handlers did before §159. Both the
/// main-editor handlers (<c>BindingEditorWindow.xaml.cs</c>) and the
/// per-step browse flows (step event → owner forward → window) read
/// their specs from here, so a filter string can never drift between
/// the main card and a combination step. Verified live against
/// <c>Strings.g.cs</c>: S0871/S0872/S0873/S0874/S0878/S0879 and
/// S0875/S0876 are the keys the handlers used before; the values
/// (ID + EN) are unchanged.
/// <para>No dialog sets an initial directory/folder: every handler
/// before §159 left <c>InitialDirectory</c>/<c>SelectedPath</c> unset,
/// so the OS default applies on both paths. <see cref="InitialDirectory"/>
/// is null by construction and is recorded here so a future starting
/// folder, if ever ordered, has exactly one place to land.</para></summary>
public sealed record EditorDialogSpec(
    EditorDialogKind Kind,
    string TitleKey,
    string FilterKey,
    string DescriptionKey)
{
    /// <summary>Unset on every editor dialog (see the type doc): the
    /// OS default starting folder applies.</summary>
    public string? InitialDirectory => null;
}

/// <summary>Which OS dialog a spec opens.</summary>
public enum EditorDialogKind
{
    File,
    Folder,
}

public static class EditorDialogSpecs
{
    /// <summary>Launcher target picker: "Pilih aplikasi / file" +
    /// the .exe filter. Main handler: <c>BrowseLauncher_Click</c>.</summary>
    public static readonly EditorDialogSpec LauncherExe = new(
        EditorDialogKind.File, "S0871", "S0872", "");

    /// <summary>Manual browser .exe picker: "Pilih file .exe browser" +
    /// the same .exe filter as <see cref="LauncherExe"/> (one home for
    /// the filter string - the two handlers shared S0872 before §159
    /// too). Main handler: <c>BrowseManualExe_Click</c>.</summary>
    public static readonly EditorDialogSpec ManualBrowserExe = new(
        EditorDialogKind.File, "S0873", "S0872", "");

    /// <summary>Manual profile-folder picker: folder dialog with
    /// description S0874 (folder dialogs carry no filter). Main
    /// handler: <c>BrowseManualFolder</c> (also the CUSTOM arm of
    /// <c>PindaiBrowser_Click</c>).</summary>
    public static readonly EditorDialogSpec ManualProfileFolder = new(
        EditorDialogKind.Folder, "", "", "S0874");

    /// <summary>Sound file picker: "Pilih file suara" + the audio
    /// filter. Main handler: <c>BrowseSound_Click</c>. This is the
    /// §159 index defect's dialog: the step template had the label
    /// (S0065) but no button; the step command opens this same spec.</summary>
    public static readonly EditorDialogSpec SoundFile = new(
        EditorDialogKind.File, "S0878", "S0879", "");

    /// <summary>Card-icon image picker. Main-card only (the icon tab
    /// is card-level, §159 N-02): no step flow ever uses this spec.
    /// Listed here so ALL of the editor window's file-dialog filter
    /// strings live in one home. Main handler:
    /// <c>BrowseIcon_Click</c>.</summary>
    public static readonly EditorDialogSpec IconImage = new(
        EditorDialogKind.File, "S0875", "S0876", "");
}
