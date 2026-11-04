using Keystream.Core.Devices;
using Keystream.Core.Localization;

namespace Keystream.Core.Input;

public enum KeyDirection
{
    Down,
    Up,
}

/// <summary>Physical key identity: scan code + E0/E1 prefix bits. This is
/// what bindings are keyed on - NOT the virtual-key code alone. VK-only
/// identity cannot distinguish Enter from Numpad-Enter (both VK 0x0D) or
/// left/right twins; the audit's correlation bugs (B1-8/B1-9) trace back to
/// exactly that loss of information.</summary>
public readonly record struct KeyIdentity(ushort ScanCode, bool IsE0, bool IsE1)
{
    public override string ToString()
        => Localizer.Format("S1069", ScanCode, (IsE0 ? "+E0" : ""), (IsE1 ? "+E1" : ""));
}

/// <summary>
/// K1 - the rich key event that flows from the interception layer to the
/// engine. One-way and immutable: the interception worker raises it, the
/// dispatcher (Phase 2) consumes it off the driver thread. Carries the
/// full device identity and the raw timestamp so latency remains measurable
/// end-to-end instead of being self-referential (audit B1-7).
/// </summary>
public sealed record KeyEvent(
    DeviceId Device,
    KeyIdentity Key,
    uint VirtualKey,
    KeyDirection Direction,
    bool IsRepeat,
    long TimestampQpc)
{
    public bool IsDown => Direction == KeyDirection.Down;
}
