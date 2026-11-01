using DAL.IRepos;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace DAL.IConfiguration
{
    public interface IUnitOfWork : IDisposable
    {
        public IProductRepo ProductRepo { get; }
        public ICustomerRepo CustomerRepo { get; }
        public IVendorRepo Vendors { get; }
        public IInvoiceRepo InvoiceRepo { get; }
        public ICashTransactionRepo CashTransactionRepo { get; }
        public IPaymentAllocationRepo PaymentAllocationRepo { get; }
        public ICreditNoteRepo CreditNoteRepo { get; }
        public IOpeningBalanceRepo OpeningBalanceRepo { get; }
        public IExpenseCategoryRepo ExpenseCategoryRepo { get; }
        public IDailyClosingRepo DailyClosingRepo { get; }
        public IStockMovementRepo StockMovementRepo { get; }

        Task SaveChangesAsync();
        Task BeginTransactionAsync();
        Task CommitTransactionAsync();
        Task RollbackTransactionAsync();
    }
}
