using System.Windows;

namespace SmartVpn.XrayCore
{
    public partial class EditWindow : Window
    {
        public ProxyProfile Profile { get; private set; }

        public EditWindow(ProxyProfile profile)
        {
            InitializeComponent();
            Profile = profile;
            DataContext = Profile;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

