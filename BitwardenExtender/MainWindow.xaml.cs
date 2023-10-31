using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using BitwardenExtender.BwCommands;
using BitwardenExtender.Vaults;
using WinForms = System.Windows.Forms;

namespace BitwardenExtender;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
sealed partial class MainWindow : Window
{
    readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    readonly WinForms.NotifyIcon _notifyIcon = new();
    readonly IReadOnlyDictionary<string, IBwCommand> _commands;
    readonly MainVM _vm = new();
    bool _cancelClose = true;
    int _progressBarScope = 0;
    CurrentWindowInformationWindow? _currentWindowInfoWindow;

    readonly IReadOnlyList<IVault> _vaults = IVaultFactory.CreateVaults(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Terminal.Constants.DataDirectory));
    readonly IReadOnlyDictionary<string, IVault> _vaultsByUriScheme;

    public MainWindow()
    {
        DataContext = _vm;
        _vaultsByUriScheme = _vaults.ToDictionary(x => x.UriScheme, StringComparer.OrdinalIgnoreCase);

        InitializeComponent();
        _notifyIcon.Text = Title;
        _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        _notifyIcon.MouseClick += OnNotifyIconClicked;

        var item = new WinForms.ToolStripMenuItem { Text = "Beenden" };
        item.Click += (_, args) => OnMenuExitClicked(null, null);
        _notifyIcon.ContextMenuStrip = new()
        {
            Items = { item }
        };

        _commands = typeof(MainWindow).Assembly.DefinedTypes
            .Where(x => !x.IsAbstract && !x.IsInterface && x.ImplementedInterfaces.Contains(typeof(IBwCommand)))
            .Select(x => (IBwCommand)Activator.CreateInstance(x)!)
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

        Loaded += OnLoaded;

    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        static MenuItem InsertAccountItem(MenuItem parent, MenuItem? beforeItem, IVault vault, StatusDto status)
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
            subItem.Click += async (_, _) => await vault.UpdateUris();
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
                InsertAccountItem(_menuItemAccounts, _menuItemLogin, vault, status);

            var menuItemLogin = new MenuItem { Header = $"{vault.VaultName}..." };
            menuItemLogin.Click += async (_, _) =>
            {
                var status = await vault.Login();
                if (status is not null && status.Status is not Status.Unauthenticated)
                {
                    InsertAccountItem(_menuItemAccounts, _menuItemLogin, vault, status);
                    //menuItemLogin.IsEnabled = false;
                }
            };
            _menuItemLogin.Items.Add(menuItemLogin);
        }
    }

    private void OnNotifyIconClicked(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button is WinForms.MouseButtons.Left)
        {
            if (Visibility is not Visibility.Visible)
                Show();
            else
                Hide();
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

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        foreach (var vault in _vaults)
        {
            if (vault is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (vault is IDisposable disposable)
                disposable.Dispose();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!(e.Cancel = _cancelClose))
        {
            [DllImport("User32")]
            static extern bool RemoveClipboardFormatListener(nint hwnd);

            var source = (HwndSource)PresentationSource.FromVisual(this);
            source.RemoveHook(WndProc);
            RemoveClipboardFormatListener(source.Handle);
        }
        base.OnClosing(e);
        Hide();
    }

    private void OnMenuExitClicked(object? sender, RoutedEventArgs? e)
    {
        _cancelClose = false;
        Close();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WM_CLIPBOARDUPDATE = 0x031D;

        if (msg is WM_CLIPBOARDUPDATE)
        {
            var text = Clipboard.GetText();
            var parts = Clipboard.GetText().Split(':', 2);
            if (_vaultsByUriScheme.TryGetValue(parts[0], out var vault))
            {
                Clipboard.Clear();
                OnClipboardCommand(vault, parts.ElementAtOrDefault(1));
                handled = true;
            }
        }

        return nint.Zero;
    }

    private async void OnClipboardCommand(IVault vault, string? strGuid)
    {
        foreach (Button button in _buttons.Children)
            button.Click -= OnBwCommandClicked;
        _buttons.Children.Clear();
        _vm.SelectedEntry = null;

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
        Show();
        Activate();

        var status = await vault.GetStatus();
        if (status is null || status.Status is Status.Unauthenticated)
        {
            status = await vault.Login();
            if (status is null || status.Status is Status.Unauthenticated)
                return;
        }

        if (!Guid.TryParse(strGuid, out var guid))
        {
            MessageBox.Show(this, $"'{strGuid}' ist keine gültige GUID.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ItemTemplate? item = null;
        try { item = await vault.GetItem(guid); }
        catch { }

        if (item?.Id != guid)
            item = await vault.UpdateUris(guid);

        if (item is null)
        {
            MessageBox.Show(this, $"Es wurde kein Eintrag mit der GUID '{strGuid}' gefunden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _vm.SelectedEntry = new(item);
        foreach (var field in item.Fields.Where(x => !string.IsNullOrEmpty(x.Value)))
        {
            var parts = field.Value!.Split(':', 2);
            if (parts.Length is not 2 || !_commands.TryGetValue(parts[0], out var bwCommand))
                continue;
            var button = new Button { Content = field.Name, IsEnabled = bwCommand.CanExecute, Margin = new(0, 10, 0, 0), Tag = new ButtonTag(vault, item.Id, bwCommand, parts[1]) };
            button.Click += OnBwCommandClicked;
            _buttons.Children.Add(button);
        }
    }

    sealed record ButtonTag(IVault Vault, Guid ItemId, IBwCommand Command, string Arguments);

    private async void OnBwCommandClicked(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        //_vm.StatusBarText = $"Executing {button.Content}...";
        //using var scope = ShowProgressBar();
        button.IsEnabled = false;
        await InvokeBwCommand((ButtonTag)button.Tag);
        button.IsEnabled = true;

        return;

        async Task InvokeBwCommand(ButtonTag tag)
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

            Dictionary<Guid, ItemTemplate?> items = new();
            const string Pattern = @"\{(?<N>\w+)(?:@(?<R>(?>(?>(?<c>\{)?[\w@\-]*)+(?>(?<-c>\})?)+)+))?\}";

            string EvaluateMatch(Match match)
            {
                Guid guid = default;
                var r = match.Groups["R"];
                if (!r.Success)
                    guid = tag.ItemId;
                else if (!Guid.TryParse(r.Value, out guid) && !Guid.TryParse(Regex.Replace(r.Value, Pattern, EvaluateMatch), out guid))
                    return match.Value;
                var item = GetItem(tag.Vault, guid, items).Result;
                if (item is null)
                    return match.Value;

                var name = match.Groups["N"].Value;
                if (string.Equals(name, "Name", StringComparison.OrdinalIgnoreCase))
                    return Regex.Replace(item.Name ?? string.Empty, Pattern, EvaluateMatch);
                if (string.Equals(name, "Username", StringComparison.OrdinalIgnoreCase))
                    return Regex.Replace(item.Login?.Username ?? string.Empty, Pattern, EvaluateMatch);
                if (string.Equals(name, "Password", StringComparison.OrdinalIgnoreCase))
                    return Regex.Replace(item.Login?.Password?.GetAsClearText() ?? string.Empty, Pattern, EvaluateMatch);
                if (string.Equals(name, "TOTP", StringComparison.OrdinalIgnoreCase))
                    return Regex.Replace(item.Login?.Totp ?? string.Empty, Pattern, EvaluateMatch);
                var field = item.Fields.FirstOrDefault(x => string.Equals(name, x.Name, StringComparison.OrdinalIgnoreCase));
                if (field is not null)
                    return Regex.Replace(field.Value ?? string.Empty, Pattern, EvaluateMatch);
                return match.Value;
            };
            await ReplacePlaceholders(argsNode, null, null, tag.Vault, tag.ItemId, items, Pattern, EvaluateMatch);

            var args = argsNode.Deserialize(tag.Command.ArgumentsType, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (args is not null)
                await tag.Command.Execute(args);

            return;

            static async Task<ItemTemplate?> GetItem(IVault vault, Guid guid, IDictionary<Guid, ItemTemplate?> items)
            {
                if (!items.TryGetValue(guid, out var item))
                {
                    item = await vault.GetItem(guid).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(item?.Login?.Totp))
                        item = item with { Login = item.Login with { Totp = await vault.GetTotp(guid) } };
                    items.Add(guid, item);
                }
                return item;
            }

            static async Task ReplacePlaceholders(JsonNode? node, string? name, int? index, IVault vault, Guid? itemId, IDictionary<Guid,ItemTemplate?> items, string pattern, MatchEvaluator matchEvaluator)
            {
                if (node is JsonObject obj)
                {
                    ItemTemplate? item = null;
                    if (obj.TryGetPropertyValue("@", out var refNode))
                    {
                        if (refNode is JsonValue value && value.TryGetValue(out string? str) && Guid.TryParse(str, out var guid))
                        {
                            obj.Remove("@");
                            item = await GetItem(vault, guid, items).ConfigureAwait(false);
                        }
                    }
                    else if (obj.Parent is null && itemId is not null)
                    {
                        item = await GetItem(vault, itemId.Value, items).ConfigureAwait(false);
                    }
                    if (item is not null)
                    {
                        var guid = $"{item.Id}";
                        obj.TryAdd("Name", JsonValue.Create($"{{Name@{guid}}}"));
                        obj.TryAdd("Username", JsonValue.Create($"{{Username@{guid}}}"));
                        obj.TryAdd("Password", JsonValue.Create($"{{Password@{guid}}}"));
                        obj.TryAdd("TOTP", JsonValue.Create($"{{TOTP@{guid}}}"));
                        foreach (var field in item.Fields)
                            obj.TryAdd(field.Name, JsonValue.Create($"{{{field.Name}@{guid}}}"));
                    }

                    foreach (var prop in obj.AsEnumerable().ToList())
                        await ReplacePlaceholders(prop.Value, prop.Key, null, vault, null, items, pattern, matchEvaluator);
                }
                else if (node is JsonArray array)
                {
                    for (int i = 0; i < array.Count; i++)
                        await ReplacePlaceholders(array[i], null, i, vault, null, items, pattern, matchEvaluator).ConfigureAwait(false);
                }
                else if (node is JsonValue value && value.TryGetValue(out string? str))
                {
                    if (index is not null)
                        value.Parent![index.Value] = JsonValue.Create(Regex.Replace(str, pattern, matchEvaluator));
                    else
                        value.Parent![name!] = JsonValue.Create(Regex.Replace(str, pattern, matchEvaluator));
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