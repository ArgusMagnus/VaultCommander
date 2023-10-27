using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace BitwardenExtender;

sealed class MainVM : NotifyPropertyChanged
{
    bool _startWithWindows;
    public bool StartWithWindows { get => _startWithWindows; set => SetProperty(ref _startWithWindows, value, OnStartWithWindowsChanged); }

    string _statusBarText = string.Empty;
    public string StatusBarText { get => _statusBarText; set => SetProperty(ref _statusBarText, value); }

    StatusDto? _status;
    public StatusDto? Status { get => _status; set => SetProperty(ref _status, value, (_,_) => RaisePropertyChanged(nameof(IsLoggedIn))); }

    EntryVM? _selectedEntry;
    public EntryVM? SelectedEntry { get => _selectedEntry; set => SetProperty(ref _selectedEntry, value); }

    public bool IsLoggedIn => Status is not null && Status.Status is not BitwardenExtender.Status.Unauthenticated;

    public string Version { get; } = typeof(MainVM).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    const string RegKeyAutostart = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public MainVM()
    {
        using (var regKeyAutostart = Registry.CurrentUser.OpenSubKey(RegKeyAutostart, true)!)
        {
            _startWithWindows = regKeyAutostart.GetValue(nameof(BitwardenExtender)) is not null;
            if (_startWithWindows)
                regKeyAutostart.SetValue(nameof(BitwardenExtender), Path.ChangeExtension(typeof(MainVM).Assembly.Location, ".exe"));
        }
    }

    void OnStartWithWindowsChanged(bool _, bool startWithWindows)
    {
        using var regKeyAutostart = Registry.CurrentUser.OpenSubKey(RegKeyAutostart, true)!;
        if (startWithWindows)
            regKeyAutostart.SetValue(nameof(BitwardenExtender), Path.ChangeExtension(typeof(MainVM).Assembly.Location, ".exe"));
        else
            regKeyAutostart.DeleteValue(nameof(BitwardenExtender));
    }

    public sealed class EntryVM
    {
        public Guid Id { get; }
        public string? Name { get; }

        public EntryVM(ItemTemplate item)
        {
            Id = item.Id;
            Name = item.Name;
        }
    }
}
