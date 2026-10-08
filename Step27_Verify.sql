-- Step 27 check: run in SSMS against the ProcurePay database after saving one invoice draft in the app.
SELECT name FROM sys.tables WHERE name = 'Invoices';                                   -- the table exists
SELECT TOP 10 BookingNo, VendorInvoiceNo, Status, Currency, ExchangeRate, Taxable, GstAmount, Total, TdsAmount, NetPayable, BaseTotal
FROM Invoices ORDER BY Id DESC;                                                          -- your drafts
SELECT TOP 10 At, UserName, Action, RecordId, Details FROM AuditLog WHERE Module = 'Invoice' ORDER BY Id DESC;   -- Created / Edited rows
-- The milestone must NOT change yet (booking is Step 28):
SELECT m.Id, m.SeqNo, m.Amount, m.InvoicedAmount FROM PoMilestones m JOIN Invoices i ON i.MilestoneId = m.Id;
