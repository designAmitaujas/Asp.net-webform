using System;
using System.Linq;

namespace ProcurePay
{
    // Who can do what with a purchase order in its current status (rights come from Permissions.cs)
    public static class PoRules
    {
        // A draft, or a rejected PO that has been corrected, can be sent for approval
        public static bool CanSubmit(PurchaseOrder po, string role)
        {
            return (po.Status == "Draft" || po.Status == "Rejected") && Perm.Can(role, "po.create");
        }

        public static bool CanApprove(PurchaseOrder po, string role, string userName)
        {
            if (po.Status != "Pending Approval") return false;
            var p = po.Pending;
            if (p == null) return false;
            if (!Perm.Can(role, "po.approve")) return false;
            if (role != p.Role && role != "Administrator") return false;
            // whoever raised the PO can never approve it
            return !string.Equals(po.CreatedBy, userName, StringComparison.OrdinalIgnoreCase);
        }

        // Cancel until it is cancelled or closed, unless an invoice has been booked against it.
        // An approved PO can only be cancelled by an Administrator.
        public static bool CanCancel(PurchaseOrder po, string role)
        {
            if (po.Status == "Cancelled" || po.Status == "Closed") return false;
            if (po.Milestones.Any(m => m.InvoicedAmount > 0)) return false;
            if (po.Status == "Approved") return string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase);
            return Perm.Can(role, "po.create");
        }
    }

    // The workflow actions. Each one saves in a single transaction (Db.PoTransition), writes the audit row,
    // and then tells the right people. Notifications never block an action: Db.Notify swallows its own errors.
    public static class PoWorkflow
    {
        private static string Ref(PurchaseOrder po) { return po.No + " (" + po.Title + ")"; }

        private static void ToCreator(PurchaseOrder po, string actor, string title, string message)
        {
            if (!string.Equals(po.CreatedBy, actor, StringComparison.OrdinalIgnoreCase))
                Db.Notify(po.CreatedBy, po.EntityCode, title, message, "PO", po.No);
        }

        private static void ToNextApprover(PurchaseOrder po, string actor)
        {
            var p = po.Pending;
            if (p == null) return;
            Db.NotifyRole(po.EntityCode, p.Role, "PO approval needed",
                Ref(po) + " for " + Fmt.Money(po.Total, po.Currency) + " is waiting for your approval (level " + p.Level + ": " + p.Label + ").",
                "PO", po.No, actor);
        }

        // Approval levels for a PO: the levels of the approval matrix that apply to its total AND whose role may approve POs
        // (Approver, Administrator). The finance review level is for agreements. Levels are renumbered 1, 2, ...
        // If nothing qualifies, a single Approver level is used so a PO can never skip approval.
        private static void BuildSteps(PurchaseOrder po)
        {
            var levels = Db.MatrixFor(po.Total).Where(l => Perm.Can(l.Role, "po.approve")).ToList();
            po.Approvals.Clear();
            int n = 0;
            foreach (var l in levels) po.Approvals.Add(new ApprovalStep { Level = ++n, Role = l.Role, Label = l.Label });
            if (po.Approvals.Count == 0) po.Approvals.Add(new ApprovalStep { Level = 1, Role = "Approver", Label = "PO approval" });
        }

        public static void Submit(PurchaseOrder po)
        {
            string actor = AppSession.UserName, from = po.Status;
            if (from != "Draft" && from != "Rejected") throw new InvalidOperationException("Only a draft or rejected purchase order can be sent for approval.");
            BuildSteps(po);
            po.Status = "Pending Approval";
            Db.PoTransition(po, from, "Submitted", "Levels=" + po.Approvals.Count + (from == "Rejected" ? "; sent again after rejection" : ""), true);
            ToNextApprover(po, actor);
        }

        public static void Approve(PurchaseOrder po, string user, string remarks)
        {
            remarks = (remarks ?? "").Trim();
            var p = po.Pending;
            if (p == null) throw new InvalidOperationException("This purchase order is not waiting for approval.");
            p.Status = "Approved"; p.ApprovedBy = user; p.At = DateTime.Now; p.Remarks = remarks;
            if (po.Pending == null) po.Status = "Approved";
            bool fully = po.Status == "Approved";
            Db.PoTransition(po, "Pending Approval", "Approved", "Level " + p.Level + " (" + p.Label + ")" + (remarks.Length > 0 ? "; " + remarks : ""), fully, p.Level);

            if (fully) ToCreator(po, user, "PO approved", Ref(po) + " has passed every approval level.");
            else
            {
                ToCreator(po, user, "PO approval level passed", Ref(po) + " was approved at level " + p.Level + " (" + p.Label + ") by " + user + ".");
                ToNextApprover(po, user);
            }
        }

        public static void Reject(PurchaseOrder po, string user, string remarks)
        {
            if (string.IsNullOrWhiteSpace(remarks)) throw new InvalidOperationException("Remarks are required to reject a purchase order.");
            var p = po.Pending;
            if (p == null) throw new InvalidOperationException("This purchase order is not waiting for approval.");
            p.Status = "Rejected"; p.ApprovedBy = user; p.At = DateTime.Now; p.Remarks = remarks;
            po.Status = "Rejected";
            Db.PoTransition(po, "Pending Approval", "Rejected", "Level " + p.Level + " (" + p.Label + "); " + remarks, false, p.Level);
            ToCreator(po, user, "PO rejected", Ref(po) + " was rejected at level " + p.Level + " by " + user + ": " + remarks);
        }

        public static void Cancel(PurchaseOrder po, string user, string reason)
        {
            string was = po.Status, pendingRole = po.Pending != null ? po.Pending.Role : null;
            foreach (var step in po.Approvals.Where(x => x.Status == "Pending")) step.Status = "Cancelled";   // nobody is waiting any more
            po.Status = "Cancelled"; po.CancelReason = reason; po.CancelledBy = user; po.CancelledAt = DateTime.Now;
            Db.PoTransition(po, was, "Cancelled", "Was " + was + "; " + reason, false);

            string msg = Ref(po) + " was cancelled by " + user + ": " + reason;
            ToCreator(po, user, "PO cancelled", msg);
            if (was == "Pending Approval" && pendingRole != null)
                Db.NotifyRole(po.EntityCode, pendingRole, "PO cancelled", msg, "PO", po.No, user);
        }
    }
}
