using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Keystream.Actions.Common;
using Keystream.Core.Actions;
using Keystream.Core.Localization;

namespace Keystream.Actions.Steam;

public sealed class SteamAction : IAction
{
    private static bool _bigPictureState = false;
    private static bool _statusToggleState = false;
    private static bool _downloadsToggleState = false;
    private static bool _friendsToggleState = false;

    public ActionDescriptor Descriptor => new(
        "steam",
        Localizer.Get("S1848"),
        Localizer.Get("S1071"),
        Localizer.Get("S1849")
    );

    public ValidationResult Validate(JsonObject parameters)
    {
        string command = ParamReader.GetString(parameters, "command", "Command")?.ToLowerInvariant() ?? "";
        if (string.IsNullOrWhiteSpace(command))
        {
            return ValidationResult.Invalid(Localizer.Get("S1884"));
        }

        if (command == "launch")
        {
            string? appId = ParamReader.GetString(parameters, "appId", "AppId");
            if (string.IsNullOrWhiteSpace(appId))
            {
                return ValidationResult.Invalid(Localizer.Get("S1885"));
            }
        }

        return ValidationResult.Valid;
    }

    public Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context)
    {
        string command = ParamReader.GetString(parameters, "command", "Command")?.ToLowerInvariant() ?? "launch";
        bool isToggle = ParamReader.GetBool(parameters, false, "isToggle", "IsToggle");

        try
        {
            var installInfo = SteamDiscovery.GetInstallInfo();
            if (!installInfo.IsInstalled)
            {
                return Task.FromResult(ActionResult.Fail(Localizer.Get("S1855")));
            }

            switch (command)
            {
                case "launch":
                {
                    string appId = ParamReader.GetString(parameters, "appId", "AppId") ?? "";
                    string gameName = ParamReader.GetString(parameters, "gameName", "GameName") ?? appId;
                    string runningAction = ParamReader.GetString(parameters, "runningAction", "RunningAction") ?? "focus";

                    if (string.IsNullOrWhiteSpace(appId))
                    {
                        return Task.FromResult(ActionResult.Fail(Localizer.Get("S1886")));
                    }

                    if (isToggle && SteamDiscovery.IsGameRunning(appId))
                    {
                        if (string.Equals(runningAction, "close", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!SteamDiscovery.CloseGame(appId))
                                return Task.FromResult(ActionResult.Fail(Localizer.Format("S1887", gameName)));
                            return Task.FromResult(ActionResult.Ok(0, Localizer.Format("S1888", gameName)));
                        }
                        else
                        {
                            if (!SteamDiscovery.BringGameToFront(appId))
                                return Task.FromResult(ActionResult.Fail(Localizer.Format("S1889", gameName)));
                            return Task.FromResult(ActionResult.Ok(0, Localizer.Format("S1890", gameName)));
                        }
                    }

                    RunShellUri($"steam://rungameid/{appId}");
                    return Task.FromResult(ActionResult.Ok(0, Localizer.Format("S1891", gameName)));
                }

                case "bigpicture":
                {
                    string subCommand = ParamReader.GetString(parameters, "subCommand", "SubCommand", "mode", "Mode")?.ToLowerInvariant() ?? "";
                    if (string.IsNullOrEmpty(subCommand))
                    {
                        subCommand = isToggle ? "toggle" : "open";
                    }

                    if (subCommand == "open")
                    {
                        RunShellUri("steam://open/bigpicture");
                        return Task.FromResult(ActionResult.Ok(0, Localizer.Get("S1892")));
                    }
                    else if (subCommand == "close")
                    {
                        RunShellUri("steam://close/bigpicture");
                        return Task.FromResult(ActionResult.Ok(0, Localizer.Get("S1893")));
                    }
                    else // "toggle"
                    {
                        IntPtr bpWindow = SteamDiscovery.FindBigPictureWindow();
                        bool isBpRunning = bpWindow != IntPtr.Zero && SteamDiscovery.IsWindowVisible(bpWindow);

                        if (isBpRunning || _bigPictureState)
                        {
                            RunShellUri("steam://close/bigpicture");
                            _bigPictureState = false;
                            return Task.FromResult(ActionResult.Ok(0, Localizer.Get("S1893")));
                        }
                        else
                        {
                            RunShellUri("steam://open/bigpicture");
                            _bigPictureState = true;
                            return Task.FromResult(ActionResult.Ok(0, Localizer.Get("S1892")));
                        }
                    }
                }

                case "openbigpicture":
                {
                    RunShellUri("steam://open/bigpicture");
                    return Task.FromResult(ActionResult.Ok(0, Localizer.Get("S1892")));
                }

                case "closebigpicture":
                {
                    RunShellUri("steam://close/bigpicture");
                    return Task.FromResult(ActionResult.Ok(0, Localizer.Get("S1893")));
                }

                case "status":
                {
                    string status = ParamReader.GetString(parameters, "status", "Status")?.ToLowerInvariant() ?? "online";
                    string secondStatus = ParamReader.GetString(parameters, "secondStatus", "SecondStatus")?.ToLowerInvariant() ?? "invisible";

                    string chosenStatus = status;
                    if (isToggle)
                        chosenStatus = _statusToggleState ? secondStatus : status;

                    RunShellUri($"steam://friends/status/{chosenStatus}");
                    // State dibalik SETELAH launch sukses: RunShellUri melempar bila gagal,
                    // lalu catch luar Fail dan state tetap utuh.
                    if (isToggle)
                        _statusToggleState = !_statusToggleState;
                    return Task.FromResult(ActionResult.Ok(0, Localizer.Format("S1894", chosenStatus)));
                }

                case "friends":
                {
                    if (isToggle)
                    {
                        IntPtr friendsHwnd = SteamDiscovery.FindFriendsWindow();
                        bool isFriendsOpenAndVisible = friendsHwnd != IntPtr.Zero &&
                                                       SteamDiscovery.IsWindowVisible(friendsHwnd) &&
                                                       !SteamDiscovery.IsIconic(friendsHwnd);

                        if (isFriendsOpenAndVisible || (_friendsToggleState && friendsHwnd != IntPtr.Zero))
                        {
                            _friendsToggleState = false;
                            if (friendsHwnd != IntPtr.Zero)
                            {
                                SteamDiscovery.CloseWindowGracefully(friendsHwnd);
                            }
                            return Task.FromResult(ActionResult.Ok(0, Localizer.Get("S1895")));
                        }
                        else
                        {
                            _friendsToggleState = true;
                            RunShellUri("steam://open/friends");
                            if (friendsHwnd != IntPtr.Zero)
                            {
                                SteamDiscovery.RestoreAndBringToFront(friendsHwnd);
                            }
                            return Task.FromResult(ActionResult.Ok(0, Localizer.Get("S1896")));
                        }
                    }
                    else
                    {
                        RunShellUri("steam://open/friends");
                        return Task.FromResult(ActionResult.Ok(0, Localizer.Get("S1896")));
                    }
                }

                case "downloads":
                {
                    if (isToggle)
                    {
                        IntPtr steamHwnd = SteamDiscovery.FindSteamMainWindow();
                        bool isWindowOpenAndVisible = steamHwnd != IntPtr.Zero &&
                                                      SteamDiscovery.IsWindowVisible(steamHwnd) &&
                                                      !SteamDiscovery.IsIconic(steamHwnd);

                        bool shouldHide = isWindowOpenAndVisible &&
                                          (_downloadsToggleState || SteamDiscovery.IsWindowOrChildFocused(steamHwnd));

                        if (shouldHide)
                        {
                            _downloadsToggleState = false;
                            SteamDiscovery.MinimizeOrHideWindow(steamHwnd);
                            return Task.FromResult(ActionResult.Ok(0, Localizer.Get("S1897")));
                        }
                        else
                        {
                            _downloadsToggleState = true;
                            RunShellUri("steam://open/downloads");
                            if (steamHwnd != IntPtr.Zero)
                            {
                                SteamDiscovery.RestoreAndBringToFront(steamHwnd);
                            }
                            return Task.FromResult(ActionResult.Ok(0, Localizer.Get("S1898")));
                        }
                    }
                    else
                    {
                        RunShellUri("steam://open/downloads");
                        return Task.FromResult(ActionResult.Ok(0, Localizer.Get("S1898")));
                    }
                }

                case "openclose":
                {
                    if (SteamDiscovery.IsSteamRunning())
                    {
                        RunShellUri("steam://exit");
                        return Task.FromResult(ActionResult.Ok(0, Localizer.Get("S1899")));
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(installInfo.SteamExe) && File.Exists(installInfo.SteamExe))
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = installInfo.SteamExe,
                                UseShellExecute = true
                            });
                        }
                        else
                        {
                            RunShellUri("steam://open/main");
                        }
                        return Task.FromResult(ActionResult.Ok(0, Localizer.Get("S1900")));
                    }
                }

                case "exit":
                {
                    RunShellUri("steam://exit");
                    return Task.FromResult(ActionResult.Ok(0, Localizer.Get("S1899")));
                }

                default:
                    return Task.FromResult(ActionResult.Fail(Localizer.Format("S1901", command)));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(ActionResult.Fail(Localizer.Format("S1902", ex.Message)));
        }
    }

    public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
        => ExecuteAsync(parameters, context);

    private static void RunShellUri(string uri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = uri,
            UseShellExecute = true
        });
    }
}

