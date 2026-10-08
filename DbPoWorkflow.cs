using System;
using System.Data.SqlClient;

namespace ProcurePay
{
    // Purchase orders: moving a PO from one status to the next (submit, approve, reject, cancel).
    // One transaction per move. The PO row is locked and its stored status must still be what the screen thought it was,
    // so two people can never approve the same level twice, or approve a PO that was cancelled a moment ago.
    public static partial class Db
    {
        // po already holds the NEW state (status, approval steps, cancel details); fromStatus is the status it had before.
        // checkAgreement: the agreement must still be Signed, and (when sending a PO for approval) its balance must still cover the PO.
        // expectedPendingLevel: for approve / reject, the level the person is deciding; it must still be pending.
        public static void PoTransition(PurchaseOrder po, string fromStatus, string action, string details, bool checkAgreement, int? expectedPendingLevel = null)
        {
            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                string stored, storedCurrency; int agreementId; decimal subTotal; DateTime storedPoDate;
                using (var r = Cmd(c, "SELECT Status, AgreementId, SubTotal, Currency, PoDate FROM PurchaseOrders WITH (UPDLOCK, HOLDLOCK) WHERE Id=@i", tx, "@i", po.Id).ExecuteReader())
                {
                    if (!r.Read()) throw PoRule("This purchase order no longer exists.");
                    stored = r.GetString(0); agreementId = r.GetInt32(1); subTotal = r.GetDecimal(2); storedCurrency = r.GetString(3); storedPoDate = r.GetDateTime(4);
                }
                if (stored != fromStatus)
                    throw PoRule("This purchase order was changed by someone else and is now " + stored + ". The screen has been refreshed.");

                if (expectedPendingLevel.HasValue)
                {
                    int open = (int)Cmd(c, "SELECT COUNT(*) FROM PoApprovalSteps WHERE PoId=@i AND Level=@l AND Status='Pending'", tx,
                        "@i", po.Id, "@l", expectedPendingLevel.Value).ExecuteScalar();
                    if (open == 0) throw PoRule("This approval level has already been decided by someone else.");
                }

                if (checkAgreement)
                {
                    string agStatus = Cmd(c, "SELECT Status FROM Agreements WITH (UPDLOCK, HOLDLOCK) WHERE Id=@a", tx, "@a", agreementId).ExecuteScalar() as string;
                    if (agStatus != "Signed")
                        throw PoRule("The agreement " + po.AgreementNo + " is now " + (agStatus ?? "missing") + ", so this purchase order cannot go forward.");
                    if (fromStatus == "Draft" || fromStatus == "Rejected")
                    {
                        decimal balance = AgreementBalance(c, tx, agreementId, po.Id);
                        if (subTotal > balance)
                            throw PoRule("The PO sub-total " + subTotal.ToString("N2") + " " + po.Currency + " is more than the agreement balance of " + balance.ToString("N2") + " " + po.Currency + ". Edit the PO first.");
                    }
                }

                // Budget (step 26): a PO starts to count as committed when it is sent for approval, so the budget is enforced here.
                BudgetCheckResult budget = null;
                if (po.Status == "Pending Approval" && (fromStatus == "Draft" || fromStatus == "Rejected"))
                {
                    budget = BudgetCheck(c, tx, po.EntityCode, agreementId, po.Id, subTotal, storedCurrency, storedPoDate, true);
                    if (budget.Blocks)
                        throw PoRule(budget.Message + " Ask Accounts to add or raise the budget, or correct the agreement's \"GL / budget line\", then send it again.");
                }

                if (po.Status == "Cancelled")
                {
                    int invoiced = (int)Cmd(c, @"SELECT (SELECT COUNT(*) FROM PoMilestones WHERE PoId=@i AND InvoicedAmount>0)
                                                    + (SELECT COUNT(*) FROM Invoices WHERE PoId=@i AND Status<>'Cancelled')", tx, "@i", po.Id).ExecuteScalar();
                    if (invoiced > 0) throw PoRule("This purchase order already has invoices (including drafts) against it and cannot be cancelled. Cancel those invoices first.");
                }

                Cmd(c, "UPDATE PurchaseOrders SET Status=@s, CancelReason=@cr, CancelledBy=@cb, CancelledAt=@ca WHERE Id=@i", tx,
                    "@s", po.Status, "@cr", N(po.CancelReason), "@cb", N(po.CancelledBy),
                    "@ca", po.CancelledAt.HasValue ? (object)po.CancelledAt.Value : DBNull.Value, "@i", po.Id).ExecuteNonQuery();

                // the approval steps are replaced as a set (a PO has two or three at most)
                Cmd(c, "DELETE FROM PoApprovalSteps WHERE PoId=@i", tx, "@i", po.Id).ExecuteNonQuery();
                foreach (var s in po.Approvals)
                    Cmd(c, "INSERT PoApprovalSteps (PoId,Level,Role,Label,Status,ApprovedBy,At,Remarks) VALUES (@i,@l,@r,@lb,@st,@ab,@at,@rm)", tx,
                        "@i", po.Id, "@l", s.Level, "@r", N(s.Role), "@lb", N(s.Label), "@st", s.Status, "@ab", N(s.ApprovedBy),
                        "@at", s.At.HasValue ? (object)s.At.Value : DBNull.Value, "@rm", N(s.Remarks)).ExecuteNonQuery();

                string auditDetails = details;
                if (budget != null && budget.Outcome != "Ok" && budget.Outcome != "Off") auditDetails = (details ?? "") + "; Budget: " + budget.Message;
                Audit("PO", po.No, action, auditDetails, po.EntityCode, c, tx);
                tx.Commit();

                // sent for approval outside its budget (Warn mode): tell Accounts, who own the budgets
                if (budget != null && budget.IsProblem)
                    NotifyRole(po.EntityCode, "Accounts", "PO sent outside its budget", po.No + " (" + po.Title + ") was sent for approval. " + budget.Message, "PO", po.No, AppSession.UserName);
            }
        }
    }
}
