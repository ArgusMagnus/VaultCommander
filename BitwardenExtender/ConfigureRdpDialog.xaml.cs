using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using VaultCommander.BwCommands;

namespace VaultCommander;

/// <summary>
/// Interaction logic for ConfigureRdpDialog.xaml
/// </summary>
sealed partial class ConfigureRdpDialog : Window
{
    public IReadOnlyList<ScreenInfo> SelectedScreens { get; private set; } = Array.Empty<ScreenInfo>();
    readonly ViewModel _vm;

    public ConfigureRdpDialog(IEnumerable<ScreenInfo> screens)
    {
        DataContext = (_vm = new(screens));
        InitializeComponent();

        var minX = screens.Min(x => x.Left);
        var minY = screens.Min(x => x.Top);

        foreach (var screenVM in _vm.Screens)
        {
            var screen = screenVM.ScreenInfo;
            var button = new ToggleButton
            {
                Content = screen.Index,
                FontSize = 32,
                Margin = new((screen.Left - minX) / 10.0, (screen.Top - minY) / 10.0, 0, 0),
                Width = screen.Width / 10.0,
                Height = screen.Height / 10.0,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            BindingOperations.SetBinding(button, ToggleButton.IsCheckedProperty, new Binding(nameof(ScreenInfoVM.IsSelected)) { Mode = BindingMode.TwoWay, Source = screenVM });
            _screens.Children.Add(button);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DialogResult is true)
            SelectedScreens = _vm.Screens.Where(x => x.IsSelected).Select(x => x.ScreenInfo).ToList();
        base.OnClosed(e);
    }

    private void OnButtonOkClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    sealed class ViewModel : NotifyPropertyChanged
    {
        bool _useSingleScreen = true;
        public bool UseSingleScreen { get => _useSingleScreen; set => SetProperty(ref _useSingleScreen, value, OnUseSingleScreenChanged); }

        void OnUseSingleScreenChanged(bool oldValue, bool newValue)
        {
            if (!newValue)
                return;
            foreach (var screen in Screens)
                screen.IsSelected = false;
        }

        bool _useMultipleScreensAll;
        public bool UseMultipleScreensAll { get => _useMultipleScreensAll; set => SetProperty(ref _useMultipleScreensAll, value, OnUseMultipleScreensAllChanged); }

        void OnUseMultipleScreensAllChanged(bool oldValue, bool newValue)
        {
            if (!newValue)
                return;
            foreach (var screen in Screens)
                screen.IsSelected = true;
        }

        bool _useMultipleScreensSameAsMain;
        public bool UseMultipleScreensSameAsMain { get => _useMultipleScreensSameAsMain; set => SetProperty(ref _useMultipleScreensSameAsMain, value, OnUseMultipleScreensSameAsMainChanged); }

        void OnUseMultipleScreensSameAsMainChanged(bool oldValue, bool newValue)
        {
            if (!newValue)
                return;
            var primary = Screens.First(x => x.ScreenInfo.IsPrimary).ScreenInfo;
            foreach (var screen in Screens)
                screen.IsSelected = screen.ScreenInfo.Top == primary.Top && screen.ScreenInfo.Height == primary.Height;
        }

        bool _useMultipleScreens;
        public bool UseMultipleScreens { get => _useMultipleScreens; set => SetProperty(ref _useMultipleScreens, value); }

        public IEnumerable<ScreenInfoVM> Screens { get; }

        public ViewModel(IEnumerable<ScreenInfo> screens) => Screens = screens.Select(x => new ScreenInfoVM(this, x)).ToList();
    }

    sealed class ScreenInfoVM : NotifyPropertyChanged
    {
        readonly ViewModel _vm;

        bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value, OnIsSelectedChanged); }

        void OnIsSelectedChanged(bool oldValue, bool newValue)
        {
            if (_vm.Screens.Any(x => x.IsSelected))
                _vm.UseMultipleScreens = true;
            else
                _vm.UseSingleScreen = true;
        }

        public ScreenInfo ScreenInfo { get; }

        public ScreenInfoVM(ViewModel vm, ScreenInfo screenInfo)
        {
            _vm = vm;
            ScreenInfo = screenInfo;
        }
    }
}
