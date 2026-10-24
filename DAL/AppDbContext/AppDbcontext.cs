using DAL.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DAL.AppDbContext
{
    public class AppDbcontext : IdentityDbContext<AppUser>
    {
        public AppDbcontext(DbContextOptions<AppDbcontext> options) : base(options)
        {
        }

        public DbSet<Product> Products => Set<Product>();
        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<Vendor> Vendors => Set<Vendor>();
        public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
        public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
        public DbSet<CreditNoteItem> CreditNoteItems => Set<CreditNoteItem>();
        public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();
        public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
        public DbSet<OpeningBalanceEntry> OpeningBalanceEntries => Set<OpeningBalanceEntry>();
        public DbSet<DailyClosing> DailyClosings => Set<DailyClosing>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ==========================================
            // Product Configuration
            // ==========================================
            modelBuilder.Entity<Product>(entity =>
            {
                entity.HasKey(p => p.Id);
                entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
                entity.Property(p => p.PurchasePrice).HasPrecision(18, 2);
                entity.Property(p => p.WholesalePrice).HasPrecision(18, 2);
                entity.Property(p => p.RetailPrice).HasPrecision(18, 2);
            });

            // ==========================================
            // Customer Configuration
            // ==========================================
            modelBuilder.Entity<Customer>(entity =>
            {
                entity.HasKey(c => c.Id);
                entity.Property(c => c.Name).HasMaxLength(200).IsRequired();
                entity.Property(c => c.PhoneNumber).HasMaxLength(50);
                entity.Property(c => c.Address).HasMaxLength(300);
            });

            // ==========================================
            // Vendor Configuration
            // ==========================================
            modelBuilder.Entity<Vendor>(entity =>
            {
                entity.HasKey(v => v.Id);
                entity.Property(v => v.Name).HasMaxLength(200).IsRequired();
                entity.Property(v => v.PhoneNumber).HasMaxLength(50);
                entity.Property(v => v.Address).HasMaxLength(300);
            });

            // ==========================================
            // ExpenseCategory Configuration
            // ==========================================
            modelBuilder.Entity<ExpenseCategory>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).HasMaxLength(150).IsRequired();
            });

            // ==========================================
            // Invoice Configuration
            // ==========================================
            modelBuilder.Entity<Invoice>(entity =>
            {
                entity.HasKey(i => i.Id);
                entity.Property(i => i.InvoiceNumber).HasMaxLength(50).IsRequired();
                entity.HasIndex(i => i.InvoiceNumber).IsUnique();

                entity.Property(i => i.SubTotal).HasPrecision(18, 2);
                entity.Property(i => i.ItemDiscountTotal).HasPrecision(18, 2);
                entity.Property(i => i.InvoiceDiscountAmount).HasPrecision(18, 2);
                entity.Property(i => i.TotalAmount).HasPrecision(18, 2);
                entity.Property(i => i.Notes).HasMaxLength(1000);
                entity.Property(i => i.CancellationReason).HasMaxLength(500);

                // Relationship: Customer -> Invoices (Wholesale)
                entity.HasOne(i => i.Customer)
                      .WithMany(c => c.Invoices)
                      .HasForeignKey(i => i.CustomerId)
                      .OnDelete(DeleteBehavior.Restrict);

                // Relationship: Vendor -> Invoices (Purchase)
                entity.HasOne(i => i.Vendor)
                      .WithMany(v => v.Invoices)
                      .HasForeignKey(i => i.VendorId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ==========================================
            // InvoiceItem Configuration
            // ==========================================
            modelBuilder.Entity<InvoiceItem>(entity =>
            {
                entity.HasKey(ii => ii.Id);
                entity.Property(ii => ii.ProductName).HasMaxLength(200).IsRequired();

                // Fractional quantity precision (e.g. 2.500 kg)
                entity.Property(ii => ii.Quantity).HasPrecision(18, 3);
                
                entity.Property(ii => ii.UnitPrice).HasPrecision(18, 2);
                entity.Property(ii => ii.LineDiscountAmount).HasPrecision(18, 2);
                entity.Property(ii => ii.LineTotal).HasPrecision(18, 2);

                entity.HasOne(ii => ii.Invoice)
                      .WithMany(i => i.Items)
                      .HasForeignKey(ii => ii.InvoiceId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(ii => ii.Product)
                      .WithMany(p => p.InvoiceItems)
                      .HasForeignKey(ii => ii.ProductId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ==========================================
            // CreditNote Configuration
            // ==========================================
            modelBuilder.Entity<CreditNote>(entity =>
            {
                entity.HasKey(cn => cn.Id);
                entity.Property(cn => cn.CreditNoteNumber).HasMaxLength(50).IsRequired();
                entity.HasIndex(cn => cn.CreditNoteNumber).IsUnique();

                entity.Property(cn => cn.TotalAmount).HasPrecision(18, 2);
                entity.Property(cn => cn.Reason).HasMaxLength(500).IsRequired();

                entity.HasOne(cn => cn.OriginalInvoice)
                      .WithMany(i => i.CreditNotes)
                      .HasForeignKey(cn => cn.OriginalInvoiceId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ==========================================
            // CreditNoteItem Configuration
            // ==========================================
            modelBuilder.Entity<CreditNoteItem>(entity =>
            {
                entity.HasKey(cni => cni.Id);
                entity.Property(cni => cni.ProductName).HasMaxLength(200).IsRequired();

                // Fractional quantity precision (e.g. 1.250 kg)
                entity.Property(cni => cni.Quantity).HasPrecision(18, 3);
                
                entity.Property(cni => cni.UnitPrice).HasPrecision(18, 2);
                entity.Property(cni => cni.LineTotal).HasPrecision(18, 2);

                entity.HasOne(cni => cni.CreditNote)
                      .WithMany(cn => cn.Items)
                      .HasForeignKey(cni => cni.CreditNoteId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(cni => cni.Product)
                      .WithMany(p => p.CreditNoteItems)
                      .HasForeignKey(cni => cni.ProductId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ==========================================
            // CashTransaction Configuration
            // ==========================================
            modelBuilder.Entity<CashTransaction>(entity =>
            {
                entity.HasKey(ct => ct.Id);
                entity.Property(ct => ct.Amount).HasPrecision(18, 2);
                entity.Property(ct => ct.Notes).HasMaxLength(1000);

                entity.HasOne(ct => ct.Customer)
                      .WithMany(c => c.CashTransactions)
                      .HasForeignKey(ct => ct.CustomerId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(ct => ct.Vendor)
                      .WithMany(v => v.CashTransactions)
                      .HasForeignKey(ct => ct.VendorId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(ct => ct.SourceInvoice)
                      .WithMany(i => i.LinkedCashTransactions)
                      .HasForeignKey(ct => ct.SourceInvoiceId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(ct => ct.CreditNote)
                      .WithMany(cn => cn.LinkedCashTransactions)
                      .HasForeignKey(ct => ct.CreditNoteId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(ct => ct.ExpenseCategory)
                      .WithMany(ec => ec.CashTransactions)
                      .HasForeignKey(ct => ct.ExpenseCategoryId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(ct => ct.ReversalOfTransaction)
                      .WithMany(ct => ct.Reversals)
                      .HasForeignKey(ct => ct.ReversalOfTransactionId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(ct => ct.DailyClosing)
                      .WithMany(dc => dc.Transactions)
                      .HasForeignKey(ct => ct.DailyClosingId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ==========================================
            // PaymentAllocation Configuration
            // ==========================================
            modelBuilder.Entity<PaymentAllocation>(entity =>
            {
                entity.HasKey(pa => pa.Id);
                entity.Property(pa => pa.Amount).HasPrecision(18, 2);

                entity.HasOne(pa => pa.CashTransaction)
                      .WithMany(ct => ct.PaymentAllocations)
                      .HasForeignKey(pa => pa.CashTransactionId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(pa => pa.Invoice)
                      .WithMany(i => i.PaymentAllocations)
                      .HasForeignKey(pa => pa.InvoiceId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ==========================================
            // OpeningBalanceEntry Configuration
            // ==========================================
            modelBuilder.Entity<OpeningBalanceEntry>(entity =>
            {
                entity.HasKey(ob => ob.Id);
                entity.Property(ob => ob.Amount).HasPrecision(18, 2);
                entity.Property(ob => ob.Notes).HasMaxLength(500);

                entity.HasOne(ob => ob.Customer)
                      .WithMany(c => c.OpeningBalanceEntries)
                      .HasForeignKey(ob => ob.CustomerId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(ob => ob.Vendor)
                      .WithMany(v => v.OpeningBalanceEntries)
                      .HasForeignKey(ob => ob.VendorId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ==========================================
            // DailyClosing Configuration
            // ==========================================
            modelBuilder.Entity<DailyClosing>(entity =>
            {
                entity.HasKey(dc => dc.Id);
                
                // One record per business day
                entity.HasIndex(dc => dc.BusinessDate).IsUnique();

                entity.Property(dc => dc.OpeningCashAmount).HasPrecision(18, 2);
                entity.Property(dc => dc.ExpectedClosingCashAmount).HasPrecision(18, 2);
                entity.Property(dc => dc.CountedClosingCashAmount).HasPrecision(18, 2);
                entity.Property(dc => dc.DifferenceAmount).HasPrecision(18, 2);
                entity.Property(dc => dc.Notes).HasMaxLength(1000);
            });
        }
    }
}
