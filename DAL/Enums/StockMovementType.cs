namespace DAL.Enums
{
    // Immutable stock ledger movement types.
    // In: goods entered the freezers (receipt / opening balance / returned fish).
    // Out: goods physically left the freezers (maritime dispatch / authorized withdrawal).
    // Adjustment: signed correction from physical count (increase or decrease, reason required).
    public enum StockMovementType
    {
        In = 1,
        Out = 2,
        Adjustment = 3
    }
}
