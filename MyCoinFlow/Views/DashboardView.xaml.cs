using System.Windows;
using System.Windows.Controls;
using System.Printing;
using System.Windows.Media;
using System.Windows.Documents;        // FixedDocument, FixedPage, PageContent
using System.Windows.Shapes;           // Rectangle


namespace MyCoinFlow.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();
            if (DataContext == null)
                DataContext = new MyCoinFlow.ViewModels.DashboardViewModel();
        }

        private void PrintDashboard_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // Safety: Dein Druckbereich (Element) muss im XAML mit x:Name="PrintScope" benannt sein
            if (PrintScope is not FrameworkElement scope) return;

            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true) return;

            // Erzwinge A4 quer
            dlg.PrintTicket ??= new PrintTicket();
            dlg.PrintTicket.PageOrientation = PageOrientation.Landscape;
            dlg.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);

            // A4 in WPF-DIPs (96 DPI): 11.69" x 8.27" -> Landscape
            const double pageWidth = 11.69 * 96.0;   // ~1122
            const double pageHeight = 8.27 * 96.0;   // ~794
            const double margin = 24.0;              // 24 DIP ~ 6 mm

            // FixedDocument mit 1 Seite (A4 quer)
            var document = new FixedDocument
            {
                DocumentPaginator = { PageSize = new System.Windows.Size(pageWidth, pageHeight) }
            };

            var fixedPage = new FixedPage
            {
                Width = pageWidth,
                Height = pageHeight,
                Background = System.Windows.Media.Brushes.White
            };

            // VisualBrush der aktuellen Ansicht => Stretch.Uniform skaliert auf eine Seite
            var brush = new VisualBrush(scope)
            {
                Stretch = Stretch.Uniform,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };

            // Rechteck als Zeichenfläche für den Visual‑Snapshot (mit Rändern)
            var rect = new Rectangle
            {
                Width = pageWidth - 2 * margin,
                Height = pageHeight - 2 * margin,
                Fill = brush
            };
            FixedPage.SetLeft(rect, margin);
            FixedPage.SetTop(rect, margin);
            fixedPage.Children.Add(rect);

            // Seite ins Dokument
            var pageContent = new PageContent();
            ((System.Windows.Markup.IAddChild)pageContent).AddChild(fixedPage);
            document.Pages.Add(pageContent);

            // Drucken über DocumentPaginator (respektiert PageOrientation/PageMediaSize)
            dlg.PrintDocument(document.DocumentPaginator, "MyCoinFlow Dashboard");
        }


    }
}
