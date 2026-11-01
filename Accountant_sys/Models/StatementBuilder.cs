namespace Accountant_sys.Models
{
    public static class StatementBuilder
    {
        public static readonly DateTime HistoryStart = new DateTime(2000, 1, 1);
        public static DateTime HistoryEnd => DateTime.Now.AddYears(1);

        public static StatementViewModel Build(
            List<StatementLine> sourceLines,
            bool isVendor,
            int partyId,
            string partyName,
            string controllerName,
            DateTime? from,
            DateTime? to)
        {
            var sorted = sourceLines
                .Select((line, index) => new { line, index })
                .OrderBy(x => x.line.Date)
                .ThenBy(x => x.index)
                .Select(x => x.line)
                .ToList();

            decimal running = 0;
            foreach (var line in sorted)
            {
                running += isVendor ? (line.Credit - line.Debit) : (line.Debit - line.Credit);
                line.RunningBalance = running;
            }

            var fromDate = from?.Date;
            var toDate = to?.Date;

            var model = new StatementViewModel
            {
                PartyId = partyId,
                PartyName = partyName,
                PartyType = isVendor ? "Vendor" : "Customer",
                ControllerName = controllerName,
                From = from,
                To = to
            };

            foreach (var line in sorted)
            {
                var inPeriod =
                    (!fromDate.HasValue || line.Date >= fromDate) &&
                    (!toDate.HasValue || line.Date < toDate.Value.AddDays(1));

                if (!inPeriod)
                {
                    model.BeginningBalance = line.RunningBalance;
                    continue;
                }

                model.Lines.Add(line);
                if (line.DocumentType.StartsWith("فاتورة"))
                    model.TotalInvoices += isVendor ? line.Credit : line.Debit;
                else if (line.DocumentType.StartsWith("إشعار"))
                    model.TotalReturns += isVendor ? line.Debit : line.Credit;
                else if (line.DocumentType.StartsWith("سند"))
                    model.TotalPayments += isVendor ? line.Debit : line.Credit;
            }

            var last = model.Lines.LastOrDefault();
            model.EndingBalance = last != null ? last.RunningBalance : model.BeginningBalance;
            return model;
        }
    }
}
