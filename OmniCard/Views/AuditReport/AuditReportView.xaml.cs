using System.Windows;
using Microsoft.Win32;
using OmniCard.Services;

namespace OmniCard.Views.AuditReport;

public partial class AuditReportView : Window
{
    public AuditReportViewModel ViewModel { get; }

    public AuditReportView(AuditReportViewModel viewModel, IAuditPdfExporter pdfExporter)
    {
        ViewModel = viewModel;
        DataContext = viewModel;

        viewModel.ExportPdf = () =>
        {
            if (viewModel.Report is null) return;
            var dlg = new SaveFileDialog
            {
                Filter = "PDF Files|*.pdf",
                FileName = $"Audit_{viewModel.Report.LocationName}_{DateTime.Now:yyyy-MM-dd}.pdf"
            };
            if (dlg.ShowDialog() == true)
            {
                pdfExporter.Export(viewModel.Report, dlg.FileName);
                MessageBox.Show("PDF exported successfully.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };

        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
