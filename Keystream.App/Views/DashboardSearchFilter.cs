using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Keystream.Actions.Common;
using Keystream.Core.Config;

namespace Keystream.App.Views;

/// <summary>
/// §124 butir 2 - logika murni filter pencarian dashboard, sengaja
/// bebas dari tipe UI agar bisa ditautkan langsung ke wb-harness
/// net8.0 polos (disiplin yang sama seperti TextBoxRevertLogic dan
/// FloatingRecordPanelLogic): hanya System, koleksi, dan Json di
/// sini; tidak ada tipe tampilan, lookup sumber daya, atau akses
/// berkas. Bagian UI (kotak cari di MainWindow, properti teks di
/// MainViewModel) hanya memanggil ke sini.
///
/// Semantik cocok (sama untuk Grid dan List, halaman aktif saja -
/// pemanggil hanya memberi field kartu halaman aktif):
///  - kueri kosong / hanya spasi = semua kartu cocok;
///  - kueri dipecah per kata (spasi); SETIAP kata harus muncul
///    sebagai substring (OrdinalIgnoreCase) di SALAH SATU field -
///    satu kata boleh cocok di label, kata lain di target;
///  - satu kata = sama dengan Contains seperti FilterBox di
///    SearchablePanelPicker.
/// BuildTargetFields hanya membaca LANGKAH PERTAMA kartu (sama
/// seperti DetailText yang tampil di kartu) dan hanya teks
/// targetnya: nama macro / nama grup browser diterima sudah
/// ter-resolusi dari pemanggil - isi langkah macro TIDAK PERNAH
/// dibaca di sini. Id mentah (macroId, groupId) sengaja tidak
/// disertakan: itu bukan teks yang dibaca pengguna.</summary>
public static class DashboardSearchFilter
{
    /// <summary>True bila setiap kata kueri muncul di setidaknya satu field.</summary>
    public static bool Matches(string? query, IEnumerable<string?> fields)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        string[] tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return true;
        var list = new List<string>();
        foreach (string? f in fields)
            if (!string.IsNullOrWhiteSpace(f)) list.Add(f);
        if (list.Count == 0) return false;
        foreach (string token in tokens)
        {
            bool found = false;
            foreach (string field in list)
            {
                if (field.Contains(token, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
            }
            if (!found) return false;
        }
        return true;
    }

    /// <summary>Teks target langkah pertama, per jenis aksi. Nama macro
    /// dan nama grup browser berasal dari pustaka/konfigurasi (sudah
    /// di-resolusi pemanggil), bukan dari isi langkahnya.</summary>
    public static string[] BuildTargetFields(ActionStep? first, string? macroName, string? groupName)
    {
        if (first is null) return Array.Empty<string>();
        var p = first.Params;
        string P(string key) => ParamReader.GetString(p, key) ?? "";
        var outFields = new List<string>();
        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            string v = value.Trim();
            if (!outFields.Contains(v, StringComparer.Ordinal)) outFields.Add(v);
        }
        void AddArray(params string[] names)
        {
            foreach (string name in names)
            {
                foreach (var kv in p)
                {
                    if (kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase)
                        && kv.Value is JsonArray arr)
                    {
                        foreach (var node in arr)
                            if (node is JsonValue jv && jv.TryGetValue<string>(out string? s)) Add(s);
                    }
                }
            }
        }

        string id = (first.ActionId ?? "").Trim().ToLowerInvariant();
        switch (id)
        {
            case "macro":
                Add(macroName);
                break;
            case "browser":
                Add(P("profileName"));
                Add(P("profileKey"));
                Add(P("url"));
                Add(P("browserExe"));
                AddArray("profileNames", "profileKeys");
                break;
            case "browsergroup":
                Add(groupName);
                break;
            case "launcher":
                Add(P("target"));
                break;
            case "link":
                Add(P("url"));
                Add(P("target"));
                break;
            case "sound":
                Add(P("file"));
                break;
            case "obs":
                Add(P("command"));
                Add(P("sceneName"));
                Add(P("sourceName"));
                Add(P("inputName"));
                Add(P("itemName"));
                Add(P("filterName"));
                Add(P("collectionName"));
                Add(P("profileName"));
                Add(P("hotkeyName"));
                Add(P("filePath"));
                break;
            case "profile":
                Add(P("profileName"));
                break;
            case "deck":
                Add(P("pageName"));
                Add(P("command"));
                break;
            case "hotkey":
                Add(P("keys"));
                break;
            case "media":
            case "basic":
            case "windows":
            // §129 (integrasi): id partisi baru "windows-command" /
            // "windows-tools" menyimpan teks command di param "command"
            // yang sama (BindingEditorViewModel) - indeks persis seperti
            // "windows" lama, tanpa perilaku lain yang berubah.
            case "windows-command":
            case "windows-tools":
                Add(P("command"));
                break;
            default:
                break;
        }
        return outFields.ToArray();
    }
}
