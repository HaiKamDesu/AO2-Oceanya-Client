using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace OceanyaClient
{
    public partial class GoogleDriveAccountSelectionWindow : OceanyaWindowContentControl
    {
        public GoogleDriveAccountSelectionWindow(
            IReadOnlyList<GoogleDriveSignedInAccount> accounts,
            string? selectedTokenStoreKey = null)
        {
            InitializeComponent();
            Title = "Choose Google Account";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));
            AccountComboBox.ItemsSource = accounts ?? new List<GoogleDriveSignedInAccount>();
            SelectedAccount = (AccountComboBox.ItemsSource as IEnumerable<GoogleDriveSignedInAccount>)?
                .FirstOrDefault(account => string.Equals(
                    account.TokenStoreKey,
                    selectedTokenStoreKey?.Trim() ?? string.Empty,
                    System.StringComparison.OrdinalIgnoreCase))
                ?? (AccountComboBox.ItemsSource as IEnumerable<GoogleDriveSignedInAccount>)?.FirstOrDefault();
            AccountComboBox.SelectedItem = SelectedAccount;
            ConfirmButton.IsEnabled = SelectedAccount != null;
        }

        public GoogleDriveSignedInAccount? SelectedAccount { get; private set; }

        public override string HeaderText => "CHOOSE GOOGLE ACCOUNT";

        private void AccountComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            SelectedAccount = AccountComboBox.SelectedItem as GoogleDriveSignedInAccount;
            ConfirmButton.IsEnabled = SelectedAccount != null;
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedAccount == null)
            {
                return;
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
