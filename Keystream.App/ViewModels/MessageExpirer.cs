using System;

namespace Keystream.App.ViewModels;

/// <summary>§128 - logika kedaluwarsa pesan per seksi, sengaja WPF-FREE:
/// hanya string, TimeSpan, dan delegate - tanpa tipe WPF apa pun - agar
/// dapat diuji di harness net8.0 polos (disiplin yang sama seperti
/// MouseRecordPlanner.cs / TextBoxRevertLogic.cs). Satu-satunya bagian
/// WPF (DispatcherTimer sekali-tembak di UI thread) tinggal di
/// SettingsViewModel sebagai delegate penjadwal tipis yang diteruskan
/// ke konstruktor; kelas ini tidak pernah menyentuh Dispatcher,
/// jendela, atau kontrol.
///
/// Aturan: setiap pesan hidup 3,5 detik (konvensi footer Notify),
/// lalu dihapus. Setiap Arm() menaikkan token generasi slot itu;
/// callback penjadwal hanya menghapus bila tokennya masih sama
/// dengan generasi saat ini - pesan baru yang datang selagi timer
/// lama berjalan tidak pernah dihapus oleh timer lama. Nilai kosong
/// tidak menjadwalkan apa pun. Callback yang fire sesudah jendela
/// ditutup hanya memanggil aksi clear (set properti VM menjadi ""),
/// jadi tidak dapat melempar dari sisi UI; InvalidateAll() yang
/// dipanggil dari Dispose() membuat callback yang tertunda menjadi
/// basi sehingga menjadi no-op.</summary>
public sealed class MessageExpirer
{
    /// <summary>Durasi hidup tiap pesan: 3,5 detik, sama dengan
    /// konvensi footer Notify aplikasi.</summary>
    public static TimeSpan Duration { get; } = TimeSpan.FromSeconds(3.5);

    private readonly Action<TimeSpan, Action> _schedule;
    private readonly System.Collections.Generic.Dictionary<string, long> _generations =
        new(StringComparer.Ordinal);

    /// <param name="schedule">Penjadwal sekali-tembak: dipanggil dengan
    /// (Duration, callback). Di produksi callback berjalan di UI thread
    /// via DispatcherTimer; di harness callback direkam dan difire
    /// manual oleh tes.</param>
    public MessageExpirer(Action<TimeSpan, Action> schedule)
    {
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
    }

    /// <summary>Jadwalkan penghapusan pesan slot ini setelah
    /// <see cref="Duration"/>. Nilai kosong/whitespace tidak
    /// menjadwalkan apa pun dan mengembalikan false. Memanggil Arm
    /// lagi untuk slot yang sama me-restart hitungan (token baru)
    /// dan membuat callback Arm sebelumnya basi.</summary>
    public bool Arm(string slot, string? value, Action clear)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (clear is null) throw new ArgumentNullException(nameof(clear));
        long token = _generations.TryGetValue(slot, out long current) ? current + 1 : 1;
        _generations[slot] = token;
        _schedule(Duration, () =>
        {
            if (_generations.TryGetValue(slot, out long latest) && latest == token)
                clear();
        });
        return true;
    }

    /// <summary>Generasi saat ini untuk slot (0 bila belum pernah
    /// di-Arm). Hanya untuk tes.</summary>
    public long Generation(string slot)
        => _generations.TryGetValue(slot, out long g) ? g : 0;

    /// <summary>Buat semua callback yang tertunda menjadi basi tanpa
    /// menghapus pesan apa pun - dipakai saat jendela ditutup agar
    /// timer yang fire belakangan menjadi no-op.</summary>
    public void InvalidateAll()
    {
        foreach (string slot in new System.Collections.Generic.List<string>(_generations.Keys))
            _generations[slot]++;
    }

    /// <summary>§151 P1a - buat callback yang tertunda untuk SATU slot
    /// menjadi basi, tanpa menghapus pesannya dan tanpa menyentuh
    /// slot lain. Dipakai sebelum sebuah pesan MENETAP ditulis ke
    /// slot itu (pesan kegagalan Reset): timer kedaluwarsa dari
    /// pesan sebelumnya di slot yang sama tidak boleh menghapus
    /// pesan menetap yang baru. Slot yang belum pernah di-arm
    /// tidak berubah (generasi tetap 0).</summary>
    public void Invalidate(string slot)
    {
        if (_generations.TryGetValue(slot, out long current))
            _generations[slot] = current + 1;
    }
}
