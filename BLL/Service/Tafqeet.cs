using System;

namespace BLL.Service
{
    // Single source of truth for Tafqeet (تفقيط) — converting EGP amounts to
    // Arabic words on invoices, receipts, statements and daily-closing reports.
    // Call contract: always pass the pre-rounded N2 value (the same value that
    // is rendered with ToString("N2")) so the recited words can never drift
    // from the printed number (e.g. 1.996m is rendered as 2.00 and must
    // therefore be recited as "اثنان جنيهاً مصرياً").
    public static class Tafqeet
    {
        private static readonly string[] Ones = { "", "واحد", "اثنان", "ثلاثة", "أربعة", "خمسة", "ستة", "سبعة", "ثمانية", "تسعة", "عشرة", "أحد عشر", "اثنا عشر", "ثلاثة عشر", "أربعة عشر", "خمسة عشر", "ستة عشر", "سبعة عشر", "ثمانية عشر", "تسعة عشر" };
        private static readonly string[] Tens = { "", "", "عشرون", "ثلاثون", "أربعون", "خمسون", "ستون", "سبعون", "ثمانون", "تسعون" };
        private static readonly string[] Hundreds = { "", "مائة", "مائتان", "ثلاثمائة", "أربعمائة", "خمسمائة", "ستمائة", "سبعمائة", "ثمانمائة", "تسعمائة" };

        public static string ToArabicWords(decimal amount)
        {
            amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
            if (amount == 0) return "صفر جنيه مصري لا غير";
            long integerPart = (long)Math.Floor(amount);
            int decimalPart = (int)Math.Round((amount - integerPart) * 100);
            if (decimalPart >= 100)
            {
                integerPart += decimalPart / 100;
                decimalPart %= 100;
            }

            string words = ConvertGroup(integerPart);
            string result = "فقط " + words + " جنيهاً مصرياً";
            if (decimalPart > 0)
            {
                result += " و " + ConvertGroup(decimalPart) + " قرشاً";
            }
            result += " لا غير";
            return result;
        }

        private static string ConvertGroup(long number)
        {
            if (number == 0) return "";
            if (number < 20) return Ones[number];
            if (number < 100)
            {
                long unit = number % 10;
                long ten = number / 10;
                return (unit > 0 ? Ones[unit] + " و " : "") + Tens[ten];
            }
            if (number < 1000)
            {
                long rem = number % 100;
                long hundred = number / 100;
                return Hundreds[hundred] + (rem > 0 ? " و " + ConvertGroup(rem) : "");
            }
            if (number < 1000000)
            {
                long rem = number % 1000;
                long thousands = number / 1000;
                string thText = thousands == 1 ? "ألف" : (thousands == 2 ? "ألفان" : (thousands <= 10 ? ConvertGroup(thousands) + " آلاف" : ConvertGroup(thousands) + " ألفاً"));
                return thText + (rem > 0 ? " و " + ConvertGroup(rem) : "");
            }
            long millRem = number % 1000000;
            long millions = number / 1000000;
            string millText = millions == 1 ? "مليون" : (millions == 2 ? "مليونان" : (millions <= 10 ? ConvertGroup(millions) + " ملايين" : ConvertGroup(millions) + " مليوناً"));
            return millText + (millRem > 0 ? " و " + ConvertGroup(millRem) : "");
        }
    }
}
