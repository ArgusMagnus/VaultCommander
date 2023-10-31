using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace VaultCommander;

/// <summary>
/// Interaction logic for ShowCurrentWindowTitleWindow.xaml
/// </summary>
sealed partial class CurrentWindowInformationWindow : Window
{
    readonly ViewModel _vm = new();
    readonly WindowsEventHook _eventHook = new(WindowsEventHook.Events.Foreground, WindowsEventHook.Flags.SkipOwnProcess);

    public CurrentWindowInformationWindow()
    {
        DataContext = _vm;
        InitializeComponent();

        _eventHook.Event += OnWindowsEvent;
    }

    private async void OnWindowsEvent(WindowsEventHook sender, WindowsEventHook.EventArgs args)
    {
        switch (args.Event)
        {
            case WindowsEventHook.Events.Foreground:
                if (args.Window != WindowHandle.Null)
                {
                    _vm.WindowTitle = args.Window.TryGetText(out var text) ? text : string.Empty;
                    _vm.WindowClass = args.Window.ClassName;
                    _vm.ProcessId = args.Window.TryGetThreadAndProcessId(out _, out var processId) ? processId : 0;
                    _vm.ProcessName = processId is 0 ? string.Empty : Process.GetProcessById(processId).ProcessName;
                }
                break;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _eventHook.Dispose();
    }

    sealed class ViewModel : NotifyPropertyChanged
    {
        string? _windowTitle;
        public string? WindowTitle { get => _windowTitle; set => SetProperty(ref _windowTitle, value); }

        string? _windowClass;
        public string? WindowClass { get => _windowClass; set => SetProperty(ref _windowClass, value); }

        int _processId;
        public int ProcessId { get => _processId; set => SetProperty(ref _processId, value); }

        string? _processName;
        public string? ProcessName { get => _processName; set => SetProperty(ref _processName, value); }
    }
}
