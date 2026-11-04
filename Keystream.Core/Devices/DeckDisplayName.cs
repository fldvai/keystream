using System.Collections.Generic;
using System.Text.RegularExpressions;
using Keystream.Core.Localization;

namespace Keystream.Core.Devices;

/// <summary>§161 - satu rumah untuk nama tampilan deck.
///
/// <see cref="For"/> adalah ekstraksi byte-identik perilaku dari
/// <c>DeckDevicePickerWindow.DisplayName</c> (nama asli → S0882 VID/PID →
/// S0883 "Keyboard #N", tidak pernah kosong): rantai fallback yang sama,
/// regex yang sama, kunci string yang sama. Dipakai jalur identifikasi
/// (<c>AppHost</c>) dan backfill startup (<c>AppHost</c>); pemakaian di
/// picker adalah seam Worker B. Murni: tanpa P/Invoke, tanpa enumerasi
/// di dalam - peta nama (<paramref name="names"/>) disuplai pemanggil
/// dari <c>HidDeviceNameResolver.Resolve</c>, persis seperti picker.
/// Pemanggil menyuplai peta dengan comparer ordinal-ignore-case
/// (seperti yang dikembalikan resolver); helper tidak memaksakan
/// comparer sendiri, mengikuti <c>TryGetValue</c> peta yang diterima.
///
/// <see cref="ShouldBackfill"/> adalah predikat backfill startup (§161):
/// hanya identitas aktif yang namanya kosong yang boleh di-backfill -
/// identitas yang menuntut re-identifikasi ditangani wizard, bukan
/// backfill. Murni dan dapat diuji harness tanpa WPF/enumerasi.</summary>
public static class DeckDisplayName
{
    /// <summary>Nama tampilan untuk satu perangkat: nama asli dari
    /// <paramref name="names"/> bila ada dan non-blank; bila tidak,
    /// S0882 "Keyboard VID_{0} · PID_{1}" bila hardware ID cocok pola
    /// VID/PID; bila tidak cocok, S0883 "Keyboard #{deviceNumber}".
    /// Tidak pernah mengembalikan string kosong. <paramref name="hardwareId"/>
    /// null diperlakukan sebagai "" (→ S0883); <paramref name="names"/>
    /// null diperlakukan sebagai peta kosong (→ fallback).</summary>
    public static string For(string hardwareId, int deviceNumber, IReadOnlyDictionary<string, string> names)
    {
        string id = hardwareId ?? "";
        if (names is not null && names.TryGetValue(id, out var real)
            && !string.IsNullOrWhiteSpace(real))
            return real;
        var m = Regex.Match(id, @"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})");
        return m.Success
            ? Localizer.Format("S0882", m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value.ToUpperInvariant())
            : Localizer.Format("S0883", deviceNumber);
    }

    /// <summary>True hanya bila nama kosong/whitespace DAN identitas
    /// adalah InterceptionHardwareId yang valid DAN re-identifikasi
    /// tidak dituntut. <see cref="DeviceId"/> kosong/tidak valid atau
    /// kind lain tidak pernah di-backfill; nama yang sudah terisi
    /// tidak pernah ditimpa.</summary>
    public static bool ShouldBackfill(string? friendlyName, DeviceId deviceId, bool requiresReidentification)
        => string.IsNullOrWhiteSpace(friendlyName)
           && deviceId.Kind == DeviceIdKind.InterceptionHardwareId
           && deviceId.IsValid
           && !requiresReidentification;
}
