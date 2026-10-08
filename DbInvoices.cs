using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace ProcurePay
{
    // Invoices: loading, and saving a draft in one transaction.
    //
    // Rules checked here (so no screen can get around them):
    //  - an invoice is raised only against an APPROVED purchase order of the same entity, and one of that PO's milestones
    //  - the vendor and currency always come from the PO
    //  - when the PO currency differs from the entity's currency an exchange rate must be given
    //  - GST, total, TDS, net payable and the base-currency total are worked out here; whatever the screen sent is ignored
    //  - only a Draft or Rejected invoice can be changed
    // Step 28 adds the booking rules (duplicate check, milestone balance, currency conversion into the milestone/budget figures).
    public static partial class Db
    {
        private static InvalidOperationException InvRule(string message) { return new InvalidOperationException(message); }

        private const string InvoiceSelect = @"SELECT i.Id, i.EntityCode, i.BookingNo, i.PoId, p.PoNo, p.Title, i.MilestoneId, m.SeqNo, m.Description, m.Amount, m.InvoicedAmount,
                i.VendorId, v.Name, i.VendorInvoiceNo, i.InvoiceDate, i.DueDate, i.Currency, i.ExchangeRate,
                i.Taxable, i.GstPercent, i.GstAmount, i.Total, i.TdsSection, i.TdsPercent, i.TdsAmount, i.NetPayable, i.BaseTotal,
                i.FileRef, i.Remarks, i.Status, i.CreatedBy, i.CreatedAt, i.UpdatedBy, i.UpdatedAt, i.CancelReason, i.CancelledBy, i.CancelledAt
            FROM Invoices i
            JOIN PurchaseOrders p ON p.Id=i.PoId
            JOIN PoMilestones m ON m.Id=i.MilestoneId
            JOIN Vendors v ON v.VendorId=i.VendorId ";

        private static Invoice ReadInvoice(SqlDataReader r)
        {
            return new Invoice
            {
                Id = r.GetInt32(0), EntityCode = r.GetString(1), No = r.GetString(2),
                PoId = r.GetInt32(3), PoNo = r.GetString(4), PoTitle = r.GetString(5),
                MilestoneId = r.GetInt32(6), MilestoneSeq = r.GetInt32(7), MilestoneDesc = r.GetString(8),
                MilestoneAmount = r.GetDecimal(9), MilestoneInvoiced = r.GetDecimal(10),
                VendorId = r.GetInt32(11), VendorName = r.GetString(12), VendorInvoiceNo = r.GetString(13),
                InvoiceDate = r.GetDateTime(14), DueDate = r.IsDBNull(15) ? (DateTime?)null : r.GetDateTime(15),
                Currency = r.GetString(16), ExchangeRate = r.GetDecimal(17),
                Taxable = r.GetDecimal(18), GstPercent = r.GetDecimal(19), GstAmount = r.GetDecimal(20), Total = r.GetDecimal(21),
                TdsSection = S(r, 22), TdsPercent = r.GetDecimal(23), TdsAmount = r.GetDecimal(24),
                NetPayable = r.GetDecimal(25), BaseTotal = r.GetDecimal(26),
                FileRef = S(r, 27), Remarks = S(r, 28), Status = r.GetString(29),
                CreatedBy = S(r, 30), CreatedAt = r.GetDateTime(31), UpdatedBy = S(r, 32),
                UpdatedAt = r.IsDBNull(33) ? (DateTime?)null : r.GetDateTime(33),
                CancelReason = S(r, 34), CancelledBy = S(r, 35), CancelledAt = r.IsDBNull(36) ? (DateTime?)null : r.GetDateTime(36)
            };
        }

        // Every invoice of an entity, newest first
        public static List<Invoice> LoadInvoices(string entity)
        {
            var list = new List<Invoice>();
            using (var c = Open())
            using (var r = Cmd(c, InvoiceSelect + "WHERE i.EntityCode=@e ORDER BY i.Id DESC", null, "@e", entity).ExecuteReader())
                while (r.Read()) list.Add(ReadInvoice(r));
            return list;
        }

        // One invoice, freshly read (null if it is gone)
        public static Invoice LoadInvoice(string entity, int id)
        {
            using (var c = Open())
            using (var r = Cmd(c, InvoiceSelect + "WHERE i.EntityCode=@e AND i.Id=@i", null, "@e", entity, "@i", id).ExecuteReader())
                return r.Read() ? ReadInvoice(r) : null;
        }

        // Approved purchase orders that still have a milestone with money left: the list the "new invoice" form offers
        public static List<PurchaseOrder> PosOpenForInvoice(string entity)
        {
            return LoadPos(entity).Where(p => p.Status == "Approved" && p.Milestones.Any(m => m.Balance > 0)).ToList();
        }

        // Inserts (Id == 0, assigns IB/<FY>/<seq>) or updates a draft invoice, in one transaction.
        public static void SaveInvoice(Invoice inv)
        {
            // ---- checks that need no database
            inv.VendorInvoiceNo = (inv.VendorInvoiceNo ?? "").Trim();
            if (inv.VendorInvoiceNo.Length == 0) throw InvRule("Enter the vendor's invoice number.");
            if (inv.VendorInvoiceNo.Length > 50) throw InvRule("The vendor's invoice number can be at most 50 characters.");
            if (inv.PoId <= 0) throw InvRule("Choose the purchase order this invoice is for.");
            if (inv.MilestoneId <= 0) throw InvRule("Choose the milestone this invoice is for.");
            if (inv.Taxable <= 0) throw InvRule("The taxable value must be more than zero.");
            if (inv.GstPercent < 0 || inv.GstPercent > 100) throw InvRule("GST % must be between 0 and 100.");
            if (inv.TdsPercent < 0 || inv.TdsPercent > 100) throw InvRule("TDS % must be between 0 and 100.");
            if (inv.ExchangeRate <= 0) throw InvRule("The exchange rate must be more than zero.");
            if (inv.DueDate.HasValue && inv.DueDate.Value.Date < inv.InvoiceDate.Date) throw InvRule("The due date cannot be before the invoice date.");

            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                bool isNew = inv.Id == 0;
                var changes = new List<string>();

                // ---- the PO: lock it so it cannot be cancelled while this invoice is being saved
                string poStatus, poEntity, poCurrency, poNo; int poVendor;
                using (var r = Cmd(c, "SELECT Status, EntityCode, Currency, VendorId, PoNo FROM PurchaseOrders WITH (UPDLOCK, HOLDLOCK) WHERE Id=@p", tx, "@p", inv.PoId).ExecuteReader())
                {
                    if (!r.Read()) throw InvRule("The purchase order for this invoice no longer exists.");
                    poStatus = r.GetString(0); poEntity = r.GetString(1); poCurrency = r.GetString(2); poVendor = r.GetInt32(3); poNo = r.GetString(4);
                }
                if (poEntity != inv.EntityCode) throw InvRule("The purchase order belongs to a different entity.");
                if (poStatus != "Approved") throw InvRule("An invoice can only be booked against an approved purchase order. " + poNo + " is " + poStatus + ".");

                int msPo = 0;
                using (var r = Cmd(c, "SELECT PoId, SeqNo FROM PoMilestones WHERE Id=@m", tx, "@m", inv.MilestoneId).ExecuteReader())
                    if (r.Read()) { msPo = r.GetInt32(0); inv.MilestoneSeq = r.GetInt32(1); }
                if (msPo != inv.PoId) throw InvRule("That milestone does not belong to purchase order " + poNo + ".");

                // vendor and currency always follow the PO
                inv.VendorId = poVendor;
                inv.Currency = poCurrency;

                string baseCcy = (string)Cmd(c, "SELECT Currency FROM Entities WHERE Code=@e", tx, "@e", inv.EntityCode).ExecuteScalar();
                if (string.Equals(baseCcy, poCurrency, StringComparison.OrdinalIgnoreCase)) inv.ExchangeRate = 1m;
                else if (inv.ExchangeRate == 1m)
                    throw InvRule("This invoice is in " + poCurrency + " but the books of " + inv.EntityCode + " are kept in " + baseCcy + ". Enter the exchange rate (1 " + poCurrency + " = ? " + baseCcy + ").");
                inv.Recalculate();

                // ---- the invoice itself
                if (!isNew)
                {
                    string stored, oEntity, oNo, oVin, oTdsSec, oFile, oRemarks; int oPo, oMs;
                    DateTime oDate; DateTime? oDue; decimal oTaxable, oGst, oTdsPct, oRate;
                    using (var r = Cmd(c, @"SELECT Status, EntityCode, PoId, MilestoneId, BookingNo, VendorInvoiceNo, InvoiceDate, DueDate, Taxable, GstPercent,
                                                   TdsSection, TdsPercent, ExchangeRate, FileRef, Remarks
                                            FROM Invoices WITH (UPDLOCK, HOLDLOCK) WHERE Id=@i", tx, "@i", inv.Id).ExecuteReader())
                    {
                        if (!r.Read()) throw InvRule("This invoice no longer exists.");
                        stored = r.GetString(0); oEntity = r.GetString(1); oPo = r.GetInt32(2); oMs = r.GetInt32(3); oNo = r.GetString(4); oVin = r.GetString(5);
                        oDate = r.GetDateTime(6); oDue = r.IsDBNull(7) ? (DateTime?)null : r.GetDateTime(7); oTaxable = r.GetDecimal(8); oGst = r.GetDecimal(9);
                        oTdsSec = S(r, 10); oTdsPct = r.GetDecimal(11); oRate = r.GetDecimal(12); oFile = S(r, 13); oRemarks = S(r, 14);
                    }
                    if (oEntity != inv.EntityCode) throw InvRule("This invoice belongs to a different entity.");
                    if (stored != "Draft" && stored != "Rejected") throw InvRule("This invoice is " + stored + " and can no longer be edited.");
                    if (oPo != inv.PoId) throw InvRule("An invoice cannot be moved to another purchase order. Cancel it and book a new one.");
                    inv.No = oNo;

                    Diff(changes, "Vendor invoice no.", oVin, inv.VendorInvoiceNo);
                    Diff(changes, "Invoice date", oDate.ToString("yyyy-MM-dd"), inv.InvoiceDate.ToString("yyyy-MM-dd"));
                    Diff(changes, "Due date", oDue.HasValue ? oDue.Value.ToString("yyyy-MM-dd") : "", inv.DueDate.HasValue ? inv.DueDate.Value.ToString("yyyy-MM-dd") : "");
                    Diff(changes, "Milestone", oMs.ToString(), inv.MilestoneId.ToString());
                    Diff(changes, "Taxable value", Num(oTaxable), Num(inv.Taxable));
                    Diff(changes, "GST %", Num(oGst), Num(inv.GstPercent));
                    Diff(changes, "TDS section", oTdsSec, inv.TdsSection);
                    Diff(changes, "TDS %", Num(oTdsPct), Num(inv.TdsPercent));
                    Diff(changes, "Exchange rate", oRate.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture), inv.ExchangeRate.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));
                    Diff(changes, "Invoice copy", oFile, inv.FileRef);
                    Diff(changes, "Remarks", oRemarks, inv.Remarks);
                }

                object due = inv.DueDate.HasValue ? (object)inv.DueDate.Value.Date : DBNull.Value;
                if (isNew)
                {
                    string prefix = "IB/" + Current.FinancialYear() + "/";
                    object max = Cmd(c, @"SELECT MAX(CAST(RIGHT(BookingNo,4) AS INT)) FROM Invoices WITH (UPDLOCK, HOLDLOCK)
                                          WHERE EntityCode=@e AND BookingNo LIKE @p", tx, "@e", inv.EntityCode, "@p", prefix + "%").ExecuteScalar();
                    inv.No = prefix + ((max == DBNull.Value || max == null ? 0 : (int)max) + 1).ToString("0000");
                    inv.CreatedAt = DateTime.Now;
                    inv.Status = "Draft";
                    inv.CreatedBy = inv.CreatedBy ?? AppSession.UserName;
                    inv.Id = (int)Cmd(c, @"INSERT Invoices (EntityCode,BookingNo,PoId,MilestoneId,VendorId,VendorInvoiceNo,InvoiceDate,DueDate,Currency,ExchangeRate,
                                                            Taxable,GstPercent,GstAmount,Total,TdsSection,TdsPercent,TdsAmount,NetPayable,BaseTotal,FileRef,Remarks,Status,CreatedBy,CreatedAt)
                        OUTPUT INSERTED.Id
                        VALUES (@e,@n,@po,@ms,@v,@vin,@idt,@due,@cu,@rate,@tx,@gp,@ga,@tot,@ts,@tp,@ta,@net,@base,@fr,@rm,@st,@cb,@ca)", tx,
                        "@e", inv.EntityCode, "@n", inv.No, "@po", inv.PoId, "@ms", inv.MilestoneId, "@v", inv.VendorId, "@vin", inv.VendorInvoiceNo,
                        "@idt", inv.InvoiceDate.Date, "@due", due, "@cu", inv.Currency, "@rate", inv.ExchangeRate,
                        "@tx", inv.Taxable, "@gp", inv.GstPercent, "@ga", inv.GstAmount, "@tot", inv.Total,
                        "@ts", N(Nz(inv.TdsSection)), "@tp", inv.TdsPercent, "@ta", inv.TdsAmount, "@net", inv.NetPayable, "@base", inv.BaseTotal,
                        "@fr", N(Nz(inv.FileRef)), "@rm", N(Nz(inv.Remarks)), "@st", inv.Status, "@cb", N(inv.CreatedBy), "@ca", inv.CreatedAt).ExecuteScalar();
                    Audit("Invoice", inv.No, "Created",
                        "PO " + poNo + "; milestone " + inv.MilestoneSeq + "; vendor invoice " + inv.VendorInvoiceNo + "; total " + Num(inv.Total) + " " + inv.Currency + "; net payable " + Num(inv.NetPayable),
                        inv.EntityCode, c, tx);
                }
                else
                {
                    Cmd(c, @"UPDATE Invoices SET MilestoneId=@ms,VendorId=@v,VendorInvoiceNo=@vin,InvoiceDate=@idt,DueDate=@due,Currency=@cu,ExchangeRate=@rate,
                                    Taxable=@tx,GstPercent=@gp,GstAmount=@ga,Total=@tot,TdsSection=@ts,TdsPercent=@tp,TdsAmount=@ta,NetPayable=@net,BaseTotal=@base,
                                    FileRef=@fr,Remarks=@rm,UpdatedBy=@ub,UpdatedAt=SYSDATETIME()
                             WHERE Id=@i", tx,
                        "@i", inv.Id, "@ms", inv.MilestoneId, "@v", inv.VendorId, "@vin", inv.VendorInvoiceNo,
                        "@idt", inv.InvoiceDate.Date, "@due", due, "@cu", inv.Currency, "@rate", inv.ExchangeRate,
                        "@tx", inv.Taxable, "@gp", inv.GstPercent, "@ga", inv.GstAmount, "@tot", inv.Total,
                        "@ts", N(Nz(inv.TdsSection)), "@tp", inv.TdsPercent, "@ta", inv.TdsAmount, "@net", inv.NetPayable, "@base", inv.BaseTotal,
                        "@fr", N(Nz(inv.FileRef)), "@rm", N(Nz(inv.Remarks)), "@ub", N(AppSession.UserName)).ExecuteNonQuery();
                    if (changes.Count > 0) Audit("Invoice", inv.No, "Edited", string.Join("; ", changes), inv.EntityCode, c, tx);
                }
                tx.Commit();
            }
        }
    }
}
