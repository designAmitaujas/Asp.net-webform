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
        public int Id;   // 0 until saved
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
        public string Supplier;        // vendor name (kept in step with the Vendors table)
        public int? VendorId;          // the vendor this agreement is with
        public decimal Value;
        public string Currency;
        public string Status = "Draft";   // Draft, Pending Approval, Approved, Signed, Rejected
        public string CreatedBy;
        public DateTime CreatedAt;
        public string SignedBy;
        public string SignedRole;
        public DateTime? SignedAt;
        public string CancelReason, CancelledBy;
        public DateTime? CancelledAt;
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
                case "VendorId": return VendorId.HasValue ? VendorId.Value.ToString() : (string.IsNullOrWhiteSpace(Supplier) ? "" : "L");   // "L" = old text-only supplier
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

    // Who can do what with an agreement in its current status (rights come from Permissions.cs)
    public static class AgreementRules
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

        // Cancel is allowed until the agreement is cancelled. A signed agreement can only be cancelled by an Administrator.
        public static bool CanCancel(Agreement a, string role)
        {
            if (a.Status == "Cancelled") return false;
            if (!Perm.Can(role, "agreement.cancel")) return false;
            if (a.Status == "Signed") return string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        // Signed and cancelled agreements are locked: no field edits (documents can still be attached to a signed one)
        public static bool IsLocked(Agreement a)
        {
            return a.Status == "Signed" || a.Status == "Cancelled";
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
                case "Cancelled": return "Cancelled" + (string.IsNullOrEmpty(a.CancelledBy) ? "" : " by " + a.CancelledBy);
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
    public static class AgreementWorkflow
    {
        // ---- notifications (they never block the action: Db.Notify swallows its own errors)
        private static string Ref(Agreement a) { return a.No + " (" + a.Title + ")"; }

        private static void ToCreator(Agreement a, string actor, string title, string message)
        {
            if (!string.Equals(a.CreatedBy, actor, StringComparison.OrdinalIgnoreCase))
                Db.Notify(a.CreatedBy, a.EntityCode, title, message, "Agreement", a.No);
        }

        // Tells the role that holds the next approval level that the agreement is waiting for them
        private static void ToNextApprover(Agreement a, string actor)
        {
            var p = a.Pending;
            if (p == null) return;
            Db.NotifyRole(a.EntityCode, p.Role, "Approval needed",
                Ref(a) + " is waiting for your approval (level " + p.Level + ": " + p.Label + ").", "Agreement", a.No, actor);
        }

        private static void ToSignatories(Agreement a, string actor)
        {
            Db.NotifyRole(a.EntityCode, "Authorised Signatory", "Ready to sign", Ref(a) + " is fully approved and waiting for signature.", "Agreement", a.No, actor);
        }

        public static void Submit(Agreement a)
        {
            a.Approvals.Clear();
            var levels = Db.MatrixFor(a.Value);   // approval matrix: a level applies when value >= MinValue
            if (levels.Count == 0) { a.Status = "Approved"; AgreementStore.Save(a); Db.Audit("Agreement", a.No, "Submitted", "Auto-approved (no approval level)", a.EntityCode); ToSignatories(a, AppSession.UserName); return; }
            foreach (var l in levels)
                a.Approvals.Add(new ApprovalStep { Level = l.Level, Role = l.Role, Label = l.Label });
            a.Status = "Pending Approval";
            AgreementStore.Save(a);
            Db.Audit("Agreement", a.No, "Submitted", "Levels=" + a.Approvals.Count, a.EntityCode);
            ToNextApprover(a, AppSession.UserName);
        }

        public static void Approve(Agreement a, string user, string remarks)
        {
            var p = a.Pending;
            p.Status = "Approved"; p.ApprovedBy = user; p.At = DateTime.Now; p.Remarks = remarks;
            if (a.Pending == null) a.Status = "Approved";
            AgreementStore.Save(a);
            Db.Audit("Agreement", a.No, "Approved", "Level " + p.Level + " (" + p.Label + "); " + remarks, a.EntityCode);
            if (a.Status == "Approved")
            {
                ToCreator(a, user, "Agreement fully approved", Ref(a) + " has passed every approval level.");
                ToSignatories(a, user);
            }
            else
            {
                ToCreator(a, user, "Approval level passed", Ref(a) + " was approved at level " + p.Level + " (" + p.Label + ") by " + user + ".");
                ToNextApprover(a, user);
            }
        }

        public static void Reject(Agreement a, string user, string remarks)
        {
            var p = a.Pending;
            p.Status = "Rejected"; p.ApprovedBy = user; p.At = DateTime.Now; p.Remarks = remarks;
            a.Status = "Rejected";
            AgreementStore.Save(a);
            Db.Audit("Agreement", a.No, "Rejected", "Level " + p.Level + " (" + p.Label + "); " + remarks, a.EntityCode);
            ToCreator(a, user, "Agreement rejected", Ref(a) + " was rejected at level " + p.Level + " by " + user + ": " + remarks);
        }

        public static void Sign(Agreement a, string user, string role)
        {
            a.Status = "Signed"; a.SignedBy = user; a.SignedRole = role; a.SignedAt = DateTime.Now;
            AgreementStore.Save(a);
            Db.Audit("Agreement", a.No, "Signed", "Role=" + role, a.EntityCode);
            ToCreator(a, user, "Agreement signed", Ref(a) + " was signed and executed by " + user + ".");
        }

        public static void Cancel(Agreement a, string user, string reason)
        {
            string was = a.Status, pendingRole = a.PendingRole;
            foreach (var step in a.Approvals.Where(x => x.Status == "Pending")) step.Status = "Cancelled";   // no longer waiting on anyone
            a.Status = "Cancelled"; a.CancelReason = reason; a.CancelledBy = user; a.CancelledAt = DateTime.Now;
            AgreementStore.Save(a);
            Db.Audit("Agreement", a.No, "Cancelled", "Was " + was + "; " + reason, a.EntityCode);

            string msg = a.No + " (" + a.Title + ") was cancelled by " + user + ": " + reason;
            if (!string.Equals(a.CreatedBy, user, StringComparison.OrdinalIgnoreCase))
                Db.Notify(a.CreatedBy, a.EntityCode, "Agreement cancelled", msg, "Agreement", a.No);
            if (was == "Pending Approval" && pendingRole != null)
                Db.NotifyRole(a.EntityCode, pendingRole, "Agreement cancelled", msg, "Agreement", a.No, user);
        }

        public static void Reopen(Agreement a)
        {
            a.Approvals.Clear();
            a.Status = "Draft";
            AgreementStore.Save(a);
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

    // Agreements are stored in SQL Server (see Db.cs)
    public static class AgreementStore
    {
        public static List<Agreement> ForEntity(string code) { return Db.LoadAgreements(code); }

        // Stores a new agreement: gives it an id and the next number, e.g. PA/2026-27/0007
        public static Agreement Add(Agreement a) { Db.SaveAgreement(a); return a; }

        // Saves changes to an existing agreement (fields, approvals, documents)
        public static void Save(Agreement a) { Db.SaveAgreement(a); }

        public static int SupplierCount(string code) { return Db.SupplierCount(code); }
    }
}
