using System;
using System.Globalization;
using System.Linq;

namespace ProcurePay
{
    public class FieldDef
    {
        public string Prop, Label, Type, Fmt;   // Type: text | textarea | number | date | bool | select | vendor
        public bool Required, Full;
        public string[] Options;

        public FieldDef(string prop, string label, string type, bool required = false,
                        string[] options = null, bool full = false, string fmt = null)
        {
            Prop = prop; Label = label; Type = type; Required = required; Options = options; Full = full; Fmt = fmt;
        }
    }

    public class TabDef
    {
        public string Name;
        public FieldDef[] Fields;
        public TabDef(string name, params FieldDef[] fields) { Name = name; Fields = fields; }
    }

    // Same field list as the web version. The details screen (and, in Step 10, the form) is built from this.
    public static class AgreementSchema
    {
        public static readonly string[] Currencies = { "INR", "USD", "EUR", "GBP", "AED", "SGD" };

        public static readonly TabDef[] Tabs =
        {
            new TabDef("Classification",
                new FieldDef("Type", "Agreement type", "select", true, new[] { "Purchase Agreement", "Master Service Agreement", "Supply Agreement", "Annual Maintenance Contract", "Works Contract", "Rate Contract", "Framework Agreement", "Service Level Agreement" }),
                new FieldDef("Category", "Procurement category", "select", false, new[] { "Goods", "Services", "Works" }),
                new FieldDef("Title", "Title", "text", true, null, true),
                new FieldDef("Description", "Description / purpose", "textarea", false, null, true),
                new FieldDef("RefRfq", "Linked RFQ / tender / PR no.", "text"),
                new FieldDef("Revision", "Revision", "text")),

            new TabDef("Parties",
                new FieldDef("VendorId", "Supplier", "vendor", true),
                new FieldDef("ContactPerson", "Supplier contact person", "text"),
                new FieldDef("ContactEmail", "Contact email", "text"),
                new FieldDef("ContactPhone", "Contact phone", "text"),
                new FieldDef("MsmeRegistered", "MSME / Udyam registered", "bool"),
                new FieldDef("UdyamNo", "Udyam registration no.", "text")),

            new TabDef("Commercial",
                new FieldDef("Value", "Contract value", "number", true, null, false, "money"),
                new FieldDef("Currency", "Currency", "select", false, Currencies),
                new FieldDef("TaxTreatment", "Price is", "select", false, new[] { "Exclusive of GST", "Inclusive of GST" }),
                new FieldDef("GstRate", "GST rate", "select", false, new[] { "0", "5", "12", "18", "28" }, false, "pct"),
                new FieldDef("HsnSac", "HSN / SAC", "text"),
                new FieldDef("PriceBasis", "Price basis", "select", false, new[] { "Fixed price", "Rate contract", "Cost plus", "Variable / indexed" }),
                new FieldDef("Escalation", "Price escalation clause", "textarea", false, null, true)),

            new TabDef("Payment & Security",
                new FieldDef("PaymentTermsDays", "Credit period (days)", "number"),
                new FieldDef("PaymentMode", "Payment mode", "select", false, new[] { "", "NEFT / RTGS", "Cheque", "Letter of Credit", "Advance", "Milestone linked" }),
                new FieldDef("AdvancePct", "Advance %", "number", false, null, false, "pct"),
                new FieldDef("RetentionPct", "Retention %", "number", false, null, false, "pct"),
                new FieldDef("SecurityDeposit", "Security deposit / EMD", "number", false, null, false, "money"),
                new FieldDef("PbgAmount", "Performance BG", "number", false, null, false, "money"),
                new FieldDef("PbgValidity", "PBG valid till", "date"),
                new FieldDef("TdsApplicable", "TDS / withholding applicable", "bool"),
                new FieldDef("TdsSection", "TDS section", "select", false, new[] { "", "194C", "194J", "194I", "194Q", "194H", "Other" }),
                new FieldDef("TdsRate", "TDS / WHT rate %", "number", false, null, false, "pct"),
                new FieldDef("EarlyPayDiscount", "Early-payment discount", "text"),
                new FieldDef("LdClause", "Liquidated damages / penalty", "textarea", false, null, true)),

            new TabDef("Duration",
                new FieldDef("StartDate", "Effective date", "date"),
                new FieldDef("EndDate", "Expiry date", "date"),
                new FieldDef("AutoRenew", "Auto-renewal", "bool"),
                new FieldDef("RenewalNoticeDays", "Renewal notice (days)", "number"),
                new FieldDef("TerminationNoticeDays", "Termination notice (days)", "number")),

            new TabDef("Scope & Delivery",
                new FieldDef("Scope", "Scope of work / supply", "textarea", false, null, true),
                new FieldDef("Deliverables", "Key deliverables", "textarea", false, null, true),
                new FieldDef("DeliveryLocation", "Delivery / service location", "text", false, null, true),
                new FieldDef("DeliverySchedule", "Delivery schedule / lead time", "text", false, null, true),
                new FieldDef("Incoterms", "Incoterms (imports)", "select", false, new[] { "—", "EXW", "FOB", "CIF", "CIP", "DAP", "DDP" }),
                new FieldDef("QualityStandards", "Quality standards / specs", "text", false, null, true),
                new FieldDef("AcceptanceCriteria", "Acceptance criteria", "textarea", false, null, true),
                new FieldDef("WarrantyMonths", "Warranty (months)", "number")),

            new TabDef("Legal & Compliance",
                new FieldDef("GoverningLaw", "Governing law", "text"),
                new FieldDef("Jurisdiction", "Jurisdiction", "text"),
                new FieldDef("DisputeResolution", "Dispute resolution", "select", false, new[] { "Arbitration", "Courts", "Mediation then arbitration" }),
                new FieldDef("Confidentiality", "Confidentiality / NDA", "bool"),
                new FieldDef("Indemnity", "Indemnity clause", "bool"),
                new FieldDef("InsuranceReq", "Insurance requirement", "text", false, null, true),
                new FieldDef("ForceMajeure", "Force majeure clause", "bool"),
                new FieldDef("IprClause", "IPR / ownership clause", "bool")),

            new TabDef("Internal",
                new FieldDef("CostCenter", "Cost centre", "text"),
                new FieldDef("Project", "Project / job", "text"),
                new FieldDef("GlAccount", "GL / budget line", "text"),
                new FieldDef("RequestedBy", "Requested by / owner", "text"),
                new FieldDef("Terms", "Other key terms / remarks", "textarea", false, null, true))
        };

        // Should this field appear on the details screen?
        public static bool HasValue(FieldDef f, Agreement a)
        {
            string v = a.Get(f.Prop);
            if (f.Type == "bool") return v == "true";
            return !string.IsNullOrWhiteSpace(v);
        }

        // Human-readable value
        public static string Display(FieldDef f, Agreement a)
        {
            string v = a.Get(f.Prop);
            if (f.Type == "bool") return v == "true" ? "Yes" : "No";
            if (f.Type == "vendor") return string.IsNullOrWhiteSpace(a.Supplier) ? "—" : a.Supplier;
            if (string.IsNullOrWhiteSpace(v)) return "—";

            if (f.Fmt == "money")
            {
                decimal m;
                if (decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out m))
                    return Fmt.Money(m, a.Currency);
            }
            if (f.Fmt == "pct") return v + "%";
            if (f.Type == "date")
            {
                DateTime d;
                if (DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                    return d.ToString("dd MMM yyyy");
            }
            return v;
        }
    }
}