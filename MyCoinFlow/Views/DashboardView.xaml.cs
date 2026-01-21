using System.Windows;
using System.Windows.Controls;
using System.Printing;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Shapes;

namespace MyCoinFlow.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();
            // DataContext kommt über DataTemplate (App.xaml) vom MainViewModel (CurrentViewModel)
        }

        private void PrintDashboard_Click(object sender, RoutedEventArgs e)
        {
            // Druckt den Bereich x:Name="PrintScope" auf A4 quer – vollständig skaliert auf 1 Seite
            if (PrintScope is not FrameworkElement scope) return;

            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true) return;

            dlg.PrintTicket ??= new PrintTicket();
            dlg.PrintTicket.PageOrientation = PageOrientation.Landscape;
            dlg.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);

            const double pageWidth = 11.69 * 96.0;   // A4 landscape in DIP
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
