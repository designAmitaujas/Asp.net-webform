using System.Collections.Generic;

namespace ProcurePay
{
    public class EntityInfo
    {
        public string Code;
        public string Name;
        public string Currency;
        public override string ToString() { return Name; }   // dropdown shows the name
    }

    public class EntityAccess
    {
        public EntityInfo Entity;
        public string Role;
    }

    // The entity and role the user is working in right now
    public static class Current
    {
        public static EntityInfo Entity;
        public static string Role;

        public static bool CanEditMasters { get { return Perm.Can(Role, "masters.edit"); } }

        // Indian financial year, April to March, e.g. 2026-27
        public static string FinancialYear()
        {
            var d = System.DateTime.Today;
            int start = d.Month >= 4 ? d.Year : d.Year - 1;
            return start + "-" + ((start + 1) % 100).ToString("00");
        }
    }
}