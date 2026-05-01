/*
 * Copyright 2026 Julien Bombled
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.IO;
using System.Windows.Controls;
using Heimdall.App.ViewModels;
using Heimdall.App.Views;
using Heimdall.Core.Logging;
using Heimdall.Core.Models;

namespace Heimdall.App.Services;

/// <summary>
/// Builds the WPF <see cref="ContextMenu"/> for a session tab header
/// right-click. Composition branches on the session type (tool vs
/// connection), split state and hosted view type (an
/// <see cref="EmbeddedSshView"/> unlocks the transcript and macro items).
/// Extracted from <c>MainWindow.xaml.cs</c> to reduce code-behind size
/// and enable targeted unit testing of the menu-building logic.
/// </summary>
/// <remarks>
/// Window-layer actions that need access to <see cref="MainWindow"/>
/// state (fullscreen, floating window creation, split orchestration) are
/// routed through <see cref="ISessionTabContextCallbacks"/>. Everything
/// else (command dispatch, dialog prompts, status-text updates,
/// localization, settings access) is invoked directly on
/// <see cref="MainViewModel"/>.
/// </remarks>
public sealed class SessionTabContextMenuFactory
{
    /// <summary>
    /// Initialises a new <see cref="SessionTabContextMenuFactory"/>.
    /// </summary>
    public SessionTabContextMenuFactory()
    {
    }

    /// <summary>
    /// Builds the right-click context menu for a session tab. The caller
    /// is responsible for opening the returned menu (setting
    /// <see cref="ContextMenu.PlacementTarget"/>, <see cref="ContextMenu.Placement"/>
    /// and <see cref="ContextMenu.IsOpen"/>).
    /// </summary>
    public ContextMenu CreateMenu(
        SessionTabViewModel session,
        MainViewModel vm,
        ISessionTabContextCallbacks callbacks)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(callbacks);

        var menu = new ContextMenu();
        var isToolTab = session.ConnectionType?.StartsWith(
            "TOOL:", StringComparison.OrdinalIgnoreCase) == true;

        AppendCloseItem(menu, session, vm, isToolTab);

        if (!isToolTab)
        {
            AppendConnectionActions(menu, session, vm, callbacks);
        }

        AppendDetachItem(menu, session, vm, callbacks);

        if (session.HostControl is EmbeddedSshView sshView)
        {
            AppendTranscriptItem(menu, session, vm, sshView);
            AppendMacroItems(menu, vm, sshView);
        }

        AppendCloseAllItem(menu, session, vm);

        menu.Items.Add(new Separator());

        AppendSplitItems(menu, session, vm, callbacks);

        return menu;
    }

    // ── Close ────────────────────────────────────────────────────────

    private static void AppendCloseItem(
        ContextMenu menu,
        SessionTabViewModel session,
        MainViewModel vm,
        bool isToolTab)
    {
        var closeItem = new MenuItem
        {
            Header = vm.Localize(isToolTab ? "SessionCloseTab" : "SessionDisconnect")
        };
        closeItem.Click += async (_, _) =>
            await vm.Connection.CloseSessionAsync(session, DisconnectReason.UserAction);
        menu.Items.Add(closeItem);
    }

    // ── Connection-only actions (aspect ratio, fullscreen, duplicate) ──

    private static void AppendConnectionActions(
        ContextMenu menu,
        SessionTabViewModel session,
        MainViewModel vm,
        ISessionTabContextCallbacks callbacks)
    {
        menu.Items.Add(new Separator());

        // Aspect ratio submenu
        var aspectMenu = new MenuItem { Header = vm.Localize("SessionAspectRatio") };
        foreach (var (label, tag) in new[]
        {
            ("Stretch", "Stretch"),
            ("Auto", "Auto"),
            ("16:9", "Ratio16x9"),
            ("4:3", "Ratio4x3"),
            ("21:9", "Ratio21x9")
        })
        {
            var item = new MenuItem { Header = label, Tag = tag };
            item.Click += callbacks.OnAspectRatioClick;
            aspectMenu.Items.Add(item);
        }
        menu.Items.Add(aspectMenu);

        if (session.PrimaryPane.HostControl is EmbeddedRdpView rdpView)
        {
            var resolutionMenu = new MenuItem { Header = vm.Localize("SessionResolution") };
            var matchWindowItem = new MenuItem
            {
                Header = vm.Localize("RdpResolutionMatchWindow"),
                Tag = ResolutionChoice.MatchWindow
            };
            matchWindowItem.Click += (_, _) => callbacks.OnResolutionChanged(
                session.PrimaryPane,
                ResolutionChoice.MatchWindow);
            resolutionMenu.Items.Add(matchWindowItem);

            foreach (var preset in ResolutionPresetCatalog.GetPresets(vm.CurrentSettings))
            {
                var choice = ResolutionChoice.Fixed(preset.Width, preset.Height);
                var item = new MenuItem
                {
                    Header = preset.DisplayText,
                    Tag = choice,
                    ToolTip = rdpView.WouldScaleResolution(preset.Width, preset.Height)
                        ? vm.Localize("RdpResolutionLargerThanWindowTooltip")
                        : null
                };
                item.Click += (_, _) => callbacks.OnResolutionChanged(session.PrimaryPane, choice);
                resolutionMenu.Items.Add(item);
            }

            resolutionMenu.Items.Add(new Separator());

            var customItem = new MenuItem
            {
                Header = vm.Localize("RdpResolutionCustom"),
                Tag = ResolutionChoice.Custom
            };
            customItem.Click += (_, _) => callbacks.OnResolutionChanged(
                session.PrimaryPane,
                ResolutionChoice.Custom);
            resolutionMenu.Items.Add(customItem);

            resolutionMenu.Items.Add(new Separator());

            var saveDefaultItem = new MenuItem
            {
                Header = vm.Localize("RdpResolutionSaveDefaultForServer"),
                Tag = ResolutionChoice.SaveAsDefaultForServer
            };
            saveDefaultItem.Click += (_, _) => callbacks.OnResolutionChanged(
                session.PrimaryPane,
                ResolutionChoice.SaveAsDefaultForServer);
            resolutionMenu.Items.Add(saveDefaultItem);

            menu.Items.Add(resolutionMenu);
        }

        menu.Items.Add(new Separator());

        // Fullscreen toggle
        var fullscreenItem = new MenuItem { Header = vm.Localize("SessionFullscreen") };
        fullscreenItem.Click += (_, _) => callbacks.ToggleFullscreen();
        menu.Items.Add(fullscreenItem);

        // Duplicate tab (reconnect same server in new tab)
        var duplicateItem = new MenuItem { Header = vm.Localize("SessionDuplicateTab") };
        duplicateItem.Click += (_, _) =>
        {
            var lookupId = !string.IsNullOrEmpty(session.OriginalServerId)
                ? session.OriginalServerId
                : session.ServerId;

            if (!string.IsNullOrEmpty(lookupId) && vm.ServerList.ConnectCommand is not null)
            {
                var serverVm = vm.ServerList.Servers.FirstOrDefault(
                    s => string.Equals(s.Id, lookupId, StringComparison.Ordinal));
                if (serverVm is not null)
                {
                    vm.ServerList.ConnectCommand.Execute(serverVm);
                }
            }
        };
        menu.Items.Add(duplicateItem);

        if (session.IsAdHoc && session.AdHocProfileSnapshot is not null)
        {
            var saveAsProfileItem = new MenuItem
            {
                Header = vm.Localize("SessionSaveAsProfile")
            };
            saveAsProfileItem.Click += (_, _) =>
                vm.ServerList.SaveAdHocAsProfileCommand.Execute(session.AdHocProfileSnapshot);
            menu.Items.Add(saveAsProfileItem);
        }
    }

    // ── Detach (branches on split state) ─────────────────────────────

    private static void AppendDetachItem(
        ContextMenu menu,
        SessionTabViewModel session,
        MainViewModel vm,
        ISessionTabContextCallbacks callbacks)
    {
        if (!session.IsSplit)
        {
            var detachItem = new MenuItem { Header = vm.Localize("SessionCtxDetach") };
            detachItem.Click += (_, _) => callbacks.DetachSessionToFloatingWindow(session);
            menu.Items.Add(detachItem);
        }
        else
        {
            var detachSecondaryItem = new MenuItem
            {
                Header = vm.Localize("SplitDetachSecondary"),
                IsEnabled = session.SecondaryHostControl is not null
            };
            detachSecondaryItem.Click += (_, _) => callbacks.DetachSecondaryToFloatingWindow(session);
            menu.Items.Add(detachSecondaryItem);
        }
    }

    // ── Transcript toggle (SSH only) ─────────────────────────────────

    private static void AppendTranscriptItem(
        ContextMenu menu,
        SessionTabViewModel session,
        MainViewModel vm,
        EmbeddedSshView sshView)
    {
        var transcriptItem = new MenuItem
        {
            Header = sshView.IsTranscriptActive
                ? vm.Localize("SessionStopTranscript")
                : vm.Localize("SessionStartTranscript")
        };
        transcriptItem.Click += (_, _) =>
        {
            if (sshView.IsTranscriptActive)
            {
                sshView.StopTranscript();
                vm.StatusText = vm.Localize("SessionTranscriptStopped");
            }
            else
            {
                var logDir = vm.CurrentSettings?.SessionLogDirectory ?? @"logs\sessions";
                if (!Path.IsPathRooted(logDir))
                {
                    logDir = Path.Combine(AppContext.BaseDirectory, logDir);
                }

                var invalidChars = Path.GetInvalidFileNameChars();
                var serverName = string.Concat(
                    session.Title.Select(c => Array.IndexOf(invalidChars, c) >= 0 ? '_' : c));
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var logFile = Path.Combine(logDir, $"transcript_{serverName}_{timestamp}.log");

                sshView.StartTranscript(logFile);
                vm.StatusText = string.Format(vm.Localize("SessionTranscriptStarted"), logFile);
            }
        };
        menu.Items.Add(transcriptItem);
    }

    // ── Macro record toggle + play submenu (SSH only) ────────────────

    private static void AppendMacroItems(
        ContextMenu menu,
        MainViewModel vm,
        EmbeddedSshView sshView)
    {
        // Macro recording toggle
        var macroRecordItem = new MenuItem
        {
            Header = sshView.IsRecordingMacro
                ? vm.Localize("MacroStopRecording")
                : vm.Localize("MacroStartRecording")
        };
        macroRecordItem.Click += async (_, _) =>
        {
            if (sshView.IsRecordingMacro)
            {
                var entries = sshView.StopRecording();
                if (entries.Count > 0)
                {
                    var name = await vm.DialogService.ShowInputAsync(
                        vm.Localize("MacroNameTitle"),
                        vm.Localize("MacroNamePrompt"));

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        var macro = new TerminalMacro
                        {
                            Name = name,
                            Entries = entries
                        };
                        await MacroService.SaveMacroAsync(macro);
                        vm.StatusText = string.Format(vm.Localize("MacroRecordingStopped"), name);
                    }
                }
            }
            else
            {
                sshView.StartRecording();
                vm.StatusText = vm.Localize("MacroRecordingStarted");
            }
        };
        menu.Items.Add(macroRecordItem);

        // Play macro submenu
        var playMenu = new MenuItem { Header = vm.Localize("MacroPlaySubmenu") };

        var macros = MacroService.LoadMacros();
        if (macros.Count == 0)
        {
            var emptyItem = new MenuItem
            {
                Header = vm.Localize("MacroNoMacros"),
                IsEnabled = false
            };
            playMenu.Items.Add(emptyItem);
        }
        else
        {
            foreach (var macro in macros)
            {
                var macroItem = new MenuItem { Header = macro.Name, Tag = macro };
                macroItem.Click += async (s, _) =>
                {
                    if (s is MenuItem { Tag: TerminalMacro m })
                    {
                        vm.StatusText = string.Format(vm.Localize("MacroPlaying"), m.Name);
                        try
                        {
                            await sshView.PlayMacro(m, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            FileLogger.Warn($"Macro playback failed: {ex.Message}");
                        }
                    }
                };
                playMenu.Items.Add(macroItem);
            }
        }
        menu.Items.Add(playMenu);
    }

    // ── Close session (always present) ───────────────────────────────

    private static void AppendCloseAllItem(
        ContextMenu menu,
        SessionTabViewModel session,
        MainViewModel vm)
    {
        var closeAllItem = new MenuItem { Header = vm.Localize("SessionCloseSession") };
        closeAllItem.Click += async (_, _) =>
            await vm.Connection.CloseSessionAsync(session, DisconnectReason.UserAction);
        menu.Items.Add(closeAllItem);
    }

    // ── Split / merge / unsplit items ────────────────────────────────

    private static void AppendSplitItems(
        ContextMenu menu,
        SessionTabViewModel session,
        MainViewModel vm,
        ISessionTabContextCallbacks callbacks)
    {
        if (!session.IsSplit)
        {
            // "Split..." submenu with orientation sub-items
            var splitMenu = new MenuItem { Header = vm.Localize("SplitMenu") };

            var splitH = new MenuItem { Header = vm.Localize("OrientationHorizontal") };
            splitH.Click += (_, _) => callbacks.RequestSplitSession(session, SplitOrientation.Horizontal);
            splitMenu.Items.Add(splitH);

            var splitV = new MenuItem { Header = vm.Localize("OrientationVertical") };
            splitV.Click += (_, _) => callbacks.RequestSplitSession(session, SplitOrientation.Vertical);
            splitMenu.Items.Add(splitV);

            menu.Items.Add(splitMenu);

            // "Merge with..." submenu — nested per session with orientation sub-items
            var otherSessions = vm.Connection.ActiveSessions
                .Where(s => s != session && s.HostControl is not null)
                .ToList();

            if (otherSessions.Count > 0)
            {
                var mergeMenu = new MenuItem { Header = vm.Localize("SplitMergeWith") };

                foreach (var other in otherSessions)
                {
                    var sourceTab = other;
                    var sessionMenu = new MenuItem { Header = sourceTab.Title };

                    // Use OriginalServerId as stable lookup key (ServerId may be empty during connection)
                    var mergeId = !string.IsNullOrEmpty(sourceTab.OriginalServerId)
                        ? sourceTab.OriginalServerId
                        : sourceTab.ServerId;

                    var mergeH = new MenuItem { Header = vm.Localize("OrientationHorizontal") };
                    mergeH.Click += (_, _) => vm.MergeExistingSession(
                        session, mergeId, SplitOrientation.Horizontal);
                    sessionMenu.Items.Add(mergeH);

                    var mergeV = new MenuItem { Header = vm.Localize("OrientationVertical") };
                    mergeV.Click += (_, _) => vm.MergeExistingSession(
                        session, mergeId, SplitOrientation.Vertical);
                    sessionMenu.Items.Add(mergeV);

                    mergeMenu.Items.Add(sessionMenu);
                }

                menu.Items.Add(mergeMenu);
            }
        }
        else
        {
            var unsplit = new MenuItem { Header = vm.Localize("SplitUnsplit") };
            unsplit.Click += (_, _) => callbacks.UnsplitSession(session);
            menu.Items.Add(unsplit);

            var swapItem = new MenuItem { Header = vm.Localize("SplitSwapPanes") };
            swapItem.Click += async (_, _) => await vm.SwapSplitPanesAsync(session);
            menu.Items.Add(swapItem);

            var toggleItem = new MenuItem
            {
                Header = vm.Localize("SplitToggleOrientation"),
                InputGestureText = "Ctrl+Shift+O"
            };
            toggleItem.Click += (_, _) => vm.ToggleSplitOrientation(session);
            menu.Items.Add(toggleItem);

            var closeSecItem = new MenuItem { Header = vm.Localize("SplitCloseSecondary") };
            closeSecItem.Click += (_, _) => vm.CloseSecondaryPaneCommand.Execute(session);
            menu.Items.Add(closeSecItem);
        }
    }
}
