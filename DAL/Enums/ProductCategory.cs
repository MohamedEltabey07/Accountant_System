namespace DAL.Enums
{
    // RegularProduct = 1 preserves existing rows after migration (default value).
    // Only FrozenFish products participate in the freezer stock ledger.
    public enum ProductCategory
    {
        RegularProduct = 1,
        FrozenFish = 2
    }
}
