namespace Keystream.Core.Config;

/// <summary>
/// One-time migration (UI v2.8): the v2.7 per-profile navigation slots
/// retire into the single Global page. Every profile's NavBindings move
/// to DeckConfig.GlobalPage - deduplicated by physical key (the first
/// profile's binding for a key wins, since all profiles share one deck) -
/// and the NavBindings lists are cleared. Pure and idempotent: running it
/// on an already-migrated config changes nothing. Returns true when the
/// config changed.
/// </summary>
public static class GlobalMigration
{
    public static bool MigrateNavBindingsToGlobalPage(DeckConfig config)
    {
        bool changed = false;
        // A-02: tahan explicit-null dari config hasil edit-tangan
        // (jalur Load sudah dinormalisasi, tapi migrasi juga dipanggil
        // langsung) — null menjadi daftar kosong.
        config.GlobalPage ??= new();
        config.GlobalPage.Bindings ??= new();
        config.Profiles ??= new();
        var takenKeys = new HashSet<(ushort, bool)>(
            config.GlobalPage.Bindings.Where(b => b is not null).Select(b => (b.ScanCode, b.IsE0)));
        foreach (var profile in config.Profiles)
        {
            if (profile is null) continue;
            profile.NavBindings ??= new();
            if (profile.NavBindings.Count > 0) changed = true; // slots retire
            foreach (var nav in profile.NavBindings)
            {
                if (nav is null) continue;
                if (nav.ScanCode != 0 && takenKeys.Add((nav.ScanCode, nav.IsE0)))
                    config.GlobalPage.Bindings.Add(nav);
            }
            profile.NavBindings.Clear();
        }
        return changed;
    }
}
