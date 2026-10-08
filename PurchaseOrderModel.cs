using System;
using System.Collections.Generic;
using System.Linq;

namespace ProcurePay
{
    // One line of a PO: what is being bought
    public class PoLine
    {
        public int Id;                 // 0 until saved
        public string Description;
        public string Unit;
        public decimal Quantity;
        public decimal Rate;
        public decimal TaxPercent;
        public decimal Amount;         // Quantity x Rate, before tax (set by PurchaseOrder.Recalculate)

        public decimal Tax { get { return PurchaseOrder.Round(Amount * TaxPercent / 100m); } }
    }

    // One payment milestone of a PO. Invoices (a later step) are booked against these.
    public class PoMilestone
    {
        public int Id;                 // 0 until saved
        public int SeqNo;
        public string Description;
        public DateTime? DueDate;
        public decimal Amount;         // includes tax; all milestones together equal the PO total
        public decimal InvoicedAmount;
        public string Status = "Open"; // Open, Invoiced, Paid

        public decimal Balance { get { return Amount - InvoicedAmount; } }
    }

    public class PurchaseOrder
    {
        public int Id;
        public string EntityCode;
        public string No;              // PO/<FY>/0001
        public int AgreementId;
        public string AgreementNo;     // for display
        public int VendorId;
        public string VendorName;      // for display
        public string Title;
        public DateTime PoDate = DateTime.Today;
        public DateTime? DeliveryDate;
        public string Currency;
        public decimal SubTotal, TaxTotal, Total;
        public string Status = "Draft"; // Draft, Pending Approval, Approved, Rejected, Cancelled, Closed
        public string Remarks;
        public string CreatedBy;
        public DateTime CreatedAt;
        public string CancelReason, CancelledBy;
        public DateTime? CancelledAt;

        public List<PoLine> Lines = new List<PoLine>();
        public List<PoMilestone> Milestones = new List<PoMilestone>();
        public List<ApprovalStep> Approvals = new List<ApprovalStep>();

        public ApprovalStep Pending { get { return Approvals.OrderBy(s => s.Level).FirstOrDefault(s => s.Status == "Pending"); } }

        // Only a draft or a rejected PO can still be edited
        public bool IsEditable { get { return Status == "Draft" || Status == "Rejected"; } }

        public static decimal Round(decimal v) { return Math.Round(v, 2, MidpointRounding.AwayFromZero); }

        // Works out every line amount and the three totals. Call after any change to the lines.
        public void Recalculate()
        {
            foreach (var l in Lines) l.Amount = Round(l.Quantity * l.Rate);
            SubTotal = Lines.Sum(l => l.Amount);
            TaxTotal = Lines.Sum(l => l.Tax);
            Total = SubTotal + TaxTotal;
        }
    }
}
