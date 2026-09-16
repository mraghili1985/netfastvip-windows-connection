using System;
using System.Windows;

namespace SmartVpn;

// دیالوگ پیام/تأیید داخلی برنامه — جایگزین MessageBox ویندوزی (هم‌تم با هر ۳ حالت تم: تیره/روشن/سیستم، راست‌به‌چپ، دکمه فارسی)
// توجه: نوار عنوان ویندوز دیگر اینجا به‌صورت دستی تیره نمی‌شود: همان‌طور که App.xaml.cs روی همه‌ی پنجره‌ها (Window.LoadedEvent) یک منبع واحد دارد، همین مکانیسم مرکزی (DarkTitleBar.Apply) خودش روی این پنجره هم اجرا می‌شود و با تم فعلی هماهنگ می‌ماند؛ دیگه لازم نیست خودمان تیره را اینجا دوباره hardcode کنیم.
public partial class AskDialog : Window
{
    // ---- متن‌های فارسی ----
    private const string BtnOk = "باشه";
    private const string BtnYes = "بله";
    private const string BtnNo = "خیر";

    // شماره دکمه‌ای که کاربر زد (0 = اولی)؛ -1 یعنی بستن پنجره/Esc
    public int Choice { get; private set; } = -1;

    public AskDialog(string message, string btn0, string? btn1 = null, string? btn2 = null)
    {
        InitializeComponent();
        Title = AppConfig.BrandName; // نام برند از config.json
        MsgText.Text = message;
        Btn0.Content = btn0;
        if (btn1 != null) { Btn1.Content = btn1; Btn1.Visibility = Visibility.Visible; }
        if (btn2 != null) { Btn2.Content = btn2; Btn2.Visibility = Visibility.Visible; }
    }

    private void Btn0_Click(object sender, RoutedEventArgs e) { Choice = 0; DialogResult = true; }
    private void Btn1_Click(object sender, RoutedEventArgs e) { Choice = 1; DialogResult = true; }
    private void Btn2_Click(object sender, RoutedEventArgs e) { Choice = 2; DialogResult = true; }

    // پیام ساده با یک دکمه «باشه»
    public static void Info(Window owner, string message)
    {
        var d = new AskDialog(message, BtnOk) { Owner = owner };
        d.ShowDialog();
    }

    // تأیید بله/خیر — true فقط وقتی کاربر صراحتاً «بله» بزند
    public static bool Confirm(Window owner, string message, string yes = BtnYes, string no = BtnNo)
    {
        var d = new AskDialog(message, yes, no) { Owner = owner };
        return d.ShowDialog() == true && d.Choice == 0;
    }

    // انتخاب بین دو گزینه — خروجی: 0، 1 یا -1 (بستن پنجره)؛
    // دکمه انصراف فقط اگر cancel داده شود نمایش داده می‌شود (دکمه بستن کفایت می‌کند)
    public static int Choose(Window owner, string message, string opt0, string opt1, string? cancel = null)
    {
        var d = new AskDialog(message, opt0, opt1, cancel) { Owner = owner };
        if (d.ShowDialog() != true || d.Choice == 2) return -1;
        return d.Choice;
    }
}
