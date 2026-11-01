# CODE_REVIEW_AUDIT_REPORT.md
**Accountant_sys — End-to-End Code Review & Vulnerability Assessment**
**Scope:** Full solution (`DAL`, `BLL`, `Accountant_sys` MVC web layer, `Accountant_sys.Tests`)
**Stack:** .NET 10 · ASP.NET Core MVC · EF Core · SQL Server · Bootstrap 5.3 RTL
**Reviewer:** Lead Software Architect / Principal Security & Financial Systems Auditor
**Date:** 2026-11-01

---

## 1. Executive Summary

The codebase is a well-structured three-layer accounting system with a genuinely good ledger architecture: **StockMovements as an immutable stock ledger**, entity status machines (Draft → Posted → Cancelled), type-separated customer/maritime accounting worlds, and a dry-run test suite covering the core financial formula. This is above-average for a small-team ERP.

However, the audit found **serious gaps between the invariants the code claims in comments and what the code actually enforces**:

- **The claimed "Serializable transaction" protection does not exist** — `UnitOfWork.BeginTransactionAsync()` uses the database default isolation level, and the payment/allocation **validations run outside** their transactions, leaving a classic double-submission over-allocation race.
- **Every daily cash total is computed in the wrong clock** — `CreatedAt` is UTC but every boundary passed to it is server-local (`DateTime.Today` / `BusinessDate`), so the Daily Closing "Expected drawer cash" silently drifts every afternoon/evening in a UTC+2/+3 deployment (Egypt).
- **Closed days are not actually closed** — no mechanism blocks payments or cash movements tied to an already-closed business date; the closing snapshot is not the financial anchor it claims to be.
- **8 high-frequency screens load the entire ledger into memory** to compute balances one item at a time (severe N+1 patterns in Dashboard, Invoice Index, Credit Note Index, Customer/Vendor lists, account statements).
- A handful of **crash paths (`NullReferenceException` by design)** turn "record not found" business errors into production 500s.

**Readiness score: 6.2 / 10** — solid architecture and UX polish, but not production-ready for financial data until the transaction/isolation, UTC-boundary, closed-day, and crash-path items below are merged.

**Overall risk posture:** 🟠 **Elevated**. No external-attack surface catastrophe (global `[Authorize]`, full anti-forgery coverage on every POST, no raw SQL), but the internal financial-integrity risks are real and will bite within weeks of live operation.

---

## 2. Severity Matrix

| Severity | Count | Definition |
|:---:|:---:|---|
| 🔴 **Critical** | **4** | Direct financial loss, data corruption, wrong ledger totals, production crashes |
| 🟠 **High** | **6** | Business-rule bypass, statement/report corruption, severe data omission |
| 🟡 **Medium** | **6** | Edge-case validation flaws, date-boundary quirks, performance bottlenecks |
| 🟢 **Low / Architectural** | **7** | Code smells, maintainability, minor styling inconsistencies |
| **Total findings** | **23** | |

---

## 3. Deep-Dive Vulnerability & Bug Analysis (Grouped by Module)

---

### Module A — Payments & Settlements (BLL)

---

#### [CRIT-01] Over-Allocation Race Condition in Concurrent Payment Submission
- **Affected files:**
  - `BLL/Service/PaymentService.cs:67-97` (validation loop → transaction start)
  - `BLL/Service/PaymentService.cs:167-190` (vendor path, identical flaw)
- **Root cause:** All settlement rejections (invoice ownership, status, per-invoice remaining balance, customer total balance) run **before** `BeginTransactionAsync()`. Inside the transaction, the allocations are written **without re-validating** against the ledger. There is no re-read, no row-level lock, and no gate.
- **Failure scenario (step-by-step):**
  1. Cashier A opens the payment screen; the client fetches invoice `SAL-0010` remaining = 5,000 EGP.
  2. Cashier B opens the same screen at the same moment; both see remaining = 5,000.
  3. A submits 5,000 → validation passes → transaction writes allocation → commit.
  4. B submits 5,000 *before A's commit is visible in B's connection* → B's pre-transaction validation reads remaining = 5,000 (uncommitted A invisible) → validation passes.
  5. B's transaction commits. Invoice `SAL-0010` now holds **10,000 of allocations against a 5,000 invoice**; `ComputeSettlement` returns `-5,000` remaining; `DerivePaymentStatus` marks it "Paid", and the customer overdraws their account.
- **Business/financial impact:** Customer outstanding debt understated (revenue leakage), negative per-invoice remaining balances referenced on printed receipts and statements, cash drawer receives two identical receipts that both "look" valid.
- **Vulnerable code:**
  ```csharp
  // PaymentService.cs:89-97 — check happens OUTSIDE the transaction:
  var invoiceRemaining = await _invoiceService.GetInvoiceRemainingBalanceAsync(allocation.InvoiceId);
  if (allocation.AllocatedAmount > invoiceRemaining)
      throw new InvalidOperationException(...);

  await _unitOfWork.BeginTransactionAsync();   // transaction starts AFTER validation
  try
  {
      var cashTx = new CashTransaction { ... };
      await _unitOfWork.CashTransactionRepo.AddAsync(cashTx);
      ...
  }
  ```
- **Remediation (drop-in):** Move validation inside the transaction and re-read under isolation; for the whole `RecordCustomerPaymentAsync`/`RecordVendorPaymentAsync` flow use the same protective pattern as maritime posting:
  ```csharp
  await StockConcurrencyGate.Gate.WaitAsync();       // serialize settlement writes
  try
  {
      await _unitOfWork.BeginTransactionAsync();
      await _unitOfWork.InvoiceRepo.BeginSerializableTransactionAsync(); // or set effective isolation
      try
      {
          // ---- RUN ALL CHECKS HERE, INSIDE THE TRANSACTION ----
          var customerTotalBalance = await _unitOfWork.CustomerRepo.GetCustomerBalanceAsync(dto.CustomerId);
          if (dto.Amount > customerTotalBalance) throw new InvalidOperationException(...);

          foreach (var allocation in dto.Allocations)
          {
              var invoice = await _unitOfWork.InvoiceRepo.GetByIdAsync(allocation.InvoiceId);
              if (invoice == null) throw new InvalidOperationException(...);
              if (invoice.CustomerId != dto.CustomerId) throw new InvalidOperationException(...);
              if (invoice.Status != InvoiceStatus.Posted) throw new InvalidOperationException(...);

              // RE-READ remaining INSIDE the transaction:
              var tracked = await _unitOfWork.InvoiceRepo.GetInvoiceWithAllocationsAndCreditNotesAsync(invoice.Id);
              var (_, remaining) = _invoiceService.ComputeSettlementPublic(tracked);
              if (allocation.AllocatedAmount > remaining)
                  throw new InvalidOperationException(...);
          }

          await _unitOfWork.SaveChangesAsync();
          await _unitOfWork.CommitTransactionAsync();
          return cashTx.Id;
      }
      catch { await _unitOfWork.RollbackTransactionAsync(); throw; }
  }
  finally { StockConcurrencyGate.Gate.Release(); }
  ```
  Minimal version (lowest-risk hotfix): keep the pre-transaction validation for friendly error messages, but **duplicate the per-invoice remaining check inside the transaction** before `AddRangeAsync`. Additionally add the defensive DB guard:
  ```sql
  -- MIGRATION: reject over-allocation at the database itself
  CREATE OR ALTER FUNCTION dbo.InvoiceAllocatedTotal(@InvoiceId INT) RETURNS DECIMAL(18,2) ...
  -- or enforce in code via an UPDATE with WHERE remaining >= @amount on the invoice row.
  ```

---

#### [CRIT-02] Daily Cash Totals Computed in UTC Against Local Date Boundaries (Closing Ledger Drift)
- **Affected files:**
  - `DAL/Repos/CashTransactionRepo.cs:84-98` (`GetTotalCashInAsync` / `GetTotalCashOutAsync` — file these totals against `CreatedAt`)
  - `BLL/Service/DailyClosingService.cs:47-52` (closing preview/hard close pass `DateTime` **local** dates)
  - `BLL/Service/DashboardService.cs:85-108` (drawer cash + "today" counts)
  - `BLL/Service/CashTransactionService.cs:84-91` (daily summary)
- **Root cause:** `CashTransaction.CreatedAt` defaults to `DateTime.UtcNow` (`DAL/Entities/CashTransaction.cs:45`), but every query boundaries passed are **server-local** (`DateTime.Today`, `dto.BusinessDate.Date`, `date.Date`). On a UTC+2/+3 server (Egypt), every cash transaction created between **00:00 and 03:00 local** is stamped the **previous** UTC day, and every transaction created after **21:00/22:00 local** is stamped the **next** UTC day.
- **Failure scenario:**
  1. It is 22:30 local (20:30 UTC); cashier collects a 5,000 EGP settlement.
  2. `CreatedAt` = today-local **minus one day, 20:30 UTC** (yesterday's UTC day).
  3. The day clerk runs `PerformDailyClosingAsync` for today-local. The 5,000-row is filtered **out of today** (`CreatedAt >= today 00:00` fails) and into yesterday's bucket.
  4. Today's `ExpectedClosingCashAmount` understates the drawer cash by 5,000 → phantom "عجز بالخزينة" is recorded and locked.
- **Business/financial impact:** Locked daily-closing records permanently disagree with reality; each discrepancy triggers a false cashier-custody outage or conceals actual theft because the reference number itself drifts. Reconciliations become impossible to port.
- **Vulnerable code:**
  ```csharp
  // CashTransactionRepo.cs:86 — CreatedAt is UTC; fromDate/toDate are LOCAL:
  var totalInCash = await _context.CashTransactions
      .Where(i => i.CreatedAt >= fromDate && i.CreatedAt <= toDate)
      .Where(d => d.Direction == CashDirection.In)
      .Select(t => t.Amount).SumAsync();

  // DailyClosingService.cs:47-48 — LOCAL business date fed into the UTC filter:
  var moneyIn = await _unitOfWork.CashTransactionRepo.GetTotalCashInAsync(
      dto.BusinessDate.Date, dto.BusinessDate.AddDays(1).Date);
  ```
- **Remediation (drop-in):** Sum by the declared **business date** field, not the audit timestamp — that is what `TransactionDate` exists for:
  ```csharp
  // CashTransactionRepo.cs — filter on TransactionDate (already stored local from form inputs):
  public async Task<decimal> GetTotalCashInAsync(DateTime fromDate, DateTime toDateExclusive)
  {
      return await _context.CashTransactions
          .Where(i => i.TransactionDate >= fromDate && i.TransactionDate < toDateExclusive)
          .Where(d => d.Direction == CashDirection.In)
          .Select(t => t.Amount)
          .SumAsync();
  }
  public async Task<decimal> GetTotalCashOutAsync(DateTime fromDate, DateTime toDateExclusive)
      => ... same with Direction == CashDirection.Out ...;
  ```
  Then in `DailyClosingService.PerformDailyClosingAsync` (lines 47-52) and `CashTransactionService.GetDailyCashSummaryAsync` (lines 87-88) and `DashboardService` (lines 86-89) switch every call to the **half-open interval** `[from, from+1day)` signature shown above. Add the missing index:
  ```sql
  -- Migration: CREATE INDEX IX_CashTransactions_TransactionDate_Direction
  --             ON CashTransactions (TransactionDate, Direction) INCLUDE (Amount);
  ```
  `Audit trail` `CreatedAt` remains untouched.

---

#### [CRIT-03] Closed Business Days Are Not Immutable — Post-Close Ledger Mutations Are Unblocked
- **Affected files:**
  - `BLL/Service/PaymentService.cs:28-133` (no closed-day check on `dto.PaymentDate`)
  - `BLL/Service/CashTransactionService.cs:25-67` (no check on `dto.TransactionDate`)
  - `BLL/Service/CreditNoteService.cs:77-162` (cash refunds stamped `DateTime.UtcNow` with no closed-day check)
  - `BLL/Service/DailyClosingService.cs:43-82` (nothing prevents updating a reopened row's derived numbers with a business date older than the current open shift)
- **Root cause:** The system defines `IsDateClosedAsync` but **never calls it** before accepting a customer payment, vendor payment, or any cash-category movement. A specific trap: `PaymentDate`/`TransactionDate` come straight from the client DTO with zero validation (no future-date check either).
- **Failure scenario:**
  1. Day **2026-10-30** is closed and locked with `DifferenceAmount = 0` (auditor-clean).
  2. On **2026-10-31**, a user registers a payment for the closed day 2026-10-30 (the date field is freely editable, or the client auto-fills a stale date after midnight rollover).
  3. The transaction is accepted, the customer's balance and voucher register change, and drawer totals grab 2026-10-30 into the ledger engine — **but the reported closing of 2026-10-30 still shows the old snapshot**. The two official reports of the same day now disagree, silently.
- **Business/financial impact:** Locked-day audits become falsifiable; any dispute resolution ("we closed at 5,000, you show 5,500") cannot be proven — the exact situation the DailyClosing feature exists to prevent — plus treasurer-retention ambiguity: "who changed Ours/the numbers?" cannot be answered.
- **Vulnerable code:**
  ```csharp
  // PaymentService.RecordCustomerPaymentAsync:49-65 — party checks exist,
  // exist but the DATE validation boundary is entirely missing:
  var customer = await _unitOfWork.CustomerRepo.GetByIdAsync(dto.CustomerId);
  if (customer == null || !customer.IsActive)
      throw new InvalidOperationException("العميل غير موجود أو حسابه معطل.");
  // ... proceeds straight to BeginTransactionAsync, never checks IsDateClosedAsync(dto.PaymentDate)
  ```
- **Remediation (drop-in):** Add a shared guard helper and call it at the top of every cash-writing flow:
  ```csharp
  // New: BLL/Service/CashLedgerGuard.cs
  public static class CashLedgerGuard
  {
      public static async Task EnsureCashDateOpenAsync(IUnitOfWork uow, DateTime businessDate)
      {
          var dateOnly = businessDate.Date;
          if (dateOnly > DateTime.Today)
              throw new InvalidOperationException("لا يمكن تسجيل حركة نقدية بتاريخ مستقبلي.");
          if (await uow.DailyClosingRepo.IsDateClosedAsync(dateOnly))
              throw new InvalidOperationException(
                  $"ليوم {dateOnly:yyyy-MM-dd} يومية مقفلة ومُعتمدة. أعد فتح اليومية أولاً قبل تسجيل حركة بتاريخ مغلق.");
          // Reasonably-recent-day only (backdated flood guard):
          if (dateOnly < DateTime.Today.AddDays(-90))
              throw new InvalidOperationException("لا يمكن تسجيل حركة نقدية بتاريخ سحيق (أعيد فتح اليومية أولاً).");
      }
  }
  ```
  Then call from: `RecordCustomerPaymentAsync`, `RecordVendorPaymentAsync`, `RecordCashMovementAsync` (once exposed), and `PostCreditNoteAsync` for the refund branch (`if dto.IsCashRefund { await CashLedgerGuard.EnsureCashDateOpenAsync(_unitOfWork, DateTime.Today); }`).

---

#### [CRIT-04] "Record Not Found" Is Implemented As A Thrown `NullReferenceException` — Production 500s & Dead Branches
- **Affected files:**
  - `DAL/Repos/GenericRepo.cs:44-54` (the source: throws on missing entity)
  - Full blast radius (every encountering UI flow): `BLL/Service/PaymentService.cs:72-77` & `:172-177` (dead null-branches), `BLL/Service/InvoiceService.cs:116-117` & `:234-235` (dead branches → NRE 500), `BLL/Service/CreditNoteService.cs:129-131` (typo hot-path), `BLL/Service/ExpenseCategoryService.cs:54-56` + `:64-65` (raw NRE on edit/toggle), `BLL/Service/DailyClosingService.cs:86-88`, `BLL/Service/OpeningBalanceService.cs:28-29` (nullable `.Value` on possibly-null `dto.CustomerId`), `BLL/Service/CashTransactionService.cs:27` (`ArgumentNullException` misused for null DTO).
- **Root cause:** `GenericRepo.GetByIdAsync` is declared to *reject* nulls (item == null → `?? throw new NullReferenceException()`), while **every single caller** written against it expects the classic `if (x == null) return NotFound()` pattern — a contract mismatch across ~20 call sites.
- **Failure scenario:**
  1. User submits `/Invoice/AddItem` for an invoice whose `ProductId` no longer exists (or any forged/rotated ID via a bad link).
  2. `_productService.GetProductByIdAsync(dto.ProductId)` throws `NullReferenceException` from the repo instead of returning null.
  3. `InvoiceController.AddItem` (line 151-158) catches only `InvalidOperationException` → unhandled exception → **HTTP 500**, user loses the entire page state including form data.
- **Business/financial impact:** Any mistyped/stale link, drift in concurrent soft-delete, or testing attempt becomes a hard production crash page; additional operations silently skip null-handling branches that can never run, hiding intent from future maintainers.
- **Vulnerable code:**
  ```csharp
  // GenericRepo.cs:50-54
  public async Task<T> GetByIdAsync(int id)
  {
      var item = await _dbSet.FindAsync(id) ?? throw new NullReferenceException();
      return item;
  }

  // Dead branch written by the caller — can never execute:
  // InvoiceService.cs:116-117
  var product = await _productService.GetProductByIdAsync(dto.ProductId);
  if (product == null) return false;   // NEVER REACHED — the repo already threw
  ```
- **Remediation (drop-in):** Make the repository return null (matching ALL callers) and introduce an explicit throwing variant:
  ```csharp
  // GenericRepo.cs — corrected contract:
  public async Task<T?> GetByIdAsync(int id)
  {
      return await _dbSet.FindAsync(id);           // null when not found — caller decides
  }

  public async Task<T> GetByIdRequiredAsync(int id, string entityName = "العنصر")
  {
      var item = await _dbSet.FindAsync(id)
          ?? throw new InvalidOperationException($"{entityName} رقم ({id}) غير موجود بالنظام.");
      return item;
  }
  ```
  Also remove the misleading exceptions in `ExpenseCategoryService.UpdateCategoryAsync` / `ToggleCategoryStatusAsync` (lines 52-58, 62-68) by null-checking `existCat` and returning `false`; and in `OpeningBalanceService` (lines 24-30, 40-45) add `dto.CustomerId.HasValue` / `dto.VendorId.HasValue` guards before touching `.Value`.

---

### Module B — Invoices, Credit Notes & Stock (BLL)

---

#### [HIGH-01] Credit-Note Return Quantity Enforced Only At Draft Creation — Posting Skips Re-Validation (TOCTOU Over-Return)
- **Affected files:**
  - `BLL/Service/CreditNoteService.cs:33-59` (validation happens once — at draft creation)
  - `BLL/Service/CreditNoteService.cs:77-162` (`PostCreditNoteAsync` never re-checks returnable quantity, refund cash, or invoice not-cancelled)
- **Root cause:** `CreateDraftCreditNoteAsync` validates the requested quantity against *already-posted* returns (`GetTotalReturnedQtyForInvoiceItemAsync`). `PostCreditNoteAsync` trusts the draft written earlier. Between draft creation and posting, **another draft for the same product can be created and posted**, and nothing catches the double-count.
- **Failure scenario:**
  1. Invoice has 100 kg of fish X, nothing returned yet.
  2. User creates draft CN-001 for 60 kg (passes: max 100).
  3. User immediately creates draft CN-002 for 60 kg (passes: still 0 posted).
  4. User posts CN-001 (customers' debit −60). Then posts CN-002 → **120 kg returned against a 100-kg invoice**, refund cash of 120×price leaves the drawer, and maritime-stock In-movement re-injects 20 kg that never existed.
- **Business/financial impact:** Over-refunded customers and a synthetic freezer surplus; also the drawer can be drained more than the invoice ever received (see HIGH-03).
- **Vulnerable code:**
  ```csharp
  // CreditNoteService.cs:81-83 — posting validates ONLY the draft status:
  var creditNote = await _unitOfWork.CreditNoteRepo.GetByIdWithItemsAsync(dto.CreditNoteId);
  if (creditNote == null) throw new NullReferenceException(...);
  if (creditNote.Status != CreditNoteStatus.Draft) throw new InvalidOperationException(...);
  // ✗ No re-read of GetTotalReturnedQtyForInvoiceItemAsync面对 the new state.
  ```
- **Remediation (drop-in):** Re-validate quantities inside `PostCreditNoteAsync` right after the status checks, reusing the same repo helper:
  ```csharp
  // Inside PostCreditNoteAsync, after status validation:
  var alreadyReturned = await _unitOfWork.CreditNoteRepo.GetTotalReturnedQtyForInvoiceItemAsync(creditNote.OriginalInvoiceId);
  foreach (var group in creditNote.Items.GroupBy(i => i.ProductId))
  {
      alreadyReturned.TryGetValue(group.Key, out var previous);
      var originalQty = checkInvoice.Items.Where(ii => ii.ProductId == group.Key).Sum(ii => ii.Quantity);
      if (previous + group.Sum(i => i.Quantity) > originalQty)
          throw new InvalidOperationException(
              $"ترحيل الإشعار سيجعل الكمية المرتجعة من الصنف ({group.First().ProductName}) تتجاوز كمية الفاتورة الأصلية.");
  }
  ```
  Also reject posting onto a **cancelled** original invoice: `if (checkInvoice.Status != InvoiceStatus.Posted) throw ...`.

---

#### [HIGH-02] Account Statement "Beginning Balance" Corrupted by Out-of-Period Running Balances
- **Affected files:** `Accountant_sys/Models/StatementBuilder.cs:44-54`
- **Root cause:** `Build()` iterates the **entire sorted ledger** and assigns `model.BeginningBalance = line.RunningBalance` for **every** out-of-period line — including lines that come **after** the requested period. The last (most recent) out-of-period line overwrites the beginning balance with today's final account balance.
- **Failure scenario:**
  1. Customer statement requested for January 2015 (from = 2015-01-01, to = 2015-01-31).
  2. All 2015-Present lines run first; each out-of-period line sets `BeginningBalance = running`.
  3. The final out-of-period line (today) sets BeginningBalance = **current account balance**.
  4. The printed statement header shows "رصيد أول المدة" = 2026 balance and the tafqeet line "فقط وقدره" recites a wildly wrong amount on a legally-referenced A4 document.
- **Business/financial impact:** Customer/vendor/partner statements — official printed documents already carrying a "final balance" disclaimer — become evidence-grade liabilities.
- **Vulnerable code:**
  ```csharp
  // StatementBuilder.cs:44-54
  foreach (var line in sorted)
  {
      var inPeriod = (!fromDate.HasValue || line.Date >= fromDate) &&
                     (!toDate.HasValue || line.Date < toDate.Value.AddDays(1));
      if (!inPeriod)
      {
          model.BeginningBalance = line.RunningBalance;  // ✗ overwrites even for lines AFTER 'to'
          continue;
      }
      ...
  }
  ```
- **Remediation (drop-in):** Guard the direction:
  ```csharp
  foreach (var line in sorted)
  {
      var inPeriod = (!fromDate.HasValue || line.Date >= fromDate) &&
                     (!toDate.HasValue || line.Date < toDate.Value.AddDays(1));
      if (inPeriod)
      {
          model.Lines.Add(line);
          if (line.DocumentType.StartsWith("فاتورة")) model.TotalInvoices += isVendor ? line.Credit : line.Debit;
          else if (line.DocumentType.StartsWith("إشعار")) model.TotalReturns += isVendor ? line.Debit : line.Credit;
          else if (line.DocumentType.StartsWith("سند")) model.TotalPayments += isVendor ? line.Debit : line.Credit;
      }
      else if (fromDate.HasValue && line.Date < fromDate.Value)
      {
          model.BeginningBalance = line.RunningBalance;   // ONLY pre-period lines seed the opening balance
      }
  }
  ```

---

#### [HIGH-03] Sequential Document Numbering Is Race-Prone; Maritime Check Loads the Entire Invoice Table
- **Affected files:**
  - `DAL/Repos/InvoiceRepo.cs:62-87` (`GetNextInvoiceNumberAsync` — MAX+1 pattern, memory scan of all prefixed rows)
  - `DAL/Repos/CreditNoteRepo.cs:48-65` (`GetNextCreditNoteNumberAsync` — last-by-Id, skips gaps, breaks under delete/out-of-order Ids)
  - `BLL/Service/InvoiceService.cs:76-82` (duplicate external-order check loads **every** invoice row into memory on every maritime draft)
- **Root cause:** Number generators read-then-write the max without any lock; the unique indexes (`IX_Invoices_InvoiceNumber`, `IX_CreditNotes_CreditNoteNumber`) turn the race into an `SqlException`500 instead of a retry. The maritime duplicate-order guard uses `GetAllAsync().Any(...)` — a full-table tracked read.
- **Failure scenario:** Two cashiers both have a draft open; both hit "ترحيل" within the same second (the gate does NOT protect these ~95% of invoices, only maritime). Invoice `SAL-0125` is issued twice → one of the updates aborts with a raw `DbUpdateException` (unhandled → white 500). Meanwhile credit note numbering via `OrderByDescending(c => c.Id).Take(1)` misbehaves if any row was deleted/created out of order.
- **Business/financial impact:** 500 error storms during busy shifts; gaps and collisions question auditability of the "sequential invoice number" premise in tax inspection documentation.
- **Vulnerable code:**
  ```csharp
  // InvoiceService.cs:76-77 — full table into memory:
  var duplicateExternal = await _unitOfWork.InvoiceRepo.GetAllAsync();
  var duplicated = duplicateExternal.Any(i => i.Type == InvoiceType.MaritimeSupply && ...);

  // CreditNoteRepo.cs:51-54 — "last one by Id" is not "max":
  var lastNumber = await _context.CreditNotes
      .OrderByDescending(c => c.Id)
      .Select(c => c.CreditNoteNumber)
      .FirstOrDefaultAsync();
  ```
- **Remediation (drop-in):** Replace with a single-row locking read (SQL Server MIN serializable read for correctness), and fix the maritime duplicate check to a server-side predicate:
  ```csharp
  // InvoiceRepo.cs — atomic next number:
  public async Task<string> GetNextInvoiceNumberAsync(InvoiceType type)
  {
      var prefix = type switch { ... };  // unchanged
      var maxSeq = await _context.Invoices
          .FromSqlInterpolated(
              $"SELECT ISNULL(MAX(CAST(RIGHT([InvoiceNumber], 4) AS INT)), 0) FROM [Invoices] WITH (PAGLOCK, REPEATABLE READ) WHERE [InvoiceNumber] LIKE {prefix + '%'}")
          .FirstAsync();
      return $"{prefix}{(maxSeq + 1):D4}";
  }

  // InvoiceService.cs:76 — replace full-table load with a server-side EXISTS:
  var duplicated = (await _unitOfWork.InvoiceRepo.GetAllAsync()) is { } all
      ? false
      : false;
  // → should become (new repo method):
  public async Task<bool> ExternalOrderNumberExistsAsync(string externalOrderNumber) =>
      await _context.Invoices.AnyAsync(i =>
          i.Type == InvoiceType.MaritimeSupply &&
          i.Status != InvoiceStatus.Cancelled &&
          i.ExternalOrderNumber == externalOrderNumber);
  ```
  For credit notes use the same MAX-pattern as invoices (numeric parse over `LIKE 'CN-%'`).

---

#### [HIGH-04] Item-Line Discount Has No Cap — Invoices Can Be Posted With Negative Totals
- **Affected files:**
  - `BLL/Service/InvoiceService.cs:148` (add: `LineTotal = (Quantity × UnitPrice) − LineDiscountAmount`, no cap)
  - `BLL/Service/InvoiceService.cs:164-167` (`UpdateDraftItemAsync` — same, no cap)
  - `DAL/Entities/Invoice.cs` & `DAL/AppDbContext/AppDbcontext.cs:90-93` — **no `CHECK (TotalAmount >= 0)` constraint** exists for `Invoice.SubTotal/TotalAmount`.
- **Root cause:** The DTO validation (`[Range(0, double.MaxValue)]` on `LineDiscountAmount`) forbids negatives but not **discounts larger than the line total**. A forged or fat-fingered line like `Quantity = 1, UnitPrice = 100, LineDiscountAmount = 999` posts an invoice with `TotalAmount = -899` → customer liability **credit** of 899 EGP, and `DerivePaymentStatus` then classifies it bizarrely (`remaining >= totalAmount` logic badly inverts for negative totals).
- **Business/financial impact:** Recorded negative receivables; a paid-in-full message presented for a non-existent refund; per-invoice allocations can even exceed their legitimately larger totals.
- **Vulnerable code:**
  ```csharp
  // InvoiceService.cs:145-148 — add; there is no upper bound on the discount:
  var item = _mapper.Map<InvoiceItem>(dto);
  item.ProductName = product.Name;
  item.UnitPrice = unitPrice.Value;
  item.LineTotal = (item.Quantity * item.UnitPrice) - item.LineDiscountAmount;
  ```
- **Remediation (drop-in):** Cap the discount at line level and clamp invoice-level, in both `AddItemToDraftAsync` and `UpdateDraftItemAsync`:
  ```csharp
  var grossLine = item.Quantity * item.UnitPrice;
  if (item.LineDiscountAmount > grossLine)
      throw new InvalidOperationException(
          $"خصم البند ({item.LineDiscountAmount:N2}) يتجاوز قيمة البند الإجمالية ({grossLine:N2}) لصنف ({product.Name}).");
  item.LineTotal = grossLine - item.LineDiscountAmount;
  ```
  ...and mirror it as a migration:
  ```sql
  ALTER TABLE InvoiceItems ADD CONSTRAINT CK_InvoiceItems_DiscountBounded
      CHECK ([LineDiscountAmount] >= 0 AND [LineDiscountAmount] <= [Quantity] * [UnitPrice]);
  ALTER TABLE Invoices    ADD CONSTRAINT CK_Invoices_TotalNonNegative
      CHECK ([TotalAmount] >= 0);
  ```

---

#### [HIGH-05] Cash-Flow Suicide: Retail Sales & Expense Movements Permanently Unreachable; Retail Invoice Posts Zero Drawer Income
- **Affected files:**
  - `Accountant_sys/Program.cs` + all 13 controllers — **no controller action anywhere** invokes `ICashTransactionService.RecordCashMovementAsync` (verified by search).
  - `BLL/Service/InvoiceService.cs:186-209` (`PostInvoiceAsync` — posting `InvoiceType.StoreCashSales` writes the invoice & stock ledger but **no `CashTransaction`**, so a full retail cash sale is invisible to `CurrentDrawerCash`, daily closing, and `TotalCashIn`).
  - `BLL/Service/CashTransactionService.cs:29-61` (even if exposed, `Category.CustomerPayment/VendorPayment` are allowed through the manual cash path without allocations — a bypass of the settlement ledger).
- **Root cause / Failure scenario:** The cash-tile on the dashboard, daily closing "Cash In", and every drawer reconciliation silently **omit all retail income** and **all operating expenses** because (a) the manual-cash UI doesn't exist, and (b) retail invoice posting doesn't create a cash transaction. A day where the store sells 50,000 EGP cash but expenses 10,000 will close showing ~zero net drawer activity.
- **Business/financial impact:** Every drawer reconciliation of mixed trade days fails; alleged "عجز" documents are generated against false accounting floors; expenses are completely untracked (heat/handled cash from `ExpenseCategory` table is populated but never spent against in any UI).
- **Vulnerable code / evidence:**
  ```csharp
  // CashTransactionService.cs:34-41 — manual customer payments WITHOUT allocations accepted:
  else if (dto.Category == CashCategory.CustomerPayment) { dto.Direction = CashDirection.In; }
  // ✗ No CustomerId? No allocation? Accepted anyway → drawer inflates; settlement math untouched.

  // The expense path that every reconciliation depends on, but no controller can reach:
  // (verified: zero references of RecordCashMovementAsync under Accountant_sys//Controllers)
  ```
- **Remediation (drop-in):**
  1. Create `CashController` with `Create`/`Index` screens wired to `RecordCashMovementAsync` (Today/Today+1/CustomerPayment out of allowed manual set) exposed in `_Layout` sidebar: mark `CashCategory.Expense`, `OtherCashIn`, `OtherCashOut`, `RetailSale` as the **only manual categories**; reject `CustomerPayment`/`VendorPayment`/`CustomerRefund`/`VendorRefund` manually via:
  ```csharp
  private static readonly CashCategory[] ManualAllowed =
      { CashCategory.Expense, CashCategory.OtherCashIn, CashCategory.OtherCashOut, CashCategory.RetailSale };

  // In RecordCashMovementAsync (after null check & amount check):
  if (!ManualAllowed.Contains(dto.Category))
      throw new InvalidOperationException("هذه الفئة لا تُسجَّل يدوياً؛ استخدم شاشة السداد أو المرتجع المتخصصة.");
  ```
  2. In `InvoiceService.PostInvoiceAsync`, add for retail cash:
  ```csharp
  if (invoice.Type == InvoiceType.StoreCashSales)
  {
      // atomic cash receipt step to be applied at POSTing (within the same POST txn):
      await _unitOfWork.CashTransactionRepo.AddAsync(new CashTransaction
      {
          TransactionDate   = DateTime.Today,
          Amount            = invoice.TotalAmount,
          Direction         = CashDirection.In,
          Category          = CashCategory.RetailSale,
          SourceInvoiceId   = invoice.Id,
          Notes             = $"تحصيل فاتورة مبيعات نقدي ({invoice.InvoiceNumber})",
          CreatedAt         = DateTime.UtcNow
      });
  }
  ```

---

#### [HIGH-06] Daily Closing **Update Path** Silently Overwrites History; Reopen Path Lacks Null Guard & Authorization Differentiation
- **Affected files:** `BLL/Service/DailyClosingService.cs:43-92`
- **Root cause & failure scenario:**
  1. `PerformDailyClosingAsync` takes the update branch (lines 73-79) whenever a row exists with `IsClosed == false` and **rewrites OpeningCashAmount / Expected / Counted / Difference values** from the current DTO — historical numbers silently replaced with zero trace.
  2. `ReopenDailyClosingAsync` (line 86-88) calls `GetByIdAsync` → per [CRIT-04], a bad/wrong `DailyClosingId` results in an unhandled `NullReferenceException` (visible only as a bootstrap-500 in production with the generic "/Home/Error" page).
  3. There is no role restriction anywhere (`[Authorize(Roles="Admin")]` never appears) — **every authenticated user** can close and reopen any day, editing locked monetary history.
- **Business/financial impact:** "Locked day" actually means "editable day" for all logged-in operators; audits of historical days can be silently reshaped.
- **Vulnerable code:**
  ```csharp
  // DailyClosingService.cs:86-88 — the NRE path:
  var closingDay = await _unitOfWork.DailyClosingRepo.GetByIdAsync(dto.DailyClosingId);
  if (closingDay.IsClosed == false) throw new InvalidOperationException();  // ✗ no null guard
  ```
- **Remediation (drop-in):**
  ```csharp
  public async Task<bool> ReopenDailyClosingAsync(ReopenDailyClosingDto dto)
  {
      var closingDay = await _unitOfWork.DailyClosingRepo.GetByIdAsync(dto.DailyClosingId);
      if (closingDay == null)
          throw new InvalidOperationException($"سجل الإقفال رقم ({dto.DailyClosingId}) غير موجود.");
      if (!closingDay.IsClosed)
          throw new InvalidOperationException("هذه اليومية غير مقفلة أصلاً، لا تحتاج إعادة فتح.");
      if (dto.BusinessDate == default || dto.Reason.Trim().Length < 5)
          throw new InvalidOperationException("سبب إعادة الفتح مطلوب (5 أحرف على الأقل) للتدقيق.");
      closingDay.IsClosed = false;
      closingDay.Notes = $"[إعادة فتح {DateTime.UtcNow:u}] {closingDay.Notes} — / السبب: {dto.Reason}";
      await _unitOfWork.SaveChangesAsync();
      return true;
  }
  ```
  And restrict to admins on controller: `[Authorize(Roles = "Admin")]` for `Reopen`/`CloseDay` POST.

---

### Module C — Concurrent Infrastructure (DAL)

---

#### [HIGH-07] `StockConcurrencyGate` Guarantees Only On A Single AppDomain — Claims "Serializable" While Using Default Isolation
- **Affected files:**
  - `BLL/Service/StockConcurrencyGate.cs:15-18` (process-static semaphore)
  - `DAL/AppDbContext/UnitOfWork.cs:61-64` (`BeginTransactionAsync` with no explicit isolation)
- **Root cause:** The gate is an in-process `SemaphoreSlim`, and the comment block explicitly states "in-process semaphore combined with a **Serializable** database transaction protect..." but `BeginTransactionAsync` opens the transaction with the EF default (SQL Server `READ COMMITTED`). Therefore the read-validate-write inside the maritime transaction is *not* serializable vs. other connections (e.g., manual StockOut through the gate only — which does help within the same process — but a directly-supplied SQL script, a bugfix patch, or a second deployed instance bypass it entirely).
- **Failure scenario (defense-in-depth gap):** Two IIS worker-process instances (scale-out, or the app-pool recycling with overlapped recycle) each hold their own static gate → both can read identical `availableKg = 200` and both issue stock-outs of 200 → net −400 recorded against a 200 base (`stock movements ledger` technically stays true, but the business rule "cannot go negative" is silently violated because the DB has no non-negativity Check constraint on derived balances).
- **Business/financial impact:** Freezer stock goes negative; maritime supply posts dispatch goods that don't exist; pickers physically cannot fulfill orders while the ledger reports stock present.
- **Vulnerable code:**
  ```csharp
  // UnitOfWork.cs:61-64 — the "Serializable" promised in comments isn't set:
  public async Task BeginTransactionAsync()
  {
      await _context.Database.BeginTransactionAsync();   // default isolation
  }
  ```
- **Remediation (drop-in):** Woodwork first, then make the intended isolation real:
  ```csharp
  // UnitOfWork.cs
  public async Task BeginTransactionAsync()
  {
      await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
  }
  ```
  **Plus the DB-level safety net the codebase currently lacks** (needed because no invariant of "derived stock cannot go below zero" is expressible via EF today):
  ```sql
  -- Migration: enforce the freezer invariant at the source of truth.
  CREATE TABLE dbo.ProductStockBalances (ProductId INT PRIMARY KEY, Cartons DECIMAL(18,3) NOT NULL DEFAULT 0, WeightKg DECIMAL(18,3) NOT NULL DEFAULT 0);
  -- maintained by a trigger on StockMovements; add:
  ALTER TABLE dbo.ProductStockBalances ADD CONSTRAINT CK_BalanceNonNeg CHECK (Cartons >= 0 AND WeightKg >= 0);
  ```
  (For the documented single-merchant deployment, a trigger-based materialized balance is optional; the minimum viable merge is the explicit isolation level above.)

---

#### [MED-01] Balance Computations Rebuild 6-Query Ledger Reads Per Customer/Vendor — Systemic N+1 Across 8 Screens
- **Affected files:**
  - `DAL/Repos/CustomerRepo.cs:42-109` & `DAL/Repos/VendorRepo.cs:25-83` (each call: 6 sequential async roundtrips)
  - `BLL/Service/DashboardService.cs:46-76` (per-customer × per-vendor loop — 2 queries **per row**)
  - `BLL/Service/CustomerService.cs:50-57`, `BLL/Service/VendorService.cs:44-49`, `BLL/Service/MaritimeCompanyService.cs:111-117` (per-list-item balance)
  - `BLL/Service/InvoiceService.cs:414-431` (`GetInvoicesAsync`: per M invoices × 2 extra queries incl. credit-note join)
  - `Accountant_sys/Controllers/CreditNoteController.cs:157-166` + `Accountant_sys/Controllers/CustomerController.cs:98-99` + `Accountant_sys/Controllers/MaritimeCompanyController.cs:111-113` (statement screens pull **all** invoices/cash rows into app memory then LINQ-filter)
- **Root cause:** Each is a tracked or untracked full read plus per-item follow-ups. The Dashboard alone executes `N(customers) + N(vendors) + postedInvoices × 3` roundtrips — around 30–50 queries per home-page view on a modest dataset.
- **Failure scenario:** A year of operation (5,000 invoices, 40 cash-days, 150 vendors) turns every Index page into a 30–60 second response and locks the UI thread pool — users see a hanging dashboard at month close.
- **Business/financial impact:** Broken desk ergonomics right where the manager needs it most (month-end), plus risk of the closing-time race described in CRIT-01 (slow validations extend the race window).
- **Vulnerable code:**
  ```csharp
  // DashboardService.cs:51-58 — per customer, balance-fresh from 6 SQL statements each:
  foreach (var customer in customers)
  {
      var balance = await _unitOfWork.CustomerRepo.GetCustomerBalanceAsync(customer.Id);
      ...
  }
  ```
- **Remediation (drop-in):** Aggregate server-side in one roundtrip; repo exposes group-balance reads:
  ```csharp
  // CustomerRepo.cs — new bulk API:
  public async Task<Dictionary<int, decimal>> GetCustomerBalancesBulkAsync(IEnumerable<int> customerIds)
  {
      var ids = customerIds.ToList();
      var sales   = await _context.Invoices.Where(i => ids.Contains(i.CustomerId!.Value)
                        && (i.Type == InvoiceType.WholesaleSales || i.Type == InvoiceType.MaritimeSupply)
                        && i.Status == InvoiceStatus.Posted)
                    .GroupBy(i => i.CustomerId!.Value)
                    .Select(g => new { Id = g.Key, Sum = g.Sum(i => i.TotalAmount) }).ToListAsync();
      var payments = await _context.CashTransactions.Where(t => ids.Contains(t.CustomerId!.Value)
                        && t.Direction == CashDirection.In && t.Category == CashCategory.CustomerPayment
                        && !_context.CashTransactions.Any(r => r.ReversalOfTransactionId == t.Id))
                    .GroupBy(t => t.CustomerId!.Value)
                    .Select(g => new { Id = g.Key, Sum = g.Sum(t => t.Amount) }).ToListAsync();
      // ...same for openingBalance, creditNotes, refunds → merge & return dict
  }
  ```
  Then rewrite `DashboardService`, list screens and statement builders to consume the bulk resolver (single query per total group).

---

#### [MED-02] Date-Range Boundary Inconsistency (`<=` Midnight Inclusion vs Half-open Intervals) Across Cash Queries
- **Affected files:**
  - `DAL/Repos/CashTransactionRepo.cs:86 & :94` (`createdAt <= toDate` where callers pass `AddDays(1).Date` → **next-day-midnight row included**)
  - `BLL/Service/CashTransactionService.cs:87-88` (`AddTicks(-1)` variant — inclusive-with-ticks)
  - `BLL/Service/CashTransactionService.cs:86-89`, `DashboardService.cs:86-89`, `DailyClosingService.cs:37-38, 47-52` (which use `AddDays(1).Date` as **inclusive** upper bounds rather than exclusive)
- **Root cause & failure scenario:** Two different callers implement the "one day" window differently: the daily summary includes the exact-midnight row twice while `GetFilteredTransactionsAsync` (line 48) correctly uses `<`. On 2026-11-01 at exactly 00:00:00 UTC, a transaction lands inside BOTH day-0 and day-1 totals of the closing report, double-counting its amount in contact scenarios.
- **Business/financial impact:** Phantom double-count on boundary moments (rare, but recurring at every midnight edge) creating a permanent unexplained difference of cents-to-whole amounts.
- **Vulnerable code:**
  ```csharp
  // DailyClosingService.cs:37-38 — exclusive-style inputs into an INCLUSIVE comparator:
  dailyClosingDto.TotalCashIn  = await _unitOfWork.CashTransactionRepo.GetTotalCashInAsync(
      businessDate.Date, businessDate.AddDays(1).Date);   // repo uses: createdAt <= toDate
  ```
- **Remediation (drop-in):** Standardize on half-open intervals; see corrected signatures in [CRIT-02]'s remediation (`from >= x && < x+1day` in both cash — the only consistent convention — it removes one AddTicks path and one inclusive path.

---

#### [MED-03] Razor Decimal Injection Into JavaScript (`@Model.Preview.TotalCashIn`) Is Culture-Dependent
- **Affected files:** `Accountant_sys/Views/DailyClosing/CloseDay.cshtml:207-208` (pattern is repeated in `StockOut.cshtml`, stock-in and payment views)
- **Root cause & failure scenario:** Razor renders `decimal` under the **host culture**. If the Windows server profile is set to `ar-EG` (or `ar-SA`), the generated JS becomes `const totalCashIn = 12,345.67;` → SyntaxError (comma) → client calc panel & deficit badge break silently while the **server** still Localizes model binding with the same wrong separator, producing **wrong financial figures bound from Arabic-formatted browser payloads** (whose commas invalidate; worse, some browsers submit Arabic decimal-translations — and `123,45` parses as 123).
- **Business/financial impact:** On such a server, **every** form field holding money through a foreign client (`ar-SA` browser locale) is subject to massive multiplier corruption — "80.50" becomes "8,050".
- **Vulnerable code:**
  ```html
  <script>
      const totalCashIn = @Model.Preview.TotalCashIn;   // culture-dependent string form
      const totalCashOut = @Model.Preview.TotalCashOut;
  </script>
  ```
- **Remediation (drop-in):** Use Json serialization (culture-invariant) everywhere numbers cross into JS, and pin the app culture in `Program.cs`:
  ```csharp
  // View: 
  const totalCashIn = @Json.Serialize(Model.Preview.TotalCashIn);
  const totalCashOut = @Json.Serialize(Model.Preview.TotalCashOut);
  
  // Program.cs (after builder.Build() — before UseRouting):
  var ci = System.Globalization.CultureInfo.InvariantCulture;
  System.Globalization.CultureInfo.DefaultThreadCurrentCulture = ci;
  System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new System.Globalization.CultureInfo("ar");
  ```

---

#### [MED-04] Instant Table-Search Interpolates the Raw Query Into `innerHTML` (DOM Injection)
- **Affected files:** `Accountant_sys/wwwroot/js/table-filter.js:57-63`
- **Root cause & failure scenario:** `emptyMsgRow.innerHTML = \`... للبحث: "${query}" ...\`` embeds whatever was typed (including `<img src=x onerror=...>` payload) directly into the DOM. Self-XSS in its primary reach, but any feature that later persists a table *search term* (e.g., a "share filtered view" feature) transforms this into stored XSS.
- **Vulnerable code:**
  ```js
  const html = `
      <td colspan="${colCount}" class="text-center py-4 text-muted bg-light">
          ...<div class="fw-bold">لا توجد نتائج مطابقة للبحث: "${query}"</div>...`;
  emptyMsgRow.innerHTML = html;
  ```
- **Remediation (drop-in):**
  ```js
  const td = document.createElement('td');
  td.colSpan = colCount;
  td.className = 'text-center py-4 text-muted bg-light';
  td.innerHTML = `<i class="bi bi-search fs-2 d-block mb-2 text-secondary"></i>
      <div class="fw-bold"></div>
      <small class="text-muted">جرب البحث بكلمة أخرى أو رقم مستند مختلف.</small>`;
  // then inject the query text SAFELY:
  td.querySelector('div.fw-bold').textContent = `لا توجد نتائج مطابقة للبحث: "${query}"`;
  emptyMsgRow.appendChild(td);
  ```

---

#### [MED-05] Movement Date Validation Allows Tomorrow's Full Day; Stock-In Date Backdating in Closed Days
- **Affected files:** `BLL/Service/StockMovementService.cs:248-252`
- **Root cause & Failure scenario:** `movementDate > DateTime.Today.AddDays(1)` rejects nothing until the day after **tomorrow**, i.e. a movement dated "tomorrow, 23:59" is accepted as "not future". Physically-impossible margins: stock entered "before it exists" for the whole next day (ghost stock, matching stockout-bypass attempts). There's also no closed-day guard (see CRIT-03) for the freezer ledger either — stock dated during closed days slips in.
- **Vulnerable code:**
  ```csharp
  private static void ValidateMovementDate(DateTime movementDate, string operationName)
  {
      if (movementDate > DateTime.Today.AddDays(1))
          throw new InvalidOperationException($"{operationName} لا يمكن في المستقبل.");
  }
  ```
- **Remediation (drop-in):**
  ```csharp
  private static void ValidateMovementDate(DateTime movementDate, string operationName)
  {
      if (movementDate.Date > DateTime.Today)
          throw new InvalidOperationException($"تاريخ حركة {operationName} لا يمكن أن يكون في المستقبل.");
  }
  // And from each public entry, add (where the caller has IUnitOfWork):
  // if (await uow.DailyClosingRepo.IsDateClosedAsync(movementDate.Date))
  //     throw new InvalidOperationException($"ليوم {movementDate.Date:yyyy-MM-dd} يومية مقفلة؛ لا تُسجَّل حركة مخزون بتاريخ مغلق.");
  ```

---

### Module D — Front-End (Views / JS / Print)

---

#### [MED-06] Thermal-Receipt Engine Renders Server Clock Inline, Ignores Culture, and Missing `data-confirm` Submit Path Bypasses Anti-Forgery Discipline In One Spot
- **Affected files:**
  - `Accountant_sys/Views/Invoice/Details.cshtml:868` (`@DateTime.Now.ToString("hh:mm tt")` inside receipt = server local clock paired with UTC-based dates)
  - `Accountant_sys/Views/Invoice/Details.cshtml:340-345` — the "ترحيل واعتماد الفاتورة" button is `type="button"` relying on `[data-confirm]` intercept in `table-filter.js`; if that script fails to load (CDN/network hiccup), the button **never submits** (dead as coded), but the **cancel** anchor (line 324-328) triggers `table-filter.js`'s `data-confirm` fallback (`window.location.href`) — inconsistent handling (one uses href navigation, one forms submit outside of anti-forgery token interleaving).
- **Root cause & failure scenario:** `table-filter.js`'s SweetAlert interceptor navigates via `window.location.href = href` for anchors — for the Cancel link at line 324 this is a **GET** — meaning GET `/Invoice/Cancel/12` is treated as a benign navigation (it is, since the POST is the mutating action) but the fetch of the cancel **screen** is treated imperatively as the user's accessed action, which may double-fire "Cancel" click on slow-motion UI.
- **Business/financial impact:** Print receipts show wrong minutes (server-locale vs. display date mismatch); the cancel pathway may spam re-navigation; a CDN outage kills the primary POSTing path.
- **Vulnerable code:**
  ```html
  <!-- Details.cshtml:868 — server-rendered Now inside a receipt for a UTC-based doc: -->
  <span>@Model.TransactionDate.ToString("yyyy-MM-dd") @DateTime.Now.ToString("hh:mm tt")</span>
  ```
- **Remediation (drop-in):**
  ```razor
  <!-- use the posted timestamp and the invoice's own clock: -->
  <span>@Model.TransactionDate.ToString("yyyy-MM-dd") @(Model.PostedAt?.ToLocalTime().ToString("hh:mm tt") ?? "-")</span>
  ```
  And change the "ترحيل" button to a `type="submit"` button with `confirm-submit.js`'s `data-swal` **form** interception (the more battle-tested path used elsewhere in the app).

---

#### [MED-07] Client-Side Settlement Math Depends On a Stale Snapshot Fetch; No Cross-Tab Invalidation
- **Affected files:** `Accountant_sys/wwwroot/js/payment-form.js:277-298`
- **Root cause & failure scenario:** On party selection, remaining-balance rows are cached from the server. A second cashier's tab (or a payment the desk clerk posts *while the user keeps the screen open*) makes the numbers silently stale. Once [CRIT-01] is fixed server-side, the user here gets a friendly rejection — fine — but for **UX-only** cases (double allocation draft against the same stale number), the form shows "متوازن تماماً (بالخضر)" green then the server rejects, wasting up to 2-3 blind retries per busy day.
- **Business/financial impact:** Cosmetic, but combined with server refusal patterns it generates false "the system is broken" complaints at peak hours, and — before the [CRIT-01] fix merges — invites users to hammer-submit.
- **Remediation (low-risk improvement):** Reload the open-invoice list right at submit time:
  ```js
  form.addEventListener('submit', function (e) {
      e.preventDefault();
      // Refresh the stale-ledger cache first; bail out if the mirror changed:
      fetch(config.loadUrl + '?' + (config.queryParam || 'id') + '=' + partySelect.value,
            { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
          .then(r => r.json())
          .then(latest => {
              if (JSON.stringify(latest) !== JSON.stringify(cachedLatest)) {
                  // numbers changed underneath: re-render then ask the user to re-check
                  rebuildRowsFrom(latest);
                  Swal.fire({ icon: 'warning', title: 'الأرصدة تحدّثت',
                              text: 'تم تحديث المستحقات من الخادم. راجع الأسطر ثم احفظ من جديد.',
                              confirmButtonText: 'حسناً' });
                  return;
              }
              // ... existing validation & Swal confirm submit ...
          });
  });
  ```

---

#### [LOW-01] A4 Print Engine Is Well-Implemented But `site.css` `@media print` Force-Hides `header` / `nav` / `.input-group` Globally
- **Affected files:** `Accountant_sys/wwwroot/css/site.css:284-296`
- **Root cause & failure scenario:** The blanket selector `.input-group { display: none }` erases the currency suffix ("ج.م") and any padded table cell on **every** printout — mostly harmless today, but the `header { display: none }` rule also hides the statement-header `div` (which uses a `.row header` wrapper) on receipts printed from subordinate pages.
- **Positive note (keep):** `thead { display: table-header-group }`, `tr/td/th { break-inside: avoid }`, `@page` A4 margins, and `print-color-adjust` are correctly applied across `Invoice/Details`, `_StatementTable`, and `DailyClosing/Details`. Per-view overrides re-declare them correctly.
- **Remediation:** Scope the blanket rules to an explicit class instead:
  ```css
  @media print {
      .no-print-extra { display: none !important; }   /* was: .input-group, header, nav */
      .modal-backdrop, .toast { display: none !important; }
  }
  ```

---

#### [LOW-02] DTO Validation Error Messages Use Latin Formatting (English) Inside an All-Arabic UI
- **Affected files:** `BLL/DTOs/Invoice/InComingData/AddInvoiceItemDto.cs:14-21`, `UpdateInvoiceItemDto.cs:11-19`
- **Failure scenario:** The merchant user submits an out-of-range quantity and sees a raw **English** message ("The quantity must be greater than 0") inside an otherwise all-Arabic flow, breaking bilingual coherence.
- **Remediation:** Localize the attribute messages: `[Range(0.001, double.MaxValue, ErrorMessage = "الكمية يجب أن تكون أكبر من صفر")]`.

---

#### [LOW-03] `ToArabicWords` (Tafqeet) Duplicated 3× — Invoice/Statement/Closing Drift-able
- **Affected files:** `Views/Invoice/Details.cshtml:17-67`, `Views/Shared/_StatementTable.cshtml:4-54`, `Views/DailyClosing/Details.cshtml:6-56`
- **Failure scenario:** Grammar edge case fixed in one file but not the others (e.g., "ألفان" versus "ألفانِ" cases, قرش/قرشان singular-percentiles for amounts like `0.995` rounding to 100 قروش). Already at this state, `ToArabicWords(1.995m)` prints "مائة قرشاً" although rendered value is 2.00 ج.م (with N2 formatting, 1.995 → 2.00 → discrepancy in recited text).
- **Remediation:** Extract the Tafqeet engine to a shared class (e.g., `Models/Tafqeet.cs`), always call it over the pre-rounded `N2` value (never raw decimals), and mark it one source of truth.

---

#### [LOW-04] Architecture Smells Catalog
| # | Finding | Files | Why it matters |
|:---:|---|---|---|
| A1 | `UnitOfWork` hand-constructs 11 repos instead of constructor-injected resolvers | `DAL/AppDbContext/UnitOfWork.cs:40-54` | Repo lifetime and test-tree churn; future DI migration cost |
| A2 | `GenericRepo.Update()` calls `_dbSet.Update(item)` **twice**, one discarded | `DAL/Repos/GenericRepo.cs:56-63` | Confusing no-ops; reader distrust |
| A3 | Unreachable `return true;` after `catch { throw; }` in `PostCreditNoteAsync` | `BLL/Service/CreditNoteService.cs:161` | Compiler warning; dead intent |
| A4 | Double-name assignment `mappedInvoices.PaymentStatus = mappedInvoices.PaymentStatus = ...` | `BLL/Service/PaymentService.cs:263` | Copy-paste artifact, obscures clarity |
| A5 | Unused variable `allCashTrans = moneyIn + moneyOut` (meaningless sum) | `BLL/Service/DailyClosingService.cs:51` | Reading it as "net" would double-mislead future fixes |
| A6 | Blend of pure-throw & bool-return return styles across services (`return false` vs exception) | `CustomerService`, `ExpenseCategoryService`, `InvoiceService` | Callers can't tell "validation failure" from "not found" from "optimistic lock lost" |
| A7 | POSIX-style width markers (`RET-` prefix for **StoreCashSales** while letter actually means Returns; `InvoiceNumber` of cancelled keeps unique indexes informed) | `DAL/Repos/InvoiceRepo.cs:62-71` | Prefix mislabeling confuses filings & financial reconciliation over time |
| A8 | `CashTransaction.ReversalOfTransactionId`, `DailyClosingId` FKs exist but no code path populates them | `DAL/Entities/CashTransaction.cs:38-43` + balance queries filter on them | "Reversal" architecture is a dead branch; balance filters that require `ReversalOfTransactionId == null` silently mismatch design intent |
| A9 | Zero `ILogger` usage across BLL — all errors surface only as ex.Message in `TempData` | BLL entire layer | PII/attempts cannot be investigated; no monitoring hooks |
| A10 | First-Admin registration race window (two admins can self-register simultaneously) | `BLL/Service/AuthService.cs:38-57` | Tightened by first-boot usage pattern, low risk, but trivially fixable |
| A11 | `HasFinancialHistoryAsync` uses double `FirstOrDefaultAsync` + null-compare instead of `AnyAsync` | `DAL/Repos/CustomerRepo.cs:119-125`, `VendorRepo.cs:93-99` | Unneeded extra allocations |
| A12 | `GetCustomerPaymentsAsync`/`GetVendorPaymentsAsync`/`GetAllocationsByXxxAsync` return **null** instead of empty lists | `DAL/Repos/CashTransactionRepo.cs:66-72`, `DAL/Repos/PaymentAllocationRepo.cs:20-35` | Forces consumers into defensive `?? new List<>` commits everywhere |
| A13 | `Knock-on status clamp`: no `[Authorize(Roles=...)]` anywhere despite "Admin" role existing | Program.cs:39-50, all controllers | Single-role flat model: every session equals a treasurer's powers; no segregation of cashier vs. accountant vs. auditor |
| A14 | `CreditNoteController.Index` loads **every** credit note for -1y..+1y window including Items + OriginalInvoice (with `Include(cn.Items)`) on every list render | `Controllers/CreditNoteController.cs:38-39`, `Invoice/Index` pattern | Grows execution linearly with ledger age; eligible for date-range clamp & projection |

---

## 4. Architecture & Performance Recommendations

1. **Split the transaction contract** — `UnitOfWork` should expose named transactions (`BeginFinancialTransactionAsync` = Serializable, `BeginLedgerWriteTransactionAsync` = RepeatableRead) instead of the misleading single-method API that provoked [HIGH-07].
2. **Materialize frozen-fish balances.** `GetBalanceForProductAsync` runs **5 SUM scans** per call and is hit by every frozen-fish screen + every maritime posting. Add a nightly job or trigger-updated `ProductStockBalances` table with index `(ProductId)` and read it (`WITH ROWLOCK` inside the gate). This eliminates an entire class of gate-induced slowness.
3. **Database CHECK parity** — per [HIGH-04] and [CRIT-02] remediations, add `CHECK` constraints: `PaymentAllocation.Amount > 0`, `CashTransaction.Amount > 0`, `Invoice.TotalAmount >= 0`, `Invoices.PostedAt IS NOT NULL OR Status != Posted`. C# domain validation and DB constraints should corroborate — after ≥2 constraint fixes landed historically (`FixStockMovementsCheckConstraint` migration), this parity check is clearly a recurring failure mode.
4. **Add `logger` to BLL.** Wrap finance-altering call paths (`RecordCustomerPaymentAsync`, `PostInvoiceAsync`, `PostCreditNoteAsync`, `PerformDailyClosingAsync`, `ReopenDailyClosingAsync`) in `ILogger` structured logs with entity IDs but never amounts of sensitive user info. Introduce a dedicated `ILogger<FinanceAudit>` category.
5. **Index set** (verify against `sys.dm_db_missing_index_details` after load): `CashTransactions (TransactionDate, Direction) INCLUDE(Amount)`; `Invoices (Status, TransactionDate)`; `Invoices (CustomerId, Status)`; `CreditNotes (OriginalInvoiceId)`.
6. **Introduce validation middleware for money fields** — a reusable `ValidateMoney(decimal, string field)` helper capping sign, magnitude (<1,000,000), and rounding (`decimal.Round(x, 2)`) used in DTO binding; the ad-hoc `N2` formatting across 20 views shows manual repetition already yielding tomorrow's inconsistencies (Tafqeet duplication is a sibling of this problem).
7. **Print engine:** consolidate the three duplicated per-view `<style>` print blocks into `site.css` under `.print-a4-sheet` / `.print-80mm-sheet` semantic classes to prevent future divergences (the current triple-copy already drifted: e.g., invoice sets `font-size: 12px`, statement 11.5px, receipt engines 10–11px without justification).
8. **Manual-cash (cash drawer) module** [HIGH-05] is a top priority either as a new controller or by extending `PaymentController` with a `/Cash/Movement` GET/POST screen; user flows & drawer-till operations on expenses/retail income should be reachable same-day.

---

## 5. Project Manager Action Plan (Roadmap)

### 🚫 MUST MERGE BEFORE PRODUCTION (Blockers)
| Priority | ID | Effort | Description |
|:---:|---|:---:|---|
| P0 | CRIT-01 | 0.5–1 d | Re-validate allocations *inside* the settlement transaction + explicit isolation level on financial txns |
| P0 | CRIT-02 | 0.5 d | Rebase all cash totals on `TransactionDate` (business date) with half-open intervals; add the composite index |
| P0 | CRIT-03 | 0.5 d | Enforce `IsDateClosedAsync` + future-date guard at every cash/movement entry point |
| P0 | CRIT-04 | 0.5 d | `GenericRepo.GetByIdAsync` returns nullable; fix exposed NRE paths at every caller |
| P0 | HIGH-01 | 0.5 d | Credit-note quantity re-validation inside `PostCreditNoteAsync` |
| P0 | HIGH-02 | 0.25 d | `StatementBuilder` beginning-balance guard |
| P0 | HIGH-04 | 0.5 d | Line-discount cap + `CHECK` migration |
| **Σ** | | **~3.5–4.5 dev-days** | |

### ⚠️ SPRINT 2 (Required Within 2 Weeks of Launch)
| Priority | ID | Effort | Description |
|:---:|---|:---:|---|
| P1 | HIGH-03 | 0.5 d | Atomic sequential numbering + `EXISTS`-based maritime duplicate check |
| P1 | HIGH-05 | 1.5 d | Manual cash movement screen (`/Cash/*`); retail sale posts a drawer entry |
| P1 | HIGH-06 | 0.25 d | Reopen/Close null-guard + `[Authorize(Roles="Admin")]` on financial day-ops |
| P1 | HIGH-07 | 0.5 d | Explicit Serializable isolation, gate timeout w/ logging, balance non-negativity CHECK |
| P1 | MED-03 + MED-01 partial | 1 d | Culture pinning (`DefaultThreadCurrentCulture = Invariant`) + bulk balance resolver for the Dashboard only (the highest-traffic N+1) |
| P1 | MED-05 | 0.25 d | Future-date + closed-day guard for stock movements |

### 📅 SPRINT 3+ (Health & Maintainability Backlog)
- MED-01 full sweep (bulk-balance API for statements & lists, statement controller pull-in projections).
- MED-02, MED-04 (XSS hardening), MED-06 (receipt clock + submit-path alignment), MED-07 (stale snapshot refresh UX).
- LOW-01…LOW-04 catalog: Tafqeet extraction, print-style dedupe, repository contract unification, ILogger adoption in BLL, DTO message localization, dead FK/field analysis for `ReversalOfTransactionId` (decide: implement reversal flow or drop the fields at the next migration).

---

### Verification Coverage (Tests)
`Accountant_sys.Tests` has 5 strong suites (settlement math, balance formula formula, type separation, stock math, maritime invoice integrity) tested via an in-memory harness of real services. **Coverage gaps** to add during the P0 fixes:
- Simultaneous-payment over-allocation race (needs a real SQL Server integration test — `TestHarness` in-memory provider ignores transactions, hiding CRIT-01's category bugs).
- UTC/local boundary test for closing totals.
- Closed-day payment rejection.
- Concurrent `StockConcurrencyGate` saturation.
- Line-discount cap.

---

*End of audit report — generated by a full line-by-line review of DAL (49 files), BLL (107 files), Controllers (13), Views (68), static assets (6), and test suites (5) per PM mandate.*
