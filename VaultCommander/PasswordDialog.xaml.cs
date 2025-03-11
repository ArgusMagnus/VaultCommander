using System.Windows;
using System.Windows.Controls;

namespace VaultCommander;

/// <summary>
/// Interaction logic for PasswordDialog.xaml
/// </summary>
sealed partial class PasswordDialog : Window
{
    PasswordDialog()
    {
        InitializeComponent();
    }

    private void OnButtonOK(object sender, RoutedEventArgs e) => DialogResult = true;

    public static (string? Server, string? UserEmail, EncryptedString? Password) Show(Window owner, string? server, string? userEmail, bool emailOnly = false)
    {
        PasswordDialog dlg = new() { Owner = owner };
        dlg._serverBox.Text = server;
        if (!string.IsNullOrEmpty(userEmail))
        {
            dlg._emailBox.Text = userEmail;
            dlg._emailBox.IsReadOnly = true;
            dlg._serverBox.IsReadOnly = true;
            if (!emailOnly)
            {
                dlg._serverLabel.Visibility = Visibility.Collapsed;
                dlg._serverBox.Visibility = Visibility.Collapsed;
            }
        }
        if (emailOnly)
        {
            dlg._passwordLabel.Visibility = Visibility.Collapsed;
            dlg._passwordBox.Visibility = Visibility.Collapsed;
        }
        return dlg.ShowDialog() is true ? (dlg._serverBox.Text, dlg._emailBox.Text, new(dlg._passwordBox.Password)) : default;
    }

    private void OnUseSsoChanged(object sender, RoutedEventArgs e)
    {
        var cb = (CheckBox)sender;
        _passwordBox.IsEnabled = cb.IsChecked is not true;
    }
}
