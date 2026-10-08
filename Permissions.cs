using System;
using System.Collections.Generic;

namespace ProcurePay
{
    // Who may do what. One place to change when a role gains or loses a right.
    // Use:  Perm.Can("agreement.approve")            -> checks the signed-in user's role in the current entity
    //       Perm.Can(role, "agreement.approve")      -> checks an explicit role
    public static class Perm
    {
        private static readonly Dictionary<string, HashSet<string>> Map =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "Procurement", new HashSet<string> {
                "agreement.view", "agreement.create", "agreement.submit", "agreement.cancel",
                "po.view", "po.create", "budget.view", "invoice.view", "invoice.book", "reports.view" } },

            { "Accounts", new HashSet<string> {
                "agreement.view", "agreement.approve",
                "po.view", "budget.view", "budget.edit", "invoice.view", "invoice.book", "invoice.approve",
                "voucher.view", "voucher.initiate", "voucher.approve", "tally.export", "reports.view" } },

            { "Approver", new HashSet<string> {
                "agreement.view", "agreement.approve",
                "po.view", "po.approve", "budget.view", "invoice.view", "invoice.approve",
                "voucher.view", "voucher.approve", "reports.view" } },

            { "Authorised Signatory", new HashSet<string> {
                "agreement.view", "agreement.sign", "po.view", "reports.view" } },

            { "Administrator", new HashSet<string> { "*" } }   // "*" = everything
        };

        // Every role, in the order the screens list them
        public static readonly string[] Roles = { "Procurement", "Accounts", "Approver", "Authorised Signatory", "Administrator" };

        public static bool Can(string role, string permission)
        {
            if (string.IsNullOrEmpty(role)) return false;
            HashSet<string> set;
            if (!Map.TryGetValue(role, out set)) return false;
            return set.Contains("*") || set.Contains(permission);
        }

        public static bool Can(string permission) { return Can(Current.Role, permission); }
    }
}
