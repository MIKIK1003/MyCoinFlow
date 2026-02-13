using System.Windows;
using System.Windows.Controls;
using System.Printing;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Shapes;
using MyCoinFlow.ViewModels;
using MyCoinFlow.Services;

namespace MyCoinFlow.Views
{
    public partial class DashboardView : UserControl
    {
        private bool _isStweActive;

        public DashboardView()
        {
            InitializeComponent();

            ApplyEditionVisibility();

            SetDashboardMode(false); // Default: Budget
        }

        private void ApplyEditionVisibility()
        {
            // Basic: STWE-Auswertung darf nicht anwählbar sein
            var isPlus = AppEdition.IsPlus;

            if (!isPlus)
            {
                // Button ausblenden
                if (BtnToStwe != null)
                    BtnToStwe.Visibility = Visibility.Collapsed;

                // sicherstellen: Budget-Modus aktiv
                _isStweActive = false;
                if (BudgetContentGrid != null) BudgetContentGrid.Visibility = Visibility.Visible;
                if (StweContent != null) StweContent.Visibility = Visibility.Collapsed;
                if (BtnToBudget != null) BtnToBudget.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Plus: Button darf sichtbar sein (Budget-Modus zeigt STWE-Toggle)
                if (BtnToStwe != null && !_isStweActive)
                    BtnToStwe.Visibility = Visibility.Visible;
            }
        }

        private void SwitchToStwe_Click(object sender, RoutedEventArgs e)
        {
            if (!AppEdition.IsPlus) return; // extra Sicherung
            SetDashboardMode(true);
        }

        private void SwitchToBudget_Click(object sender, RoutedEventArgs e) => SetDashboardMode(false);

        private void SetDashboardMode(bool stwe)
        {
            if (_isStweActive == stwe) return;
            _isStweActive = stwe;

            BudgetContentGrid.Visibility = stwe ? Visibility.Collapsed : Visibility.Visible;
            StweContent.Visibility = stwe ? Visibility.Visible : Visibility.Collapsed;

            // Toggle-Buttons
            if (AppEdition.IsPlus)
            {
                BtnToStwe.Visibility = stwe ? Visibility.Collapsed : Visibility.Visible;
                BtnToBudget.Visibility = stwe ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                BtnToStwe.Visibility = Visibility.Collapsed;
                BtnToBudget.Visibility = Visibility.Collapsed;
            }

            if (stwe && StweContent.DataContext == null)
            {
                StweContent.DataContext = new DashboardStweViewModel();
            }
        }

        private void PrintDashboard_Click(object sender, RoutedEventArgs e)
        {
            if (PrintScope is not FrameworkElement scope) return;

            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true) return;

            dlg.PrintTicket ??= new PrintTicket();
            dlg.PrintTicket.PageOrientation = PageOrientation.Landscape;
            dlg.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);

            const double pageWidth = 11.69 * 96.0;
            const double pageHeight = 8.27 * 96.0;
            const double margin = 24.0;

            var document = new FixedDocument
            {
                DocumentPaginator = { PageSize = new Size(pageWidth, pageHeight) }
            };

            var fixedPage = new FixedPage
            {
                Width = pageWidth,
                Height = pageHeight,
                Background = Brushes.White
            };

            var brush = new VisualBrush(scope)
            {
                Stretch = Stretch.Uniform,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };

            var rect = new Rectangle
            {
                Width = pageWidth - 2 * margin,
                Height = pageHeight - 2 * margin,
                Fill = brush
            };
            FixedPage.SetLeft(rect, margin);
            FixedPage.SetTop(rect, margin);
            fixedPage.Children.Add(rect);

            var pageContent = new PageContent();
            ((System.Windows.Markup.IAddChild)pageContent).AddChild(fixedPage);
            document.Pages.Add(pageContent);

            dlg.PrintDocument(document.DocumentPaginator, "MyCoinFlow Dashboard");
        }
    }
}
