# Plan: Maritime Supply Orders — Charges, Posted Editing, KG Input Fix, Safe Deletion

## Confirmed design decisions (user-approved)
1. Post-amendment underpayment → allow, warn in confirm dialog, difference flows as customer credit (negative balance).
2. External supply order number stays editable during posted amendment, with full uniqueness re-validation.
3. Posted "remove" reuses the existing Cancel void workflow, upgraded to detach payment allocations while preserving the underlying cash transaction.

## Root causes (verified in code)
- **KG focus jump:** `Views/Invoice/Details.cshtml:1042` — `refresh()` (bound to KG input `input` event at line 1081) calls `unitPriceInput.focus()` in non-frozen-product branch → cursor leaves KG field after first keystroke. Fix = delete that one line.
- **External number reuse exception:** `DAL/AppDbContext/AppDbcontext.cs:99-101` + migration `20261031222729` create a **global** unique filtered index `IX_Invoices_ExternalOrderNumber` (`[Type]=4 AND ExternalOrderNumber IS NOT NULL`) that includes Cancelled invoices; app-level check in `InvoiceService.CreateDraftInvoiceAsync:90-96` is also global (message claims per-company) and excludes Cancelled.
- Previous session's 3 new test files (`FreezerSourcingTests/MaritimeStatementTests/ActiveFilteringTests`) do not compile yet — `TestHarness` lacks `VendorService`/`ExpenseCategoryService` properties; unfinished business, stabilized first.

## Phase 1 — Stabilize test suite
- Add `VendorService`/`ExpenseCategoryService` to `Accountant_sys.Tests/Helpers/TestHarness.cs`; finish/fix the three in-progress test files (unused usings, seed signatures); run `dotnet test` to green baseline.

## Phase 2 — DAL
Entities:
- `DAL/Entities/InvoiceAdditionalCharge.cs`: Id, InvoiceId, Description, Quantity? (18,3), UnitPrice? (18,2), Amount (18,2), IsBillableToCompany (default true), Notes?, CreatedAt; nav Invoice.
- `DAL/Entities/InvoiceAmendment.cs`: Id, InvoiceId (FK, Cascade), AmendedAtUtc, AmendedByUserId (optional FK AppUser), Reason (500), BeforeJson, AfterJson (nvarchar(max)); nav Invoice.
- `Invoice` gains `ICollection<InvoiceAdditionalCharge> AdditionalCharges`, `ICollection<InvoiceAmendment> Amendments`.

`AppDbContext`:
- Precision per convention (18,3 qty / 18,2 money); cascade deletes for both children; amendment only.
- Replace global unique index: `HasIndex(CustomerId, ExternalOrderNumber)` unique=false, filtered `[Type]=4 AND [ExternalOrderNumber] IS NOT NULL` (query perf).

Migrations (2, non-destructive):
- `AddInvoiceAdditionalChargesAndAmendments` (tables).
- `RedefineMaritimeExternalOrderUniqueness`: DropIndex `IX_Invoices_ExternalOrderNumber`, CreateIndex perf index, then `migrationBuilder.Sql`: CREATE UNIQUE INDEX `IX_Invoices_Customer_ExternalOrderNumber_Active` ON Invoices(CustomerId, ExternalOrderNumber) WHERE Type=4 AND ExternalOrderNumber IS NOT NULL AND Status<>3. Include pre-apply duplicate-check SQL in report.

## Phase 3 — BLL
DTOs:
- In: `AddInvoiceChargeDto` (InvoiceId, Description, Quantity?, UnitPrice?, Amount, IsBillableToCompany=true, Notes?), `UpdateInvoiceChargeDto` (+ChargeId), `AmendPostedMaritimeInvoiceDto` {InvoiceId, Reason, ExternalOrderNumber?, SupplyOrderDate?, RemovedItemIds[], UpdatedItems[] {ItemId,Quantity,UnitPrice,LineDiscountAmount,IsFromFreezer}, NewItems[] (AddInvoiceItemDto), Charges[] {ChargeId?, Description,Quantity?,UnitPrice?,Amount,IsBillableToCompany,Notes}, RemovedChargeIds[]}.
- Out: `InvoiceChargeDto`; `InvoiceDto` += Charges list, BillableChargesTotal, InternalChargesTotal, Amendments summary.

`InvoiceService`:
- `RecalculateInvoiceTotals` += billable charges sum (single source of truth).
- Charge CRUD (draft only): Add/Update/Remove — validate description+amount>0 or qty*price; maritime only.
- `CreateDraftInvoiceAsync` duplicate check → per-company (`CustomerId == dto.CustomerId`), exclude Cancelled, inside same save.
- `AmendPostedMaritimeInvoiceAsync(dto, userId?)` — maritime+Posted only; full validation BEFORE mutation (rollback-safe, InMemory-friendly); inside `StockConcurrencyGate` + transaction: RecalculateInvoiceTotals per-product freezer diff (old posted items vs new items, only IsFromFreezer rows): extra KG → in-transaction availability check + Out movement; decreased KG → compensating In movement; sourcing flip reconciles both ways; price-only edit → zero movements. Update ExternalOrderNumber/SupplyOrderDate (uniqueness re-check). Preserve payments; record `InvoiceAmendment` with reason/user/JSON snapshots. New total < paid allowed (credit) — no allocation edits.
- `DeleteDraftInvoiceAsync(invoiceId)`: Draft only → hard delete invoice + items + charges (no movements).
- `CancelPostedInvoiceAsync` extended: for MaritimeSupply, allow when posted credit notes absent; payment allocations are removed while their `CashTransaction` rows persist (= unallocated advance per existing settlement math); reversal movements as today; wholesale/vendor behavior unchanged.
- Thread `userId` into posted maritime movements (`CreatedByUserId`).
- Repo: `IInvoiceRepo.GetPostedMaritimeSupplyOrdersAsync` etc. unaffected; add `GetInvoiceWithChargesAsync` if needed.

## Phase 4 — Web
`InvoiceController`: `AddCharge/UpdateCharge/RemoveCharge` (POST PRG), `Delete` (POST, draft), `Amend` GET/POST, `CurrentUserId()` helper.
Views:
- `Details.cshtml`: remove `unitPriceInput.focus()`; add "المصاريف والتكاليف الإضافية" section on draft page (dynamic rows, own auto-totals, billable toggle default ON, notes; doesn't touch cash); items table unchanged; A4 print + thermal receipt get charges table + new final total; posted maritime: "تصحيح أمر التوريد" button → Amend; draft: "حذف الطلب" (data-confirm); show amendment/void info panels.
- New `Invoice/Amend.cshtml`: reason, external no./date editable, per-item editable rows (qty price discount freezer toggle), remove item, add item form, charges grid, live totals + delta, data-swal confirm.
- `MaritimeCompany/Details.cshtml`: row actions (Post→تصحيح; Draft→حذف).
- Statement partials: detailed mode shows per-order charges rows; summary totals unchanged mathematically.
- `Payment`/expense: optional Notes hint referencing supply order (no cash automation).

## Phase 5 — Tests
- New scenarios: charges totals (none/one/multi/internal-only), draft-preserves-charges, no auto cash movement; amendment (draft edit, posted KG correction stock diff only, insufficient stock rejected + rollback, price-only no movement, sourcing flip, payments preserved, audit rows written, invalid → no changes); delete/void (draft hard delete, posted void reversals once, allocations detached with cash kept, balances exclude voided, external-number reuse after void (per-company), different company reuse OK, duplicate active rejected, draft→posted internal number never reused).
- KG input (18–21): manual QA checklist (no browser-test infra).
- Regression: full suite incl. previous task's FreezerSourcing/Statement/ActiveFiltering tests.

## Delivery report (at end)
Root causes, entity/money semantics, charge flow, amendment/stock reconciliation, delete semantics, uniqueness strategy + migration safety instructions, files changed, test results, limitations (InMemory unique-filter caveat, no closing-date gates exist — amendments not blocked by closings, per-session single-user gate assumption).
