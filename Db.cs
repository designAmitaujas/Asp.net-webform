using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Security.Cryptography;

namespace ProcurePay
{
    // All SQL Server access lives here. The connection string is in App.config (name: ProcurePay).
    public static partial class Db
    {
        public static string ConnectionString
        {
            get
            {
                var cs = ConfigurationManager.ConnectionStrings["ProcurePay"];
                if (cs == null) throw new InvalidOperationException("Connection string 'ProcurePay' is missing in App.config.");
                return cs.ConnectionString;
            }
        }

        public static SqlConnection Open()
        {
            var c = new SqlConnection(ConnectionString);
            c.Open();
            return c;
        }

        public static SqlCommand Cmd(SqlConnection c, string sql, SqlTransaction tx = null, params object[] kv)
        {
            var cmd = new SqlCommand(sql, c, tx);
            for (int i = 0; i + 1 < kv.Length; i += 2)
                cmd.Parameters.AddWithValue((string)kv[i], kv[i + 1] ?? DBNull.Value);
            return cmd;
        }

        public static object Scalar(string sql, params object[] kv)
        {
            using (var c = Open()) using (var cmd = Cmd(c, sql, null, kv)) return cmd.ExecuteScalar();
        }

        // ---- passwords (PBKDF2)
        public static byte[] NewSalt() { var s = new byte[16]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(s); return s; }
        public static byte[] Hash(string password, byte[] salt)
        {
            using (var k = new Rfc2898DeriveBytes(password, salt, 20000, HashAlgorithmName.SHA256)) return k.GetBytes(32);
        }

        // ---- first-run setup: creates tables and demo users if the database is empty
        private static bool schemaDone;
        public static void EnsureSchema()
        {
            if (schemaDone) return;
            using (var c = Open())
            {
                Cmd(c, SchemaSql).ExecuteNonQuery();
                if ((int)Cmd(c, "SELECT COUNT(*) FROM Entities").ExecuteScalar() == 0) Seed(c);
            }
            schemaDone = true;
        }

        private const string SchemaSql = @"
IF OBJECT_ID('Entities') IS NULL CREATE TABLE Entities (Code NVARCHAR(10) NOT NULL PRIMARY KEY, Name NVARCHAR(150) NOT NULL, Currency NVARCHAR(5) NOT NULL);
IF OBJECT_ID('Users') IS NULL CREATE TABLE Users (UserName NVARCHAR(50) NOT NULL PRIMARY KEY, FullName NVARCHAR(100) NOT NULL, PasswordHash VARBINARY(64) NOT NULL, Salt VARBINARY(16) NOT NULL, IsActive BIT NOT NULL DEFAULT 1);
IF OBJECT_ID('UserEntityAccess') IS NULL CREATE TABLE UserEntityAccess (UserName NVARCHAR(50) NOT NULL REFERENCES Users(UserName), EntityCode NVARCHAR(10) NOT NULL REFERENCES Entities(Code), Role NVARCHAR(40) NOT NULL, PRIMARY KEY (UserName, EntityCode));
IF OBJECT_ID('Vendors') IS NULL CREATE TABLE Vendors (VendorId INT IDENTITY PRIMARY KEY, EntityCode NVARCHAR(10) NOT NULL REFERENCES Entities(Code), Name NVARCHAR(200) NOT NULL);
IF COL_LENGTH('Vendors','IsActive') IS NULL ALTER TABLE Vendors ADD IsActive BIT NOT NULL DEFAULT 1;
IF OBJECT_ID('Agreements') IS NULL CREATE TABLE Agreements (Id INT IDENTITY PRIMARY KEY, EntityCode NVARCHAR(10) NOT NULL REFERENCES Entities(Code), AgreementNo NVARCHAR(40) NOT NULL, Type NVARCHAR(60), Title NVARCHAR(250) NOT NULL, Supplier NVARCHAR(200), Value DECIMAL(18,2) NOT NULL DEFAULT 0, Currency NVARCHAR(5), Status NVARCHAR(30) NOT NULL DEFAULT 'Draft', CreatedBy NVARCHAR(50), CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(), SignedBy NVARCHAR(100), SignedRole NVARCHAR(40), SignedAt DATETIME2 NULL, CONSTRAINT UQ_Agreement_No UNIQUE (EntityCode, AgreementNo));
IF OBJECT_ID('AgreementFields') IS NULL CREATE TABLE AgreementFields (AgreementId INT NOT NULL REFERENCES Agreements(Id) ON DELETE CASCADE, Prop NVARCHAR(60) NOT NULL, Val NVARCHAR(MAX), PRIMARY KEY (AgreementId, Prop));
IF OBJECT_ID('ApprovalSteps') IS NULL CREATE TABLE ApprovalSteps (AgreementId INT NOT NULL REFERENCES Agreements(Id) ON DELETE CASCADE, Level INT NOT NULL, Role NVARCHAR(40), Label NVARCHAR(100), Status NVARCHAR(20) NOT NULL DEFAULT 'Pending', ApprovedBy NVARCHAR(50), At DATETIME2 NULL, Remarks NVARCHAR(500), PRIMARY KEY (AgreementId, Level));
IF OBJECT_ID('DocLinks') IS NULL CREATE TABLE DocLinks (Id INT IDENTITY PRIMARY KEY, AgreementId INT NOT NULL REFERENCES Agreements(Id) ON DELETE CASCADE, Name NVARCHAR(200), Url NVARCHAR(500), AddedBy NVARCHAR(50));
IF OBJECT_ID('ApprovalMatrix') IS NULL CREATE TABLE ApprovalMatrix (Level INT PRIMARY KEY, Role NVARCHAR(40), Label NVARCHAR(100), MinValue DECIMAL(18,2));
IF OBJECT_ID('AuditLog') IS NULL CREATE TABLE AuditLog (Id BIGINT IDENTITY PRIMARY KEY, At DATETIME2 NOT NULL DEFAULT SYSDATETIME(), UserName NVARCHAR(50), EntityCode NVARCHAR(10), Module NVARCHAR(40) NOT NULL, RecordId NVARCHAR(60), Action NVARCHAR(40) NOT NULL, Details NVARCHAR(1000));
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_AuditLog_Module_At') CREATE INDEX IX_AuditLog_Module_At ON AuditLog (Module, At DESC);
IF OBJECT_ID('Notifications') IS NULL CREATE TABLE Notifications (Id BIGINT IDENTITY PRIMARY KEY, UserName NVARCHAR(50) NOT NULL REFERENCES Users(UserName), EntityCode NVARCHAR(10) NULL, Title NVARCHAR(150) NOT NULL, Message NVARCHAR(500), Module NVARCHAR(40), RecordId NVARCHAR(60), IsRead BIT NOT NULL DEFAULT 0, CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(), ReadAt DATETIME2 NULL);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Notifications_User') CREATE INDEX IX_Notifications_User ON Notifications (UserName, IsRead, CreatedAt DESC);
IF OBJECT_ID('Settings') IS NULL CREATE TABLE Settings (EntityCode NVARCHAR(10) NOT NULL DEFAULT '', SettingKey NVARCHAR(80) NOT NULL, SettingValue NVARCHAR(500), UpdatedBy NVARCHAR(50), UpdatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(), PRIMARY KEY (EntityCode, SettingKey));
IF COL_LENGTH('Agreements','CancelReason') IS NULL ALTER TABLE Agreements ADD CancelReason NVARCHAR(500) NULL, CancelledBy NVARCHAR(50) NULL, CancelledAt DATETIME2 NULL;
IF COL_LENGTH('Agreements','VendorId') IS NULL ALTER TABLE Agreements ADD VendorId INT NULL REFERENCES Vendors(VendorId);
EXEC('UPDATE a SET a.VendorId=v.VendorId FROM Agreements a JOIN Vendors v ON v.EntityCode=a.EntityCode AND v.Name=a.Supplier WHERE a.VendorId IS NULL');
IF COL_LENGTH('Vendors','Gstin') IS NULL ALTER TABLE Vendors ADD Gstin NVARCHAR(15) NULL, Pan NVARCHAR(10) NULL, ContactPerson NVARCHAR(100) NULL, Email NVARCHAR(150) NULL, Phone NVARCHAR(30) NULL, Address NVARCHAR(400) NULL, BankName NVARCHAR(100) NULL, AccountNo NVARCHAR(30) NULL, Ifsc NVARCHAR(15) NULL, IsMsme BIT NOT NULL DEFAULT 0, MsmeNo NVARCHAR(30) NULL, PaymentTermsDays INT NULL;

-- ---- Purchase Orders (step 22). A PO is raised against one signed agreement and one vendor.
IF OBJECT_ID('PurchaseOrders') IS NULL CREATE TABLE PurchaseOrders (
    Id INT IDENTITY PRIMARY KEY,
    EntityCode NVARCHAR(10) NOT NULL REFERENCES Entities(Code),
    PoNo NVARCHAR(40) NOT NULL,
    AgreementId INT NOT NULL REFERENCES Agreements(Id),
    VendorId INT NOT NULL REFERENCES Vendors(VendorId),
    Title NVARCHAR(250) NOT NULL,
    PoDate DATE NOT NULL DEFAULT (CAST(SYSDATETIME() AS DATE)),
    DeliveryDate DATE NULL,
    Currency NVARCHAR(5) NOT NULL,
    SubTotal DECIMAL(18,2) NOT NULL DEFAULT 0,
    TaxTotal DECIMAL(18,2) NOT NULL DEFAULT 0,
    Total DECIMAL(18,2) NOT NULL DEFAULT 0,
    Status NVARCHAR(30) NOT NULL DEFAULT 'Draft',
    Remarks NVARCHAR(1000) NULL,
    CreatedBy NVARCHAR(50) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CancelReason NVARCHAR(500) NULL,
    CancelledBy NVARCHAR(50) NULL,
    CancelledAt DATETIME2 NULL,
    CONSTRAINT UQ_PO_No UNIQUE (EntityCode, PoNo),
    CONSTRAINT CK_PO_Status CHECK (Status IN ('Draft','Pending Approval','Approved','Rejected','Cancelled','Closed')),
    CONSTRAINT CK_PO_Totals CHECK (SubTotal >= 0 AND TaxTotal >= 0 AND Total = SubTotal + TaxTotal)
);
IF OBJECT_ID('PurchaseOrderLines') IS NULL CREATE TABLE PurchaseOrderLines (
    Id INT IDENTITY PRIMARY KEY,
    PoId INT NOT NULL REFERENCES PurchaseOrders(Id) ON DELETE CASCADE,
    LineNumber INT NOT NULL,
    Description NVARCHAR(300) NOT NULL,
    Unit NVARCHAR(20) NULL,
    Quantity DECIMAL(18,3) NOT NULL,
    Rate DECIMAL(18,2) NOT NULL,
    TaxPercent DECIMAL(5,2) NOT NULL DEFAULT 0,
    Amount DECIMAL(18,2) NOT NULL,
    CONSTRAINT UQ_POLine UNIQUE (PoId, LineNumber),
    CONSTRAINT CK_POLine_Values CHECK (Quantity > 0 AND Rate >= 0 AND TaxPercent BETWEEN 0 AND 100)
);
IF OBJECT_ID('PoApprovalSteps') IS NULL CREATE TABLE PoApprovalSteps (
    PoId INT NOT NULL REFERENCES PurchaseOrders(Id) ON DELETE CASCADE,
    Level INT NOT NULL,
    Role NVARCHAR(40) NULL,
    Label NVARCHAR(100) NULL,
    Status NVARCHAR(20) NOT NULL DEFAULT 'Pending',
    ApprovedBy NVARCHAR(50) NULL,
    At DATETIME2 NULL,
    Remarks NVARCHAR(500) NULL,
    PRIMARY KEY (PoId, Level)
);
IF OBJECT_ID('PoMilestones') IS NULL CREATE TABLE PoMilestones (
    Id INT IDENTITY PRIMARY KEY,
    PoId INT NOT NULL REFERENCES PurchaseOrders(Id) ON DELETE CASCADE,
    SeqNo INT NOT NULL,
    Description NVARCHAR(250) NOT NULL,
    DueDate DATE NULL,
    Amount DECIMAL(18,2) NOT NULL,
    InvoicedAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
    Status NVARCHAR(20) NOT NULL DEFAULT 'Open',
    CONSTRAINT UQ_PoMilestone UNIQUE (PoId, SeqNo),
    CONSTRAINT CK_PoMilestone_Values CHECK (Amount > 0 AND InvoicedAmount >= 0 AND InvoicedAmount <= Amount),
    CONSTRAINT CK_PoMilestone_Status CHECK (Status IN ('Open','Invoiced','Paid'))
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_PO_Entity_Status') CREATE INDEX IX_PO_Entity_Status ON PurchaseOrders (EntityCode, Status);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_PO_Agreement') CREATE INDEX IX_PO_Agreement ON PurchaseOrders (AgreementId);

-- ---- Budgets (step 25). One row per budget line, per entity, per financial year. Amount is in the entity's currency.
-- BudgetLine is matched, ignoring capitals, to the agreement field ""GL / budget line"".
IF OBJECT_ID('Budgets') IS NULL CREATE TABLE Budgets (
    Id INT IDENTITY PRIMARY KEY,
    EntityCode NVARCHAR(10) NOT NULL REFERENCES Entities(Code),
    FY NVARCHAR(10) NOT NULL,
    BudgetLine NVARCHAR(150) NOT NULL,
    Description NVARCHAR(250) NULL,
    Amount DECIMAL(18,2) NOT NULL,
    Currency NVARCHAR(5) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedBy NVARCHAR(50) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    UpdatedBy NVARCHAR(50) NULL,
    UpdatedAt DATETIME2 NULL,
    RowVer ROWVERSION,
    CONSTRAINT UQ_Budget_Line UNIQUE (EntityCode, FY, BudgetLine),
    CONSTRAINT CK_Budget_Amount CHECK (Amount > 0),
    CONSTRAINT CK_Budget_FY CHECK (FY LIKE '[0-9][0-9][0-9][0-9]-[0-9][0-9]')
);

-- ---- Invoices (step 27). One invoice is booked against ONE milestone of an APPROVED purchase order.
-- Amounts are in the PO's currency. ExchangeRate converts to the entity's currency (1 when they are the same); BaseTotal = Total x ExchangeRate.
-- TDS is worked out on the taxable value (before GST). Status Draft until it is booked (step 28) and approved (step 29).
IF OBJECT_ID('Invoices') IS NULL CREATE TABLE Invoices (
    Id INT IDENTITY PRIMARY KEY,
    EntityCode NVARCHAR(10) NOT NULL REFERENCES Entities(Code),
    BookingNo NVARCHAR(40) NOT NULL,
    PoId INT NOT NULL REFERENCES PurchaseOrders(Id),
    MilestoneId INT NOT NULL REFERENCES PoMilestones(Id),
    VendorId INT NOT NULL REFERENCES Vendors(VendorId),
    VendorInvoiceNo NVARCHAR(50) NOT NULL,
    InvoiceDate DATE NOT NULL,
    DueDate DATE NULL,
    Currency NVARCHAR(5) NOT NULL,
    ExchangeRate DECIMAL(18,6) NOT NULL DEFAULT 1,
    Taxable DECIMAL(18,2) NOT NULL,
    GstPercent DECIMAL(5,2) NOT NULL DEFAULT 0,
    GstAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
    Total DECIMAL(18,2) NOT NULL,
    TdsSection NVARCHAR(20) NULL,
    TdsPercent DECIMAL(5,2) NOT NULL DEFAULT 0,
    TdsAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
    NetPayable DECIMAL(18,2) NOT NULL,
    BaseTotal DECIMAL(18,2) NOT NULL,
    FileRef NVARCHAR(500) NULL,
    Remarks NVARCHAR(1000) NULL,
    Status NVARCHAR(30) NOT NULL DEFAULT 'Draft',
    CreatedBy NVARCHAR(50) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    UpdatedBy NVARCHAR(50) NULL,
    UpdatedAt DATETIME2 NULL,
    CancelReason NVARCHAR(500) NULL,
    CancelledBy NVARCHAR(50) NULL,
    CancelledAt DATETIME2 NULL,
    CONSTRAINT UQ_Invoice_No UNIQUE (EntityCode, BookingNo),
    CONSTRAINT CK_Invoice_Status CHECK (Status IN ('Draft','Pending Approval','Approved','Rejected','Cancelled','Paid')),
    CONSTRAINT CK_Invoice_Rate CHECK (ExchangeRate > 0),
    CONSTRAINT CK_Invoice_Pct CHECK (GstPercent BETWEEN 0 AND 100 AND TdsPercent BETWEEN 0 AND 100),
    CONSTRAINT CK_Invoice_Amounts CHECK (Taxable > 0 AND GstAmount >= 0 AND Total = Taxable + GstAmount AND TdsAmount >= 0 AND TdsAmount <= Total AND NetPayable = Total - TdsAmount)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Invoice_Entity_Status') CREATE INDEX IX_Invoice_Entity_Status ON Invoices (EntityCode, Status);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Invoice_Po') CREATE INDEX IX_Invoice_Po ON Invoices (PoId, MilestoneId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Invoice_Vendor_No') CREATE INDEX IX_Invoice_Vendor_No ON Invoices (EntityCode, VendorId, VendorInvoiceNo);
";

        private static void Seed(SqlConnection c)
        {
            Action<string, object[]> run = (sql, kv) => { using (var cmd = Cmd(c, sql, null, kv)) cmd.ExecuteNonQuery(); };

            foreach (var e in new[] { new[] { "IN", "Meridian India Pvt Ltd", "INR" }, new[] { "US", "Meridian Inc (USA)", "USD" }, new[] { "UK", "Meridian UK Ltd", "GBP" } })
                run("INSERT Entities VALUES (@a,@b,@c)", new object[] { "@a", e[0], "@b", e[1], "@c", e[2] });

            run("INSERT ApprovalMatrix VALUES (1,'Accounts','Finance & budget review',0),(2,'Approver','Management approval',0),(3,'Administrator','Partner sign-off',500000)", new object[0]);

            var users = new[] {
                new[] { "rahul", "Rahul Mehta" }, new[] { "ashish", "Ashish (Partner)" }, new[] { "priya", "Priya Shah" },
                new[] { "nikhil", "Nikhil Rao" }, new[] { "admin", "Administrator" } };
            foreach (var u in users)
            {
                var salt = NewSalt();
                run("INSERT Users (UserName,FullName,PasswordHash,Salt) VALUES (@u,@f,@h,@s)",
                    new object[] { "@u", u[0], "@f", u[1], "@h", Hash("Admin@123", salt), "@s", salt });
            }
            var acc = new[] {
                "rahul|IN|Procurement", "rahul|US|Procurement",
                "ashish|IN|Authorised Signatory", "ashish|US|Authorised Signatory", "ashish|UK|Authorised Signatory",
                "priya|IN|Accounts", "priya|US|Approver", "priya|UK|Accounts",
                "nikhil|IN|Approver", "nikhil|US|Accounts", "nikhil|UK|Approver",
                "admin|IN|Administrator", "admin|US|Administrator", "admin|UK|Administrator" };
            foreach (var a in acc)
            {
                var p = a.Split('|');
                run("INSERT UserEntityAccess VALUES (@u,@e,@r)", new object[] { "@u", p[0], "@e", p[1], "@r", p[2] });
            }
            var vendors = new[] {
                "IN|Apex Facility Services", "IN|Green Power Solutions", "IN|Nova Tech Systems", "IN|Skyline Interiors Pvt Ltd",
                "IN|Vertex Logistics Pvt Ltd", "IN|Zenith Instruments GmbH",
                "US|Brightlane LLC", "US|Cobalt Cloud Inc", "US|Summit Consulting LLC",
                "UK|Kingsway Services Ltd", "UK|Thames Digital Ltd" };
            foreach (var v in vendors)
            {
                var p = v.Split('|');
                run("INSERT Vendors (EntityCode,Name) VALUES (@e,@n)", new object[] { "@e", p[0], "@n", p[1] });
            }
        }

        // ---- login: returns null if wrong, otherwise the user's full name
        public static string Authenticate(string user, string password)
        {
            EnsureSchema();
            using (var c = Open())
            using (var cmd = Cmd(c, "SELECT FullName, PasswordHash, Salt FROM Users WHERE UserName=@u AND IsActive=1", null, "@u", user))
            using (var r = cmd.ExecuteReader())
            {
                if (!r.Read()) return null;
                var salt = (byte[])r["Salt"]; var stored = (byte[])r["PasswordHash"];
                var h = Hash(password, salt);
                int diff = 0;
                for (int i = 0; i < h.Length && i < stored.Length; i++) diff |= h[i] ^ stored[i];
                return diff == 0 && h.Length == stored.Length ? (string)r["FullName"] : null;
            }
        }

        public static List<EntityAccess> AccessFor(string user)
        {
            var list = new List<EntityAccess>();
            using (var c = Open())
            using (var cmd = Cmd(c, @"SELECT e.Code, e.Name, e.Currency, a.Role FROM UserEntityAccess a
                                      JOIN Entities e ON e.Code=a.EntityCode WHERE a.UserName=@u ORDER BY e.Code", null, "@u", user))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    list.Add(new EntityAccess
                    {
                        Entity = new EntityInfo { Code = (string)r["Code"], Name = (string)r["Name"], Currency = (string)r["Currency"] },
                        Role = (string)r["Role"]
                    });
            return list;
        }

        public static List<string> Vendors(string entity)
        {
            var list = new List<string>();
            using (var c = Open())
            using (var cmd = Cmd(c, "SELECT Name FROM Vendors WHERE EntityCode=@e AND IsActive=1 ORDER BY Name", null, "@e", entity))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) list.Add((string)r["Name"]);
            return list;
        }

        // Active vendors for the agreement form drop-down: id + name
        public static List<VendorRow> VendorChoices(string entity)
        {
            var list = new List<VendorRow>();
            using (var c = Open())
            using (var cmd = Cmd(c, "SELECT VendorId, Name FROM Vendors WHERE EntityCode=@e AND IsActive=1 ORDER BY Name", null, "@e", entity))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) list.Add(new VendorRow { Id = r.GetInt32(0), Name = r.GetString(1), EntityCode = entity, IsActive = true });
            return list;
        }

        // Name of a vendor by id (null if not found)
        public static string VendorName(int id)
        {
            object o = Scalar("SELECT Name FROM Vendors WHERE VendorId=@i", "@i", id);
            return o == null ? null : (string)o;
        }

        public static List<MatrixLevel> MatrixFor(decimal value)
        {
            var list = new List<MatrixLevel>();
            using (var c = Open())
            using (var cmd = Cmd(c, "SELECT Level, Role, Label, MinValue FROM ApprovalMatrix WHERE MinValue<=@v ORDER BY Level", null, "@v", value))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    list.Add(new MatrixLevel { Level = (int)r["Level"], Role = (string)r["Role"], Label = (string)r["Label"], MinValue = (decimal)r["MinValue"] });
            return list;
        }

        // ---- agreements
        private static object N(string s) { return s == null ? (object)DBNull.Value : s; }
        private static string S(object o) { return o == DBNull.Value ? null : (string)o; }

        public static List<Agreement> LoadAgreements(string entity)
        {
            var map = new Dictionary<int, Agreement>();
            using (var c = Open())
            {
                using (var r = Cmd(c, "SELECT a.*, v.Name AS VendorName FROM Agreements a LEFT JOIN Vendors v ON v.VendorId=a.VendorId WHERE a.EntityCode=@e", null, "@e", entity).ExecuteReader())
                    while (r.Read())
                    {
                        var a = new Agreement
                        {
                            Id = (int)r["Id"], EntityCode = (string)r["EntityCode"], No = (string)r["AgreementNo"],
                            Type = S(r["Type"]), Title = (string)r["Title"], Supplier = S(r["VendorName"]) ?? S(r["Supplier"]), VendorId = r["VendorId"] == DBNull.Value ? (int?)null : (int)r["VendorId"],
                            Value = (decimal)r["Value"], Currency = S(r["Currency"]), Status = (string)r["Status"],
                            CreatedBy = S(r["CreatedBy"]), CreatedAt = (DateTime)r["CreatedAt"],
                            SignedBy = S(r["SignedBy"]), SignedRole = S(r["SignedRole"]),
                            CancelReason = S(r["CancelReason"]), CancelledBy = S(r["CancelledBy"]),
                            CancelledAt = r["CancelledAt"] == DBNull.Value ? (DateTime?)null : (DateTime)r["CancelledAt"],
                            SignedAt = r["SignedAt"] == DBNull.Value ? (DateTime?)null : (DateTime)r["SignedAt"]
                        };
                        map[a.Id] = a;
                    }
                const string sub = "(SELECT Id FROM Agreements WHERE EntityCode=@e)";
                using (var r = Cmd(c, "SELECT AgreementId, Prop, Val FROM AgreementFields WHERE AgreementId IN " + sub, null, "@e", entity).ExecuteReader())
                    while (r.Read()) map[(int)r["AgreementId"]].Extra[(string)r["Prop"]] = S(r["Val"]) ?? "";
                using (var r = Cmd(c, "SELECT * FROM ApprovalSteps WHERE AgreementId IN " + sub + " ORDER BY Level", null, "@e", entity).ExecuteReader())
                    while (r.Read())
                        map[(int)r["AgreementId"]].Approvals.Add(new ApprovalStep
                        {
                            Level = (int)r["Level"], Role = S(r["Role"]), Label = S(r["Label"]), Status = (string)r["Status"],
                            ApprovedBy = S(r["ApprovedBy"]), Remarks = S(r["Remarks"]),
                            At = r["At"] == DBNull.Value ? (DateTime?)null : (DateTime)r["At"]
                        });
                using (var r = Cmd(c, "SELECT AgreementId, Id, Name, Url, AddedBy FROM DocLinks WHERE AgreementId IN " + sub + " ORDER BY Id", null, "@e", entity).ExecuteReader())
                    while (r.Read())
                        map[(int)r["AgreementId"]].Documents.Add(new DocLink { Id = (int)r["Id"], Name = S(r["Name"]), Url = S(r["Url"]), AddedBy = S(r["AddedBy"]) });
            }
            return map.Values.ToList();
        }

        public static int SupplierCount(string entity)
        {
            return (int)Scalar("SELECT COUNT(*) FROM Vendors WHERE EntityCode=@e AND IsActive=1", "@e", entity);
        }

        private const string VendorCols = "VendorId, EntityCode, Name, IsActive, Gstin, Pan, ContactPerson, Email, Phone, Address, BankName, AccountNo, Ifsc, IsMsme, MsmeNo, PaymentTermsDays";

        private static string S(SqlDataReader r, int i) { return r.IsDBNull(i) ? "" : r.GetString(i); }

        private static VendorRow ReadVendor(SqlDataReader r)
        {
            return new VendorRow
            {
                Id = r.GetInt32(0), EntityCode = r.GetString(1), Name = r.GetString(2), IsActive = r.GetBoolean(3),
                Gstin = S(r, 4), Pan = S(r, 5), ContactPerson = S(r, 6), Email = S(r, 7), Phone = S(r, 8), Address = S(r, 9),
                BankName = S(r, 10), AccountNo = S(r, 11), Ifsc = S(r, 12), IsMsme = r.GetBoolean(13), MsmeNo = S(r, 14),
                PaymentTermsDays = r.IsDBNull(15) ? (int?)null : r.GetInt32(15)
            };
        }

        // All vendors of an entity (active and inactive), for the Masters screen
        public static List<VendorRow> VendorList(string entity)
        {
            var list = new List<VendorRow>();
            using (var c = Open())
            using (var cmd = Cmd(c, "SELECT " + VendorCols + " FROM Vendors WHERE EntityCode=@e ORDER BY Name", null, "@e", entity))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) list.Add(ReadVendor(r));
            return list;
        }

        // True if another vendor in the same entity already has this name (case-insensitive)
        public static bool VendorNameExists(string entity, string name, int exceptId)
        {
            return (int)Scalar("SELECT COUNT(*) FROM Vendors WHERE EntityCode=@e AND Name=@n AND VendorId<>@i",
                "@e", entity, "@n", name.Trim(), "@i", exceptId) > 0;
        }

        // Insert (Id = 0) or update. Returns the vendor id. Writes an audit row in the same transaction.
        public static int VendorSave(VendorRow v)
        {
            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                object[] kv = {
                    "@e", v.EntityCode, "@n", v.Name.Trim(), "@a", v.IsActive,
                    "@gst", N(Nz(v.Gstin)), "@pan", N(Nz(v.Pan)), "@cp", N(Nz(v.ContactPerson)), "@em", N(Nz(v.Email)),
                    "@ph", N(Nz(v.Phone)), "@ad", N(Nz(v.Address)), "@bn", N(Nz(v.BankName)), "@ac", N(Nz(v.AccountNo)),
                    "@if", N(Nz(v.Ifsc)), "@ms", v.IsMsme, "@mn", N(v.IsMsme ? Nz(v.MsmeNo) : null),
                    "@pt", v.PaymentTermsDays.HasValue ? (object)v.PaymentTermsDays.Value : DBNull.Value };
                string action;
                if (v.Id == 0)
                {
                    v.Id = (int)Cmd(c, @"INSERT Vendors (EntityCode,Name,IsActive,Gstin,Pan,ContactPerson,Email,Phone,Address,BankName,AccountNo,Ifsc,IsMsme,MsmeNo,PaymentTermsDays)
                        OUTPUT INSERTED.VendorId VALUES (@e,@n,@a,@gst,@pan,@cp,@em,@ph,@ad,@bn,@ac,@if,@ms,@mn,@pt)", tx, kv).ExecuteScalar();
                    action = "Created";
                }
                else
                {
                    var all = new List<object>(kv); all.Add("@id"); all.Add(v.Id);
                    Cmd(c, @"UPDATE Vendors SET Name=@n,IsActive=@a,Gstin=@gst,Pan=@pan,ContactPerson=@cp,Email=@em,Phone=@ph,Address=@ad,
                        BankName=@bn,AccountNo=@ac,Ifsc=@if,IsMsme=@ms,MsmeNo=@mn,PaymentTermsDays=@pt WHERE VendorId=@id AND EntityCode=@e", tx, all.ToArray()).ExecuteNonQuery();
                    action = "Updated";
                }
                Audit("Vendor", v.Id.ToString(), action, v.Name + (v.IsActive ? "" : " (inactive)"), v.EntityCode, c, tx);
                tx.Commit();
                return v.Id;
            }
        }

        public static void VendorSetActive(int id, string entity, string name, bool active)
        {
            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                Cmd(c, "UPDATE Vendors SET IsActive=@a WHERE VendorId=@i AND EntityCode=@e", tx, "@a", active, "@i", id, "@e", entity).ExecuteNonQuery();
                Audit("Vendor", id.ToString(), active ? "Activated" : "Deactivated", name, entity, c, tx);
                tx.Commit();
            }
        }

        private static string Nz(string s) { s = (s ?? "").Trim(); return s.Length == 0 ? null : s; }

        // ---- audit trail: one row per important action. Never throws, so a logging failure can't break a save.
        public static void Audit(string module, string recordId, string action, string details = null, string entity = null, SqlConnection c = null, SqlTransaction tx = null)
        {
            try
            {
                const string sql = @"INSERT AuditLog (UserName,EntityCode,Module,RecordId,Action,Details) VALUES (@u,@e,@m,@r,@a,@d)";
                if (details != null && details.Length > 1000) details = details.Substring(0, 1000);
                object[] kv = { "@u", N(AppSession.UserName), "@e", N(entity), "@m", module, "@r", N(recordId), "@a", action, "@d", N(details) };
                if (c != null) Cmd(c, sql, tx, kv).ExecuteNonQuery();
                else using (var c2 = Open()) Cmd(c2, sql, null, kv).ExecuteNonQuery();
            }
            catch { /* swallow */ }
        }

        private static string Cut(string s, int max) { return s != null && s.Length > max ? s.Substring(0, max - 1) + "\u2026" : s; }

        // ---- notifications: a message for one user. module+recordId say which item to open when it is clicked.
        public static void Notify(string userName, string entity, string title, string message, string module = null, string recordId = null, SqlConnection c = null, SqlTransaction tx = null)
        {
            try
            {
                if (string.IsNullOrEmpty(userName)) return;
                const string sql = @"INSERT Notifications (UserName,EntityCode,Title,Message,Module,RecordId) VALUES (@u,@e,@t,@m,@mo,@r)";
                object[] kv = { "@u", userName, "@e", N(entity), "@t", Cut(title, 150), "@m", N(Cut(message, 500)), "@mo", N(module), "@r", N(recordId) };
                if (c != null) Cmd(c, sql, tx, kv).ExecuteNonQuery();
                else using (var c2 = Open()) Cmd(c2, sql, null, kv).ExecuteNonQuery();
            }
            catch { /* swallow */ }
        }

        // Notifies every active user who holds this role in this entity (skips the person who caused the event).
        public static void NotifyRole(string entity, string role, string title, string message, string module = null, string recordId = null, string exceptUser = null)
        {
            try
            {
                using (var c = Open())
                {
                    var users = new List<string>();
                    using (var rd = Cmd(c, @"SELECT a.UserName FROM UserEntityAccess a JOIN Users u ON u.UserName=a.UserName
                                             WHERE a.EntityCode=@e AND a.Role=@r AND u.IsActive=1", null, "@e", entity, "@r", role).ExecuteReader())
                        while (rd.Read()) users.Add(rd.GetString(0));
                    foreach (var u in users)
                        if (!string.Equals(u, exceptUser, StringComparison.OrdinalIgnoreCase))
                            Notify(u, entity, title, message, module, recordId, c, null);
                }
            }
            catch { /* swallow */ }
        }

        // ---- settings: key/value, global (entity = null) or per entity. Falls back to the global value, then to the default.
        public static string GetSetting(string key, string defaultValue = null, string entity = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(entity))
                {
                    object v = Scalar("SELECT SettingValue FROM Settings WHERE EntityCode=@e AND SettingKey=@k", "@e", entity, "@k", key);
                    if (v != null && v != DBNull.Value) return (string)v;
                }
                object g = Scalar("SELECT SettingValue FROM Settings WHERE EntityCode='' AND SettingKey=@k", "@k", key);
                return (g == null || g == DBNull.Value) ? defaultValue : (string)g;
            }
            catch { return defaultValue; }
        }

        public static void SetSetting(string key, string value, string entity = null)
        {
            using (var c = Open())
            {
                Cmd(c, @"MERGE Settings AS t USING (SELECT @e AS EntityCode, @k AS SettingKey) AS s
                         ON t.EntityCode=s.EntityCode AND t.SettingKey=s.SettingKey
                         WHEN MATCHED THEN UPDATE SET SettingValue=@v, UpdatedBy=@u, UpdatedAt=SYSDATETIME()
                         WHEN NOT MATCHED THEN INSERT (EntityCode,SettingKey,SettingValue,UpdatedBy) VALUES (@e,@k,@v,@u);", null,
                    "@e", entity ?? "", "@k", key, "@v", N(value), "@u", N(AppSession.UserName)).ExecuteNonQuery();
                Audit("Settings", key, "Changed", "Value=" + value, entity, c, null);
            }
        }
    }
}
