using System.Globalization;
using System.Security;
using System.Text;

namespace Gggs.Modules.ProjectFinancials.Domain;

/// <summary>One document for the accounting system: an invoice, or a credit note reversing one.</summary>
public sealed record ExportVoucher(bool IsCredit, string Number, DateOnly Date, string Party, decimal Net, decimal Tax, string Narration);

/// <summary>Where the accounting system should post these. The tax ledger takes the whole tax; splitting GST is left to the books.</summary>
public sealed record ExportLedgers(string Company, string SalesLedger, string TaxLedger);

/// <summary>Invoices and credit notes in forms an accounting system can import.</summary>
public static class InvoiceExport
{
    /// <summary>
    /// A Tally Prime "Import Data" envelope with one voucher per document. Debits are negative in Tally's
    /// convention, so an invoice debits the client and credits sales and tax; a credit note does the reverse.
    /// </summary>
    public static string TallyXml(ExportLedgers ledgers, IEnumerable<ExportVoucher> vouchers)
    {
        ArgumentNullException.ThrowIfNull(ledgers);
        ArgumentNullException.ThrowIfNull(vouchers);

        var xml = new StringBuilder();
        xml.Append("<ENVELOPE><HEADER><TALLYREQUEST>Import Data</TALLYREQUEST></HEADER><BODY><IMPORTDATA><REQUESTDESC><REPORTNAME>Vouchers</REPORTNAME>");
        xml.Append("<STATICVARIABLES><SVCURRENTCOMPANY>").Append(SecurityElement.Escape(ledgers.Company)).Append("</SVCURRENTCOMPANY></STATICVARIABLES></REQUESTDESC><REQUESTDATA>");

        foreach (ExportVoucher voucher in vouchers)
        {
            string type = voucher.IsCredit ? "Credit Note" : "Sales";
            xml.Append("<TALLYMESSAGE xmlns:UDF=\"TallyUDF\"><VOUCHER VCHTYPE=\"").Append(type).Append("\" ACTION=\"Create\"><DATE>").Append(voucher.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append("</DATE>");
            xml.Append("<VOUCHERTYPENAME>").Append(type).Append("</VOUCHERTYPENAME><VOUCHERNUMBER>").Append(SecurityElement.Escape(voucher.Number)).Append("</VOUCHERNUMBER>");
            xml.Append("<PARTYLEDGERNAME>").Append(SecurityElement.Escape(voucher.Party)).Append("</PARTYLEDGERNAME><NARRATION>").Append(SecurityElement.Escape(voucher.Narration)).Append("</NARRATION>");

            Entry(xml, voucher.Party, voucher.Net + voucher.Tax, debit: !voucher.IsCredit);
            Entry(xml, ledgers.SalesLedger, voucher.Net, debit: voucher.IsCredit);
            if (voucher.Tax != 0)
            {
                Entry(xml, ledgers.TaxLedger, voucher.Tax, debit: voucher.IsCredit);
            }

            xml.Append("</VOUCHER></TALLYMESSAGE>");
        }

        xml.Append("</REQUESTDATA></IMPORTDATA></BODY></ENVELOPE>");
        return xml.ToString();
    }

    public static string Csv(IEnumerable<ExportVoucher> vouchers)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        var csv = new StringBuilder("Type,Number,Date,Client,Net,Tax,Total,Narration\r\n");
        foreach (ExportVoucher voucher in vouchers)
        {
            decimal sign = voucher.IsCredit ? -1 : 1;
            csv.AppendJoin(',', voucher.IsCredit ? "Credit note" : "Invoice", Field(voucher.Number), voucher.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Field(voucher.Party), Money(sign * voucher.Net), Money(sign * voucher.Tax), Money(sign * (voucher.Net + voucher.Tax)), Field(voucher.Narration));
            csv.Append("\r\n");
        }

        return csv.ToString();
    }

    private static void Entry(StringBuilder xml, string ledger, decimal amount, bool debit)
        => xml.Append("<ALLLEDGERENTRIES.LIST><LEDGERNAME>").Append(SecurityElement.Escape(ledger)).Append("</LEDGERNAME><ISDEEMEDPOSITIVE>").Append(debit ? "Yes" : "No")
            .Append("</ISDEEMEDPOSITIVE><AMOUNT>").Append(Money(debit ? -amount : amount)).Append("</AMOUNT></ALLLEDGERENTRIES.LIST>");

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Quotes a field, and defuses anything a spreadsheet would run as a formula.</summary>
    private static string Field(string value)
    {
        string safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
        return safe.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : safe;
    }
}
