using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProcurePay
{
    // One problem found on the agreement form: which field, and what to tell the user.
    public class FormIssue
    {
        public string Prop, Message;
        public FormIssue(string prop, string message) { Prop = prop; Message = message; }
    }

    // All agreement-form rules in one place. Takes the raw text of every field (by property name),
    // tidies the numbers in place, and returns the list of problems. No screen code here.
    public static class AgreementValidator
    {
        private static readonly Regex EmailRx = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        private static readonly Regex PhoneRx = new Regex(@"^[0-9+\-()\s]{6,20}$");
        private static readonly Regex UdyamRx = new Regex(@"^UDYAM-[A-Z]{2}-\d{2}-\d{7}$", RegexOptions.IgnoreCase);
        private static readonly Regex HsnRx = new Regex(@"^\d{4}(\d{2})?(\d{2})?$");

        // Fields that must be whole numbers, with their allowed range
        private static readonly Dictionary<string, int[]> WholeNumbers = new Dictionary<string, int[]>
        {
            { "PaymentTermsDays", new[] { 0, 365 } },
            { "RenewalNoticeDays", new[] { 0, 365 } },
            { "TerminationNoticeDays", new[] { 0, 365 } },
            { "WarrantyMonths", new[] { 0, 240 } }
        };

        public static bool ParseNumber(string s, out decimal n)
        {
            return decimal.TryParse((s ?? "").Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out n);
        }

        private static bool ParseDate(string s, out DateTime d)
        {
            return DateTime.TryParseExact(s ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
        }

        private static decimal Num(Dictionary<string, string> v, string prop)
        {
            decimal n; return ParseNumber(v[prop], out n) ? n : 0;
        }

        public static List<FormIssue> Validate(Dictionary<string, string> v, string entityCode)
        {
            var issues = new List<FormIssue>();
            Action<string, string> add = (p, m) => issues.Add(new FormIssue(p, m));
            bool india = entityCode == "IN";
            Func<string, bool> has = p => v.ContainsKey(p) && v[p].Length > 0;

            // ---- Classification
            if (!has("Type")) add("Type", "Agreement type is required.");
            if (!has("Title")) add("Title", "Title is required.");
            else
            {
                if (v["Title"].Length < 3) add("Title", "Title is too short (at least 3 characters).");
                if (v["Title"].Length > 250) add("Title", "Title is too long (maximum 250 characters).");
            }

            // ---- Parties
            if (!has("VendorId")) add("VendorId", "Please choose an approved supplier.");
            if (has("ContactEmail") && !EmailRx.IsMatch(v["ContactEmail"])) add("ContactEmail", "Contact email is not a valid address.");
            if (has("ContactPhone") && !PhoneRx.IsMatch(v["ContactPhone"])) add("ContactPhone", "Contact phone should be 6-20 digits (+, -, spaces and brackets are fine).");
            if (has("MsmeRegistered"))
            {
                if (!has("UdyamNo")) add("UdyamNo", "Enter the Udyam registration number, or untick MSME.");
                else if (india && !UdyamRx.IsMatch(v["UdyamNo"])) add("UdyamNo", "Udyam number should look like UDYAM-GJ-01-1234567.");
            }

            // ---- numbers: tidy up and check the range
            foreach (var tab in AgreementSchema.Tabs)
                foreach (var f in tab.Fields.Where(x => x.Type == "number"))
                {
                    string s = v[f.Prop];
                    if (s.Length == 0) continue;
                    decimal n;
                    if (!ParseNumber(s, out n)) { add(f.Prop, f.Label + " must be a number."); continue; }
                    if (n < 0) { add(f.Prop, f.Label + " cannot be negative."); continue; }
                    if (f.Fmt == "pct" && n > 100) { add(f.Prop, f.Label + " must be between 0 and 100."); continue; }
                    int[] range;
                    if (WholeNumbers.TryGetValue(f.Prop, out range))
                    {
                        if (n != Math.Floor(n)) { add(f.Prop, f.Label + " must be a whole number."); continue; }
                        if (n < range[0] || n > range[1]) { add(f.Prop, f.Label + " must be between " + range[0] + " and " + range[1] + "."); continue; }
                    }
                    v[f.Prop] = n.ToString("0.##", CultureInfo.InvariantCulture);
                }

            // ---- Commercial
            decimal value;
            if (!has("Value") || !ParseNumber(v["Value"], out value) || value <= 0)
            {
                if (!issues.Any(x => x.Prop == "Value")) add("Value", "Contract value must be greater than zero.");
                value = 0;
            }
            if (!has("Currency")) add("Currency", "Currency is required.");
            if (india)
            {
                if (!has("GstRate")) add("GstRate", "Select a GST rate (choose 0 if GST does not apply).");
                if (has("HsnSac") && !HsnRx.IsMatch(v["HsnSac"])) add("HsnSac", "HSN / SAC must be 4, 6 or 8 digits.");
            }

            // ---- Payment & Security
            if (Num(v, "AdvancePct") + Num(v, "RetentionPct") > 100)
                add("RetentionPct", "Advance % and Retention % together cannot exceed 100.");
            if (value > 0)
            {
                if (Num(v, "SecurityDeposit") > value) add("SecurityDeposit", "Security deposit cannot be more than the contract value.");
                if (Num(v, "PbgAmount") > value) add("PbgAmount", "Performance BG cannot be more than the contract value.");
            }
            if (Num(v, "PbgAmount") > 0 && !has("PbgValidity")) add("PbgValidity", "Enter the date the Performance BG is valid till.");
            if (has("TdsApplicable"))
            {
                if (india && !has("TdsSection")) add("TdsSection", "Select the TDS section, or untick TDS applicable.");
                if (!has("TdsRate") || Num(v, "TdsRate") <= 0) add("TdsRate", "Enter the TDS / withholding rate, or untick TDS applicable.");
            }

            // ---- Duration
            DateTime start = DateTime.MinValue, end = DateTime.MinValue, pbg;
            bool hasStart = has("StartDate") && ParseDate(v["StartDate"], out start);
            bool hasEnd = has("EndDate") && ParseDate(v["EndDate"], out end);
            if (has("StartDate") && !hasStart) add("StartDate", "Effective date is not a valid date.");
            if (has("EndDate") && !hasEnd) add("EndDate", "Expiry date is not a valid date.");
            if (hasStart && hasEnd && end < start) add("EndDate", "Expiry date cannot be before the effective date.");
            if (has("AutoRenew") && !hasEnd) add("EndDate", "Auto-renewal needs an expiry date.");
            if (hasStart && has("PbgValidity") && ParseDate(v["PbgValidity"], out pbg) && pbg < start)
                add("PbgValidity", "Performance BG validity cannot be before the effective date.");

            return issues;
        }
    }
}
