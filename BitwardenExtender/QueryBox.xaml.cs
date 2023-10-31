using System;
using System.Collections.Generic;
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
/// Interaction logic for QueryBox.xaml
/// </summary>
sealed partial class QueryBox : Window
{
    int _selectedButton = -1;

    QueryBox()
    {
        InitializeComponent();
    }

    public static int Show(string message, string title, params string[] buttons)
    {
        QueryBox win = new() { Owner = Application.Current.MainWindow };
        win.Title = title;
        win._message.Text = message;
        for (int i = 0; i < buttons.Length; i++)
        {
            var button = new Button { Content = buttons[i], Padding = new(6, 2, 6, 2), Margin = new(5), Tag = i };
            button.Click += win.OnButtonClicked;
            win._buttons.Children.Add(button);
        }
        win.ShowDialog();
        return win._selectedButton;
    }

    private void OnButtonClicked(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        _selectedButton = (int)element.Tag;
        DialogResult = true;
    }
}
