using System.Windows;
using Microsoft.Win32;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.DecklistCheck;

public partial class DecklistCheckView : Window
{
    public DecklistCheckViewModel ViewModel { get; }

    public DecklistCheckView(DecklistCheckViewModel viewModel, IDecklistPdfExporter pdfExporter)
    {
        ViewModel = viewModel;
        DataContext = this;

        viewModel.ExportPdf = result =>
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PDF Files|*.pdf",
                FileName = $"Decklist_{result.DeckName}_{DateTime.Now:yyyy-MM-dd}.pdf"
            };
            if (dlg.ShowDialog() == true)
            {
                pdfExporter.Export(result, dlg.FileName);
                MessageBox.Show("PDF exported successfully.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };

        InitializeComponent();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
