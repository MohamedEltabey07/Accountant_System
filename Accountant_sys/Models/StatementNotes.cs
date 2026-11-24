namespace Accountant_sys.Models
{
    /// <summary>
    /// Controls which free-text voucher notes are fit for the official printed
    /// statement. The system has no structured "public note" field, so long or
    /// clearly internal/colloquial notes are suppressed from print and remain
    /// available in the screen's detailed mode.
    /// </summary>
    public static class StatementNotes
    {
        private static readonly string[] InternalMarkers =
        {
            "المفروض", "عشان", "هيبقوا", "خصمهم", "بيد", "علي اخده", "كان هو دافع"
        };

        public static string? GetFullNote(string? notes)
            => string.IsNullOrWhiteSpace(notes)
                ? null
                : string.Join(" ", notes.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        public static string? GetPublicNote(string? notes, int maxLength = 120)
        {
            var cleaned = GetFullNote(notes);
            if (cleaned == null) return null;
            if (cleaned.Length > maxLength) return null;
            if (InternalMarkers.Any(m => cleaned.Contains(m, StringComparison.OrdinalIgnoreCase))) return null;
            return cleaned;
        }
    }
}
