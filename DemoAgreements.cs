using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ProcurePay
{
    public class ApprovalStep
    {
        public int Level;
        public string Role;
        public string Label;
        public string Status = "Pending";   // Pending, Approved, Rejected
        public string ApprovedBy;
        public DateTime? At;
        public string Remarks;
    }

    public class DocLink
    {
        public string Name;
        public string Url;
        public string AddedBy;
    }

    public class Agreement
    {
        public int Id;
        public string EntityCode;
        public string No;
        public string Type;
        public string Title;
        public string Supplier;
        public decimal Value;
        public string Currency;
        public string Status = "Draft";   // Draft, Pending Approval, Approved, Signed, Rejected
        public string CreatedBy;
        public DateTime CreatedAt;
        public string SignedBy;
        public string SignedRole;
        public DateTime? SignedAt;
        public List<ApprovalStep> Approvals = new List<ApprovalStep>();
        public List<DocLink> Documents = new List<DocLink>();
        public Dictionary<string, string> Extra = new Dictionary<string, string>();   // all other form fields

        public ApprovalStep Pending
        {
            get { return Approvals.Where(x => x.Status == "Pending").OrderBy(x => x.Level).FirstOrDefault(); }
        }
        public string PendingRole { get { var p = Pending; return p == null ? null : p.Role; } }
        public string PendingLabel { get { var p = Pending; return p == null ? null : p.Label; } }
        public int ApprovedLevels { get { return Approvals.Count(x => x.Status == "Approved"); } }
        public int TotalLevels { get { return Approvals.Count; } }

        // Raw text of any form field, by its property name
        public string Get(string prop)
        {
            string v;
            switch (prop)
            {
                case "Type": return Type;
                case "Title": return Title;
                case "VendorId": return Supplier;
                case "Value": return Value.ToString("0.##", CultureInfo.InvariantCulture);
                case "Currency": return Currency;
            }
            return Extra.TryGetValue(prop, out v) ? v : "";
        }
    }

    public class MatrixLevel
    {
        public int Level;
        public string Role;
        public string Label;
        public decimal MinValue;
    }

    // Approval matrix (table ApprovalMatrix): a level applies when the agreement value >= MinValue
    public static class DemoMatrix
    {
        public static List<MatrixLevel> For(decimal value) { return Db.MatrixFor(value); }
    }

    // Temporary copy of the server rules. Later the SERVER decides and the app just shows buttons.
    public static class DemoRules
    {
        public static bool CanCreate(string role)
        {
            return Perm.Can(role, "agreement.create");
        }

        public static bool CanSubmit(string role)
        {
            return Perm.Can(role, "agreement.submit");
        }

        public static bool CanSubmit(Agreement a, string role)
        {
            return a.Status == "Draft" && CanSubmit(role);
        }

        public static bool CanApprove(Agreement a, string role, string userName)
        {
            if (a.Status != "Pending Approval") return false;
            if (!Perm.Can(role, "agreement.approve")) return false;
            if (role != a.PendingRole && role != "Administrator") return false;
            // the creator can never approve their own agreement
            return !string.Equals(a.CreatedBy, userName, StringComparison.OrdinalIgnoreCase);
        }

        public static bool CanSign(Agreement a, string role)
        {
            return a.Status == "Approved" && Perm.Can(role, "agreement.sign");
        }

        public static bool CanEdit(Agreement a, string role)
        {
            return (a.Status == "Draft" || a.Status == "Rejected") && CanCreate(role);
        }

        public static bool CanReopen(Agreement a, string role)
        {
            return a.Status == "Rejected" && CanCreate(role);
        }

        public static string Stage(Agreement a)
        {
            switch (a.Status)
            {
                case "Signed": return "Executed";
                case "Approved": return "Fully approved — awaiting signature";
                case "Rejected":
                    var r = a.Approvals.FirstOrDefault(x => x.Status == "Rejected");
                    return r != null ? "Rejected at L" + r.Level : "Rejected";
                case "Pending Approval":
                    var p = a.Pending;
                    return "Level " + (a.ApprovedLevels + 1) + " of " + a.TotalLevels + (p != null ? " · " + p.Label : "");
                default: return "Not submitted";
            }
        }
    }

    // The workflow actions (saved to SQL Server)
    public static class DemoWorkflow
    {
        public static void Submit(Agreement a)
        {
            a.Approvals.Clear();
            var levels = DemoMatrix.For(a.Value);
            if (levels.Count == 0) { a.Status = "Approved"; DemoAgreements.Save(a); Db.Audit("Agreement", a.No, "Submitted", "Auto-approved (no approval level)", a.EntityCode); return; }
            foreach (var l in levels)
                a.Approvals.Add(new ApprovalStep { Level = l.Level, Role = l.Role, Label = l.Label });
            a.Status = "Pending Approval";
            DemoAgreements.Save(a);
            Db.Audit("Agreement", a.No, "Submitted", "Levels=" + a.Approvals.Count, a.EntityCode);
        }

        public static void Approve(Agreement a, string user, string remarks)
        {
            var p = a.Pending;
            p.Status = "Approved"; p.ApprovedBy = user; p.At = DateTime.Now; p.Remarks = remarks;
            if (a.Pending == null) a.Status = "Approved";
            DemoAgreements.Save(a);
            Db.Audit("Agreement", a.No, "Approved", "Level " + p.Level + " (" + p.Label + "); " + remarks, a.EntityCode);
        }

        public static void Reject(Agreement a, string user, string remarks)
        {
            var p = a.Pending;
            p.Status = "Rejected"; p.ApprovedBy = user; p.At = DateTime.Now; p.Remarks = remarks;
            a.Status = "Rejected";
            DemoAgreements.Save(a);
            Db.Audit("Agreement", a.No, "Rejected", "Level " + p.Level + " (" + p.Label + "); " + remarks, a.EntityCode);
        }

        public static void Sign(Agreement a, string user, string role)
        {
            a.Status = "Signed"; a.SignedBy = user; a.SignedRole = role; a.SignedAt = DateTime.Now;
            DemoAgreements.Save(a);
            Db.Audit("Agreement", a.No, "Signed", "Role=" + role, a.EntityCode);
        }

        public static void Reopen(Agreement a)
        {
            a.Approvals.Clear();
            a.Status = "Draft";
            DemoAgreements.Save(a);
            Db.Audit("Agreement", a.No, "Reopened", null, a.EntityCode);
        }
    }

    public static class Fmt
    {
        public static string Money(decimal v, string currency)
        {
            string sym;
            switch (currency)
            {
                case "INR": sym = "\u20B9"; break;
                case "USD": sym = "$"; break;
                case "EUR": sym = "\u20AC"; break;
                case "GBP": sym = "\u00A3"; break;
                default: sym = currency + " "; break;
            }
            var culture = currency == "INR" ? new CultureInfo("en-IN") : CultureInfo.InvariantCulture;
            return sym + v.ToString("N2", culture);
        }
    }

    // Agreements are stored in SQL Server (see Db.cs). Class name kept so the screens did not need to change.
    public static class DemoAgreements
    {
        public static List<Agreement> ForEntity(string code) { return Db.LoadAgreements(code); }

        // Stores a new agreement: gives it an id and the next number, e.g. PA/2026-27/0007
        public static Agreement Add(Agreement a) { Db.SaveAgreement(a); return a; }

        // Saves changes to an existing agreement (fields, approvals, documents)
        public static void Save(Agreement a) { Db.SaveAgreement(a); }

        public static int SupplierCount(string code) { return Db.SupplierCount(code); }
    }
}
