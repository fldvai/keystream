# Keystream v1.0.0

> **Turn any secondary physical keyboard, mechanical numpad, or macropad into a powerful, dedicated macro deck.**

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-blue.svg)](https://github.com/fldvai/keystream)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-green.svg)](LICENSE)
[![Framework](https://img.shields.io/badge/.NET-8.0%20Desktop-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![VirusTotal](https://img.shields.io/badge/VirusTotal-0%2F70%20Clean-success.svg)](https://www.virustotal.com)
[![Code Signed](https://img.shields.io/badge/Authenticode-DigiCert%20RFC%203161-brightgreen.svg)](KeystreamSetup.exe)

---

## Overview

**Keystream** is an open-source, low-latency macro deck controller for Windows. Unlike conventional macro tools that hook into user-mode keyboard APIs (which steal keystrokes from your main typing keyboard), Keystream operates at the hardware filter driver level via **Interception**.

This allows Windows to treat your primary typing keyboard and your secondary keyboard (or numpad) as completely distinct physical entities. You can type normally on your main keyboard while your secondary keyboard becomes an interactive control deck for live streaming, creative workflows, soundboards, and system automation.

---

## Key Features & Capabilities

### True Hardware Keyboard Isolation
* **Hardware-Level Differentiation:** Intercepts and filters inputs exclusively from your designated macro deck. Your main keyboard typing experience remains 100% unaffected.
* **Smart Device Identification Wizard:** Plug in your secondary device, press any key during onboarding, and Keystream captures and pairs its unique hardware ID automatically.
* **Kernel Driver Diagnostics:** Real-time diagnostics in the setup wizard detect whether the filter driver is active, uninstalled, or pending a system restart, providing one-click guidance.

### Multi-Action Trigger Engine
* **Single Tap:** Instant execution for scene changes, mute toggles, sound clips, and shortcuts.
* **Long Press:** Hold-to-activate actions with configurable delay thresholds (e.g., hold for 500ms to emergency-kill stream or reboot).
* **Encoder / Dial Emulation:** Full support for rotary knobs and scrollers with directional step handling.
* **Toggle States & Chords:** Visual state tracking and multi-key combinations.

### Native Production Integrations
* **OBS Studio (WebSocket v5):** Instant scene switching, source visibility, audio muting, stream/record toggles, and replay buffer triggers.
* **Streamer.bot Integration:** Trigger actions, sub-actions, and queue events directly via local WebSocket.
* **Windows Media & Audio:** Master volume adjustment, system mute, media playback (Play/Pause, Next, Previous).
* **Application & Web Launchers:** Launch software with custom arguments or open web URLs in your default browser.
* **Keystroke Macros:** Send complex hotkeys, sequences, and formatted text blocks.

### Dynamic Context-Aware Profiles
* **Window Auto-Switching:** Automatically switch deck layouts based on your active foreground application (e.g., OBS, Adobe Premiere, Blender, DaVinci Resolve, VS Code, or games).
* **Switch-Back Temporary Layers:** Jump to a utility profile and automatically return to your primary profile after triggering an action.
* **Profile Management:** Easily create, clone, reorder, import, and export profiles in clean JSON format.

### Cyber Dark UI & Custom Icon Engine
* Modern, dark cyberpunk aesthetic built with high-performance WPF.
* **Integrated Vector Icon Library:** Access a rich suite of built-in vector icons with custom color tinting.
* **Smart URL Favicon Fetcher:** Paste any web URL to automatically fetch, cache, and display high-resolution website favicons.
* Real-time visual key matrix with live feedback on pressed keys.

### Failsafe & System Protection
* **Panic Bypass Switch:** Global emergency hotkey (`Ctrl + Shift + Esc` by default) instantly disarms low-level interception and restores normal typing to the secondary keyboard.
* **100% Offline & Private:** Zero telemetry, no cloud accounts, and no background analytics. All configurations are stored locally on your machine.

---

## What's Included in this Release

| Component | Version | Description |
|---|---|---|
| **Keystream Core** | `v1.0.0` | Main macro deck application with WPF dark cyber interface |
| **Keystream Setup** | `v1.0.0` | 3.84 MB standalone installer with integrated driver setup and silent update support |
| **Interception Driver** | `v1.0.1` | Signed low-level kernel filter driver for hardware keyboard separation |

---

## Installation & First-Time Setup

1. **Download:**
   Download **`KeystreamSetup.exe`** from the [Assets](#assets) section below.
2. **Run Installer:**
   Run the setup wizard. It will install Keystream to `%LocalAppData%\Keystream` and automatically configure the low-level keyboard filter driver.
3. **One-Time PC Restart:**
   Restart your PC once so Windows can initialize the newly registered kernel filter driver.
4. **Pair Your Deck:**
   Launch Keystream. The **Smart Setup Wizard** will appear. Simply press any key on your secondary keyboard to pair it as your dedicated macro deck.

> [!TIP]
> If you already have .NET 8 Desktop Runtime installed, installation takes less than 5 seconds. If needed, the installer will guide you to the official Microsoft .NET download.

---

## System Requirements

| Specification | Minimum | Recommended |
|---|---|---|
| **Operating System** | Windows 10 64-bit (Version 1809 or higher) | Windows 11 64-bit (Latest build) |
| **Architecture** | x64 (64-bit) | x64 (64-bit) |
| **Runtime** | [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0) | .NET 8 Desktop Runtime (x64) |
| **Secondary Device** | Any standard USB keyboard, wireless keyboard, or numpad | Dedicated mechanical numpad or macropad |
| **Privileges** | Administrator rights required for driver install only | Standard user rights for daily operation |

---

## Security & Verification

We take binary integrity and user trust seriously. The official installer is digitally signed and thoroughly verified:

* **Digital Signature:** Authenticode signed with **RFC 3161 DigiCert SHA-256 Timestamping** (Valid through 2037).
* **VirusTotal Result:** **0 / 70 Detections (100% Clean / Undetected)** across all major security vendors.
* **Binary Size:** 4,026,520 bytes (~3.84 MB).

### File Integrity Checksums

```text
File:       KeystreamSetup.exe
Size:       4,026,520 bytes (3.84 MB)
MD5:        8FD5F5B5B8E88E4EC10D5DC35492BC63
SHA-1:      6B68581023FD1D58A7A71E79383A20B713A78BF2
SHA-256:    DEA1505E13EC7F76D530153D3043E824B65949861ECE8850E0B8E8496039128F
```

To verify the installer checksum in Windows PowerShell:
```powershell
Get-FileHash -Algorithm SHA256 .\KeystreamSetup.exe
```

---

## License & Attribution

Keystream is free and open-source software licensed under the **GNU General Public License v3.0 (GPLv3)**.
Hardware keyboard interception is powered by Francisco Lopes' [Interception](https://github.com/oblitum/Interception) C library.

---

**Full Changelog**: https://github.com/fldvai/keystream/commits/v1.0.0

