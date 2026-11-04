using Keystream.Core.Devices;
using Keystream.Core.Localization;

namespace Keystream.Core.Engine;

public enum IdentificationStage
{
    AwaitingDeck,
    AwaitingMain,
    AwaitingDeckConfirm,
    Completed,
}

/// <summary>
/// The 3-step physical identification protocol as a pure state machine -
/// the same protocol the Gate 0/Gate A instruments proved on the user's
/// hardware, now reusable by the product wizard (audit B3-5: the legacy
/// wizard was dead on first run and auto-selected the first enumerated
/// keyboard, which could be the MAIN keyboard; this machine has no
/// auto-select path at all - only physical presses advance it).
///
/// Feed it the DeviceId of every key-DOWN while identifying. Steps:
///  1. deck press  → records the deck candidate;
///  2. main press  → must NOT match the candidate (if it matches, the user
///     pressed the same keyboard twice - rejected with a message, the
///     machine stays on step 2);
///  3. deck press  → must match the candidate, else rejected, stays on 3.
/// </summary>
public sealed class DeckIdentification
{
    public IdentificationStage Stage { get; private set; } = IdentificationStage.AwaitingDeck;
    public DeviceId? DeckCandidate { get; private set; }
    public DeviceId? MainDevice { get; private set; }
    public string? LastMessage { get; private set; }

    /// <summary>§161 - ambang tersangka-kembar: penolakan langkah-2
    /// berturut-turut sebanyak ini berarti pola, bukan salah tekan
    /// sekali. Satu penolakan bisa jadi tekan ganda yang tidak
    /// disengaja pada keyboard yang sama; dua bisa jadi percobaan
    /// ulang; tiga berturut-turut tanpa satu pun tekanan berbeda
    /// adalah pola kebuntuan kembar (setiap tekanan cocok kandidat).
    /// Wizard memakai ambang ini untuk menampilkan S1775; mesin
    /// sendiri tidak pernah auto-complete/auto-select karenanya.</summary>
    public const int TwinSuspectThreshold = 3;

    /// <summary>§161 - jumlah penolakan S1061 (tekanan langkah-2 yang
    /// cocok kandidat) BERTURUT-TURUT. Naik hanya di cabang penolakan
    /// <see cref="IdentificationStage.AwaitingMain"/>; direset ke 0
    /// setiap stage maju dan oleh <see cref="Reset"/>. Tekanan dengan
    /// identitas kosong (S1059) bukan penolakan: tidak dihitung dan
    /// tidak mereset. Transisi stage tidak berubah.</summary>
    public int ConsecutiveSameDeviceRejections { get; private set; }

    public IdentificationStage Observe(DeviceId device)
    {
        if (!device.IsValid)
        {
            LastMessage = Localizer.Get("S1059");
            return Stage;
        }

        switch (Stage)
        {
            case IdentificationStage.AwaitingDeck:
                DeckCandidate = device;
                Stage = IdentificationStage.AwaitingMain;
                ConsecutiveSameDeviceRejections = 0;
                LastMessage = Localizer.Get("S1060");
                break;

            case IdentificationStage.AwaitingMain:
                if (device.Matches(DeckCandidate!.Value))
                {
                    ConsecutiveSameDeviceRejections++;
                    LastMessage = Localizer.Get("S1061");
                    break;
                }
                MainDevice = device;
                Stage = IdentificationStage.AwaitingDeckConfirm;
                ConsecutiveSameDeviceRejections = 0;
                LastMessage = Localizer.Get("S1062");
                break;

            case IdentificationStage.AwaitingDeckConfirm:
                if (!device.Matches(DeckCandidate!.Value))
                {
                    LastMessage = Localizer.Get("S1063");
                    break;
                }
                Stage = IdentificationStage.Completed;
                ConsecutiveSameDeviceRejections = 0;
                LastMessage = Localizer.Get("S1064");
                break;

            case IdentificationStage.Completed:
                break;
        }
        return Stage;
    }

    public void Reset()
    {
        Stage = IdentificationStage.AwaitingDeck;
        DeckCandidate = null;
        MainDevice = null;
        LastMessage = null;
        ConsecutiveSameDeviceRejections = 0;
    }
}
