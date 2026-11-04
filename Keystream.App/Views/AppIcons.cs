using System.Windows.Media;

namespace Keystream.App.Views;

/// <summary>
/// The app's single icon system (v2.9.35, the user's consistency rule):
/// every icon in the UI - tiles, buttons, dialogs - is a stroke-style
/// vector on a 24×24 grid, drawn in the outline manner of The Noun
/// Project references, rendered by <see cref="IconView"/>. Emoji are
/// never used as UI icons: they render differently per font (the ✕/⏎
/// lessons) and break the visual system. Stored tile icons are KEYS
/// into this catalog; legacy emoji values resolve through the map below
/// so old configs keep working, and unknown values fall back to text.
/// </summary>
public static class AppIcons
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["apps"] = "M3.5 3.5h6.5v6.5H3.5z M14 3.5h6.5v6.5H14z M3.5 14h6.5v6.5H3.5z M14 14h6.5v6.5H14z",
        ["device"] = "M3 4.8h18V16H3z M9.5 19.8h5 M12 16v3.8",
        ["gamepad"] = "M5.5 7h13a3.5 3.5 0 0 1 3.5 3.5v4.7a2.8 2.8 0 0 1-4.9 2L15.4 15H8.6L6.9 17.2a2.8 2.8 0 0 1-4.9-2V10.5A3.5 3.5 0 0 1 5.5 7Z M7.5 10v4 M5.5 12h4 M16.2 10.8h.01 M18 13.2h.01",
        ["steam"] = "M12 2a10 10 0 0 0-10 9.77l5.48 2.27a3.4 3.4 0 0 1 1.94-.6l.75-3.08a4.4 4.4 0 1 1 4.39 4.39l-3.08.75a3.4 3.4 0 0 1-.6 1.94L8.6 21.94A10 10 0 1 0 12 2zm2.56 9.44a2.2 2.2 0 1 0 0-4.4 2.2 2.2 0 0 0 0 4.4z M8.5 16.5a1.7 1.7 0 1 0 0-3.4 1.7 1.7 0 0 0 0 3.4z",
        ["obs"] = "M12 2.5a9.5 9.5 0 1 0 9.5 9.5A9.5 9.5 0 0 0 12 2.5z M12 5.5a4 4 0 0 1 3.46 2L12 9.5 M7.2 13.8a4 4 0 0 1 0-4L9.8 11 M16.8 13.8a4 4 0 0 1-3.46 2L12 13.5",
        ["media"] = "M3.5 5h17a1.5 1.5 0 0 1 1.5 1.5v11a1.5 1.5 0 0 1-1.5 1.5h-17A1.5 1.5 0 0 1 2 17.5v-11A1.5 1.5 0 0 1 3.5 5z M7 5v14 M17 5v14 M10.5 9.5l4.5 2.5-4.5 2.5z",
        ["mic"] = "M12 2.8a3 3 0 0 1 3 3v5a3 3 0 0 1-6 0v-5a3 3 0 0 1 3-3Z M5.8 11a6.2 6.2 0 0 0 12.4 0 M12 17.2V21 M8.8 21h6.4",
        ["headphones"] = "M4 19.5v-7a8 8 0 0 1 16 0v7 M2.8 14.5h3.4v5H2.8z M17.8 14.5h3.4v5h-3.4z",
        ["volume"] = "M4 9.8v4.4h3.2L12 18.6V5.4L7.2 9.8H4Z M15.5 9.8a3.6 3.6 0 0 1 0 4.4 M18.2 7.2a7.4 7.4 0 0 1 0 9.6",
        ["volume-low"] = "M4 9.8v4.4h3.2L12 18.6V5.4L7.2 9.8H4Z M15.5 9.8a3.6 3.6 0 0 1 0 4.4",
        ["volume-x"] = "M4 9.8v4.4h3.2L12 18.6V5.4L7.2 9.8H4Z M16 9.8l5 5.4 M21 9.8l-5 5.4",
        ["music"] = "M9 17.8V6.2L19 4.2v11.3 M3 17.8a3 3 0 1 0 6 0a3 3 0 1 0-6 0 M13 15.5a3 3 0 1 0 6 0a3 3 0 1 0-6 0",
        ["play"] = "M5 4h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z M10 8.5l6 3.5-6 3.5z",
        ["play-triangle"] = "M8 5.2v13.6L19 12z",
        ["pause"] = "M7 5h3.2v14H7z M13.8 5H17v14h-3.2z",
        ["play-pause"] = "M6.5 5.6v12.8L15.5 12z M17.2 5.8h2.2v12.4h-2.2z",
        ["layers"] = "M12 2.5 3 7.5 12 12.5 21 7.5z M3 12 12 17 21 12 M3 16.5 12 21.5 21 16.5",
        ["scene"] = "M12 2.5 3 7.5 12 12.5 21 7.5z M3 12 12 17 21 12 M3 16.5 12 21.5 21 16.5",
        ["next"] = "M5.5 6l8.5 6-8.5 6z M16 6h2.5v12H16z",
        ["prev"] = "M18.5 6L10 12l8.5 6z M8 6H5.5v12H8z",
        ["record"] = "M3.5 12a8.5 8.5 0 1 0 17 0a8.5 8.5 0 1 0-17 0 M10.4 12a1.6 1.6 0 1 0 3.2 0a1.6 1.6 0 1 0-3.2 0",
        ["stop"] = "M7 7h10v10H7z",
        ["video"] = "M2.8 6.5A1.8 1.8 0 0 1 4.6 4.7h14.8a1.8 1.8 0 0 1 1.8 1.8v11a1.8 1.8 0 0 1-1.8 1.8H4.6a1.8 1.8 0 0 1-1.8-1.8z M2.8 9.2h18.4 M7.2 4.7v4.5 M12 4.7v4.5 M16.8 4.7v4.5",
        ["camera"] = "M2.5 7.5h4l2-2.5h7l2 2.5h4v11h-19z M8.8 13a3.2 3.2 0 1 0 6.4 0a3.2 3.2 0 1 0-6.4 0",
        ["sliders"] = "M6 4v6 M6 14v6 M12 4v2 M12 10v10 M18 4v10 M18 18v2 M3.5 12h5 M9.5 8h5 M15.5 16h5",
        ["joystick"] = "M9.5 5.5a2.5 2.5 0 1 0 5 0a2.5 2.5 0 1 0-5 0 M12 8v4.5 M5 20.5 7.2 13h9.6L19 20.5z",
        ["keyboard"] = "M2.5 7A1.8 1.8 0 0 1 4.3 5.2h15.4A1.8 1.8 0 0 1 21.5 7v10a1.8 1.8 0 0 1-1.8 1.8H4.3A1.8 1.8 0 0 1 2.5 17z M6 9.8h.01 M9.6 9.8h.01 M13.2 9.8h.01 M16.8 9.8h.01 M6 13.8h.01 M16.8 13.8h.01 M9.2 13.8h5.6",
        ["mouse"] = "M9.5 3h5A2.5 2.5 0 0 1 17 5.5v13a5 5 0 0 1-10 0v-13A2.5 2.5 0 0 1 9.5 3 M12 6.8v3.4",
        ["monitor"] = "M3 4.8h18V16H3z M9.5 19.8h5 M12 16v3.8",
        ["tv"] = "M8.2 3.2 12 6.8l3.8-3.6 M3.5 8.2h17V19h-17z",
        ["globe"] = "M3 12a9 9 0 1 0 18 0a9 9 0 1 0-18 0 M3 12h18 M12 3c-5.5 5-5.5 13 0 18 M12 3c5.5 5 5.5 13 0 18",
        ["message"] = "M21 11.5a8.5 8.5 0 0 1-12.4 7.2L3 20.5l1.8-5.6A8.5 8.5 0 1 1 21 11.5",
        ["folder"] = "M2.8 6.5A1.8 1.8 0 0 1 4.6 4.7h4.6l2 2.3h8.2a1.8 1.8 0 0 1 1.8 1.8V17a1.8 1.8 0 0 1-1.8 1.8H4.6a1.8 1.8 0 0 1-1.8-1.8z",
        ["zap"] = "M13 2.5 4.8 13.2h6L9.6 21.5l8.6-10.7h-6z",
        ["flame"] = "M12 3c1 3.2-2.6 4.8-2.6 7.2A5.3 5.3 0 0 0 12 20.8a5.3 5.3 0 0 0 2.6-10.4C14.4 7.4 13.2 4.9 12 3 M12 20.8a2.9 2.9 0 0 1-2.9-2.9c0-1.3 1-2.2 2.9-4.3 1.9 2.1 2.9 3 2.9 4.3a2.9 2.9 0 0 1-2.9 2.9",
        ["star"] = "M12 3.6l2.6 5.3 5.8.8-4.2 4.1 1 5.8L12 16.9l-5.2 2.7 1-5.8L3.6 9.7l5.8-.8z",
        ["heart"] = "M20.8 4.6a5.5 5.5 0 0 0-7.8 0L12 5.6l-1-1a5.5 5.5 0 0 0-7.8 7.8l1 1L12 21.2l7.8-7.8 1-1a5.5 5.5 0 0 0 0-7.8",
        ["eye"] = "M2.2 12C5.8 5.6 18.2 5.6 21.8 12C18.2 18.4 5.8 18.4 2.2 12Z M9.2 12a2.8 2.8 0 1 0 5.6 0a2.8 2.8 0 1 0-5.6 0",
        ["shield"] = "M12 2.8l7.2 2.6v5.4c0 4.9-3 8.1-7.2 9.9-4.2-1.8-7.2-5-7.2-9.9V5.4z",
        ["bulb"] = "M9.3 18h5.4 M10.3 21.2h3.4 M12 2.8a6.2 6.2 0 0 1 3.6 11.1c-.7.6-1.1 1.3-1.1 2.3h-5c0-1-.4-1.7-1.1-2.3A6.2 6.2 0 0 1 12 2.8",
        ["clock"] = "M3.2 12a8.8 8.8 0 1 0 17.6 0a8.8 8.8 0 1 0-17.6 0 M12 7.5V12l3 2",
        ["bell"] = "M6.2 16.2v-5.4a5.8 5.8 0 0 1 11.6 0v5.4l1.6 3.2H4.6z M10.2 21a2 2 0 0 0 3.6 0",
        ["lock"] = "M5.5 11h13v10h-13z M8.5 11V7a3.5 3.5 0 0 1 7 0v4 M12 15.2v2.3",
        ["scissors"] = "M3.6 8.7a2.4 2.4 0 1 0 4.8 0a2.4 2.4 0 1 0-4.8 0 M3.6 15.3a2.4 2.4 0 1 0 4.8 0a2.4 2.4 0 1 0-4.8 0 M8.2 10.6 19.5 19 M8.2 13.4 19.5 5",
        ["pin"] = "M9.5 3.5h5L13.8 10 17 12.8v2.2H7v-2.2L10.2 10z M12 15v6",
        ["rocket"] = "M12 2.6c2.6 2.2 4 5.4 4 8.9l2.9 3.7-3.9-.7a9.8 9.8 0 0 1-3 1.2 9.8 9.8 0 0 1-3-1.2l-3.9.7L8 11.5c0-3.5 1.4-6.7 4-8.9 M10.1 9.5a1.9 1.9 0 1 0 3.8 0a1.9 1.9 0 1 0-3.8 0",
        ["trash"] = "M3.8 6.2h16.4 M9.5 6.2V4a1 1 0 0 1 1-1h3a1 1 0 0 1 1 1v2.2 M6.2 6.2l.9 12.6a2 2 0 0 0 2 1.8h5.8a2 2 0 0 0 2-1.8l.9-12.6 M9.8 10.4v6 M14.2 10.4v6",
        ["edit"] = "M4.2 19.8 5 15.2 16.3 3.9a2 2 0 0 1 2.8 0l.7.7a2 2 0 0 1 0 2.8L8.4 18.7z M14.6 5.6l3.7 3.7",
        ["plus"] = "M12 5v14 M5 12h14",
        ["x"] = "M6 6l12 12 M18 6L6 18",
        ["check"] = "M4.5 12.8 9.8 18 19.5 6.5",
        ["refresh"] = "M20.2 12a8.2 8.2 0 1 1-2.4-5.8 M20.4 3.6v4.6h-4.6",
        ["gear"] = "M8.6 12a3.4 3.4 0 1 0 6.8 0a3.4 3.4 0 1 0-6.8 0 M12 2.6v2.8 M12 18.6v2.8 M2.6 12h2.8 M18.6 12h2.8 M5.1 5.1l2 2 M16.9 16.9l2 2 M18.9 5.1l-2 2 M7.1 16.9l-2 2",
        ["alert"] = "M12 3.8 2.9 19.8h18.2z M12 9.8v4.6 M12 17.2h.01",
        ["info"] = "M3 12a9 9 0 1 0 18 0a9 9 0 1 0-18 0 M12 10.8v5.4 M12 7.4h.01",
        ["power"] = "M12 2.8v7.6 M6.2 6.2a8.2 8.2 0 1 0 11.6 0",
        ["link"] = "M10.2 13.8a4.6 4.6 0 0 0 6.5 0l2.9-2.9a4.6 4.6 0 1 0-6.5-6.5l-1.4 1.4 M13.8 10.2a4.6 4.6 0 0 0-6.5 0l-2.9 2.9a4.6 4.6 0 1 0 6.5 6.5l1.4-1.4",
        ["grid"] = "M4 4h6.5v6.5H4z M13.5 4H20v6.5h-6.5z M4 13.5h6.5V20H4z M13.5 13.5H20V20h-6.5z",
        // §90: ikon toggle List - HANYA di Paths (bukan TileKeys,
        // agar grid picker tetap 77). Tiga garis horizontal.
        ["list"] = "M4 6H20 M4 12H20 M4 18H20",
        ["flag"] = "M5.5 21.5v-19 M5.5 3.5c3-1.8 5.5 1.8 9 0v7c-3.5 1.8-6-1.8-9 0",
        ["image"] = "M3 5h18v14H3z M8.5 10a1.8 1.8 0 1 0 0-0.01 M4 18l5.5-5.5 3 3L16 12l4 4",
        ["sun"] = "M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8 M12 2.5v2.2 M12 19.3v2.2 M2.5 12h2.2 M19.3 12h2.2 M5 5l1.6 1.6 M17.4 17.4L19 19 M19 5l-1.6 1.6 M6.6 17.4L5 19",
        ["moon"] = "M21 12.8A9 9 0 1 1 11.2 3 7 7 0 0 0 21 12.8Z",
        ["wifi"] = "M2.5 9.5a15 15 0 0 1 19 0 M5.5 13a10.5 10.5 0 0 1 13 0 M8.6 16.4a6 6 0 0 1 6.8 0 M12 19.2a1.1 1.1 0 1 0 0-0.01",
        ["download"] = "M12 3v11 M6.5 9.5L12 15l5.5-5.5 M4 19.5h16",
        ["upload"] = "M12 14V3 M6.5 7.5L12 2l5.5 5.5 M4 19.5h16",
        ["calendar"] = "M4 6h16v14H4z M4 10.5h16 M8 3.5v4 M16 3.5v4",
        ["mail"] = "M3 6h18v12H3z M3.5 7l8.5 6 8.5-6",
        ["battery"] = "M3 8.5h15v7H3z M21 10.7v2.6 M6.2 11h3.6v2H6.2z",
        ["printer"] = "M6 9V4h12v5 M4 9h16v6h-3 M7 13h10v7H7z M4 15h3",
        ["gift"] = "M4 11h16v9H4z M12 11v9 M4 7.5h16V11H4z M12 7.5c-4 0-4.5-3.5-2.8-3.5C10.8 4 12 5.5 12 7.5c0-2 1.2-3.5 2.8-3.5C16.5 4 16 7.5 12 7.5",
        ["coffee"] = "M5 9h11v6a4 4 0 0 1-4 4h-3a4 4 0 0 1-4-4z M16 10h1.2a2.8 2.8 0 0 1 0 5.6H16 M4 21.5h14",
        ["bookmark"] = "M7 3.5h10V21l-5-3.6L7 21z",
        ["droplet"] = "M12 3.2c2.9 4.1 5.8 7.5 5.8 10.6a5.8 5.8 0 1 1-11.6 0C6.2 10.7 9.1 7.3 12 3.2",
        ["exit"] = "M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4 M16 17l5-5-5-5 M21 12H9",
        ["copy"] = "M9 9h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2V11a2 2 0 0 1 2-2Z M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1",
        ["paste"] = "M5 8h14v13H5z M9 8V5.5A1.5 1.5 0 0 1 10.5 4h3A1.5 1.5 0 0 1 15 5.5V8 M9 12h6 M9 16h6",
        ["undo"] = "M9 13L4 8l5-5 M20 16v-4a4 4 0 0 0-4-4H4",
        ["redo"] = "M15 13l5-5-5-5 M4 16v-4a4 4 0 0 1 4-4h12",
        ["save"] = "M5 21H19a2 2 0 0 0 2-2V8l-5-5H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2z M17 21v-8H7v8 M7 3v5h8V3",
        ["search"] = "M3.5 10.5a7 7 0 1 0 14 0a7 7 0 1 0-14 0 M15.5 15.5l5 5",
        // §79 Gelombang 2: path merek gaya stroke katalog (24×24). Ini
        // ikon INSTAN + cadangan offline; kartu web template tetap
        // di-upgrade §76 ke favicon asli bila online (Auto > katalog).
        // Keterbacaan di tile nyata TIDAK bisa dibuktikan di VM - cek
        // CARA item 98–101. Discord TIDAK digambar (bentuknya tidak
        // setia dalam stroke) dan tidak dibundel PNG - kartu web-nya
        // memakai "message" + favicon Discord via §76 (jalur cadangan
        // proposal, karena PNG butuh mesin bundel baru).
        ["youtube"] = "M5.5 6h13a3 3 0 0 1 3 3v6a3 3 0 0 1-3 3h-13a3 3 0 0 1-3-3V9a3 3 0 0 1 3-3z M10 9.3l5 2.7-5 2.7z",
        ["spotify"] = "M3.5 12a8.5 8.5 0 1 0 17 0a8.5 8.5 0 1 0-17 0 M6.8 9.6Q12 7.2 17.2 9.6 M7.8 12.4Q12 10.2 16.2 12.4 M8.8 15Q12 13 15.2 15",
        ["whatsapp"] = "M21 11.5a8.5 8.5 0 0 1-12.4 7.2L3 20.5l1.8-5.6A8.5 8.5 0 1 1 21 11.5 M9.2 8.8l1.6 1.6c0.7-1 1.7-2 2.7-2.7l1.6 1.6 1.3-1.7c-2.6-1.2-5.6-0.6-7.2 1.2z",
        ["telegram"] = "M20.8 3.8L3.8 10.6l5.4 2.1 2.6 6.3 2.7-4.6 6.3-10.6z M9.2 12.7l11.6-8.9",
        // §89: 7 ikon baru agar grid picker pas 11×7 (sebelumnya 70 =
        // 6 baris penuh + 1 baris sisa 4 brand icon). Semua stroke
        // 24×24 gaya katalog; bentuk disederhanakan agar terbaca
        // pada ukuran tile kecil.
        ["home"] = "M3 11.5L12 4l9 7.5 M6 10v10h12V10 M10 20v-6h4v6",
        ["phone"] = "M7.5 3.5h9v17h-9z M10.5 18h3",
        ["bluetooth"] = "M9.5 3.5L15.5 12l-6 8.5 M9.5 3.5L15.5 7.5 M9.5 20.5l6-4 M7 9l10 6",
        ["cloud"] = "M7 18.5A3.5 3.5 0 0 0 7 11.5A5 5 0 0 1 17 11a3.75 3.75 0 0 1 0 7.5Z",
        ["expand"] = "M3.5 9.5V3.5H9.5 M14.5 3.5H20.5V9.5 M20.5 14.5V20.5H14.5 M9.5 20.5H3.5V14.5",
        ["code"] = "M8 7.5L3.5 12l4.5 4.5 M16 7.5L20.5 12l-4.5 4.5 M13.5 4.5L10.5 19.5",
        ["user"] = "M8 7a4 4 0 0 1 8 0a4 4 0 0 1-8 0Z M4.5 20.5C4.5 16 8 14 12 14c4 0 7.5 2 7.5 6.5",
    };

    /// <summary>The tile picker's icon keys, in display order.</summary>
    public static readonly IReadOnlyList<string> TileKeys = new[]
    {
        "play", "pause", "play-pause", "stop", "record", "next", "prev", "volume", "volume-low", "volume-x", "mic",
        "copy", "paste", "scissors", "undo", "redo", "save", "search", "edit", "trash", "plus", "check",
        "apps", "folder", "monitor", "tv", "keyboard", "mouse", "camera", "video", "image", "globe", "message",
        "layers", "scene", "sliders", "grid", "media", "joystick", "gamepad", "steam", "headphones", "zap", "star", "heart",
        "wifi", "battery", "power", "lock", "bell", "clock", "refresh", "gear", "download", "upload", "calendar",
        "eye", "shield", "bulb", "mail", "music", "printer", "pin", "flag", "x", "alert", "info",
        "youtube", "spotify", "whatsapp", "telegram", "home", "phone", "bluetooth", "cloud", "expand", "code", "user",
    };

    private static readonly Dictionary<string, string> LegacyEmoji = new()
    {
        ["🎮"] = "gamepad", ["🎙"] = "mic", ["🎧"] = "headphones",
        ["🔊"] = "volume", ["🔈"] = "volume-low", ["🔇"] = "volume-x",
        ["🎵"] = "music", ["⏯"] = "play-pause", ["⏭"] = "next",
        ["⏮"] = "prev", ["⏺"] = "record", ["⏹"] = "stop",
        ["🎬"] = "video", ["📷"] = "camera", ["🎛"] = "sliders",
        ["🕹"] = "joystick", ["⌨"] = "keyboard", ["🖱"] = "mouse",
        ["🖥"] = "monitor", ["📺"] = "tv", ["🌐"] = "globe",
        ["💬"] = "message", ["📁"] = "folder", ["⚡"] = "zap",
        ["🔥"] = "flame", ["⭐"] = "star", ["❤"] = "heart",
        ["👁"] = "eye", ["🛡"] = "shield", ["💡"] = "bulb",
        ["⏰"] = "clock", ["🔔"] = "bell", ["🔒"] = "lock",
        ["✂"] = "scissors", ["📌"] = "pin", ["🚀"] = "rocket",
        ["🗑"] = "trash", ["✎"] = "edit", ["⚠"] = "alert",
        ["⚙"] = "gear", ["↻"] = "refresh", ["🎨"] = "droplet",
    };

    private static readonly Dictionary<string, Geometry> Cache = new();

    public static Geometry? Get(string? key)
    {
        if (key is null || !Paths.TryGetValue(key, out var data)) return null;
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var geometry))
            {
                try
                {
                    geometry = Geometry.Parse(data);
                    geometry.Freeze();
                }
                catch (FormatException)
                {
                    geometry = Geometry.Empty; // a bad icon renders nothing, never an error
                }
                Cache[key] = geometry;
            }
            return geometry;
        }
    }

    /// <summary>Resolves a stored icon value (a catalog key, or a legacy
    /// emoji from before the vector system) to a catalog key, or null
    /// when the value is a free-form glyph to render as text.</summary>
    public static string? ResolveKey(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        string value = stored.Trim();
        if (Paths.ContainsKey(value)) return value;
        string bare = value.Replace("\uFE0F", "");
        return LegacyEmoji.TryGetValue(bare, out var key) ? key : null;
    }
}
