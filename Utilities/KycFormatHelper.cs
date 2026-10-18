using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace FinGuard.Utilities
{
    public static class KycFormatHelper
    {
        // The multiplication table (d)
        private static readonly int[,] D = new int[10, 10]
        {
            {0, 1, 2, 3, 4, 5, 6, 7, 8, 9},
            {1, 2, 3, 4, 0, 6, 7, 8, 9, 5},
            {2, 3, 4, 0, 1, 7, 8, 9, 5, 6},
            {3, 4, 0, 1, 2, 8, 9, 5, 6, 7},
            {4, 0, 1, 2, 3, 9, 5, 6, 7, 8},
            {5, 9, 8, 7, 6, 0, 4, 3, 2, 1},
            {6, 5, 9, 8, 7, 1, 0, 4, 3, 2},
            {7, 6, 5, 9, 8, 2, 1, 0, 4, 3},
            {8, 7, 6, 5, 9, 3, 2, 1, 0, 4},
            {9, 8, 7, 6, 5, 4, 3, 2, 1, 0}
        };

        // The permutation table (p)
        private static readonly int[,] P = new int[8, 10]
        {
            {0, 1, 2, 3, 4, 5, 6, 7, 8, 9},
            {1, 5, 7, 6, 2, 8, 3, 0, 9, 4},
            {5, 8, 0, 3, 7, 9, 6, 1, 4, 2},
            {8, 9, 1, 6, 0, 4, 3, 5, 2, 7},
            {9, 4, 5, 3, 1, 2, 6, 8, 7, 0},
            {4, 2, 8, 6, 5, 7, 3, 9, 0, 1},
            {2, 7, 9, 3, 8, 0, 6, 4, 1, 5},
            {7, 0, 4, 6, 9, 1, 3, 2, 5, 8}
        };

        // The inverse table (inv)
        private static readonly int[] Inv = new int[10] { 0, 4, 3, 2, 1, 5, 6, 7, 8, 9 };

        /// <summary>
        /// Validates an Aadhaar number according to UIDAI specifications:
        /// 1. Exactly 12 digits.
        /// 2. Does not start with 0 or 1.
        /// 3. Is not all identical digits.
        /// 4. Passes the Verhoeff dihedral group D5 checksum algorithm (bypassed in sandbox mode).
        /// </summary>
        public static (bool IsValid, string? ErrorMessage) ValidateAadhaar(string? aadhaar, bool isSandbox = false)
        {
            if (string.IsNullOrWhiteSpace(aadhaar))
            {
                return (false, "Aadhaar number is required. Please enter a valid 12-digit Aadhaar number.");
            }

            // Strip spaces, hyphens, and whitespace formatting characters
            string clean = Regex.Replace(aadhaar, @"[\s\-]", "");

            if (!Regex.IsMatch(clean, @"^\d{12}$"))
            {
                return (false, "Please enter a valid 12-digit Aadhaar number (must be exactly 12 numeric digits).");
            }

            if (clean.StartsWith("0") || clean.StartsWith("1"))
            {
                return (false, "Aadhaar number cannot start with 0 or 1 according to UIDAI regulations.");
            }

            // Reject all identical digits (e.g. 222222222222)
            if (clean.Distinct().Count() == 1)
            {
                return (false, "The Aadhaar checksum is invalid. Please check the number entered (repeated sequences are not valid).");
            }

            if (!isSandbox && !ValidateVerhoeff(clean))
            {
                return (false, "The Aadhaar checksum is invalid. Please check the number entered (Verhoeff checksum validation failed).");
            }

            return (true, null);
        }

        /// <summary>
        /// Official Verhoeff checksum algorithm implementation.
        /// </summary>
        public static bool ValidateVerhoeff(string num)
        {
            if (string.IsNullOrEmpty(num) || !num.All(char.IsDigit)) return false;

            int c = 0;
            char[] myArray = num.ToCharArray();
            Array.Reverse(myArray);

            for (int i = 0; i < myArray.Length; i++)
            {
                int digit = myArray[i] - '0';
                c = D[c, P[i % 8, digit]];
            }

            return c == 0;
        }

        /// <summary>
        /// Calculates the Verhoeff check digit for an 11-digit base number.
        /// </summary>
        public static int GenerateVerhoeffCheckDigit(string base11)
        {
            if (string.IsNullOrEmpty(base11) || !base11.All(char.IsDigit)) return 0;

            int c = 0;
            char[] myArray = base11.ToCharArray();
            Array.Reverse(myArray);

            for (int i = 0; i < myArray.Length; i++)
            {
                int digit = myArray[i] - '0';
                c = D[c, P[(i + 1) % 8, digit]];
            }

            return Inv[c];
        }

        /// <summary>
        /// Validates Permanent Account Number (PAN) format:
        /// 5 letters, 4 numbers, 1 letter. (e.g. ABCDE1234F).
        /// </summary>
        public static (bool IsValid, string? ErrorMessage) ValidatePan(string? pan)
        {
            if (string.IsNullOrWhiteSpace(pan))
            {
                return (false, "PAN is required.");
            }

            string clean = pan.Trim().ToUpperInvariant();

            if (!Regex.IsMatch(clean, @"^[A-Z]{5}[0-9]{4}[A-Z]{1}$"))
            {
                return (false, "Invalid PAN format. Must be 10 characters (5 uppercase letters, 4 numbers, 1 letter, e.g., ABCDE1234F).");
            }

            return (true, null);
        }

        /// <summary>
        /// Masks an Aadhaar number to display only the last 4 digits (e.g. •••• •••• 1234).
        /// Compliant with UIDAI and DPDP Act data minimization requirements.
        /// </summary>
        public static string MaskAadhaar(string? aadhaar)
        {
            if (string.IsNullOrWhiteSpace(aadhaar)) return "•••• •••• ••••";
            string clean = Regex.Replace(aadhaar, @"[\s\-]", "");
            if (clean.Length < 4) return "•••• •••• ••••";

            string last4 = clean.Substring(clean.Length - 4);
            return $"•••• •••• {last4}";
        }

        /// <summary>
        /// Masks a PAN to display the first 5 characters, 4 bullets, and the last check character (e.g. ABCDE••••F).
        /// </summary>
        public static string MaskPan(string? pan)
        {
            if (string.IsNullOrWhiteSpace(pan)) return "••••••••••";
            string clean = pan.Trim().ToUpperInvariant();
            if (clean.Length != 10) return "••••••••••";

            return $"{clean.Substring(0, 5)}••••{clean[9]}";
        }
    }
}
