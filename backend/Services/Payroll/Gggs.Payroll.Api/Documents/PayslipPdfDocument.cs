using System.Globalization;
using Gggs.Payroll.Domain.Payroll;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Gggs.Payroll.Api.Documents;

public sealed record PayslipPdfLine(string ComponentName, SalaryComponentKind Kind, decimal Amount);

/// <summary>Everything <see cref="PayslipPdfDocument"/> needs, gathered up front so the document
/// itself has no EF/database dependency - just the already-published numbers a payslip renders from
/// (see <c>Payslip</c>'s own remarks on why those numbers are immutable once published).</summary>
public sealed record PayslipPdfModel(
    string TenantName,
    string EmployeeCode,
    string EmployeeName,
    string RunNo,
    string PeriodCode,
    DateOnly PayDate,
    DateTime PublishedDate,
    decimal PaidDays,
    decimal LopDays,
    decimal TotalDays,
    decimal Gross,
    decimal NetPay,
    string Currency,
    IReadOnlyList<PayslipPdfLine> Lines);

/// <summary>
/// Renders a <c>Payslip</c> to a one-page PDF with QuestPDF - free under the Community licence below
/// the revenue threshold (architecture.md §15), .NET-native, no browser/Chromium dependency. Built
/// fresh from the already-computed run data on every request (see the payslip-PDF endpoint's own
/// remarks on why this is generated on demand rather than stored) rather than once at finalize time,
/// so there is nothing to keep in sync.
/// </summary>
public sealed class PayslipPdfDocument(PayslipPdfModel model) : IDocument
{
    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(x => x.FontSize(10));

            page.Header().Column(column =>
            {
                column.Item().Text(model.TenantName).FontSize(16).Bold();
                column.Item().Text($"Payslip for {model.PeriodCode} (Run {model.RunNo})").FontSize(11);
                column.Item().PaddingTop(8).LineHorizontal(1);
            });

            page.Content().PaddingVertical(12).Column(column =>
            {
                column.Spacing(12);

                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Employee").SemiBold();
                        c.Item().Text($"{model.EmployeeName} ({model.EmployeeCode})");
                    });

                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Pay date").SemiBold();
                        c.Item().Text(model.PayDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture));
                    });

                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Published").SemiBold();
                        c.Item().Text(model.PublishedDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture));
                    });
                });

                column.Item().Row(row =>
                {
                    row.RelativeItem().Text($"Paid days: {model.PaidDays}");
                    row.RelativeItem().Text($"LOP days: {model.LopDays}");
                    row.RelativeItem().Text($"Total days: {model.TotalDays}");
                });

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Text("Component").Bold();
                        header.Cell().AlignRight().Text("Type").Bold();
                        header.Cell().AlignRight().Text("Amount").Bold();
                        header.Cell().ColumnSpan(3).PaddingTop(2).LineHorizontal(0.5f);
                    });

                    foreach (PayslipPdfLine line in model.Lines)
                    {
                        table.Cell().Text(line.ComponentName);
                        table.Cell().AlignRight().Text(line.Kind == SalaryComponentKind.Earning ? "Earning" : "Deduction");
                        table.Cell().AlignRight().Text($"{model.Currency} {line.Amount:N2}");
                    }
                });

                column.Item().PaddingTop(4).LineHorizontal(1);

                column.Item().Row(row =>
                {
                    row.RelativeItem().Text("Gross pay").Bold();
                    row.RelativeItem().AlignRight().Text($"{model.Currency} {model.Gross:N2}").Bold();
                });

                column.Item().Row(row =>
                {
                    row.RelativeItem().Text("Net pay").Bold().FontSize(12);
                    row.RelativeItem().AlignRight().Text($"{model.Currency} {model.NetPay:N2}").Bold().FontSize(12);
                });
            });

            page.Footer().AlignCenter().Text("Computer-generated payslip - no signature required.").FontSize(8);
        });
    }
}
