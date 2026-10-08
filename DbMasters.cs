using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace ProcurePay
{
    // Masters & Access: users, entity access, entities, approval matrix and Tally settings.
    // (Vendors are in Db.cs.) Rules that must never be broken, such as "keep one active administrator",
    // are checked here, not only on the screen. Those checks throw InvalidOperationException with a message
    // the screen shows as it is.
    public static partial class Db
    {
        // ================= users
        public static List<UserRow> UserList()
        {
            var list = new List<UserRow>();
            var access = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            using (var c = Open())
            {
                using (var r = Cmd(c, "SELECT UserName, EntityCode, Role FROM UserEntityAccess ORDER BY EntityCode").ExecuteReader())
                    while (r.Read())
                    {
                        List<string> l;
                        if (!access.TryGetValue(r.GetString(0), out l)) access[r.GetString(0)] = l = new List<string>();
                        l.Add(r.GetString(1) + " " + r.GetString(2));
                    }
                using (var r = Cmd(c, "SELECT UserName, FullName, IsActive FROM Users ORDER BY UserName").ExecuteReader())
                    while (r.Read())
                    {
                        List<string> l;
                        access.TryGetValue(r.GetString(0), out l);
                        list.Add(new UserRow { UserName = r.GetString(0), FullName = r.GetString(1), IsActive = r.GetBoolean(2), Access = l == null ? "" : string.Join(", ", l) });
                    }
            }
            return list;
        }

        public static bool UserNameExists(string userName)
        {
            return (int)Scalar("SELECT COUNT(*) FROM Users WHERE UserName=@u", "@u", (userName ?? "").Trim()) > 0;
        }

        // New user with access to one entity (more access is added in Masters > Entity access)
        public static void UserCreate(string userName, string fullName, string password, string entityCode, string role)
        {
            userName = userName.Trim().ToLowerInvariant();
            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                var salt = NewSalt();
                Cmd(c, "INSERT Users (UserName,FullName,PasswordHash,Salt,IsActive) VALUES (@u,@f,@h,@s,1)", tx,
                    "@u", userName, "@f", fullName.Trim(), "@h", Hash(password, salt), "@s", salt).ExecuteNonQuery();
                Cmd(c, "INSERT UserEntityAccess (UserName,EntityCode,Role) VALUES (@u,@e,@r)", tx, "@u", userName, "@e", entityCode, "@r", role).ExecuteNonQuery();
                Audit("User", userName, "Created", fullName.Trim() + "; " + entityCode + " " + role, entityCode, c, tx);
                tx.Commit();
            }
        }

        public static void UserResetPassword(string userName, string password)
        {
            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                var salt = NewSalt();
                int n = Cmd(c, "UPDATE Users SET PasswordHash=@h, Salt=@s WHERE UserName=@u", tx, "@h", Hash(password, salt), "@s", salt, "@u", userName).ExecuteNonQuery();
                if (n == 0) throw new InvalidOperationException("User " + userName + " was not found.");
                Audit("User", userName, "PasswordReset", null, null, c, tx);
                tx.Commit();
            }
        }

        // Number of OTHER active users who are Administrator in at least one entity
        private static int OtherActiveAdmins(SqlConnection c, SqlTransaction tx, string userName)
        {
            return (int)Cmd(c, @"SELECT COUNT(DISTINCT a.UserName) FROM UserEntityAccess a JOIN Users u ON u.UserName=a.UserName
                                 WHERE a.Role='Administrator' AND u.IsActive=1 AND a.UserName<>@u", tx, "@u", userName).ExecuteScalar();
        }

        private static bool IsAdmin(SqlConnection c, SqlTransaction tx, string userName)
        {
            return (int)Cmd(c, "SELECT COUNT(*) FROM UserEntityAccess WHERE UserName=@u AND Role='Administrator'", tx, "@u", userName).ExecuteScalar() > 0;
        }

        public static void UserSetActive(string userName, bool active)
        {
            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                if (!active)
                {
                    if (string.Equals(userName, AppSession.UserName, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("You cannot deactivate your own account.");
                    if (IsAdmin(c, tx, userName) && OtherActiveAdmins(c, tx, userName) == 0)
                        throw new InvalidOperationException("This is the last active administrator. Make someone else an administrator first.");
                }
                Cmd(c, "UPDATE Users SET IsActive=@a WHERE UserName=@u", tx, "@a", active, "@u", userName).ExecuteNonQuery();
                Audit("User", userName, active ? "Activated" : "Deactivated", null, null, c, tx);
                tx.Commit();
            }
        }

        // Replaces the user's access with exactly this set: entity code -> role
        public static void UserSaveAccess(string userName, Dictionary<string, string> wanted)
        {
            if (string.Equals(userName, AppSession.UserName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("You cannot change your own access. Ask another administrator.");
            if (wanted == null || wanted.Count == 0)
                throw new InvalidOperationException("A user needs access to at least one entity.");
            foreach (var kv in wanted)
                if (!Perm.Roles.Contains(kv.Value)) throw new InvalidOperationException("Unknown role: " + kv.Value);

            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                bool losesAdmin = IsAdmin(c, tx, userName) && !wanted.Values.Contains("Administrator");
                if (losesAdmin && OtherActiveAdmins(c, tx, userName) == 0)
                    throw new InvalidOperationException("This is the last administrator. Make someone else an administrator first.");

                Cmd(c, "DELETE FROM UserEntityAccess WHERE UserName=@u", tx, "@u", userName).ExecuteNonQuery();
                foreach (var kv in wanted.OrderBy(k => k.Key))
                    Cmd(c, "INSERT UserEntityAccess (UserName,EntityCode,Role) VALUES (@u,@e,@r)", tx, "@u", userName, "@e", kv.Key, "@r", kv.Value).ExecuteNonQuery();
                Audit("User", userName, "AccessChanged", string.Join("; ", wanted.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value)), null, c, tx);
                tx.Commit();
            }
        }

        // ================= entities
        public static List<EntityInfo> EntityList()
        {
            var list = new List<EntityInfo>();
            using (var c = Open())
            using (var r = Cmd(c, "SELECT Code, Name, Currency FROM Entities ORDER BY Code").ExecuteReader())
                while (r.Read()) list.Add(new EntityInfo { Code = r.GetString(0), Name = r.GetString(1), Currency = r.GetString(2) });
            return list;
        }

        public static List<EntityRow> EntityStats()
        {
            var list = new List<EntityRow>();
            using (var c = Open())
            using (var r = Cmd(c, @"SELECT e.Code, e.Name, e.Currency,
                    (SELECT COUNT(*) FROM UserEntityAccess a WHERE a.EntityCode=e.Code),
                    (SELECT COUNT(*) FROM Vendors v WHERE v.EntityCode=e.Code),
                    (SELECT COUNT(*) FROM Agreements g WHERE g.EntityCode=e.Code)
                    FROM Entities e ORDER BY e.Code").ExecuteReader())
                while (r.Read())
                    list.Add(new EntityRow { Code = r.GetString(0), Name = r.GetString(1), Currency = r.GetString(2), Users = r.GetInt32(3), Vendors = r.GetInt32(4), Agreements = r.GetInt32(5) });
            return list;
        }

        public static bool EntityExists(string code)
        {
            return (int)Scalar("SELECT COUNT(*) FROM Entities WHERE Code=@c", "@c", (code ?? "").Trim()) > 0;
        }

        public static void EntityCreate(string code, string name, string currency)
        {
            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                Cmd(c, "INSERT Entities (Code,Name,Currency) VALUES (@c,@n,@u)", tx, "@c", code, "@n", name, "@u", currency).ExecuteNonQuery();
                Audit("Entity", code, "Created", name + " (" + currency + ")", code, c, tx);
                tx.Commit();
            }
        }

        public static void EntityUpdate(string code, string name, string currency)
        {
            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                string oldCur = (string)Cmd(c, "SELECT Currency FROM Entities WHERE Code=@c", tx, "@c", code).ExecuteScalar();
                if (oldCur == null) throw new InvalidOperationException("Entity " + code + " was not found.");
                if (oldCur != currency && (int)Cmd(c, "SELECT COUNT(*) FROM Agreements WHERE EntityCode=@c", tx, "@c", code).ExecuteScalar() > 0)
                    throw new InvalidOperationException("The currency cannot change because this entity already has agreements.");
                Cmd(c, "UPDATE Entities SET Name=@n, Currency=@u WHERE Code=@c", tx, "@n", name, "@u", currency, "@c", code).ExecuteNonQuery();
                Audit("Entity", code, "Updated", name + " (" + currency + ")", code, c, tx);
                tx.Commit();
            }
        }

        // ================= approval matrix
        public static List<MatrixLevel> MatrixAll()
        {
            var list = new List<MatrixLevel>();
            using (var c = Open())
            using (var r = Cmd(c, "SELECT Level, Role, Label, MinValue FROM ApprovalMatrix ORDER BY Level").ExecuteReader())
                while (r.Read())
                    list.Add(new MatrixLevel { Level = r.GetInt32(0), Role = r.IsDBNull(1) ? "" : r.GetString(1), Label = r.IsDBNull(2) ? "" : r.GetString(2), MinValue = r.GetDecimal(3) });
            return list;
        }

        // Active users per role, across all entities (used to warn about a level nobody can approve)
        public static Dictionary<string, int> RoleUserCounts()
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using (var c = Open())
            using (var r = Cmd(c, @"SELECT a.Role, COUNT(DISTINCT a.UserName) FROM UserEntityAccess a
                                    JOIN Users u ON u.UserName=a.UserName AND u.IsActive=1 GROUP BY a.Role").ExecuteReader())
                while (r.Read()) d[r.GetString(0)] = r.GetInt32(1);
            return d;
        }

        // Replaces the whole matrix. Levels are numbered 1, 2, 3 ... in the order given.
        public static void MatrixSave(List<MatrixLevel> levels)
        {
            if (levels == null || levels.Count == 0) throw new InvalidOperationException("At least one approval level is required.");
            foreach (var l in levels)
            {
                if (string.IsNullOrWhiteSpace(l.Label)) throw new InvalidOperationException("Every level needs a step name.");
                if (!Perm.Roles.Contains(l.Role)) throw new InvalidOperationException("Unknown approver role: " + l.Role);
                if (l.MinValue < 0) throw new InvalidOperationException("A level cannot apply from a negative value.");
            }
            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                Cmd(c, "DELETE FROM ApprovalMatrix", tx).ExecuteNonQuery();
                for (int i = 0; i < levels.Count; i++)
                    Cmd(c, "INSERT ApprovalMatrix (Level,Role,Label,MinValue) VALUES (@n,@r,@l,@m)", tx,
                        "@n", i + 1, "@r", levels[i].Role, "@l", levels[i].Label.Trim(), "@m", levels[i].MinValue).ExecuteNonQuery();
                Audit("Matrix", null, "Saved", string.Join("; ", levels.Select((l, i) => "L" + (i + 1) + " " + l.Role + " from " + l.MinValue.ToString("0.##"))), null, c, tx);
                tx.Commit();
            }
        }

        // ================= Tally settings (kept per entity in the Settings table)
        // key, caption, hint. The Tally export (a later step) reads these keys.
        public static readonly string[][] TallyFields =
        {
            new[] { "tally.company",              "Tally company name",     "Exactly as it appears in Tally" },
            new[] { "tally.voucher_type",         "Purchase voucher type",  "Usually Purchase" },
            new[] { "tally.purchase_ledger",      "Purchase ledger",        "Debited on a purchase voucher" },
            new[] { "tally.gst_ledger",           "GST input ledger",       "Input tax on purchases" },
            new[] { "tally.tds_ledger",           "TDS payable ledger",     "Tax deducted at source" },
            new[] { "tally.payment_voucher_type", "Payment voucher type",   "Usually Payment" },
            new[] { "tally.bank_ledger",          "Bank ledger",            "Credited on a payment voucher" }
        };

        public static Dictionary<string, string> TallyLoad(string entityCode)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var c = Open())
            using (var r = Cmd(c, "SELECT SettingKey, SettingValue FROM Settings WHERE EntityCode=@e AND SettingKey LIKE 'tally.%'", null, "@e", entityCode).ExecuteReader())
                while (r.Read()) d[r.GetString(0)] = r.IsDBNull(1) ? "" : r.GetString(1);
            return d;
        }

        public static void TallySave(string entityCode, Dictionary<string, string> values)
        {
            var known = new HashSet<string>(TallyFields.Select(f => f[0]), StringComparer.OrdinalIgnoreCase);
            foreach (var kv in values)
            {
                if (!known.Contains(kv.Key)) throw new InvalidOperationException("Unknown Tally setting: " + kv.Key);
                if ((kv.Value ?? "").Length > 100) throw new InvalidOperationException("Each Tally name can be at most 100 characters.");
            }
            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                foreach (var kv in values)
                    Cmd(c, @"MERGE Settings AS t USING (SELECT @e AS EntityCode, @k AS SettingKey) AS s
                             ON t.EntityCode=s.EntityCode AND t.SettingKey=s.SettingKey
                             WHEN MATCHED THEN UPDATE SET SettingValue=@v, UpdatedBy=@u, UpdatedAt=SYSDATETIME()
                             WHEN NOT MATCHED THEN INSERT (EntityCode,SettingKey,SettingValue,UpdatedBy) VALUES (@e,@k,@v,@u);", tx,
                        "@e", entityCode, "@k", kv.Key, "@v", N(kv.Value), "@u", N(AppSession.UserName)).ExecuteNonQuery();
                Audit("Tally", entityCode, "SettingsSaved", string.Join("; ", values.Select(k => k.Key.Replace("tally.", "") + "=" + k.Value)), entityCode, c, tx);
                tx.Commit();
            }
        }
    }
}
