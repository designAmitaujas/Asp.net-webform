using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace ProcurePay
{
    // One budget line for one financial year. Rows with Id == 0 are not stored budgets: they are "budget lines
    // that purchase orders are already using, but nobody has budgeted", shown so they cannot go unnoticed.
    public class BudgetRow
    {
        public int Id;
        public string EntityCode;
        public string FY;
        public string Line;
        public string Description;
        public string Currency;
        public decimal Amount;
        public bool IsActive = true;
        public byte[] RowVer;      // lets the save notice that someone else changed the row first

        public decimal Committed;  // POs waiting for approval, approved or closed (before tax), in this FY
        public int PoCount;

        public bool HasBudget { get { return Id > 0; } }
        public decimal Available { get { return Amount - Committed; } }
        public decimal UsedPercent { get { return Amount > 0 ? Math.Round(Committed * 100m / Amount, 0) : 0m; } }
    }

    // What the budget check found for one purchase order
    public class BudgetCheckResult
    {
        public string Mode;       // Warn, Block or Off (the entity's setting "BudgetControl")
        public string Outcome;    // Ok, Over, NoLine, NoBudget, Inactive, OtherCurrency, Off
        public string Line, FY;
        public string Message;    // plain sentence for the screen, the audit log and notifications

        // The PO cannot be judged fairly against its budget
        public bool IsProblem { get { return Outcome == "Over" || Outcome == "NoLine" || Outcome == "NoBudget" || Outcome == "Inactive"; } }
        // In Block mode a problem stops the PO from being sent for approval
        public bool Blocks { get { return Mode == "Block" && IsProblem; } }
    }

    // Budgets: reading with how much is already committed, and saving with the rules.
    //
    //  - the budget is in the entity's currency; the financial year runs April to March (2026-27)
    //  - COMMITTED = sub-total (before tax) of purchase orders that are Pending Approval, Approved or Closed,
    //    dated inside the financial year, in the entity's currency, whose agreement carries the same "GL / budget line"
    //    text (capitals and spaces at the ends ignored). Drafts, Rejected and Cancelled POs commit nothing.
    //  - POs in a different currency are not counted yet (exchange rates come with the invoice steps); the screen says how many.
    //  - a budget line is never deleted, only made inactive
    //
    // Budget check on purchase orders (step 26). A PO starts to count as committed when it is sent for approval, so that is
    // where the budget is enforced (Db.PoTransition calls BudgetCheck with the budget row locked). Setting "BudgetControl", per entity:
    //    Warn  (default)  the PO may go ahead; it is flagged in the audit log and Accounts are notified
    //    Block            a PO that is over budget, has no budget line, or whose line has no active budget cannot be sent for approval
    //    Off              no check
    //  A PO in another currency than the entity's cannot be compared yet; it is never blocked, only reported.
    public static partial class Db
    {
        public const string BudgetCommittingStatuses = "'Pending Approval','Approved','Closed'";

        public static string FyLabel(int startYear) { return startYear + "-" + ((startYear + 1) % 100).ToString("00", CultureInfo.InvariantCulture); }

        // "2026-27" -> 1 Apr 2026 up to (not including) 1 Apr 2027
        public static void FyRange(string fy, out DateTime from, out DateTime to)
        {
            int y;
            if (fy == null || fy.Length != 7 || !int.TryParse(fy.Substring(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out y))
                throw new InvalidOperationException("Financial year must look like 2026-27.");
            from = new DateTime(y, 4, 1);
            to = new DateTime(y + 1, 4, 1);
        }

        // Last year, this year, next year, plus any year that already has budgets. Newest first.
        public static List<string> BudgetYears(string entity)
        {
            int now = int.Parse(Current.FinancialYear().Substring(0, 4), CultureInfo.InvariantCulture);
            var set = new HashSet<string> { FyLabel(now - 1), FyLabel(now), FyLabel(now + 1) };
            using (var c = Open())
            using (var r = Cmd(c, "SELECT DISTINCT FY FROM Budgets WHERE EntityCode=@e", null, "@e", entity).ExecuteReader())
                while (r.Read()) set.Add(r.GetString(0));
            return set.OrderByDescending(x => x, StringComparer.Ordinal).ToList();
        }

        // Every budget line of the year (with what is committed), then lines that POs use but nobody budgeted.
        // otherCurrencyPos = committing POs in another currency, which are not counted.
        public static List<BudgetRow> BudgetList(string entity, string fy, out int otherCurrencyPos)
        {
            DateTime from, to;
            FyRange(fy, out from, out to);
            var rows = new List<BudgetRow>();
            var byLine = new Dictionary<string, BudgetRow>(StringComparer.OrdinalIgnoreCase);
            var unbudgeted = new List<BudgetRow>();
            string ccy;

            using (var c = Open())
            {
                ccy = Cmd(c, "SELECT Currency FROM Entities WHERE Code=@e", null, "@e", entity).ExecuteScalar() as string ?? "";

                using (var r = Cmd(c, @"SELECT Id, FY, BudgetLine, Description, Amount, Currency, IsActive, RowVer
                                        FROM Budgets WHERE EntityCode=@e AND FY=@fy ORDER BY BudgetLine", null, "@e", entity, "@fy", fy).ExecuteReader())
                    while (r.Read())
                    {
                        var b = new BudgetRow
                        {
                            Id = r.GetInt32(0), EntityCode = entity, FY = r.GetString(1), Line = r.GetString(2),
                            Description = r.IsDBNull(3) ? "" : r.GetString(3), Amount = r.GetDecimal(4), Currency = r.GetString(5),
                            IsActive = r.GetBoolean(6), RowVer = (byte[])r[7]
                        };
                        rows.Add(b);
                        byLine[b.Line.Trim()] = b;
                    }

                using (var r = Cmd(c, @"SELECT LTRIM(RTRIM(ISNULL(af.Val,''))), SUM(p.SubTotal), COUNT(*)
                                        FROM PurchaseOrders p
                                        LEFT JOIN AgreementFields af ON af.AgreementId=p.AgreementId AND af.Prop='GlAccount'
                                        WHERE p.EntityCode=@e AND p.Currency=@ccy AND p.PoDate>=@f AND p.PoDate<@t
                                          AND p.Status IN (" + BudgetCommittingStatuses + @")
                                        GROUP BY LTRIM(RTRIM(ISNULL(af.Val,'')))", null,
                        "@e", entity, "@ccy", ccy, "@f", from, "@t", to).ExecuteReader())
                    while (r.Read())
                    {
                        string line = r.GetString(0);
                        decimal sum = r.GetDecimal(1);
                        int n = r.GetInt32(2);
                        BudgetRow b;
                        if (line.Length > 0 && byLine.TryGetValue(line, out b)) { b.Committed += sum; b.PoCount += n; }
                        else unbudgeted.Add(new BudgetRow { Id = 0, EntityCode = entity, FY = fy, Line = line, Currency = ccy, Committed = sum, PoCount = n });
                    }

                otherCurrencyPos = (int)Cmd(c, @"SELECT COUNT(*) FROM PurchaseOrders
                                                 WHERE EntityCode=@e AND Currency<>@ccy AND PoDate>=@f AND PoDate<@t AND Status IN (" + BudgetCommittingStatuses + ")", null,
                    "@e", entity, "@ccy", ccy, "@f", from, "@t", to).ExecuteScalar();
            }

            rows.AddRange(unbudgeted.OrderBy(x => x.Line, StringComparer.OrdinalIgnoreCase));
            return rows;
        }

        // How many committing POs in this FY already use the line (the name of such a line must not change)
        private static int PosOnBudgetLine(SqlConnection c, SqlTransaction tx, string entity, string fy, string line)
        {
            DateTime from, to;
            FyRange(fy, out from, out to);
            return (int)Cmd(c, @"SELECT COUNT(*) FROM PurchaseOrders p
                                 JOIN AgreementFields af ON af.AgreementId=p.AgreementId AND af.Prop='GlAccount'
                                 WHERE p.EntityCode=@e AND p.PoDate>=@f AND p.PoDate<@t AND p.Status IN (" + BudgetCommittingStatuses + @")
                                   AND LTRIM(RTRIM(af.Val))=@l", tx, "@e", entity, "@f", from, "@t", to, "@l", line.Trim()).ExecuteScalar();
        }

        public static string BudgetControlMode(string entity)
        {
            string m = GetSetting("BudgetControl", "Warn", entity);
            return m == "Block" || m == "Off" ? m : "Warn";
        }

        private static string SettingIn(SqlConnection c, SqlTransaction tx, string key, string def, string entity)
        {
            object v = Cmd(c, "SELECT SettingValue FROM Settings WHERE EntityCode=@e AND SettingKey=@k", tx, "@e", entity, "@k", key).ExecuteScalar();
            if (v == null || v == DBNull.Value) v = Cmd(c, "SELECT SettingValue FROM Settings WHERE EntityCode='' AND SettingKey=@k", tx, "@k", key).ExecuteScalar();
            return v == null || v == DBNull.Value ? def : (string)v;
        }

        // For a PO as it stands now (no locks): used by the screens to show the result before anything is submitted
        public static BudgetCheckResult BudgetCheckPo(PurchaseOrder po)
        {
            using (var c = Open())
                return BudgetCheck(c, null, po.EntityCode, po.AgreementId, po.Id, po.SubTotal, po.Currency, po.PoDate, false);
        }

        // Compares a PO's sub-total (before tax) with what is left on the budget line of its agreement.
        // lockRows = true (inside the submit transaction) locks the budget row, so two POs sent at the same moment
        // cannot both take the last of the money.
        private static BudgetCheckResult BudgetCheck(SqlConnection c, SqlTransaction tx, string entity, int agreementId, int poId,
                                                     decimal subTotal, string poCurrency, DateTime poDate, bool lockRows)
        {
            var res = new BudgetCheckResult();
            string mode = SettingIn(c, tx, "BudgetControl", "Warn", entity);
            res.Mode = mode == "Block" || mode == "Off" ? mode : "Warn";
            if (res.Mode == "Off") { res.Outcome = "Off"; res.Message = "Budget checking is switched off for this entity."; return res; }

            string ccy = Cmd(c, "SELECT Currency FROM Entities WHERE Code=@e", tx, "@e", entity).ExecuteScalar() as string ?? "";
            res.FY = FyLabel(poDate.Month >= 4 ? poDate.Year : poDate.Year - 1);
            if (!string.Equals(poCurrency, ccy, StringComparison.OrdinalIgnoreCase))
            {
                res.Outcome = "OtherCurrency";
                res.Message = "This PO is in " + poCurrency + " but budgets are in " + ccy + ", so it was not checked against a budget.";
                return res;
            }

            string line = (Cmd(c, "SELECT LTRIM(RTRIM(Val)) FROM AgreementFields WHERE AgreementId=@a AND Prop='GlAccount'", tx, "@a", agreementId).ExecuteScalar() as string ?? "").Trim();
            res.Line = line;
            if (line.Length == 0)
            {
                res.Outcome = "NoLine";
                res.Message = "The agreement has no \"GL / budget line\", so this PO cannot be matched to a budget.";
                return res;
            }

            decimal amount = 0; bool active = false, found = false;
            using (var r = Cmd(c, "SELECT Amount, IsActive FROM Budgets " + (lockRows ? "WITH (UPDLOCK, HOLDLOCK) " : "") +
                                  "WHERE EntityCode=@e AND FY=@fy AND BudgetLine=@l", tx, "@e", entity, "@fy", res.FY, "@l", line).ExecuteReader())
                if (r.Read()) { found = true; amount = r.GetDecimal(0); active = r.GetBoolean(1); }
            if (!found)
            {
                res.Outcome = "NoBudget";
                res.Message = "There is no budget for \"" + line + "\" in " + res.FY + ".";
                return res;
            }
            if (!active)
            {
                res.Outcome = "Inactive";
                res.Message = "The budget line \"" + line + "\" for " + res.FY + " is inactive.";
                return res;
            }

            DateTime from, to;
            FyRange(res.FY, out from, out to);
            decimal committed = (decimal)Cmd(c, @"SELECT ISNULL(SUM(p.SubTotal),0) FROM PurchaseOrders p
                                                  JOIN AgreementFields af ON af.AgreementId=p.AgreementId AND af.Prop='GlAccount'
                                                  WHERE p.EntityCode=@e AND p.Id<>@id AND p.Currency=@ccy AND p.PoDate>=@f AND p.PoDate<@t
                                                    AND p.Status IN (" + BudgetCommittingStatuses + @") AND LTRIM(RTRIM(af.Val))=@l", tx,
                "@e", entity, "@id", poId, "@ccy", ccy, "@f", from, "@t", to, "@l", line).ExecuteScalar();
            decimal left = amount - committed;
            if (subTotal > left)
            {
                res.Outcome = "Over";
                res.Message = "This PO (" + Fmt.Money(subTotal, ccy) + " before tax) is " + Fmt.Money(subTotal - left, ccy) + " over the budget line \"" + line + "\" for " + res.FY
                            + " (budget " + Fmt.Money(amount, ccy) + ", already committed " + Fmt.Money(committed, ccy) + ", left " + Fmt.Money(left, ccy) + ").";
            }
            else
            {
                res.Outcome = "Ok";
                res.Message = "Within budget: \"" + line + "\" for " + res.FY + " has " + Fmt.Money(left, ccy) + " left; " + Fmt.Money(left - subTotal, ccy) + " after this PO.";
            }
            return res;
        }

        private static InvalidOperationException BudgetRule(string message) { return new InvalidOperationException(message); }

        // Creates (Id == 0) or changes a budget line, with its audit row, in one transaction.
        public static void BudgetSave(BudgetRow b)
        {
            if (!Perm.Can("budget.edit")) throw BudgetRule("Your role cannot change budgets.");
            if (Current.Entity == null || b.EntityCode != Current.Entity.Code) throw BudgetRule("Switch to the entity this budget belongs to first.");

            b.Line = (b.Line ?? "").Trim();
            b.Description = Nz(b.Description);
            DateTime d1, d2;
            FyRange(b.FY, out d1, out d2);
            if (b.Line.Length == 0) throw BudgetRule("Enter the budget line.");
            if (b.Line.Length > 150) throw BudgetRule("The budget line can have at most 150 characters.");
            if (b.Description != null && b.Description.Length > 250) throw BudgetRule("The description can have at most 250 characters.");
            b.Amount = Math.Round(b.Amount, 2, MidpointRounding.AwayFromZero);
            if (b.Amount <= 0) throw BudgetRule("The budget amount must be more than zero.");
            if (b.Amount > 9999999999999m) throw BudgetRule("The budget amount is too large.");

            try
            {
                using (var c = Open())
                using (var tx = c.BeginTransaction())
                {
                    string ccy = Cmd(c, "SELECT Currency FROM Entities WHERE Code=@e", tx, "@e", b.EntityCode).ExecuteScalar() as string;
                    if (ccy == null) throw BudgetRule("This entity no longer exists.");
                    b.Currency = ccy;
                    string recordId = b.FY + " / " + b.Line;

                    if (b.Id == 0)
                    {
                        int same = (int)Cmd(c, "SELECT COUNT(*) FROM Budgets WITH (UPDLOCK, HOLDLOCK) WHERE EntityCode=@e AND FY=@fy AND BudgetLine=@l", tx,
                            "@e", b.EntityCode, "@fy", b.FY, "@l", b.Line).ExecuteScalar();
                        if (same > 0) throw BudgetRule("There is already a budget for \"" + b.Line + "\" in " + b.FY + ". Edit that one instead.");
                        b.IsActive = true;
                        b.Id = (int)Cmd(c, @"INSERT Budgets (EntityCode,FY,BudgetLine,Description,Amount,Currency,IsActive,CreatedBy)
                                             OUTPUT INSERTED.Id VALUES (@e,@fy,@l,@d,@a,@cu,1,@u)", tx,
                            "@e", b.EntityCode, "@fy", b.FY, "@l", b.Line, "@d", N(b.Description), "@a", b.Amount, "@cu", ccy, "@u", N(AppSession.UserName)).ExecuteScalar();
                        Audit("Budget", recordId, "Created", "Amount=" + Num(b.Amount) + " " + ccy + (b.Description != null ? "; " + b.Description : ""), b.EntityCode, c, tx);
                    }
                    else
                    {
                        string oLine, oDesc, oFy, oEntity; decimal oAmount; bool oActive;
                        using (var r = Cmd(c, "SELECT EntityCode, FY, BudgetLine, Description, Amount, IsActive FROM Budgets WITH (UPDLOCK, HOLDLOCK) WHERE Id=@i", tx, "@i", b.Id).ExecuteReader())
                        {
                            if (!r.Read()) throw BudgetRule("This budget line no longer exists.");
                            oEntity = r.GetString(0); oFy = r.GetString(1); oLine = r.GetString(2);
                            oDesc = r.IsDBNull(3) ? "" : r.GetString(3); oAmount = r.GetDecimal(4); oActive = r.GetBoolean(5);
                        }
                        if (oEntity != b.EntityCode || oFy != b.FY) throw BudgetRule("The financial year or entity of a budget line cannot be changed.");
                        if (!string.Equals(oLine, b.Line, StringComparison.Ordinal) && PosOnBudgetLine(c, tx, b.EntityCode, b.FY, oLine) > 0)
                            throw BudgetRule("Purchase orders already use the line \"" + oLine + "\", so its name cannot change. Make it inactive and add a new line instead.");
                        if (!string.Equals(oLine, b.Line, StringComparison.OrdinalIgnoreCase))
                        {
                            int same = (int)Cmd(c, "SELECT COUNT(*) FROM Budgets WHERE EntityCode=@e AND FY=@fy AND BudgetLine=@l AND Id<>@i", tx,
                                "@e", b.EntityCode, "@fy", b.FY, "@l", b.Line, "@i", b.Id).ExecuteScalar();
                            if (same > 0) throw BudgetRule("There is already a budget for \"" + b.Line + "\" in " + b.FY + ".");
                        }

                        int done = Cmd(c, @"UPDATE Budgets SET BudgetLine=@l, Description=@d, Amount=@a, IsActive=@act, UpdatedBy=@u, UpdatedAt=SYSDATETIME()
                                            WHERE Id=@i AND RowVer=@rv", tx,
                            "@l", b.Line, "@d", N(b.Description), "@a", b.Amount, "@act", b.IsActive, "@u", N(AppSession.UserName), "@i", b.Id, "@rv", b.RowVer).ExecuteNonQuery();
                        if (done == 0) throw BudgetRule("Someone else changed this budget line while you were editing. The list has been refreshed; please try again.");

                        var changes = new List<string>();
                        Diff(changes, "Line", oLine, b.Line);
                        Diff(changes, "Description", oDesc, b.Description ?? "");
                        Diff(changes, "Amount", Num(oAmount), Num(b.Amount));
                        string action = oActive && !b.IsActive ? "Made inactive" : (!oActive && b.IsActive ? "Made active" : "Edited");
                        if (changes.Count > 0 || action != "Edited")
                            Audit("Budget", b.FY + " / " + b.Line, action, changes.Count > 0 ? string.Join("; ", changes) : null, b.EntityCode, c, tx);
                    }
                    tx.Commit();
                }
            }
            catch (SqlException ex)
            {
                if (ex.Number == 2627 || ex.Number == 2601)
                    throw BudgetRule("There is already a budget for \"" + b.Line + "\" in " + b.FY + ".");
                throw;
            }
        }
    }
}
