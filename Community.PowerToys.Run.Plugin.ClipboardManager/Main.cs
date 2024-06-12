﻿// Copyright (c) Corey Hayward. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Windows.ApplicationModel.DataTransfer;
using Wox.Infrastructure;
using Wox.Plugin;
using Clipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

namespace Community.PowerToys.Run.Plugin.ClipboardManager
{
    public class Main : IPlugin, ISettingProvider, IContextMenu
    {
        private PluginInitContext _context;
        private string _iconPath;
        private int _beginTypeDelay;
        private string _pasterPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Paster", "Paster.exe");

        public string Name => "ClipboardManager";

        public string Description => "Searches the clipboard history and pastes the selected item.";

        public static string PluginID => "2d9351ea495848d98f4771c27ac211e4";

        public IEnumerable<PluginAdditionalOption> AdditionalOptions => new List<PluginAdditionalOption>()
        {
            new PluginAdditionalOption()
            {
                Key = "PasteDelay",
                DisplayLabel = "Paste Delay (ms)",
                DisplayDescription = "Sets how long in milliseconds to wait before paste occurs.",
                NumberValue = 200,
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Numberbox,
            },
        };

        public void Init(PluginInitContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            _context = context;
            _context.API.ThemeChanged += OnThemeChanged;
            UpdateIconPath(_context.API.GetCurrentTheme());
        }

        public List<Result> Query(Query query)
        {
            var results = new List<Result>();
            var clipboardTextItems = GetTextItemsFromClipboardHistory();
            if (!string.IsNullOrWhiteSpace(query?.Search))
            {
                foreach (var item in clipboardTextItems)
                {
                    var text = RunSync(async () => await item.Content.GetTextAsync());
                    if (text.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(CreateResult(item, text));
                    }
                }
            }
            else
            {
                foreach (var item in clipboardTextItems.Take(5))
                {
                    var text = RunSync(async () => await item.Content.GetTextAsync());
                    results.Add(CreateResult(item, text));
                }
            }

            return results;
        }

        private List<ClipboardHistoryItem> GetTextItemsFromClipboardHistory()
        {
            var clipboardHistoryResult = RunSync(async () => await Clipboard.GetHistoryItemsAsync());
            var clipboardTextItems = clipboardHistoryResult.Items.Where(x => x.Content.Contains(StandardDataFormats.Text)).ToList();
            return clipboardTextItems;
        }

        private Result CreateResult(ClipboardHistoryItem item, string text) 
            => new Result()
            {
                Title = text.Trim().Replace("\r\n", "↩").Replace("\r", "↩").Replace("\n", "↩"),
                SubTitle = "Paste this value.",
                IcoPath = _iconPath,
                Action = (context) =>
                {
                    Clipboard.SetHistoryItemAsContent(item);
                    Task.Run(() => RunAsSTAThread(() =>
                    {
                        Thread.Sleep(_beginTypeDelay);
                        //SendKeys.SendWait("^(v)");
                    }));
                    return true;
                },
                ContextData = item,
            };

        [DllImport("User32.dll")]
        static extern int SetForegroundWindow(IntPtr point);

        [DllImport("User32.dll")]
        static extern IntPtr GetForegroundWindow();

        public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
        {
            return new List<ContextMenuResult>
            {
                new()
                {
                    Title = "Run as administrator (Ctrl+Shift+Enter)",
                    Glyph = "\xE7EF",
                    FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                    AcceleratorKey = Key.Enter,
                    AcceleratorModifiers = ModifierKeys.Control | ModifierKeys.Shift,
                    PluginName = Name,
                    Action = _ =>
                    {
                        Clipboard.SetHistoryItemAsContent((ClipboardHistoryItem)selectedResult.ContextData);
                        Task.Run(() => RunAsSTAThread(() =>
                        {
                            Thread.Sleep(_beginTypeDelay);
                            var foregroundWindow = GetForegroundWindow();
                            Helper.OpenInShell(_pasterPath, runAs: Helper.ShellRunAsType.Administrator, runWithHiddenWindow: true);
                            SetForegroundWindow(foregroundWindow);
                        }));

                        return true;
                    },
                },
            };
        }
        private void OnThemeChanged(Theme currentTheme, Theme newTheme)
        {
            UpdateIconPath(newTheme);
        }

        private void UpdateIconPath(Theme theme)
        {
            if (theme == Theme.Light || theme == Theme.HighContrastWhite)
            {
                _iconPath = "Images/ClipboardManager.light.png";
            }
            else
            {
                _iconPath = "Images/ClipboardManager.dark.png";
            }
        }

        public System.Windows.Controls.Control CreateSettingPanel()
        {
            throw new NotImplementedException();
        }

        public void UpdateSettings(PowerLauncherPluginSettings settings)
        {
            if (settings?.AdditionalOptions is null)
            {
                return;
            }

            var typeDelay = settings.AdditionalOptions.FirstOrDefault(x => x.Key == "PasteDelay");
            _beginTypeDelay = (int)(typeDelay?.NumberValue ?? 200);
        }

        /// <summary>
        /// Start an Action within an STA Thread
        /// </summary>
        /// <param name="action">The action to execute in the STA thread</param>
        static void RunAsSTAThread(Action action)
        {
            AutoResetEvent @event = new AutoResetEvent(false);
            Thread thread = new Thread(
                () =>
                {
                    action();
                    @event.Set();
                });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            @event.WaitOne();
        }

        private T RunSync<T>(Func<Task<T>> func)
        {
            return Task.Run(func).GetAwaiter().GetResult();
        }
    }
}
