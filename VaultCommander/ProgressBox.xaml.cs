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
/// Interaction logic for ProgressBox.xaml
/// </summary>
sealed partial class ProgressBox : Window
{
    readonly ViewModel _vm;
    ProgressBox()
    {
        DataContext = _vm = new(this);
        InitializeComponent();
    }

    public interface IViewModel : IDisposable
    {
        public string? DetailText { get; set; }
        public double DetailProgress { get; set; }
        public string? StepText { get; set; }
        public double StepProgress { get; set; }
    }

    sealed class ViewModel : NotifyPropertyChanged, IViewModel
    {
        readonly ProgressBox _progressBox;
        public ViewModel(ProgressBox progressBox)
        {
            _progressBox = progressBox;
        }

        public void Dispose() => _progressBox.Dispatcher.InvokeAsync(_progressBox.Close);

        string? _detailText;
        public string? DetailText { get => _detailText; set => SetProperty(ref _detailText, value); }
        double _detailProgress;
        public double DetailProgress { get => _detailProgress; set => SetProperty(ref _detailProgress, value, (a, b) => { if (double.IsNaN(a) != double.IsNaN(b)) RaisePropertyChanged(nameof(DetailProgressIsIndeterminate)); }); }
        public bool DetailProgressIsIndeterminate => double.IsNaN(DetailProgress);

        string? _stepText;
        public string? StepText { get => _stepText; set => SetProperty(ref _stepText, value, (_,_) => RaisePropertyChanged(nameof(StepVisibility))); }
        double _stepProgress;
        public double StepProgress { get => _stepProgress; set => SetProperty(ref _stepProgress, value, (a, b) => { if (double.IsNaN(a) != double.IsNaN(b)) RaisePropertyChanged(nameof(StepProgressIsIndeterminate)); }); }
        public bool StepProgressIsIndeterminate => double.IsNaN(StepProgress);
        public Visibility StepVisibility => string.IsNullOrEmpty(StepText) ? Visibility.Collapsed : Visibility.Visible;
    }

    public static new Task<IViewModel> Show()
    {
        TaskCompletionSource<IViewModel> tcs = new();
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var window = new ProgressBox { Owner = Application.Current.MainWindow };
            window.Loaded += (sender, _) => tcs.SetResult(((ProgressBox)sender)._vm);
            window.ShowDialog();
        });
        return tcs.Task;
    }
}
