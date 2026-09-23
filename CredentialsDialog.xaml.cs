using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SmartVpn;

public partial class CredentialsDialog : Window
{
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // آدرس پنل کاربری — یک‌بار در MainWindow مقداردهی می‌شود
    public static string PanelUrl { get; set; } = "";

    public string Username => UserBox.Text.Trim();
    public string Password => PassBox.Password;
    public string Psk => PskBox.Text.Trim();
    public bool SaveRequested => SaveCheck.IsChecked == true;

    public void ShowPskField() => PskPanel.Visibility = Visibility.Visible;
    public void HideCredsFields() => CredsPanel.Visibility = Visibility.Collapsed;

    public CredentialsDialog(string prompt)
    {
        InitializeComponent();
        Loaded += (_, _) => Localization.Watch(this);
        PromptText.Text = prompt;
        UserBox.Focus();
        SourceInitialized += (_, __) => ApplyDarkTitleBar();
    }

    // نوار عنوان تیره — هماهنگ با پنجره اصلی
    private void ApplyDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int caption = 0x00171717;
            int text = 0x00F8FAFC;
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
        }
        catch { }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (CredsPanel.Visibility == Visibility.Visible && (Username.Length == 0 || Password.Length == 0))
        {
            AskDialog.Info(this, Localization.T("نام کاربری و پسورد اجباری‌ان."));
            return;
        }
        if (PskPanel.Visibility == Visibility.Visible && Psk.Length == 0)
        {
            AskDialog.Info(this, Localization.T("وارد کردن PSK اجباری است."));
            return;
        }
        DialogResult = true;
    }

    // کلیک روی «بررسی اشتراک - خرید - تمدید»
    private void Panel_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PanelUrl)) return;
        try { Process.Start(new ProcessStartInfo(PanelUrl) { UseShellExecute = true }); }
        catch { }
    }
}
