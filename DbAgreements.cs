using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace ProcurePay
{
    // Saving an agreement. Every table is updated in place (rows that did not change are not touched), all in one
    // transaction, and the audit log records exactly which fields changed, from what and to what.
    public static partial class Db
    {
        // Property name -> the caption shown on the form, for readable audit entries
        private static readonly Dictionary<string, string> FieldLabels =
            AgreementSchema.Tabs.SelectMany(t => t.Fields).ToDictionary(f => f.Prop, f => f.Label);

        private static string Show(string v)
        {
            if (string.IsNullOrEmpty(v)) return "(blank)";
            if (v == "true") return "Yes";
            v = v.Replace("\r", " ").Replace("\n", " ");
            return v.Length > 50 ? v.Substring(0, 49) + "\u2026" : v;
        }

        // Adds "Caption: old -> new" to the list when the two values differ
        private static void Diff(List<string> changes, string prop, string oldValue, string newValue)
        {
            if (string.Equals(oldValue ?? "", newValue ?? "", StringComparison.Ordinal)) return;
            string label;
            if (!FieldLabels.TryGetValue(prop, out label)) label = prop;
            changes.Add(label + ": " + Show(oldValue) + " -> " + Show(newValue));
        }

        private static string Num(decimal v) { return v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); }

        // Inserts (Id == 0, assigns the number PA/<FY>/<seq>) or updates an agreement and its child rows, in one transaction.
        // A signed agreement is locked: only its status (to Cancelled) and its linked documents may change.
        public static void SaveAgreement(Agreement a)
        {
            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                bool isNew = a.Id == 0;
                bool locked = false;
                var changes = new List<string>();
                object vendorId = a.VendorId.HasValue ? (object)a.VendorId.Value : DBNull.Value;
                object signedAt = a.SignedAt.HasValue ? (object)a.SignedAt.Value : DBNull.Value;

                if (isNew)
                {
                    string prefix = "PA/" + Current.FinancialYear() + "/";
                    object max = Cmd(c, @"SELECT MAX(CAST(RIGHT(AgreementNo,4) AS INT)) FROM Agreements WITH (UPDLOCK, HOLDLOCK)
                                          WHERE EntityCode=@e AND AgreementNo LIKE @p", tx, "@e", a.EntityCode, "@p", prefix + "%").ExecuteScalar();
                    int seq = (max == DBNull.Value || max == null ? 0 : (int)max) + 1;
                    a.No = prefix + seq.ToString("0000");
                    a.CreatedAt = DateTime.Now;
                    a.Id = (int)Cmd(c, @"INSERT Agreements (EntityCode,AgreementNo,Type,Title,Supplier,VendorId,Value,Currency,Status,CreatedBy,CreatedAt,SignedBy,SignedRole,SignedAt)
                        OUTPUT INSERTED.Id VALUES (@e,@n,@t,@ti,@s,@vi,@v,@cu,@st,@cb,@ca,@sb,@sr,@sa)", tx,
                        "@e", a.EntityCode, "@n", a.No, "@t", N(a.Type), "@ti", a.Title, "@s", N(a.Supplier), "@vi", vendorId, "@v", a.Value,
                        "@cu", N(a.Currency), "@st", a.Status, "@cb", N(a.CreatedBy), "@ca", a.CreatedAt,
                        "@sb", N(a.SignedBy), "@sr", N(a.SignedRole), "@sa", signedAt).ExecuteScalar();
                }
                else
                {
                    // read the stored row (and lock it) so the stored status, not the screen, decides what may change
                    string stored, oType, oTitle, oSupplier, oCurrency; decimal oValue;
                    using (var r = Cmd(c, "SELECT Status,Type,Title,Supplier,Value,Currency FROM Agreements WITH (UPDLOCK, HOLDLOCK) WHERE Id=@id", tx, "@id", a.Id).ExecuteReader())
                    {
                        if (!r.Read()) throw new InvalidOperationException("This agreement no longer exists.");
                        stored = r.GetString(0);
                        oType = r.IsDBNull(1) ? "" : r.GetString(1);
                        oTitle = r.GetString(2);
                        oSupplier = r.IsDBNull(3) ? "" : r.GetString(3);
                        oValue = r.GetDecimal(4);
                        oCurrency = r.IsDBNull(5) ? "" : r.GetString(5);
                    }
                    if (stored == "Cancelled")
                        throw new InvalidOperationException("This agreement is cancelled and can no longer be changed.");

                    object cr = N(a.CancelReason), cb = N(a.CancelledBy), ca = a.CancelledAt.HasValue ? (object)a.CancelledAt.Value : DBNull.Value;
                    if (stored == "Signed")
                    {
                        locked = true;
                        if (a.Status != "Signed" && a.Status != "Cancelled")
                            throw new InvalidOperationException("A signed agreement is locked. It can only be cancelled.");
                        Cmd(c, "UPDATE Agreements SET Status=@st,CancelReason=@cr,CancelledBy=@cb,CancelledAt=@ca WHERE Id=@id", tx,
                            "@id", a.Id, "@st", a.Status, "@cr", cr, "@cb", cb, "@ca", ca).ExecuteNonQuery();
                    }
                    else
                    {
                        Diff(changes, "Type", oType, a.Type);
                        Diff(changes, "Title", oTitle, a.Title);
                        Diff(changes, "VendorId", oSupplier, a.Supplier);
                        Diff(changes, "Value", Num(oValue), Num(a.Value));
                        Diff(changes, "Currency", oCurrency, a.Currency);
                        Cmd(c, @"UPDATE Agreements SET Type=@t,Title=@ti,Supplier=@s,VendorId=@vi,Value=@v,Currency=@cu,Status=@st,
                                 SignedBy=@sb,SignedRole=@sr,SignedAt=@sa,CancelReason=@cr,CancelledBy=@cb,CancelledAt=@ca WHERE Id=@id", tx,
                            "@id", a.Id, "@t", N(a.Type), "@ti", a.Title, "@s", N(a.Supplier), "@vi", vendorId, "@v", a.Value, "@cu", N(a.Currency),
                            "@st", a.Status, "@sb", N(a.SignedBy), "@sr", N(a.SignedRole), "@sa", signedAt, "@cr", cr, "@cb", cb, "@ca", ca).ExecuteNonQuery();
                    }
                }

                if (!locked)
                {
                    SyncFields(c, tx, a, changes);
                    SyncApprovals(c, tx, a);
                }
                SyncDocuments(c, tx, a, changes);

                if (isNew)
                    Audit("Agreement", a.No, "Created", "Status=" + a.Status + "; Value=" + Num(a.Value) + " " + a.Currency + "; Supplier=" + a.Supplier, a.EntityCode, c, tx);
                else if (changes.Count > 0)
                    Audit("Agreement", a.No, "Edited", string.Join("; ", changes), a.EntityCode, c, tx);   // status moves are logged by the workflow actions
                tx.Commit();
            }
        }

        // The form fields beyond the core columns (AgreementFields): insert new, update changed, delete cleared
        private static void SyncFields(SqlConnection c, SqlTransaction tx, Agreement a, List<string> changes)
        {
            var stored = new Dictionary<string, string>();
            using (var r = Cmd(c, "SELECT Prop, Val FROM AgreementFields WHERE AgreementId=@id", tx, "@id", a.Id).ExecuteReader())
                while (r.Read()) stored[r.GetString(0)] = r.IsDBNull(1) ? "" : r.GetString(1);

            foreach (var kv in a.Extra)
            {
                string old;
                if (!stored.TryGetValue(kv.Key, out old))
                {
                    Cmd(c, "INSERT AgreementFields (AgreementId,Prop,Val) VALUES (@id,@p,@v)", tx, "@id", a.Id, "@p", kv.Key, "@v", N(kv.Value)).ExecuteNonQuery();
                    Diff(changes, kv.Key, "", kv.Value);
                }
                else
                {
                    if (!string.Equals(old, kv.Value ?? "", StringComparison.Ordinal))
                    {
                        Cmd(c, "UPDATE AgreementFields SET Val=@v WHERE AgreementId=@id AND Prop=@p", tx, "@id", a.Id, "@p", kv.Key, "@v", N(kv.Value)).ExecuteNonQuery();
                        Diff(changes, kv.Key, old, kv.Value);
                    }
                    stored.Remove(kv.Key);
                }
            }
            foreach (var gone in stored)   // in the database but no longer on the agreement: the field was cleared
            {
                if (a.Extra.ContainsKey(gone.Key)) continue;
                Cmd(c, "DELETE FROM AgreementFields WHERE AgreementId=@id AND Prop=@p", tx, "@id", a.Id, "@p", gone.Key).ExecuteNonQuery();
                Diff(changes, gone.Key, gone.Value, "");
            }
        }

        // Approval steps (key: agreement + level): insert new levels, update steps that changed, delete levels no longer needed
        private static void SyncApprovals(SqlConnection c, SqlTransaction tx, Agreement a)
        {
            var stored = new Dictionary<int, ApprovalStep>();
            using (var r = Cmd(c, "SELECT Level, Role, Label, Status, ApprovedBy, At, Remarks FROM ApprovalSteps WHERE AgreementId=@id", tx, "@id", a.Id).ExecuteReader())
                while (r.Read())
                    stored[r.GetInt32(0)] = new ApprovalStep
                    {
                        Level = r.GetInt32(0), Role = r.IsDBNull(1) ? null : r.GetString(1), Label = r.IsDBNull(2) ? null : r.GetString(2),
                        Status = r.GetString(3), ApprovedBy = r.IsDBNull(4) ? null : r.GetString(4),
                        At = r.IsDBNull(5) ? (DateTime?)null : r.GetDateTime(5), Remarks = r.IsDBNull(6) ? null : r.GetString(6)
                    };

            foreach (var s in a.Approvals)
            {
                object[] kv = { "@id", a.Id, "@l", s.Level, "@r", N(s.Role), "@lb", N(s.Label), "@st", s.Status, "@ab", N(s.ApprovedBy),
                                "@at", s.At.HasValue ? (object)s.At.Value : DBNull.Value, "@rm", N(s.Remarks) };
                ApprovalStep old;
                if (!stored.TryGetValue(s.Level, out old))
                    Cmd(c, "INSERT ApprovalSteps (AgreementId,Level,Role,Label,Status,ApprovedBy,At,Remarks) VALUES (@id,@l,@r,@lb,@st,@ab,@at,@rm)", tx, kv).ExecuteNonQuery();
                else
                {
                    bool same = (old.Role ?? "") == (s.Role ?? "") && (old.Label ?? "") == (s.Label ?? "") && old.Status == s.Status
                             && (old.ApprovedBy ?? "") == (s.ApprovedBy ?? "") && old.At == s.At && (old.Remarks ?? "") == (s.Remarks ?? "");
                    if (!same)
                        Cmd(c, "UPDATE ApprovalSteps SET Role=@r,Label=@lb,Status=@st,ApprovedBy=@ab,At=@at,Remarks=@rm WHERE AgreementId=@id AND Level=@l", tx, kv).ExecuteNonQuery();
                    stored.Remove(s.Level);
                }
            }
            foreach (var level in stored.Keys)
                Cmd(c, "DELETE FROM ApprovalSteps WHERE AgreementId=@id AND Level=@l", tx, "@id", a.Id, "@l", level).ExecuteNonQuery();
        }

        // Linked documents (key: DocLinks.Id). New ones (Id 0) are inserted and get their id back.
        private static void SyncDocuments(SqlConnection c, SqlTransaction tx, Agreement a, List<string> changes)
        {
            var stored = new Dictionary<int, DocLink>();
            using (var r = Cmd(c, "SELECT Id, Name, Url FROM DocLinks WHERE AgreementId=@id", tx, "@id", a.Id).ExecuteReader())
                while (r.Read())
                    stored[r.GetInt32(0)] = new DocLink { Id = r.GetInt32(0), Name = r.IsDBNull(1) ? "" : r.GetString(1), Url = r.IsDBNull(2) ? "" : r.GetString(2) };

            foreach (var d in a.Documents)
            {
                DocLink old;
                if (d.Id == 0 || !stored.TryGetValue(d.Id, out old))
                {
                    d.Id = (int)Cmd(c, "INSERT DocLinks (AgreementId,Name,Url,AddedBy) OUTPUT INSERTED.Id VALUES (@id,@n,@u,@ab)", tx,
                        "@id", a.Id, "@n", N(d.Name), "@u", N(d.Url), "@ab", N(d.AddedBy)).ExecuteScalar();
                    changes.Add("Document added: " + Show(d.Name));
                }
                else
                {
                    if ((old.Name ?? "") != (d.Name ?? "") || (old.Url ?? "") != (d.Url ?? ""))
                    {
                        Cmd(c, "UPDATE DocLinks SET Name=@n, Url=@u WHERE Id=@i", tx, "@n", N(d.Name), "@u", N(d.Url), "@i", d.Id).ExecuteNonQuery();
                        changes.Add("Document changed: " + Show(old.Name) + (old.Name != d.Name ? " -> " + Show(d.Name) : ""));
                    }
                    stored.Remove(d.Id);
                }
            }
            foreach (var gone in stored.Values)
            {
                Cmd(c, "DELETE FROM DocLinks WHERE Id=@i", tx, "@i", gone.Id).ExecuteNonQuery();
                changes.Add("Document removed: " + Show(gone.Name));
            }
        }
    }
}
