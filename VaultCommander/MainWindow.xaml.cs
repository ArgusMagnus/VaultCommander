﻿using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using VaultCommander.Commands;
using VaultCommander.Vaults;
using WinForms = System.Windows.Forms;

namespace VaultCommander;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
sealed partial class MainWindow : Window
{
    readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    readonly WinForms.NotifyIcon _notifyIcon = new();
    readonly IReadOnlyDictionary<string, ICommand> _commands;
    readonly MainVM _vm = new();
    readonly IReadOnlyList<IVault> _vaults = IVaultFactory.CreateVaults(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Terminal.Constants.DataDirectory));
    readonly IReadOnlyDictionary<string, IVault> _vaultsByUriScheme;
    readonly string[] _arguments;

    bool _hideOnCommandExecute = true;
    bool _cancelClose = true;
    CurrentWindowInformationWindow? _currentWindowInfoWindow;

    public MainWindow(string[] arguments)
    {
        Title = nameof(VaultCommander);
        DataContext = _vm;
        _vaultsByUriScheme = _vaults.ToDictionary(x => x.UriScheme, StringComparer.OrdinalIgnoreCase);
        _arguments = arguments;

        InitializeComponent();
        _notifyIcon.Text = Title;
        _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        _notifyIcon.MouseClick += OnNotifyIconClicked;
        _notifyIcon.BalloonTipClicked += (_, _) => Show();

        var item = new WinForms.ToolStripMenuItem { Text = "Beenden" };
        item.Click += (_, args) => OnMenuExitClicked(null, null);
        _notifyIcon.ContextMenuStrip = new()
        {
            Items = { item }
        };

        _commands = typeof(MainWindow).Assembly.DefinedTypes
            .Where(x => !x.IsAbstract && !x.IsInterface && x.ImplementedInterfaces.Contains(typeof(ICommand)))
            .Select(x => (ICommand)Activator.CreateInstance(x)!)
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

        Loaded += OnLoaded;
    }

    internal void ShowBalloonTip(string title, string text, WinForms.ToolTipIcon icon)
        => _notifyIcon.ShowBalloonTip(0, title, text, icon);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (await Utils.GetLatestRelease() is ReleaseInfo release && Version.TryParse(release.Version?.TrimStart('v'), out var releaseVersion) && Version.TryParse(_vm.Version.Split('-', '+')[0], out var currentVersion) && releaseVersion > currentVersion)
        {
            using var progressBox = await ProgressBox.Show();
            progressBox.StepText = "0 / 1";
            progressBox.StepProgress = 0.5;
            progressBox.DetailText = "Update herunterladen...";

            void OnProgress(double progress)
            {
                progressBox.DetailProgress = progress * 2;
                if (progress is 0.5)
                {
                    progressBox.StepText = "1 / 1";
                    progressBox.StepProgress = 1;
                    progressBox.DetailText = "Update installieren...";
                }
            }

            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                await Utils.DownloadRelease(release, dir, OnProgress);
                var startInfo = new ProcessStartInfo
                {
                    FileName = Directory.EnumerateFiles(dir, Path.GetFileName(Environment.ProcessPath!)).First().Replace(".exe", $".{nameof(Terminal)}.exe"),
                    ArgumentList = { nameof(Terminal.Verbs.Install), AppDomain.CurrentDomain.BaseDirectory, $"{Environment.ProcessId}" }
                };
                if (Debugger.IsAttached)
                    startInfo.ArgumentList.Add("/d");
                using (var process = Process.Start(startInfo)) { }
                await Close();
                return;
            }
            catch (Exception)
            {
                Directory.Delete(dir, true);
                throw;
            }
        }

        static MenuItem InsertAccountItem(MenuItem parent, MenuItem? beforeItem, IVault vault, StatusDto status, IEnumerable<string> commandSchemes)
        {
            var item = new MenuItem { Header = $"{status.UserEmail} ({vault.VaultName})" };
            if (beforeItem is not null)
                parent.Items.Insert(parent.Items.IndexOf(beforeItem), item);
            else
                parent.Items.Add(item);

            MenuItem subItem = new() { Header = "Synchronisieren" };
            subItem.Click += async (_, _) => await vault.Sync();
            item.Items.Add(subItem);
            subItem = new() { Header = "Uris updaten" };
            subItem.Click += async (_, _) => await vault.UpdateUris(commandSchemes);
            item.Items.Add(subItem);
            subItem = new() { Header = "Abmelden" };
            subItem.Click += async (_, _) =>
            {
                await vault.Logout();
                parent.Items.Remove(item);
            };
            item.Items.Add(subItem);

            return item;
        }

        foreach (var vault in _vaults)
        {
            var status = await vault.Initialize();
            if (status is not null && status.Status is not Status.Unauthenticated)
                InsertAccountItem(_menuItemAccounts, _menuItemLogin, vault, status, _commands.Keys);

            var menuItemLogin = new MenuItem { Header = $"{vault.VaultName}..." };
            menuItemLogin.Click += async (_, _) =>
            {
                var status = await vault.Login();
                if (status is not null && status.Status is not Status.Unauthenticated)
                {
                    InsertAccountItem(_menuItemAccounts, _menuItemLogin, vault, status, _commands.Keys);
                    //menuItemLogin.IsEnabled = false;
                }
            };
            _menuItemLogin.Items.Add(menuItemLogin);
        }

        foreach (var uri in _arguments)
            await TryHandleVaultUri(uri, true);

        if (_arguments is { Length: 0 })
            await Task.WhenAll(_vaults.Select(x => Task.Run(() => RegisterUriHandlers(x.UriScheme))));

        return;
        static void RegisterUriHandlers(string scheme)
        {
            using var regKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{scheme}", true);
            regKey.SetValue(null, $"URL: {scheme} protocol ({nameof(VaultCommander)})");
            regKey.SetValue("URL Protocol", string.Empty);
            using var subKey = regKey.CreateSubKey(@"shell\open\command", true);
            subKey.SetValue(null, $@"""{Environment.ProcessPath}"" ""%1""");
        }
    }

    private void OnNotifyIconClicked(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button is WinForms.MouseButtons.Left)
        {
            if (_hideOnCommandExecute = Visibility is Visibility.Visible)
                Hide();
            else
                Show();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _notifyIcon.Visible = true;

        var source = (HwndSource)PresentationSource.FromVisual(this);
        source.AddHook(WndProc);
        AddClipboardFormatListener(source.Handle);

        return;

        [DllImport("User32")]
        static extern bool AddClipboardFormatListener(nint hwnd);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (e.Cancel = _cancelClose)
        {
            _hideOnCommandExecute = true;
            Hide();
        }
    }

    private new async Task Close()
    {
        Hide();

        [DllImport("User32")]
        static extern bool RemoveClipboardFormatListener(nint hwnd);
        var source = (HwndSource)PresentationSource.FromVisual(this);
        source.RemoveHook(WndProc);
        RemoveClipboardFormatListener(source.Handle);

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        foreach (var vault in _vaults)
        {
            if (vault is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else if (vault is IDisposable disposable)
                disposable.Dispose();
        }

        _cancelClose = false;
        base.Close();
    }

    private async void OnMenuExitClicked(object? sender, RoutedEventArgs? e) => await Close();

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WM_CLIPBOARDUPDATE = 0x031D;

        if (msg is WM_CLIPBOARDUPDATE && TryHandleVaultUri(GetClipboardText(), false).Result)
        {
            try { Clipboard.Clear(); }
            catch (COMException) { }
            handled = true;
        }

        return nint.Zero;

        static string GetClipboardText()
        {
            try { return Clipboard.GetText(); }
            catch (COMException) { }
            var delay = TimeSpan.FromMilliseconds(10);
            for (int i = 0; i < 9; i++)
            {
                Thread.Sleep(delay);
                try { return Clipboard.GetText(); }
                catch (COMException) { }
                delay *= 1.5;
            }
            return "";
        }
    }

    private async ValueTask<bool> TryHandleVaultUri(string uri, bool wait)
    {
        var parts = uri.Split(':', 2);
        var silent = false;
        var uid = parts.ElementAtOrDefault(1);
        if (!_vaultsByUriScheme.TryGetValue(parts[0], out var vault))
            return false;

        var task = HandleUri(vault, uid, silent);
        if (wait)
            await task.ConfigureAwait(false);
        return true;
    }

    private async Task HandleUri(IVault vault, string? uid, bool silent)
    {
        await Task.Yield();
        foreach (Button button in _buttons.Children)
            button.Click -= OnCommandClicked;
        _buttons.Children.Clear();
        _vm.SelectedEntry = null;

        if (string.IsNullOrEmpty(uid))
            return;

        static Point GetMousePosition()
        {
            var point = WinForms.Control.MousePosition;
            return new(point.X, point.Y);
        }
        var pos = PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice.Transform(GetMousePosition());
        pos.X -= ActualWidth / 2;
        pos.Y -= ActualHeight / 2;
        Left = pos.X;
        Top = pos.Y;
        Topmost = true;
        Show();
        _ = Dispatcher.InvokeAsync(() => Topmost = false, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        var status = await vault.GetStatus();
        if (status is null || status.Status is Status.Unauthenticated)
        {
            status = await vault.Login();
            if (status is null || status.Status is Status.Unauthenticated)
                return;
        }

        Record? record = null;
        try { record = await vault.GetItem(uid); }
        catch { }

        if (record?.Id != uid)
            record = await vault.UpdateUris(_commands.Keys, uid);

        if (record is null)
        {
            if (!silent)
                MessageBox.Show(this, $"Es wurde kein Eintrag mit der UID '{uid}' gefunden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _vm.SelectedEntry = new(record);
        foreach (var field in record.Fields.Where(x => !string.IsNullOrEmpty(x.Value)))
        {
            var parts = field.Value!.Split(':', 2);
            if (parts.Length is not 2 || !_commands.TryGetValue(parts[0], out var bwCommand))
                continue;
            var button = new Button { Content = field.Name, IsEnabled = bwCommand.CanExecute, Margin = new(0, 10, 0, 0), Tag = new ButtonTag(vault, record.Id, bwCommand, parts[1]) };
            button.Click += OnCommandClicked;
            _buttons.Children.Add(button);
        }
    }

    sealed record ButtonTag(IVault Vault, string ItemId, ICommand Command, string Arguments);

    [GeneratedRegex(@"\{(?<N>\w+)(?:@(?<R>(?>(?>(?<c>\{)?[\w@\-]*)+(?>(?<-c>\})?)+)+))?\}")]
    private static partial Regex GetArgumentParserRegex(); 

    private async void OnCommandClicked(object sender, RoutedEventArgs e)
    {
        if (_hideOnCommandExecute)
            Hide();

        var button = (Button)sender;
        button.IsEnabled = false;
        try { await InvokeCommand((ButtonTag)button.Tag); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, ex.GetType().FullName, MessageBoxButton.OK, MessageBoxImage.Error); }
        button.IsEnabled = true;

        return;

        async Task InvokeCommand(ButtonTag tag)
        {
            JsonNode argsNode = (string.IsNullOrEmpty(tag.Arguments) ? null : JsonNode.Parse(tag.Arguments)) ?? new JsonObject();

            if (tag.Command.RequireDisconnect)
            {
                foreach (var cmd in _commands.Values)
                {
                    if (!await cmd.Disconnect())
                        return;
                }
            }

            Dictionary<string, Record?> records = new();
            var regex = GetArgumentParserRegex();

            string EvaluateMatch(Match match)
            {
                string uid = tag.ItemId;
                var r = match.Groups["R"];
                if (r.Success)
                    uid = regex.Replace(r.Value, EvaluateMatch);
                var item = GetItem(tag.Vault, uid, records).Result;
                if (item is null)
                    return match.Value;

                var name = match.Groups["N"].Value;
                var field = item.Fields.FirstOrDefault(x => string.Equals(name, x.Name, StringComparison.OrdinalIgnoreCase));
                if (field is not null)
                    return regex.Replace(field.Value ?? "", EvaluateMatch);
                if (string.Equals(name, "Name", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Title", StringComparison.OrdinalIgnoreCase))
                    return regex.Replace(item.Name ?? "", EvaluateMatch);
                return match.Value;
            };
            await ReplacePlaceholders(argsNode, null, null, tag.Vault, tag.ItemId, records, regex, EvaluateMatch);

            var args = argsNode.Deserialize(tag.Command.ArgumentsType, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (args is not null)
                await tag.Command.Execute(args);

            return;

            static async Task<Record?> GetItem(IVault vault, string uid, IDictionary<string, Record?> items)
            {
                if (!items.TryGetValue(uid, out var record))
                {
                    record = await vault.GetItem(uid, true).ConfigureAwait(false);
                    items.Add(uid, record);
                }
                return record;
            }

            static async Task ReplacePlaceholders(JsonNode? node, string? name, int? index, IVault vault, string? itemId, IDictionary<string, Record?> records, Regex regex, MatchEvaluator matchEvaluator)
            {
                if (node is JsonObject obj)
                {
                    Record? record = null;
                    if (obj.TryGetPropertyValue("@", out var refNode))
                    {
                        if (refNode is JsonValue value && value.TryGetValue(out string? uid))
                        {
                            obj.Remove("@");
                            record = await GetItem(vault, uid, records).ConfigureAwait(false);
                        }
                    }
                    else if (obj.Parent is null && itemId is not null)
                    {
                        record = await GetItem(vault, itemId, records).ConfigureAwait(false);
                    }
                    if (record is not null)
                    {
                        var uid = record.Id;
                        var dict = (IDictionary<string, JsonNode?>)obj;
                        foreach (var field in record.Fields.Where(x => !dict.Keys.Contains(x.Name, StringComparer.OrdinalIgnoreCase)))
                            obj.Add(field.Name, JsonValue.Create($"{{{field.Name}@{uid}}}"));
                    }

                    foreach (var prop in obj.AsEnumerable().ToList())
                        await ReplacePlaceholders(prop.Value, prop.Key, null, vault, null, records, regex, matchEvaluator);
                }
                else if (node is JsonArray array)
                {
                    for (int i = 0; i < array.Count; i++)
                        await ReplacePlaceholders(array[i], null, i, vault, null, records, regex, matchEvaluator).ConfigureAwait(false);
                }
                else if (node is JsonValue value && value.TryGetValue(out string? str))
                {
                    if (index is not null)
                        value.Parent![index.Value] = JsonValue.Create(regex.Replace(str, matchEvaluator));
                    else
                        value.Parent![name!] = JsonValue.Create(regex.Replace(str, matchEvaluator));
                }
            }
        }
    }

    private void OnMenuToolsShowWindowInformationClicked(object sender, RoutedEventArgs e)
    {
        if (_currentWindowInfoWindow is null)
        {
            _currentWindowInfoWindow = new();
            _currentWindowInfoWindow.Closed += (_, _) => _currentWindowInfoWindow = null;
            _currentWindowInfoWindow.Show();
        }
    }
}