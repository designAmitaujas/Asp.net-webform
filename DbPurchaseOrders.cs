using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace ProcurePay
{
    // Purchase orders: loading, the rules a PO must pass, and saving it in place in one transaction.
    //
    // Rules (all checked here, inside the transaction, so no screen can get around them):
    //  - a PO is raised only against a SIGNED agreement of the same entity, and that agreement must be linked to a vendor
    //  - the PO uses the agreement's vendor and currency
    //  - the PO's sub-total (before tax) cannot exceed the agreement balance:
    //        agreement value - sub-totals of its other POs that are not Rejected or Cancelled
    //  - the milestones must add up exactly to the PO total
    //  - only a Draft or Rejected PO can be changed
    public static partial class Db
    {
        public static List<PurchaseOrder> LoadPos(string entity)
        {
            var map = new Dictionary<int, PurchaseOrder>();
            using (var c = Open())
            {
                using (var r = Cmd(c, @"SELECT p.Id, p.EntityCode, p.PoNo, p.AgreementId, a.AgreementNo, p.VendorId, v.Name, p.Title, p.PoDate, p.DeliveryDate,
                            p.Currency, p.SubTotal, p.TaxTotal, p.Total, p.Status, p.Remarks, p.CreatedBy, p.CreatedAt, p.CancelReason, p.CancelledBy, p.CancelledAt
                            FROM PurchaseOrders p JOIN Agreements a ON a.Id=p.AgreementId JOIN Vendors v ON v.VendorId=p.VendorId
                            WHERE p.EntityCode=@e ORDER BY p.Id DESC", null, "@e", entity).ExecuteReader())
                    while (r.Read())
                    {
                        var p = new PurchaseOrder
                        {
                            Id = r.GetInt32(0), EntityCode = r.GetString(1), No = r.GetString(2), AgreementId = r.GetInt32(3), AgreementNo = r.GetString(4),
                            VendorId = r.GetInt32(5), VendorName = r.GetString(6), Title = r.GetString(7), PoDate = r.GetDateTime(8),
                            DeliveryDate = r.IsDBNull(9) ? (DateTime?)null : r.GetDateTime(9), Currency = r.GetString(10),
                            SubTotal = r.GetDecimal(11), TaxTotal = r.GetDecimal(12), Total = r.GetDecimal(13), Status = r.GetString(14),
                            Remarks = r.IsDBNull(15) ? "" : r.GetString(15), CreatedBy = r.IsDBNull(16) ? "" : r.GetString(16), CreatedAt = r.GetDateTime(17),
                            CancelReason = r.IsDBNull(18) ? "" : r.GetString(18), CancelledBy = r.IsDBNull(19) ? "" : r.GetString(19),
                            CancelledAt = r.IsDBNull(20) ? (DateTime?)null : r.GetDateTime(20)
                        };
                        map[p.Id] = p;
                    }
                const string sub = "(SELECT Id FROM PurchaseOrders WHERE EntityCode=@e)";
                using (var r = Cmd(c, "SELECT PoId, Id, Description, Unit, Quantity, Rate, TaxPercent, Amount FROM PurchaseOrderLines WHERE PoId IN " + sub + " ORDER BY LineNumber", null, "@e", entity).ExecuteReader())
                    while (r.Read())
                        map[r.GetInt32(0)].Lines.Add(new PoLine { Id = r.GetInt32(1), Description = r.GetString(2), Unit = r.IsDBNull(3) ? "" : r.GetString(3),
                            Quantity = r.GetDecimal(4), Rate = r.GetDecimal(5), TaxPercent = r.GetDecimal(6), Amount = r.GetDecimal(7) });
                using (var r = Cmd(c, "SELECT PoId, Id, SeqNo, Description, DueDate, Amount, InvoicedAmount, Status FROM PoMilestones WHERE PoId IN " + sub + " ORDER BY SeqNo", null, "@e", entity).ExecuteReader())
                    while (r.Read())
                        map[r.GetInt32(0)].Milestones.Add(new PoMilestone { Id = r.GetInt32(1), SeqNo = r.GetInt32(2), Description = r.GetString(3),
                            DueDate = r.IsDBNull(4) ? (DateTime?)null : r.GetDateTime(4), Amount = r.GetDecimal(5), InvoicedAmount = r.GetDecimal(6), Status = r.GetString(7) });
                using (var r = Cmd(c, "SELECT PoId, Level, Role, Label, Status, ApprovedBy, At, Remarks FROM PoApprovalSteps WHERE PoId IN " + sub + " ORDER BY Level", null, "@e", entity).ExecuteReader())
                    while (r.Read())
                        map[r.GetInt32(0)].Approvals.Add(new ApprovalStep { Level = r.GetInt32(1), Role = r.IsDBNull(2) ? null : r.GetString(2), Label = r.IsDBNull(3) ? null : r.GetString(3),
                            Status = r.GetString(4), ApprovedBy = r.IsDBNull(5) ? null : r.GetString(5), At = r.IsDBNull(6) ? (DateTime?)null : r.GetDateTime(6),
                            Remarks = r.IsDBNull(7) ? null : r.GetString(7) });
            }
            return map.Values.OrderByDescending(p => p.Id).ToList();
        }

        // What is still free on an agreement for new POs. Pass the PO being edited (or 0) so it is not counted against itself.
        public static decimal AgreementBalance(int agreementId, int exceptPoId)
        {
            using (var c = Open()) return AgreementBalance(c, null, agreementId, exceptPoId);
        }

        private static decimal AgreementBalance(SqlConnection c, SqlTransaction tx, int agreementId, int exceptPoId)
        {
            object v = Cmd(c, @"SELECT a.Value - ISNULL((SELECT SUM(p.SubTotal) FROM PurchaseOrders p
                                    WHERE p.AgreementId=a.Id AND p.Id<>@p AND p.Status NOT IN ('Rejected','Cancelled')), 0)
                                FROM Agreements a WHERE a.Id=@a", tx, "@a", agreementId, "@p", exceptPoId).ExecuteScalar();
            return v == null || v == DBNull.Value ? 0m : (decimal)v;
        }

        // Signed agreements that still have money left and a linked vendor: the list the "new PO" form will offer
        public static List<Agreement> AgreementsOpenForPo(string entity)
        {
            return LoadAgreements(entity).Where(a => a.Status == "Signed" && a.VendorId.HasValue && AgreementBalance(a.Id, 0) > 0).ToList();
        }

        private static InvalidOperationException PoRule(string message) { return new InvalidOperationException(message); }

        // Inserts (Id == 0, assigns PO/<FY>/<seq>) or updates a PO with its lines and milestones, in one transaction.
        // Totals are recalculated here; whatever the screen sent for them is ignored.
        public static void SavePo(PurchaseOrder po)
        {
            // ---- checks that need no database
            if (string.IsNullOrWhiteSpace(po.Title)) throw PoRule("Enter a title for the purchase order.");
            if (po.Lines.Count == 0) throw PoRule("Add at least one line.");
            for (int i = 0; i < po.Lines.Count; i++)
            {
                var l = po.Lines[i];
                if (string.IsNullOrWhiteSpace(l.Description)) throw PoRule("Line " + (i + 1) + ": enter a description.");
                if (l.Quantity <= 0) throw PoRule("Line " + (i + 1) + ": quantity must be more than zero.");
                if (l.Rate < 0) throw PoRule("Line " + (i + 1) + ": rate cannot be negative.");
                if (l.TaxPercent < 0 || l.TaxPercent > 100) throw PoRule("Line " + (i + 1) + ": tax % must be between 0 and 100.");
            }
            po.Recalculate();
            if (po.Total <= 0) throw PoRule("The PO total must be more than zero.");
            if (po.Milestones.Count == 0) throw PoRule("Add at least one payment milestone.");
            for (int i = 0; i < po.Milestones.Count; i++)
            {
                var m = po.Milestones[i];
                if (string.IsNullOrWhiteSpace(m.Description)) throw PoRule("Milestone " + (i + 1) + ": enter a description.");
                if (m.Amount <= 0) throw PoRule("Milestone " + (i + 1) + ": amount must be more than zero.");
            }
            decimal scheduled = po.Milestones.Sum(m => PurchaseOrder.Round(m.Amount));
            if (scheduled != po.Total)
                throw PoRule("The milestones add up to " + scheduled.ToString("N2") + " but the PO total is " + po.Total.ToString("N2") + ". They must be equal.");

            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                bool isNew = po.Id == 0;
                var changes = new List<string>();

                // ---- the agreement: lock it so two POs raised at the same moment cannot both use the last of the balance
                string agStatus, agEntity, agCurrency; int? agVendor;
                using (var r = Cmd(c, "SELECT Status, EntityCode, Currency, VendorId FROM Agreements WITH (UPDLOCK, HOLDLOCK) WHERE Id=@a", tx, "@a", po.AgreementId).ExecuteReader())
                {
                    if (!r.Read()) throw PoRule("The agreement for this PO no longer exists.");
                    agStatus = r.GetString(0); agEntity = r.GetString(1);
                    agCurrency = r.IsDBNull(2) ? "" : r.GetString(2);
                    agVendor = r.IsDBNull(3) ? (int?)null : r.GetInt32(3);
                }
                if (agEntity != po.EntityCode) throw PoRule("The agreement belongs to a different entity.");
                if (agStatus != "Signed") throw PoRule("A purchase order can only be raised against a signed agreement. This one is " + agStatus + ".");
                if (!agVendor.HasValue) throw PoRule("This agreement is not linked to a vendor. Open it, choose the supplier from the vendor list and save it first.");
                po.VendorId = agVendor.Value;
                po.Currency = agCurrency;

                // ---- the PO itself
                string oTitle = null, oRemarks = null; decimal oTotal = 0; DateTime oDate = po.PoDate; DateTime? oDelivery = null;
                if (!isNew)
                {
                    string stored;
                    using (var r = Cmd(c, "SELECT Status, Title, PoDate, DeliveryDate, Remarks, Total FROM PurchaseOrders WITH (UPDLOCK, HOLDLOCK) WHERE Id=@i", tx, "@i", po.Id).ExecuteReader())
                    {
                        if (!r.Read()) throw PoRule("This purchase order no longer exists.");
                        stored = r.GetString(0); oTitle = r.GetString(1); oDate = r.GetDateTime(2);
                        oDelivery = r.IsDBNull(3) ? (DateTime?)null : r.GetDateTime(3);
                        oRemarks = r.IsDBNull(4) ? "" : r.GetString(4); oTotal = r.GetDecimal(5);
                    }
                    if (stored != "Draft" && stored != "Rejected") throw PoRule("This purchase order is " + stored + " and can no longer be edited.");
                }

                decimal balance = AgreementBalance(c, tx, po.AgreementId, po.Id);
                if (po.SubTotal > balance)
                    throw PoRule("The PO sub-total " + po.SubTotal.ToString("N2") + " " + po.Currency + " is more than the agreement balance of " + balance.ToString("N2") + " " + po.Currency + ".");

                object delivery = po.DeliveryDate.HasValue ? (object)po.DeliveryDate.Value.Date : DBNull.Value;
                if (isNew)
                {
                    string prefix = "PO/" + Current.FinancialYear() + "/";
                    object max = Cmd(c, @"SELECT MAX(CAST(RIGHT(PoNo,4) AS INT)) FROM PurchaseOrders WITH (UPDLOCK, HOLDLOCK)
                                          WHERE EntityCode=@e AND PoNo LIKE @p", tx, "@e", po.EntityCode, "@p", prefix + "%").ExecuteScalar();
                    po.No = prefix + ((max == DBNull.Value || max == null ? 0 : (int)max) + 1).ToString("0000");
                    po.CreatedAt = DateTime.Now;
                    po.Status = "Draft";
                    po.Id = (int)Cmd(c, @"INSERT PurchaseOrders (EntityCode,PoNo,AgreementId,VendorId,Title,PoDate,DeliveryDate,Currency,SubTotal,TaxTotal,Total,Status,Remarks,CreatedBy,CreatedAt)
                        OUTPUT INSERTED.Id VALUES (@e,@n,@a,@v,@t,@pd,@dd,@cu,@s,@x,@tt,@st,@rm,@cb,@ca)", tx,
                        "@e", po.EntityCode, "@n", po.No, "@a", po.AgreementId, "@v", po.VendorId, "@t", po.Title.Trim(), "@pd", po.PoDate.Date, "@dd", delivery,
                        "@cu", po.Currency, "@s", po.SubTotal, "@x", po.TaxTotal, "@tt", po.Total, "@st", po.Status, "@rm", N(po.Remarks),
                        "@cb", N(po.CreatedBy ?? AppSession.UserName), "@ca", po.CreatedAt).ExecuteScalar();
                }
                else
                {
                    Diff(changes, "Title", oTitle, po.Title.Trim());
                    Diff(changes, "PO date", oDate.ToString("yyyy-MM-dd"), po.PoDate.ToString("yyyy-MM-dd"));
                    Diff(changes, "Delivery date", oDelivery.HasValue ? oDelivery.Value.ToString("yyyy-MM-dd") : "", po.DeliveryDate.HasValue ? po.DeliveryDate.Value.ToString("yyyy-MM-dd") : "");
                    Diff(changes, "Remarks", oRemarks, po.Remarks);
                    Diff(changes, "Total", Num(oTotal), Num(po.Total));
                    Cmd(c, @"UPDATE PurchaseOrders SET Title=@t,PoDate=@pd,DeliveryDate=@dd,Currency=@cu,VendorId=@v,SubTotal=@s,TaxTotal=@x,Total=@tt,Remarks=@rm
                             WHERE Id=@i", tx, "@i", po.Id, "@t", po.Title.Trim(), "@pd", po.PoDate.Date, "@dd", delivery, "@cu", po.Currency, "@v", po.VendorId,
                        "@s", po.SubTotal, "@x", po.TaxTotal, "@tt", po.Total, "@rm", N(po.Remarks)).ExecuteNonQuery();
                }

                SyncPoLines(c, tx, po, changes);
                SyncPoMilestones(c, tx, po, changes);

                if (isNew)
                    Audit("PO", po.No, "Created", "Agreement " + po.AgreementNo + "; Total=" + Num(po.Total) + " " + po.Currency + "; Lines=" + po.Lines.Count + "; Milestones=" + po.Milestones.Count, po.EntityCode, c, tx);
                else if (changes.Count > 0)
                    Audit("PO", po.No, "Edited", string.Join("; ", changes), po.EntityCode, c, tx);
                tx.Commit();
            }
        }

        // Lines: existing rows are updated by id, new ones inserted, removed ones deleted. Line numbers follow the list order.
        private static void SyncPoLines(SqlConnection c, SqlTransaction tx, PurchaseOrder po, List<string> changes)
        {
            var stored = new Dictionary<int, PoLine>();
            using (var r = Cmd(c, "SELECT Id, Description, Unit, Quantity, Rate, TaxPercent FROM PurchaseOrderLines WHERE PoId=@i", tx, "@i", po.Id).ExecuteReader())
                while (r.Read())
                    stored[r.GetInt32(0)] = new PoLine { Id = r.GetInt32(0), Description = r.GetString(1), Unit = r.IsDBNull(2) ? "" : r.GetString(2), Quantity = r.GetDecimal(3), Rate = r.GetDecimal(4), TaxPercent = r.GetDecimal(5) };
            // move the numbers out of the way first so re-ordering can never trip the unique (PoId, LineNumber) rule
            if (stored.Count > 0) Cmd(c, "UPDATE PurchaseOrderLines SET LineNumber = LineNumber + 100000 WHERE PoId=@i", tx, "@i", po.Id).ExecuteNonQuery();

            int n = 0;
            foreach (var l in po.Lines)
            {
                n++;
                PoLine old;
                object[] kv = { "@i", po.Id, "@n", n, "@d", l.Description.Trim(), "@u", N(l.Unit), "@q", l.Quantity, "@r", l.Rate, "@x", l.TaxPercent, "@a", l.Amount, "@id", l.Id };
                if (l.Id == 0 || !stored.TryGetValue(l.Id, out old))
                {
                    l.Id = (int)Cmd(c, "INSERT PurchaseOrderLines (PoId,LineNumber,Description,Unit,Quantity,Rate,TaxPercent,Amount) OUTPUT INSERTED.Id VALUES (@i,@n,@d,@u,@q,@r,@x,@a)", tx, kv).ExecuteScalar();
                    changes.Add("Line added: " + Show(l.Description));
                }
                else
                {
                    Cmd(c, "UPDATE PurchaseOrderLines SET LineNumber=@n,Description=@d,Unit=@u,Quantity=@q,Rate=@r,TaxPercent=@x,Amount=@a WHERE Id=@id", tx, kv).ExecuteNonQuery();
                    if (old.Description != l.Description.Trim() || old.Quantity != l.Quantity || old.Rate != l.Rate || old.TaxPercent != l.TaxPercent)
                        changes.Add("Line " + n + " changed: " + Show(old.Description));
                    stored.Remove(l.Id);
                }
            }
            foreach (var gone in stored.Values)
            {
                Cmd(c, "DELETE FROM PurchaseOrderLines WHERE Id=@id", tx, "@id", gone.Id).ExecuteNonQuery();
                changes.Add("Line removed: " + Show(gone.Description));
            }
        }

        // Milestones: same in-place approach. A milestone that already has an invoice against it can never be removed.
        private static void SyncPoMilestones(SqlConnection c, SqlTransaction tx, PurchaseOrder po, List<string> changes)
        {
            var stored = new Dictionary<int, PoMilestone>();
            using (var r = Cmd(c, "SELECT Id, Description, DueDate, Amount, InvoicedAmount FROM PoMilestones WHERE PoId=@i", tx, "@i", po.Id).ExecuteReader())
                while (r.Read())
                    stored[r.GetInt32(0)] = new PoMilestone { Id = r.GetInt32(0), Description = r.GetString(1), DueDate = r.IsDBNull(2) ? (DateTime?)null : r.GetDateTime(2), Amount = r.GetDecimal(3), InvoicedAmount = r.GetDecimal(4) };
            if (stored.Count > 0) Cmd(c, "UPDATE PoMilestones SET SeqNo = SeqNo + 100000 WHERE PoId=@i", tx, "@i", po.Id).ExecuteNonQuery();

            int n = 0;
            foreach (var m in po.Milestones)
            {
                n++;
                PoMilestone old;
                object due = m.DueDate.HasValue ? (object)m.DueDate.Value.Date : DBNull.Value;
                decimal amt = PurchaseOrder.Round(m.Amount);
                if (m.Id == 0 || !stored.TryGetValue(m.Id, out old))
                {
                    m.Id = (int)Cmd(c, "INSERT PoMilestones (PoId,SeqNo,Description,DueDate,Amount) OUTPUT INSERTED.Id VALUES (@i,@n,@d,@du,@a)", tx,
                        "@i", po.Id, "@n", n, "@d", m.Description.Trim(), "@du", due, "@a", amt).ExecuteScalar();
                    m.SeqNo = n;
                    changes.Add("Milestone added: " + Show(m.Description) + " " + Num(amt));
                }
                else
                {
                    if (amt < old.InvoicedAmount) throw PoRule("Milestone " + n + " already has " + Num(old.InvoicedAmount) + " invoiced, so its amount cannot go below that.");
                    Cmd(c, "UPDATE PoMilestones SET SeqNo=@n,Description=@d,DueDate=@du,Amount=@a WHERE Id=@id", tx,
                        "@id", m.Id, "@n", n, "@d", m.Description.Trim(), "@du", due, "@a", amt).ExecuteNonQuery();
                    m.SeqNo = n;
                    if (old.Description != m.Description.Trim() || old.Amount != amt || old.DueDate != (m.DueDate.HasValue ? (DateTime?)m.DueDate.Value.Date : null))
                        changes.Add("Milestone " + n + " changed: " + Show(old.Description) + " " + Num(old.Amount) + " -> " + Num(amt));
                    stored.Remove(m.Id);
                }
            }
            foreach (var gone in stored.Values)
            {
                if (gone.InvoicedAmount > 0) throw PoRule("Milestone \"" + gone.Description + "\" already has an invoice against it and cannot be removed.");
                Cmd(c, "DELETE FROM PoMilestones WHERE Id=@id", tx, "@id", gone.Id).ExecuteNonQuery();
                changes.Add("Milestone removed: " + Show(gone.Description));
            }
        }
    }
}
