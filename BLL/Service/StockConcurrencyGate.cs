using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BLL.Service
{
    // Process-wide gate serializing stock-consuming operations (manual withdrawals,
    // adjustments and maritime supply posting). The system is a single-merchant
    // desktop deployment, so an in-process semaphore combined with a Serializable
    // database transaction protects against concurrent/duplicate submissions.
    // StockMovements remain the source of truth; the gate only prevents racing
    // read-validate-write sequences.
    public static class StockConcurrencyGate
    {
        public static readonly SemaphoreSlim Gate = new(1, 1);
    }
}
