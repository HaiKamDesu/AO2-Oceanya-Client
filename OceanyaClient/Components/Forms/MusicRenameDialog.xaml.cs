using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System;

namespace OceanyaClient
{
    public partial class MusicRenameDialog : OceanyaWindowContentControl
    {
        public string? ResultName { get; private set; }

        public MusicRenameDialog(string currentName)
        {
            InitializeComponent();
            Title = "Rename";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));
            txtName.Text = currentName;
            Loaded += (_, _) =>
            {
                txtName.Focus();
                txtName.SelectAll();
            };
        }

        public override string HeaderText => "RENAME";
        public override bool IsUserResizeEnabled => false;

        private void OkButton_Click(object sender, RoutedEventArgs e) => Commit();
        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void TxtName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return) Commit();
            else if (e.Key == Key.Escape) DialogResult = false;
        }

        private void Commit()
        {
            ResultName = txtName.Text;
            DialogResult = true;
        }
    }
}
