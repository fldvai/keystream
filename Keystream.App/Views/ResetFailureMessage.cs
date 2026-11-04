namespace Keystream.App.Views;

/// <summary>§151 P1a - composition of the reset-failure footer text:
/// the existing localized header (S1689) plus the failure detail
/// VERBATIM, separated by one line break. WPF-free so the
/// wb-harness can pin the composition: the detail is exception
/// text from the OS/delete path and is never localized, trimmed,
/// or paraphrased - the user and crash.log must see the same
/// words. A missing/blank detail yields the header alone (the
/// pre-§151 footer text, unchanged).</summary>
public static class ResetFailureMessage
{
    public static string Compose(string header, string? detail)
        => string.IsNullOrWhiteSpace(detail) ? header : header + "\n" + detail;
}
