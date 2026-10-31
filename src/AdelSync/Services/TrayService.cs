using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace AdelSync.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Window _window;

    public TrayService(Window window)
    {
        _window = window;
        _icon = new Forms.NotifyIcon
        {
            Visible = true,
            Text = "AdelSync — Адель Гайнуллин",
            Icon = LoadIcon()
        };
        _icon.DoubleClick += (_, _) => ShowMain();
        _icon.ContextMenuStrip = BuildMenu();
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть AdelSync", null, (_, _) => ShowMain());
        menu.Items.Add("Выход", null, (_, _) =>
        {
            _icon.Visible = false;
            Application.Current.Shutdown();
        });
        return menu;
    }

    private void ShowMain()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Balloon(string title, string text, Forms.ToolTipIcon tip = Forms.ToolTipIcon.Info)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = text;
            _icon.BalloonTipIcon = tip;
            _icon.ShowBalloonTip(4500);
        }
        catch
        {
            // ignore
        }
    }

    public void SetTip(string text) => _icon.Text = text.Length > 60 ? text[..60] : text;

    private static Icon LoadIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app.png", UriKind.Absolute);
            var streamInfo = Application.GetResourceStream(uri);
            if (streamInfo?.Stream != null)
            {
                using var bmp = new Bitmap(streamInfo.Stream);
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
        catch
        {
            // fallback
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
