using BLL.DTOs.Invoice.OutGoingData;
using BLL.DTOs.MaritimeCompany.OutGoingData;

namespace Accountant_sys.Models
{
    /// <summary>Structured product line item for statement movements (invoices, maritime supply orders, returns).</summary>
    public sealed class StatementProductItem
    {
        public string ProductName { get; init; } = string.Empty;
        public decimal Quantity { get; init; }
        public string QuantityText { get; init; } = string.Empty;
        public decimal UnitPrice { get; init; }
        public string UnitPriceText { get; init; } = string.Empty;
        public decimal LineDiscountAmount { get; init; }
        public string? LineDiscountText { get; init; }
        public decimal LineTotal { get; init; }
        public string LineTotalText { get; init; } = string.Empty;
    }

    /// <summary>One muted line or structured product item printed under a movement (item, allocation or note).</summary>
    public sealed class StatementDetailLine
    {
        public string Text { get; set; } = string.Empty;
        public bool IsNote { get; set; }
        public StatementProductItem? Product { get; set; }
        public bool IsProduct => Product != null;
    }

    /// <summary>
    /// Builds the presentation-only values used by the redesigned statement.
    /// It never recalculates ledger totals: it only reads the values produced by
    /// <see cref="StatementBuilder"/> and the invoice settlement snapshots.
    /// </summary>
    public static class StatementPresentationBuilder
    {
        private static StatementDetailLine CreateProductDetailLine(
            string productName,
            decimal quantity,
            decimal unitPrice,
            decimal lineDiscountAmount,
            decimal storedLineTotal)
        {
            var computedNet = Math.Max(0m, quantity * unitPrice - lineDiscountAmount);
            var lineTotal = storedLineTotal > 0m || computedNet == 0m ? storedLineTotal : computedNet;

            var qtyText = StatementBuilder.FormatQuantity(quantity);
            var priceText = StatementBuilder.FormatMoney(unitPrice);
            var totalText = StatementBuilder.FormatMoney(lineTotal);
            var hasDiscount = lineDiscountAmount > 0.005m;
            var discText = hasDiscount ? StatementBuilder.FormatMoney(lineDiscountAmount) : null;

            var text = $"{productName} × {qtyText} بسعر {priceText}";
            if (hasDiscount)
                text += $" − خصم {discText}";
            text += $" = {totalText}";

            return new StatementDetailLine
            {
                Text = text,
                Product = new StatementProductItem
                {
                    ProductName = productName,
                    Quantity = quantity,
                    QuantityText = qtyText,
                    UnitPrice = unitPrice,
                    UnitPriceText = priceText,
                    LineDiscountAmount = lineDiscountAmount,
                    LineDiscountText = discText,
                    LineTotal = lineTotal,
                    LineTotalText = totalText
                }
            };
        }

        /// <summary>
        /// Human-readable sub-lines for one movement. The printed statement shows
        /// only short, formal notes; internal/colloquial notes stay on screen.
        /// </summary>
        public static List<StatementDetailLine> BuildDetailLines(
            StatementLine line,
            StatementLabels labels,
            StatementInvoiceDetailDto? invoice = null,
            MaritimeSupplyOrderDto? order = null)
        {
            var result = new List<StatementDetailLine>();
            var kind = line.Kind ?? StatementLineKind.OpeningBalance;

            if (invoice != null)
            {
                foreach (var item in invoice.Items)
                {
                    result.Add(CreateProductDetailLine(
                        item.ProductName,
                        item.Quantity,
                        item.UnitPrice,
                        item.LineDiscountAmount,
                        item.LineTotal));
                }

                if (invoice.InvoiceDiscountAmount > 0.005m)
                {
                    result.Add(new StatementDetailLine
                    {
                        Text = $"خصم على الفاتورة: {StatementBuilder.FormatMoney(invoice.InvoiceDiscountAmount)} ج.م"
                    });
                }
            }
            else if (order != null)
            {
                foreach (var item in order.Items)
                {
                    result.Add(CreateProductDetailLine(
                        item.ProductName,
                        item.Quantity,
                        item.UnitPrice,
                        item.LineDiscountAmount,
                        item.LineTotal));
                }

                foreach (var charge in order.Charges.Where(c => c.IsBillableToCompany))
                {
                    result.Add(new StatementDetailLine
                    {
                        Text = $"{charge.Description} ({StatementBuilder.FormatMoney(charge.Amount)} ج.م)"
                    });
                }
            }

            if (line.CashTransaction != null
                && (kind == StatementLineKind.VendorPayment || kind == StatementLineKind.CustomerReceipt))
            {
                var allocations = line.CashTransaction.Allocations ?? new List<BLL.DTOs.Payment.OutGoingData.PaymentAllocationDto>();
                if (allocations.Any())
                {
                    var parts = allocations.Select(a =>
                        $"{(string.IsNullOrWhiteSpace(a.InvoiceNumber) ? $"#{a.InvoiceId}" : a.InvoiceNumber)} ({StatementBuilder.FormatMoney(a.Amount)})");
                    result.Add(new StatementDetailLine { Text = $"{labels.PaidRowVerb}: {string.Join(" · ", parts)}" });
                }
                else
                {
                    result.Add(new StatementDetailLine { Text = labels.OnAccountRowLabel });
                }
            }

            if (line.CreditNote != null)
            {
                if (!string.IsNullOrWhiteSpace(line.CreditNote.OriginalInvoiceNumber))
                {
                    result.Add(new StatementDetailLine
                    {
                        Text = $"على الفاتورة {line.CreditNote.OriginalInvoiceNumber}"
                    });
                }

                foreach (var item in line.CreditNote.Items)
                {
                    result.Add(CreateProductDetailLine(
                        item.ProductName,
                        item.Quantity,
                        item.UnitPrice,
                        0m,
                        item.LineTotal));
                }
            }

            var note = StatementNotes.GetPublicNote(line.Notes);
            if (note != null)
            {
                result.Add(new StatementDetailLine { Text = $"{labels.NoteLabel}: {note}", IsNote = true });
            }

            return result;
        }

        /// <summary>Search text for client-side movement filtering.</summary>
        public static string BuildSearchText(
            StatementLine line,
            StatementInvoiceDetailDto? invoice = null,
            MaritimeSupplyOrderDto? order = null)
        {
            var parts = new List<string>
            {
                line.DocumentNo ?? string.Empty,
                line.SecondaryDocNo ?? string.Empty,
                line.Description ?? string.Empty,
                line.SummaryHint ?? string.Empty
            };

            if (line.CashTransaction?.Allocations != null)
            {
                parts.AddRange(line.CashTransaction.Allocations.Select(a => a.InvoiceNumber ?? string.Empty));
            }

            if (invoice?.Items != null)
                parts.AddRange(invoice.Items.Select(i => i.ProductName));

            if (order?.Items != null)
                parts.AddRange(order.Items.Select(i => i.ProductName));

            if (line.CreditNote?.Items != null)
                parts.AddRange(line.CreditNote.Items.Select(i => i.ProductName));

            return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))).ToLowerInvariant();
        }

        public static void Apply(
            StatementViewModel model,
            IEnumerable<StatementInvoiceSettlementDto>? invoices)
        {
            model.OpeningDisplay = model.BeginningBalance + model.TotalOpening;

            var settlements = (invoices ?? Enumerable.Empty<StatementInvoiceSettlementDto>())
                .GroupBy(i => i.InvoiceId)
                .Select(g => g.First())
                .ToList();

            model.InvoiceSettlements = settlements.ToDictionary(
                i => i.InvoiceId,
                i => new StatementInvoiceSettlement
                {
                    PaidAmount = i.PaidAmount,
                    ReturnsAmount = i.ReturnsAmount,
                    RemainingAmount = i.RemainingAmount,
                    PaymentStatus = i.PaymentStatus
                });

            model.UnpaidInvoices = settlements
                .Where(i => i.RemainingAmount > 0.005m)
                .OrderBy(i => i.TransactionDate)
                .ThenBy(i => i.InvoiceId)
                .Select(i => new StatementUnpaidInvoice
                {
                    InvoiceId = i.InvoiceId,
                    InvoiceNumber = i.InvoiceNumber,
                    TransactionDate = i.TransactionDate,
                    TotalAmount = i.TotalAmount,
                    PaidAmount = i.PaidAmount,
                    ReturnsAmount = i.ReturnsAmount,
                    RemainingAmount = i.RemainingAmount,
                    PaymentStatus = i.PaymentStatus
                })
                .ToList();

            model.UnpaidTotal = model.UnpaidInvoices.Sum(i => i.RemainingAmount);
            model.OverpaymentTotal = settlements
                .Where(i => i.RemainingAmount < -0.005m)
                .Sum(i => -i.RemainingAmount);

            // A payment can be partly allocated and partly left on account
            // (the unallocated part settles the opening balance). Count only the
            // portion that is not tied to an invoice.
            model.OnAccountTotal = model.Lines
                .Where(l => l.Kind == StatementLineKind.VendorPayment
                            || l.Kind == StatementLineKind.CustomerReceipt)
                .Select(l => l.CashTransaction)
                .Where(tx => tx != null)
                .Sum(tx =>
                {
                    var allocated = tx!.Allocations?.Sum(a => a.Amount) ?? 0m;
                    return Math.Max(0m, tx.Amount - allocated);
                });

            // Identity:
            //   ending = unpaid + opening - on-account + refunds - overpayments
            // Any residual is a cross-period allocation/return (for filtered
            // statements) and is shown as an explicit bridge line.
            var expected = model.UnpaidTotal
                           + model.OpeningDisplay
                           - model.OnAccountTotal
                           + model.TotalRefunds
                           - model.OverpaymentTotal;
            model.CrossPeriodBridge = model.EndingBalance - expected;

            var lastPayment = model.Lines
                .Where(l => (l.Kind == StatementLineKind.VendorPayment
                             || l.Kind == StatementLineKind.CustomerReceipt)
                            && l.CashTransaction != null)
                .OrderByDescending(l => l.Date)
                .ThenByDescending(l => l.Timestamp)
                .ThenByDescending(l => l.DocumentId)
                .FirstOrDefault();

            model.LastPaymentAmount = lastPayment?.CashTransaction?.Amount;
            model.LastPaymentDate = lastPayment?.Date;
        }
    }
}
