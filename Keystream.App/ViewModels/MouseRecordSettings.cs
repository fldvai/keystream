namespace Keystream.App.ViewModels;

/// <summary>Cakupan perekaman mouse dalam satu sesi perekaman macro (§107).</summary>
public enum MouseScope
{
    /// <summary>Hanya klik (tombol mouse tekan/lepas) yang direkam.</summary>
    ClicksOnly,

    /// <summary>Klik + gerak. Default.</summary>
    ClicksAndMoves,

    /// <summary>Klik + gerak + scroll.</summary>
    ClicksMovesAndScroll,
}

/// <summary>Acuan koordinat posisi mouse yang direkam.</summary>
public enum MouseCoordinateMode
{
    /// <summary>Koordinat absolut terhadap seluruh layar. Default.</summary>
    Absolute,

    /// <summary>Koordinat relatif terhadap jendela yang aktif saat merekam.</summary>
    RelativeWindow,
}

/// <summary>Pengaturan perekaman mouse, HANYA untuk sesi berjalan (§107).
/// Tidak dipersistensikan: tidak menyentuh ConfigStore maupun DeckSettings.
/// Record: equality bawaan dipakai deteksi perubahan di
/// <see cref="Views.MouseSettingsWindow"/> (working copy != original).
/// Semua nilai default valid sehingga ctor modal tidak pernah throw
/// untuk pengaturan default.</summary>
public sealed record MouseRecordSettings
{
    /// <summary>Ambang gerak minimum (px) yang valid: 1..500.</summary>
    public const int ThresholdMin = 1;

    /// <summary>Ambang gerak maksimum (px) yang valid: 1..500.</summary>
    public const int ThresholdMax = 500;

    /// <summary>Jeda minimum antar langkah gerak (ms) yang valid: 0..1000.</summary>
    public const int DelayMin = 0;

    /// <summary>Jeda minimum antar langkah gerak (ms) yang valid: 0..1000.</summary>
    public const int DelayMax = 1000;

    /// <summary>Cakupan rekam. Default: klik + gerak (sesuai mockup).</summary>
    public MouseScope Scope { get; init; } = MouseScope.ClicksAndMoves;

    /// <summary>Geser mouse di bawah ambang ini (px) tidak menjadi langkah. Default 12.</summary>
    public int MoveThresholdPx { get; init; } = 12;

    /// <summary>Jeda minimum (ms) antar langkah gerak. Default 50.</summary>
    public int MinMoveDelayMs { get; init; } = 50;

    /// <summary>Acuan koordinat. Default: absolut layar.</summary>
    public MouseCoordinateMode CoordinateMode { get; init; } = MouseCoordinateMode.Absolute;

    /// <summary>Klik di jendela Keystream sendiri tidak ikut terekam. Default true.</summary>
    public bool IgnoreSelfClicks { get; init; } = true;

    /// <summary>Mode Persis (§108): tanpa penipisan gerak, celah nyata
    /// selalu terekam, dan tombol mouse selalu dipecah tekan/lepas -
    /// cakupan efektif menjadi semua (klik + gerak + scroll). Default
    /// true: pengaturan "sesuai asli" adalah default ketika mouse
    /// dicentang. Bila mati, perilaku §107 berlaku (filter cakupan,
    /// penipisan, dan celah/split ikut checkbox).</summary>
    public bool Verbatim { get; init; } = true;

    /// <summary>Salinan independen untuk diedit modal (record <c>with</c>).</summary>
    public MouseRecordSettings Copy() => this with { };
}
