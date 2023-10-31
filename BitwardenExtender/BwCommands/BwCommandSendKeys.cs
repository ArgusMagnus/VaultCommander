using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace VaultCommander.BwCommands;

sealed class BwCommandSendKeys : BwCommand<BwCommandSendKeys.Arguments>
{
    public record Arguments
    {
        public string? Keys { get; init; }
        public string? WindowTitle { get; init; }
        public string? WindowTitlePattern { get; init; }
        public string? WindowTitleRegex { get; init; }
    }

    public override string Name => "SendKeys";

    public override bool CanExecute => true;

    public override async Task Execute(Arguments args)
    {
        await Task.Yield();
        if (string.IsNullOrEmpty(args.Keys))
            return;

        var window = WindowHandle.Null;
        if ((args.WindowTitle ?? args.WindowTitlePattern ?? args.WindowTitleRegex) is not null)
        {
            if (args.WindowTitle is not null)
                window = WindowHandle.FindWindow(null, args.WindowTitle);
            else if ((args.WindowTitlePattern ?? args.WindowTitleRegex) is not null)
            {
                var pattern = args.WindowTitleRegex ?? Utils.ConvertToRegexPattern(args.WindowTitlePattern!);
                window = WindowHandle.FindWindow(x => Regex.IsMatch(x.Text ?? string.Empty, pattern));
            }

            if (window == WindowHandle.Null)
            {
                MessageBox.Show($"Fenster '{args.WindowTitle ?? args.WindowTitlePattern ?? args.WindowTitleRegex}' nicht gefunden.", nameof(VaultCommander), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
        else
        {
            WindowHandle.FromWpfWindow(Application.Current.MainWindow).TryGetWindow(WindowHandle.GW.Next, out window);
            while (window != WindowHandle.Null && (!window.IsVisible || window.Text is "Bitwarden") && window.TryGetWindow(WindowHandle.GW.Next, out window)) ;
        }

        if (window != WindowHandle.Null)
            window.Focus();
        WinForms.SendKeys.SendWait(args.Keys);
    }
}
