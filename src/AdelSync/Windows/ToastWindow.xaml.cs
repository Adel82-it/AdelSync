using System.Windows;
using System.Windows.Threading;

namespace AdelSync.Windows;

public partial class ToastWindow : Window
{
    public ToastWindow(string title, string body)
    {
        InitializeComponent();
        TitleText.Text = title;
        BodyText.Text = body;

        var desk = SystemParameters.WorkArea;
        Left = desk.Right - Width - 16;
        Top = desk.Bottom - 120;

        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            Close();
        };
        t.Start();
    }

    public static void ShowToast(string title, string body)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var w = new ToastWindow(title, body);
            w.Show();
        });
    }
}
