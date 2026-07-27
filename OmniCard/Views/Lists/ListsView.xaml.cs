using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Lists;

public partial class ListsView : UserControl
{
    private bool _wired;

    public ListsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_wired) return;
        if (DataContext is not ListsViewModel vm) return;
        _wired = true;

        var exporter = App.Host.Services.GetRequiredService<IDecklistPdfExporter>();

        vm.ExportPdf = result =>
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PDF Files|*.pdf",
                FileName = $"List_{result.DeckName}_{DateTime.Now:yyyy-MM-dd}.pdf"
            };
            if (dlg.ShowDialog() == true)
            {
                exporter.Export(result, dlg.FileName);
                MessageBox.Show("PDF exported successfully.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };

        vm.ExportDetailedPdf = result =>
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PDF Files|*.pdf",
                FileName = $"List_{result.DeckName}_Detailed_{DateTime.Now:yyyy-MM-dd}.pdf"
            };
            if (dlg.ShowDialog() == true)
            {
                exporter.ExportDetailed(result, dlg.FileName);
                MessageBox.Show("Detailed PDF exported successfully.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
    }
}
