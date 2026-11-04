using System;
using Keystream.App.ViewModels;

namespace Keystream.App.Views;

/// <summary>§113 - shared run-lifetime state between the macro modal
/// and the floating panel. The panel has NO settings and NO macro
/// picker of its own: it reads everything here, and the modal writes
/// here on every change. Lives for the app run (resets on restart);
/// never persisted to ConfigStore. WPF-FREE: only System and
/// ViewModels types, so the wb-harness can link this file directly.
/// Owned by AppHost (single instance); the modal, the panel host and
/// the standalone controller all share it. Mutations must call
/// <see cref="NotifyChanged"/> so subscribers (modal gating, panel
/// refresh) re-read.</summary>
public sealed class FloatPanelSession
{
    /// <summary>Id of the macro selected in the macro list / open in
    /// the modal (MacroConfig.Id, a string). Null when no macro is
    /// selected (empty library). The panel NEVER picks a macro.</summary>
    public string? SelectedMacroId { get; set; }

    /// <summary>3-second delay ("ancang-ancang") setting. Default
    /// ON (checked), per the user's P3 answer: a macro-page
    /// configuration like Rekam Up/Down and Rekam Mouse, shared with
    /// the panel while the modal is closed.</summary>
    public bool Delay3s { get; set; } = true;

    /// <summary>Last record Up/Down checkbox value from the modal.
    /// Default OFF (the XAML default).</summary>
    public bool RecordUpDown { get; set; } = false;

    /// <summary>Last Record Delay checkbox value from the modal.
    /// Default ON (the XAML default).</summary>
    public bool RecordDelay { get; set; } = true;

    /// <summary>Last Rekam Mouse checkbox value from the modal.
    /// Default OFF (the XAML default).</summary>
    public bool MouseEnabled { get; set; } = false;

    /// <summary>Last committed mouse record settings from the modal
    /// (gear modal). Default = fresh MouseRecordSettings (scope
    /// Klik+gerak, 12px, 50ms, Absolute, Persis ON).</summary>
    public MouseRecordSettings MouseSettings { get; set; } = new();

    /// <summary>Whether the panel is logically shown. The × control
    /// hides the window but the restore rule (reappear on state
    /// transitions) is owned by the host's hidden flag, not this.</summary>
    public bool PanelVisible { get; set; }

    /// <summary>Last panel position (device-independent pixels).
    /// NaN = never shown; the host picks the top-right default.</summary>
    public double PanelLeft { get; set; } = double.NaN;

    /// <summary>Last panel position (device-independent pixels).</summary>
    public double PanelTop { get; set; } = double.NaN;

    /// <summary>True while ANY recording/playback/countdown session
    /// is active on either surface. The other surface's Record/Play
    /// buttons stay disabled while this is true.</summary>
    public bool SessionActive { get; set; }

    /// <summary>Which surface owns the active session.</summary>
    public FloatPanelSessionOwner SessionOwner { get; set; } = FloatPanelSessionOwner.None;

    /// <summary>§120 Batch 1: true while a standalone-owned session
    /// (countdown, recording or playback) is active. The modal's
    /// panel refresh must never reset such a session's clock or
    /// visuals, and the modal must never claim ownership over it.</summary>
    public bool IsStandaloneSessionActive
        => SessionActive && SessionOwner == FloatPanelSessionOwner.Standalone;

    /// <summary>§120 Batch 1: true while a modal-owned session is
    /// active. A stale owner with an inactive session counts as
    /// neither owner's session.</summary>
    public bool IsModalSessionActive
        => SessionActive && SessionOwner == FloatPanelSessionOwner.Modal;

    /// <summary>§120 Batch 1: the modal may claim session ownership
    /// only when no standalone session is active. A modal-owned or
    /// idle session never blocks a (re-)claim.</summary>
    public bool CanModalTakeOwnership() => !IsStandaloneSessionActive;

    /// <summary>Raised after any mutation (callers must invoke
    /// <see cref="NotifyChanged"/>). Subscribers re-read; the event
    /// is raised on the mutating (UI) thread.</summary>
    public event Action? Changed;

    /// <summary>Raises <see cref="Changed"/>.</summary>
    public void NotifyChanged() => Changed?.Invoke();
}

/// <summary>Owner of the currently active panel session, if any.</summary>
public enum FloatPanelSessionOwner
{
    /// <summary>No session active.</summary>
    None,

    /// <summary>The open macro modal owns the session.</summary>
    Modal,

    /// <summary>The standalone panel controller owns the session
    /// (the modal is closed).</summary>
    Standalone,
}
