﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace VaultCommander;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
sealed partial class App : Application
{
    public static string Version { get; } = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    readonly Mutex _mutex = new(true, "9e2b0848-28e6-47e2-99c8-17db38e889e9");

    protected override void OnStartup(StartupEventArgs e)
    {
        if (_mutex.WaitOne(0, true))
        {
            base.OnStartup(e);
            Current.MainWindow = new MainWindow(e.Args);
            Current.MainWindow.Show();
            Current.MainWindow.Hide();
            ((MainWindow)Current.MainWindow).ShowBalloonTip(nameof(VaultCommander), $"{nameof(VaultCommander)} gestartet", System.Windows.Forms.ToolTipIcon.Info);
        }
        else
        {
            if (e.Args.FirstOrDefault() is string cmd)
                Clipboard.SetText(cmd);
            else
                MessageBox.Show("Die Anwendung läuft bereits.", nameof(VaultCommander), MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex.Dispose();
        base.OnExit(e);
    }
}
