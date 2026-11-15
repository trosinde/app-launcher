using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AppStart.Setup.Engine;
using AppStart.Setup.ViewModels;
using Microsoft.Win32;

namespace AppStart.Setup.Views;

/// <summary>WPF implementation of the dialogs, shell and printing used by the view models.</summary>
public sealed class UiServices : IUiServices
{
    public Window? Owner { get; set; }

    public IReadOnlyList<string> PickExeFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = SetupTexts.OpenExeTitle,
            Filter = SetupTexts.OpenExeFilter,
            Multiselect = true,
            CheckFileExists = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileNames : [];
    }

    public string? PickFolder(string? initialFolder)
    {
        var dialog = new OpenFolderDialog
        {
            Title = SetupTexts.DataDirDialogTitle,
            Multiselect = false,
        };
        if (initialFolder is not null && Directory.Exists(initialFolder))
        {
            dialog.InitialDirectory = initialFolder;
        }

        return dialog.ShowDialog(Owner) == true ? dialog.FolderName : null;
    }

    public string? PickReportSavePath(string defaultFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = SetupTexts.SaveAsTitle,
            Filter = SetupTexts.SaveAsFilter,
            FileName = defaultFileName,
            DefaultExt = ".html",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public void OpenWithShell(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    /// <summary>
    /// Prints a FlowDocument built from the report (no browser, no dependency on the HTML file association).
    /// </summary>
    public bool PrintReport(VerifyReport report)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        var document = ReportDocument.Build(report);
        document.PageWidth = dialog.PrintableAreaWidth;
        document.PageHeight = dialog.PrintableAreaHeight;
        document.PagePadding = new Thickness(56);
        document.ColumnWidth = double.PositiveInfinity;
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, SetupTexts.PrintJobName);
        return true;
    }
}

/// <summary>The verify report as a printable FlowDocument (same structure as the verify view).</summary>
public static class ReportDocument
{
    private static readonly Brush Secondary = Freeze(new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4A)));
    private static readonly Brush OkForeground = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x6B, 0x33)));
    private static readonly Brush ErrorForeground = Freeze(new SolidColorBrush(Color.FromRgb(0x9C, 0x1F, 0x17)));
    private static readonly Brush DeviationRow = Freeze(new SolidColorBrush(Color.FromRgb(0xFD, 0xF3, 0xF2)));
    private static readonly Brush Line = Freeze(new SolidColorBrush(Color.FromRgb(0xD6, 0xD6, 0xD6)));

    public static FlowDocument Build(VerifyReport report)
    {
        var date = report.CreatedAt.ToLocalTime().ToString(SetupTexts.DateFormat, System.Globalization.CultureInfo.InvariantCulture);
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Foreground = Brushes.Black,
        };

        doc.Blocks.Add(new Paragraph(new Run(SetupTexts.VerifyKicker)) { FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Secondary, Margin = new Thickness(0) });
        doc.Blocks.Add(new Paragraph(new Run(SetupTexts.VerifyHeading)) { FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 4) });
        doc.Blocks.Add(new Paragraph(new Run(SetupTexts.DeviationBadge(report.Deviations)))
        {
            FontWeight = FontWeights.SemiBold,
            Foreground = report.Deviations == 0 ? OkForeground : ErrorForeground,
            Margin = new Thickness(0, 0, 0, 12),
        });

        var meta = new Table { CellSpacing = 0 };
        for (var i = 0; i < 4; i++)
        {
            meta.Columns.Add(new TableColumn());
        }

        var labels = new TableRow();
        var values = new TableRow();
        foreach (var (label, value) in new[]
        {
            (SetupTexts.MetaComputer, report.Computer),
            (SetupTexts.MetaDate, date),
            (SetupTexts.MetaTechnician, report.Technician),
            (SetupTexts.MetaVersion, SetupTexts.Format(SetupTexts.VersionFormat, report.Version)),
        })
        {
            labels.Cells.Add(new TableCell(new Paragraph(new Run(label)) { Foreground = Secondary, Margin = new Thickness(0) }));
            values.Cells.Add(new TableCell(new Paragraph(new Run(value)) { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) }));
        }

        meta.RowGroups.Add(new TableRowGroup { Rows = { labels, values } });
        doc.Blocks.Add(meta);

        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 14, 0, 0) };
        table.Columns.Add(new TableColumn { Width = new GridLength(1.2, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(2.5, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        var group = new TableRowGroup();
        var header = new TableRow { FontWeight = FontWeights.SemiBold, Foreground = Secondary, FontSize = 10 };
        foreach (var h in new[] { SetupTexts.ColumnCheck, SetupTexts.ColumnExpected, SetupTexts.ColumnResult })
        {
            header.Cells.Add(new TableCell(new Paragraph(new Run(h))) { BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 2), Padding = new Thickness(4) });
        }

        group.Rows.Add(header);
        foreach (var check in report.Checks)
        {
            var row = CheckRow.From(check);
            var tr = new TableRow();
            if (row.IsDeviation)
            {
                tr.Background = DeviationRow;
            }

            tr.Cells.Add(Cell(row.Title, row.IsDeviation ? FontWeights.SemiBold : FontWeights.Normal, Brushes.Black));
            tr.Cells.Add(Cell(row.Text, FontWeights.Normal, row.IsDeviation ? Brushes.Black : Secondary));
            tr.Cells.Add(Cell(row.ResultText, FontWeights.SemiBold, row.IsDeviation ? ErrorForeground : OkForeground));
            group.Rows.Add(tr);
        }

        table.RowGroups.Add(group);
        doc.Blocks.Add(table);

        if (report.Deviations > 0)
        {
            var box = new Section { Background = Freeze(new SolidColorBrush(Color.FromRgb(0xFB, 0xE9, 0xE7))), Padding = new Thickness(10), Margin = new Thickness(0, 14, 0, 0) };
            box.Blocks.Add(new Paragraph(new Run(SetupTexts.DeviationBoxTitle)) { FontWeight = FontWeights.SemiBold, Foreground = ErrorForeground, Margin = new Thickness(0) });
            box.Blocks.Add(new Paragraph(new Run(SetupTexts.DeviationBoxText + " ")) { Margin = new Thickness(0, 4, 0, 0), Inlines = { new Run(SetupTexts.VerifyCommand) { FontFamily = new FontFamily("Consolas") } } });
            doc.Blocks.Add(box);
        }

        var signatures = new Table { CellSpacing = 24, Margin = new Thickness(0, 60, 0, 0) };
        signatures.Columns.Add(new TableColumn());
        signatures.Columns.Add(new TableColumn());
        var sig = new TableRow();
        foreach (var s in new[] { SetupTexts.SignatureTechnician, SetupTexts.SignatureCustomer })
        {
            sig.Cells.Add(new TableCell(new Paragraph(new Run(s)) { Foreground = Secondary, Margin = new Thickness(0, 4, 0, 0) })
            {
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(0, 1, 0, 0),
            });
        }

        signatures.RowGroups.Add(new TableRowGroup { Rows = { sig } });
        doc.Blocks.Add(signatures);
        return doc;
    }

    private static TableCell Cell(string text, FontWeight weight, Brush foreground) =>
        new(new Paragraph(new Run(text)) { Margin = new Thickness(0) })
        {
            FontWeight = weight,
            Foreground = foreground,
            Padding = new Thickness(4, 5, 4, 5),
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
