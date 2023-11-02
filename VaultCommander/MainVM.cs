using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace VaultCommander;

sealed class MainVM : NotifyPropertyChanged
{
    bool _startWithWindows;
    public bool StartWithWindows { get => _startWithWindows; set => SetProperty(ref _startWithWindows, value, OnStartWithWindowsChanged); }

    EntryVM? _selectedEntry;
    public EntryVM? SelectedEntry { get => _selectedEntry; set => SetProperty(ref _selectedEntry, value); }

    public string Version { get; } = App.Version;
    
    const string RegKeyAutostart = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public MainVM()
    {
        using (var regKeyAutostart = Registry.CurrentUser.OpenSubKey(RegKeyAutostart, true)!)
        {
            _startWithWindows = regKeyAutostart.GetValue(nameof(VaultCommander)) is not null;
            if (_startWithWindows)
                regKeyAutostart.SetValue(nameof(VaultCommander), Path.ChangeExtension(typeof(MainVM).Assembly.Location, ".exe"));
        }
    }

    void OnStartWithWindowsChanged(bool _, bool startWithWindows)
    {
        using var regKeyAutostart = Registry.CurrentUser.OpenSubKey(RegKeyAutostart, true)!;
        if (startWithWindows)
            regKeyAutostart.SetValue(nameof(VaultCommander), Path.ChangeExtension(typeof(MainVM).Assembly.Location, ".exe"));
        else
            regKeyAutostart.DeleteValue(nameof(VaultCommander));
    }

    public sealed class EntryVM
    {
        public string Id { get; }
        public string? Name { get; }

        public EntryVM(ItemTemplate item)
        {
            Id = item.Id;
            Name = item.Name;
        }
    }
}
