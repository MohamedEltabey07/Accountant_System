using AutoMapper;
using BLL.DTOs.CreditNote.InComingData;
using BLL.DTOs.CreditNote.OutGoingData;
using BLL.IService;
using DAL.Entities;
using DAL.Enums;
using DAL.IConfiguration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Service
{
    public class CreditNoteService : ICreditNoteService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;

        public CreditNoteService(IUnitOfWork unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task<int> CreateDraftCreditNoteAsync(CreateCreditNoteDraftDto dto)
        {
            if (dto == null || dto.Items == null || !dto.Items.Any()) throw new ArgumentNullException("بيانات طلب إنشاء إشعار الخصم غير مكتملة، يجب إرسال الأصناف المراد إرجاعها");
            if (dto.Items.GroupBy(x => x.ProductId).Any(g => g.Count() > 1))
                throw new InvalidOperationException("لا يمكن تكرار نفس المنتج أكثر من مرة في المرتجع");
            var checkInvoice = await _unitOfWork.InvoiceRepo.GetInvoiceWithItemsAsync(dto.OriginalInvoiceId);
            if (checkInvoice == null) throw new InvalidOperationException("الفاتورة الأصلية المحددة غير موجودة بالنظام");
            if (checkInvoice.Status != InvoiceStatus.Posted) throw new InvalidOperationException("لا يمكن عمل مرتجع إلا على فاتورة معتمدة ومرحلة فقط");
            var invoiceDetails = await _unitOfWork.CreditNoteRepo.GetTotalReturnedQtyForInvoiceItemAsync(dto.OriginalInvoiceId);
            var maxReturnedQuantity = 0.0m;
            var creditNotes = new CreditNote
            {
                CreditNoteNumber = await _unitOfWork.CreditNoteRepo.GetNextCreditNoteNumberAsync(),
                OriginalInvoiceId = dto.OriginalInvoiceId,
                TransactionDate = dto.TransactionDate,
                Reason = dto.Reason,
                Type = checkInvoice.Type == InvoiceType.VendorPurchase ? CreditNoteType.VendorReturn : CreditNoteType.CustomerReturn,
                Status = CreditNoteStatus.Draft
            };
            foreach (var item in dto.Items)
            {
                
                if (item.Quantity <= 0)
                    throw new InvalidOperationException("الكمية المرتجعة يجب أن تكون أكبر من الصفر");
                var product = checkInvoice.Items.Where(p => p.ProductId == item.ProductId).FirstOrDefault();
                if (product == null)
                    throw new InvalidOperationException($"المنتج رقم ({item.ProductId}) غير موجود بالفاتورة الأصلية");
                var originalQuantityInInvoice =product.Quantity;
                var checkItemExist = invoiceDetails.TryGetValue(item.ProductId, out var quantity);
                if (checkItemExist)
                {
                    maxReturnedQuantity =originalQuantityInInvoice - quantity;
                    if (item.Quantity > maxReturnedQuantity) throw new InvalidOperationException($"الكمية المطلوب إرجاعها من المنتج ({product.ProductName}) تتجاوز الكمية المتبقية المسموح بإرجاعها ({maxReturnedQuantity})");
                }
                if (item.Quantity > originalQuantityInInvoice)
                    throw new InvalidOperationException($"الكمية المطلوب إرجاعها من المنتج ({product.ProductName}) تتجاوز الكمية المتبقية المسموح بإرجاعها ({maxReturnedQuantity})");
                var creditItems = new CreditNoteItem
                {
                    LineTotal = item.Quantity * product.UnitPrice,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = product.UnitPrice,
                    ProductName = product.ProductName
                };
                creditNotes.Items.Add(creditItems);
            }
            creditNotes.TotalAmount = creditNotes.Items.Sum(l => l.LineTotal);
            await _unitOfWork.CreditNoteRepo.AddAsync(creditNotes);
            await _unitOfWork.SaveChangesAsync();
            return creditNotes.Id;
        }

        public async Task<bool> PostCreditNoteAsync(PostCreditNoteDto dto)
        {
            if (dto == null) throw new ArgumentNullException("بيانات ترحيل إشعار الارجاع غير موجودة");
            if (dto.CreditNoteId == 0) throw new ArgumentNullException("يجب تحديد رقم إشعار الخصم المراد ترحيله");
            var creditNote = await _unitOfWork.CreditNoteRepo.GetByIdWithItemsAsync(dto.CreditNoteId);
            if (creditNote == null) throw new NullReferenceException("إشعار الخصم المحدد غير موجود بالنظام");
            if (creditNote.Status != CreditNoteStatus.Draft) throw new InvalidOperationException("لا يمكن ترحيل إشعار الخصم إلا إذا كان في حالة مسودة (Draft) فقط");
            var checkInvoice = await _unitOfWork.InvoiceRepo.GetInvoiceWithItemsAsync(creditNote.OriginalInvoiceId);
            if (checkInvoice == null) throw new NullReferenceException("تعذر الوصول إلى الفاتورة الأصلية المرتبطة بهذا الإشعار");
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                if (dto.IsCashRefund)
                {
                    var cashTrans = new CashTransaction();
                    if (creditNote.Type == CreditNoteType.CustomerReturn)
                    {
                        cashTrans.TransactionDate = DateTime.UtcNow;
                        cashTrans.CreditNoteId = creditNote.Id;
                        cashTrans.Direction = CashDirection.Out;
                        cashTrans.Category = CashCategory.CustomerRefund;
                        cashTrans.CreatedAt = DateTime.UtcNow;
                        cashTrans.SourceInvoiceId = checkInvoice.Id;
                        cashTrans.CustomerId = checkInvoice.CustomerId;
                        cashTrans.Amount = creditNote.TotalAmount;
                    }
                    else if (creditNote.Type == CreditNoteType.VendorReturn)
                    {
                        cashTrans.TransactionDate = DateTime.UtcNow;
                        cashTrans.CreditNoteId = creditNote.Id;
                        cashTrans.Direction = CashDirection.In;
                        cashTrans.Category = CashCategory.VendorRefund;
                        cashTrans.CreatedAt = DateTime.UtcNow;
                        cashTrans.SourceInvoiceId = checkInvoice.Id;
                        cashTrans.VendorId = checkInvoice.VendorId;
                        cashTrans.Amount = creditNote.TotalAmount;
                    }
                    await _unitOfWork.CashTransactionRepo.AddAsync(cashTrans);
                   
                }
                creditNote.Status = CreditNoteStatus.Posted;
                creditNote.PostedAt = DateTime.UtcNow;
                _unitOfWork.CreditNoteRepo.Update(creditNote);

                // Returned frozen fish physically re-enters the freezers: when the
                // original invoice was a maritime supply, post an In stock movement
                // per returned line (atomically with the credit note posting).
                if (creditNote.Type == CreditNoteType.CustomerReturn &&
                    checkInvoice.Type == InvoiceType.MaritimeSupply)
                {
                    foreach (var returnedItem in creditNote.Items)
                    {
                        var product = await _unitOfWork.ProductRepo.GetByIdAsync(returnedItem.ProductId);
                        if (product == null || product.Category != ProductCategory.FrozenFish)
                            continue;

                        var perCarton = product.DefaultCartonWeightKg ?? 0m;
                        var returnMovement = new StockMovement
                        {
                            ProductId = returnedItem.ProductId,
                            ProductName = returnedItem.ProductName,
                            MovementType = StockMovementType.In,
                            MovementDate = creditNote.TransactionDate,
                            Cartons = perCarton > 0 ? StockMovementService.Round(returnedItem.Quantity / perCarton) : 0m,
                            WeightPerCartonKg = perCarton > 0 ? StockMovementService.Round(perCarton) : null,
                            TotalWeightKg = StockMovementService.Round(returnedItem.Quantity),
                            SourceInvoiceId = checkInvoice.Id,
                            Reason = $"إرجاع سمك مجمد للثلاجة بموجب إشعار المرتجع ({creditNote.CreditNoteNumber})",
                            CreatedAt = DateTime.UtcNow
                        };

                        await _unitOfWork.StockMovementRepo.AddAsync(returnMovement);
                    }
                }

                await _unitOfWork.SaveChangesAsync();
                await _unitOfWork.CommitTransactionAsync();
                return true;
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
            return true;
        }

        public async Task<CreditNoteDto?> GetCreditNoteByIdAsync(int id)
        {
            if (id <= 0) throw new ArgumentNullException("معرف إشعار الخصم غير صالح، يجب أن يكون أكبر من صفر");
            var creditNotesWithDetails = await _unitOfWork.CreditNoteRepo.GetByIdWithItemsAsync(id);
            if (creditNotesWithDetails == null) return null;
            var mappedCreditNote = _mapper.Map<CreditNoteDto>(creditNotesWithDetails);
            mappedCreditNote.OriginalInvoiceNumber = creditNotesWithDetails.OriginalInvoice.InvoiceNumber;
            return mappedCreditNote;
        }

        public async Task<List<CreditNoteDto>> GetCreditNotesAsync(CreditNoteType? type, CreditNoteStatus? status,DateTime from,DateTime to)
        {
            var filteredCreditNoteList = await _unitOfWork.CreditNoteRepo.GetFilteredCreditNotesAsync(type, status, from, to);
            if (filteredCreditNoteList == null || !filteredCreditNoteList.Any()) return new List<CreditNoteDto>();
            var mappedCreditNote = _mapper.Map<List<CreditNoteDto>>(filteredCreditNoteList);
            return mappedCreditNote;
        }
    }
}
