using BLL.DTOs.Invoice.InComingData;
using BLL.Service;
using Xunit;
using InvoiceDto = BLL.DTOs.Invoice.OutGoingData.InvoiceDto;

namespace Accountant_sys.Tests
{
    // Tafqeet (تفقيط) acceptance checks: the recited Arabic words must always match
    // the printed N2 amount. The engine pre-rounds with MidpointRounding.AwayFromZero
    // (banker-free half-up rounding) — exactly like the "N2" rendering on invoices.
    public class TafqeetTests
    {
        [Fact]
        public void FractionalAmount_1_996_RoundsTo2_00_RecitesTwoPounds_NoCoins()
        {
            // Printed value: 1.996m -> N2 -> "2.00". Words must say 2 pounds, zero piasters.
            var words = Tafqeet.ToArabicWords(1.996m);
            Assert.Equal("فقط اثنان جنيهاً مصرياً لا غير", words);
            Assert.DoesNotContain("قرش", words);
            Assert.Equal("2.00", 1.996m.ToString("N2"));
        }

        [Fact]
        public void FractionalAmount_1_995_PreRoundsTo_2_00_MidpointUp()
        {
            Assert.Equal("2.00", 1.995m.ToString("N2"));
            Assert.Equal("فقط اثنان جنيهاً مصرياً لا غير", Tafqeet.ToArabicWords(1.995m));
        }

        [Fact]
        public void Piaster_OverflowCarries_Into_Whole_Pounds()
        {
            // 0.995 prints as 1.00 — exactly one pound, no "مائة قرشاً".
            Assert.Equal("1.00", 0.995m.ToString("N2"));

            var words = Tafqeet.ToArabicWords(0.995m);
            Assert.Equal("فقط واحد جنيهاً مصرياً لا غير", words);
        }

        [Fact]
        public void PoundsAndPiasters_AreBothRecited()
        {
            Assert.Equal("فقط اثنان جنيهاً مصرياً و خمسون قرشاً لا غير", Tafqeet.ToArabicWords(2.50m));
            Assert.Equal("فقط ألفان و خمسمائة جنيهاً مصرياً و خمسة و عشرون قرشاً لا غير",
                Tafqeet.ToArabicWords(2500.25m));
        }

        [Fact]
        public void ThousandsAndHundreds_ComposeArabicGrammar()
        {
            Assert.Equal("فقط ألف جنيهاً مصرياً لا غير", Tafqeet.ToArabicWords(1000m));
            Assert.Equal("فقط مائتان و خمسة عشر جنيهاً مصرياً لا غير", Tafqeet.ToArabicWords(215m));
            Assert.Equal("فقط ثلاثة جنيهاً مصرياً لا غير", Tafqeet.ToArabicWords(3.00m));
        }

        [Fact]
        public void Zero_RecitesExplicitZero()
        {
            Assert.Equal("صفر جنيه مصري لا غير", Tafqeet.ToArabicWords(0m));
            Assert.Equal("صفر جنيه مصري لا غير", Tafqeet.ToArabicWords(0.001m)); // rounds to 0.00
        }

        [Fact]
        public void InvoicePrintContract_TotalAmountMatchesRecitedWords()
        {
            // End-to-end contract with the invoice printing path: invoice totals are
            // computed by the service and rendered with ToString("N2"); the recited
            // words over that same value must not disagree with the printed figure.
            var words = Tafqeet.ToArabicWords(12345.678m); // prints 12,345.68
            Assert.Equal("12,345.68", 12345.678m.ToString("N2"));
            Assert.StartsWith("فقط اثنا عشر ألفاً و ثلاثمائة", words);
            Assert.Contains("ثمانية و ستون قرشاً", words);
            Assert.EndsWith("لا غير", words);
        }
    }
}
