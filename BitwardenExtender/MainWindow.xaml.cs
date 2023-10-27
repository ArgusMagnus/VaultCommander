#define SAVE_PW

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
using WinForms = System.Windows.Forms;

namespace BitwardenExtender;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
sealed partial class MainWindow : Window
{
    const string CommandFieldName = nameof(BitwardenExtender);
    const string CommandPrefix = "bwext:";
    readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    readonly WinForms.NotifyIcon _notifyIcon = new();
    readonly CliClient _cli;
    readonly IReadOnlyDictionary<string, IBwCommand> _commands;
    readonly MainVM _vm = new();
    bool _cancelClose = true;
    int _progressBarScope = 0;
    CurrentWindowInformationWindow? _currentWindowInfoWindow;

#if SAVE_PW
    EncryptedString? _pw;
#endif

    public MainWindow()
    {
        _cli = new(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Terminal.Constants.CliDirectory, "bw.exe"));
        DataContext = _vm;

        InitializeComponent();
        _notifyIcon.Text = Title;
        _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
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
        if (!Directory.Exists(_cli.AppDataDir))
        {
            MessageBox.Show(this, "Bitwarden Desktop Client ist nicht installiert.", nameof(BitwardenExtender), MessageBoxButton.OK, MessageBoxImage.Error);
            OnMenuExitClicked(null, null);
            return;
        }

        _vm.StatusBarText = "Auf Updates prüfen...";

        var releaseTask = Utils.GetLatestRelease();
        var uriTask = _cli.GetUpdateUri();
        if (await releaseTask is ReleaseInfo release && release.Version?.TrimStart('v') != _vm.Version)
        {
            _vm.StatusBarText = "Downloading update...";
            _statusBarProgress.IsIndeterminate = false;
            _statusBarProgress.Visibility = Visibility.Visible;
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                await Utils.DownloadRelease(release, dir, value => _vm.StatusBarProgress = value);
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = Directory.EnumerateFiles(dir, Path.GetFileName(Environment.ProcessPath!)).First().Replace(".exe", $"{nameof(Terminal)}.exe"),
                    ArgumentList = { nameof(Terminal.Verbs.Install), AppDomain.CurrentDomain.BaseDirectory, $"{Environment.ProcessId}" }
                });
                OnMenuExitClicked(null, null);
                return;
            }
            catch (Exception)
            {
                Directory.Delete(dir, true);
                throw;
            }
        }

        _cli.TryAttachToApiServer();
        if (await uriTask is Uri uri)
        {
            _vm.StatusBarText = "Downloading Bitwarden CLI...";
            _statusBarProgress.IsIndeterminate = false;
            _statusBarProgress.Visibility = Visibility.Visible;
            _cli.KillServer();
            await Utils.DownloadAndExpandZipArchive(uri,
                name => string.Equals(".exe", Path.GetExtension(name), StringComparison.OrdinalIgnoreCase) ? _cli.ExePath : null,
                progress => _vm.StatusBarProgress = progress);
            _statusBarProgress.Visibility = Visibility.Collapsed;
        }

        if (!await _cli.StartApiServer(null))
        {
            _vm.StatusBarText = "Anmelden...";
#if SAVE_PW
            _pw = null;
#endif
            while (true)
            {
                var cred = PasswordDialog.Show(this, null);
                if (cred == default)
                {
                    OnMenuExitClicked(null, null);
                    break;
                }
                if (string.IsNullOrEmpty(cred.UserEmail) || cred.Password is null)
                    continue;
                var sessionToken = await _cli.Login(cred.UserEmail, cred.Password);
                if (string.IsNullOrEmpty(sessionToken))
                    continue;
#if SAVE_PW
                _pw = cred.Password;
#endif
                await _cli.StartApiServer(sessionToken);
                break;
            }
        }

        _vm.StatusBarText = "Fertig";
        _vm.Status = await (await _cli.GetApiClient()).GetStatus() ?? await _cli.GetStatus();
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
        if (_vm.Status?.Status is Status.Unlocked)
            await (await _cli.GetApiClient()).Lock();
        _cli.Dispose();
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
            if (text.StartsWith(CommandPrefix, StringComparison.OrdinalIgnoreCase))
            {
                Clipboard.Clear();
                OnClipboardCommand(text.Substring(CommandPrefix.Length));
                handled = true;
            }
        }

        return nint.Zero;
    }

    async Task<T> UseApi<T>(Func<ApiClient, Task<T>> func)
    {
        using var scope = ShowProgressBar();
        var api = await _cli.GetApiClient();
        T result;
        try
        {
            if ((await api.GetStatus())?.Status is Status.Locked)
            {
                EncryptedString? pw = null;
#if SAVE_PW
                pw = _pw;
                _pw = null;
#endif
                while (pw is null || !await api.Unlock(pw))
                {
                    Show();
                    Activate();
                    pw = PasswordDialog.Show(this, _vm.Status?.UserEmail).Password;
                }
#if SAVE_PW
                _pw = pw;
#endif
            }
            result = await func(api);
        }
        finally
        {
#if SAVE_PW
            if (!Debugger.IsAttached)
                await api.Lock();
#endif
        }
        _vm.Status = await api.GetStatus();
        return result;
    }

    Task UseApi(Func<ApiClient, Task> func) => UseApi(async api => { await func(api); return true; });

    private async void OnClipboardCommand(string strGuid)
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

        if (!Guid.TryParse(strGuid, out var guid))
        {
            MessageBox.Show(this, $"'{strGuid}' ist keine gültige GUID.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ItemTemplate? item = null;
        try
        {
            var response = await UseApi(api => api.GetItem(guid));
            if (response?.Success is true)
                item = response.Data;
        }
        catch { }

        if (item?.Id != guid)
            item = await UpdateGuids(guid);

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
            var button = new Button { Content = field.Name, IsEnabled = bwCommand.CanExecute, Margin = new(0, 10, 0, 0), Tag = new ButtonTag(item.Id, bwCommand, parts[1]) };
            button.Click += OnBwCommandClicked;
            _buttons.Children.Add(button);
        }
    }

    private async Task<ItemTemplate?> UpdateGuids(Guid? guid)
    {
        var (item, count) = await UseApi(async api =>
        {
            await api.Sync();
            ItemTemplate? item = null;
            var count = 0;
            var itemsDto = await api.GetItems();
            if (itemsDto?.Success is not true || itemsDto.Data?.Data is null)
                return (item, count);

            foreach (var data in itemsDto.Data.Data)
            {
                var element = data.Fields.Select((x, i) => (x, i)).FirstOrDefault(x => x.x.Name == CommandFieldName);
                if (data.Login is not null)
                {
                    if (element != default)
                        data.Fields.RemoveAt(element.i);
                    var uri = data.Login.Uris.Select((x, i) => (x, i)).FirstOrDefault(x => x.x.Uri?.StartsWith(CommandPrefix, StringComparison.OrdinalIgnoreCase) is true);
                    if (Guid.TryParse(uri.x?.Uri?.Substring(CommandPrefix.Length), out var tmp) && tmp == data.Id)
                        continue;
                    var newUri = new ItemUri { Uri = $"{CommandPrefix}{data.Id}", Match = UriMatchType.Never };
                    if (uri == default)
                        data.Login.Uris.Add(newUri);
                    else
                        data.Login.Uris[uri.i] = newUri;
                }
                else
                {
                    if (element.x?.Value?.StartsWith(CommandPrefix, StringComparison.OrdinalIgnoreCase) is true && Guid.TryParse(element.x.Value.Substring(CommandPrefix.Length), out var tmp) && tmp == data.Id)
                        continue;
                    var newField = new Field { Name = CommandFieldName, Value = $"{CommandPrefix}{data.Id}", Type = FieldType.Text };
                    if (element == default)
                        data.Fields.Insert(0, newField);
                    else
                        data.Fields[element.i] = newField;
                }

                await api.PutItem(data);
                if (data.Id == guid)
                    item = data;
                count++;
            }

            return (item, count);
        });

        _vm.StatusBarText = $"{count} Einträge aktualisiert";
        return item;
    }

    private async void OnMenuUpdateGuidsClicked(object sender, RoutedEventArgs e) => await UpdateGuids(null);

    sealed record ButtonTag(Guid ItemId, IBwCommand Command, string Arguments);

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

            await UseApi(async api =>
            {
                await Task.Delay(0).ConfigureAwait(false);
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
                    var item = GetItem(api, guid, items).Result;
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
                await ReplacePlaceholders(argsNode, null, null, api, tag.ItemId, items, Pattern, EvaluateMatch);
            });

            var args = argsNode.Deserialize(tag.Command.ArgumentsType, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (args is not null)
                await tag.Command.Execute(args);

            return;

            static async Task<ItemTemplate?> GetItem(ApiClient api, Guid guid, IDictionary<Guid, ItemTemplate?> items)
            {
                if (!items.TryGetValue(guid, out var item))
                {
                    item = (await api.GetItem(guid).ConfigureAwait(false))?.Data;
                    if (!string.IsNullOrEmpty(item?.Login?.Totp))
                        item = item with { Login = item.Login with { Totp = (await api.GetTotp(guid))?.Data?.Data } };
                    items.Add(guid, item);
                }
                return item;
            }

            static async Task ReplacePlaceholders(JsonNode? node, string? name, int? index, ApiClient api, Guid? itemId, IDictionary<Guid,ItemTemplate?> items, string pattern, MatchEvaluator matchEvaluator)
            {
                if (node is JsonObject obj)
                {
                    ItemTemplate? item = null;
                    if (obj.TryGetPropertyValue("@", out var refNode))
                    {
                        if (refNode is JsonValue value && value.TryGetValue(out string? str) && Guid.TryParse(str, out var guid))
                        {
                            obj.Remove("@");
                            item = await GetItem(api, guid, items).ConfigureAwait(false);
                        }
                    }
                    else if (obj.Parent is null && itemId is not null)
                    {
                        item = await GetItem(api, itemId.Value, items).ConfigureAwait(false);
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
                        await ReplacePlaceholders(prop.Value, prop.Key, null, api, null, items, pattern, matchEvaluator);
                }
                else if (node is JsonArray array)
                {
                    for (int i = 0; i < array.Count; i++)
                        await ReplacePlaceholders(array[i], null, i, api, null, items, pattern, matchEvaluator).ConfigureAwait(false);
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

    private async void OnMenuLogoutClicked(object sender, RoutedEventArgs e)
    {
        _cli.KillServer();
        await _cli.Logout();
        if (Directory.Exists(_cli.AppDataDir))
        {
            try { Directory.Delete(_cli.AppDataDir, true); }
            catch
            {
                foreach (var file in Directory.EnumerateFiles(_cli.AppDataDir))
                {
                    try { File.Delete(file); }
                    catch { }
                }
            }
        }
        OnMenuExitClicked(null, null);
    }

    private async void OnMenuSyncClicked(object sender, RoutedEventArgs e) => await UseApi(api => api.Sync());

    private void OnMenuToolsShowWindowInformationClicked(object sender, RoutedEventArgs e)
    {
        if (_currentWindowInfoWindow is null)
        {
            _currentWindowInfoWindow = new();
            _currentWindowInfoWindow.Closed += (_, _) => _currentWindowInfoWindow = null;
            _currentWindowInfoWindow.Show();
        }
    }

    ProgressBarScope ShowProgressBar() => new(this);

    readonly struct ProgressBarScope : IDisposable
    {
        readonly MainWindow _window;
        public ProgressBarScope(MainWindow window)
        {
            _window = window;
            if (_window._progressBarScope++ is 0)
            {
                _window._statusBarProgress.IsIndeterminate = true;
                _window._statusBarProgress.Visibility = Visibility.Visible;
            }
        }

        public void Dispose()
        {
            if (--_window._progressBarScope is 0)
            {
                _window._statusBarProgress.Visibility = Visibility.Collapsed;
            }
        }
    }
}