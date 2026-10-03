using System;
using System.Windows;

namespace SmartVpn.XrayCore
{
    public partial class PromptWindow : Window
    {
        public string Result { get; private set; } = "";

        public PromptWindow(string defaultUrl = "")
        {
            InitializeComponent();
            UrlInput.Text = defaultUrl;
            Loaded += (s, e) =>
            {
                UrlInput.Focus();
                UrlInput.SelectAll();
            };
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            Result = UrlInput.Text.Trim();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    UrlInput.Text = Clipboard.GetText().Trim();
                }
            }
            catch { }
        }
    }
}
