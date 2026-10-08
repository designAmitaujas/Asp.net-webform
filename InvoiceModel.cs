using System;

namespace ProcurePay
{
    // A vendor invoice booked against one milestone of an approved purchase order.
    // All amounts are in the PO's currency; BaseTotal is the invoice total in the entity's own currency.
    public class Invoice
    {
        public int Id;
        public string EntityCode;
        public string No;                  // our booking number: IB/<FY>/0001 (not the vendor's number)

        public int PoId;
        public string PoNo, PoTitle;       // for display
        public int MilestoneId;
        public int MilestoneSeq;
        public string MilestoneDesc;
        public decimal MilestoneAmount, MilestoneInvoiced;   // as stored right now, for display

        public int VendorId;
        public string VendorName;          // for display

        public string VendorInvoiceNo;     // the number printed on the vendor's invoice
        public DateTime InvoiceDate = DateTime.Today;
        public DateTime? DueDate;

        public string Currency;            // always the PO's currency
        public decimal ExchangeRate = 1m;  // 1 unit of Currency = ExchangeRate units of the entity's currency

        public decimal Taxable;            // value before GST
        public decimal GstPercent, GstAmount;
        public decimal Total;              // Taxable + GST
        public string TdsSection;
        public decimal TdsPercent, TdsAmount;   // TDS is worked out on the taxable value
        public decimal NetPayable;         // Total - TDS
        public decimal BaseTotal;          // Total x ExchangeRate

        public string FileRef;             // link or path of the invoice copy
        public string Remarks;
        public string Status = "Draft";    // Draft, Pending Approval, Approved, Rejected, Cancelled, Paid
        public string CreatedBy;
        public DateTime CreatedAt;
        public string UpdatedBy;
        public DateTime? UpdatedAt;
        public string CancelReason, CancelledBy;
        public DateTime? CancelledAt;

        // Only a draft or a rejected invoice can still be edited
        public bool IsEditable { get { return Status == "Draft" || Status == "Rejected"; } }

        public decimal MilestoneBalance { get { return MilestoneAmount - MilestoneInvoiced; } }

        public static decimal Round(decimal v) { return Math.Round(v, 2, MidpointRounding.AwayFromZero); }

        // Works out GST, total, TDS, net payable and the base-currency total from the typed values.
        public void Recalculate()
        {
            Taxable = Round(Taxable);
            GstAmount = Round(Taxable * GstPercent / 100m);
            Total = Taxable + GstAmount;
            TdsAmount = Round(Taxable * TdsPercent / 100m);
            NetPayable = Total - TdsAmount;
            BaseTotal = Round(Total * ExchangeRate);
        }
    }
}
