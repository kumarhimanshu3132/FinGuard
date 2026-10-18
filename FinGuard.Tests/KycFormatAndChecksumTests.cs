using System;
using FinGuard.Utilities;
using Xunit;

namespace FinGuard.Tests
{
    public class KycFormatAndChecksumTests
    {
        [Theory]
        [InlineData("54829104826")] // Check digit generator tests
        [InlineData("98765432109")]
        [InlineData("36759821405")]
        public void VerhoeffAlgorithm_GeneratesAndValidates_ValidChecksum(string base11)
        {
            int checkDigit = KycFormatHelper.GenerateVerhoeffCheckDigit(base11);
            string full12 = base11 + checkDigit;

            var (isValid, errorMessage) = KycFormatHelper.ValidateAadhaar(full12);

            Assert.True(isValid, $"Aadhaar {full12} should be valid. Error: {errorMessage}");
            Assert.Null(errorMessage);
        }

        [Theory]
        [InlineData("999999990019")] // Official UIDAI test vector
        [InlineData("548291048263")] // Known mathematically valid 12-digit vector
        [InlineData("367598214056")] // 36759821405 + check digit 6
        public void ValidateAadhaar_KnownValidTestVectors_ReturnsValid(string aadhaar)
        {
            var (isValid, errorMessage) = KycFormatHelper.ValidateAadhaar(aadhaar);
            Assert.True(isValid, $"Aadhaar {aadhaar} should be valid but failed with: {errorMessage}");
            Assert.Null(errorMessage);
        }

        [Fact]
        public void Verhoeff_CheckDigit_Generates_ExactExpectedDigit()
        {
            int cd = KycFormatHelper.GenerateVerhoeffCheckDigit("98765432109");
            string full = "98765432109" + cd;
            var (isValid, err) = KycFormatHelper.ValidateAadhaar(full);
            Assert.True(isValid, $"Calculated full Aadhaar {full} failed: {err}");
        }

        [Theory]
        [InlineData("9999 9999 0019")] // Standard 4-digit grouped spaces
        [InlineData("5482 9104 8263")]
        [InlineData("9999-9999-0019")] // Hyphen delimited
        [InlineData("5482-9104-8263")]
        [InlineData("  5482 9104 8263  ")] // Leading and trailing whitespace
        [InlineData("9999  9999  0019")] // Multiple internal spaces
        public void ValidateAadhaar_FormattedInput_NormalizesAndValidates(string formattedAadhaar)
        {
            var (isValid, errorMessage) = KycFormatHelper.ValidateAadhaar(formattedAadhaar);
            Assert.True(isValid, $"Formatted Aadhaar '{formattedAadhaar}' should be valid after normalization. Error: {errorMessage}");
            Assert.Null(errorMessage);
        }

        [Theory]
        [InlineData("548291048261")] // The exact failing number (check digit 1 instead of 3)
        [InlineData("999999990018")] // Check digit 8 instead of 9
        [InlineData("5482 9104 8261")] // Formatted with corrupted check digit
        [InlineData("9876 5432 1090")] // Check digit 0 instead of 2
        public void ValidateAadhaar_InvalidChecksums_ReturnsInvalidWithDescriptiveMessage(string invalidAadhaar)
        {
            var (isValid, errorMessage) = KycFormatHelper.ValidateAadhaar(invalidAadhaar);
            Assert.False(isValid, $"Aadhaar '{invalidAadhaar}' should have failed checksum validation.");
            Assert.Contains("The Aadhaar checksum is invalid. Please check the number entered", errorMessage);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void ValidateAadhaar_NullOrEmpty_ReturnsInvalid(string? input)
        {
            var (isValid, errorMessage) = KycFormatHelper.ValidateAadhaar(input);
            Assert.False(isValid);
            Assert.Contains("required", errorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Please enter a valid 12-digit Aadhaar number", errorMessage);
        }

        [Theory]
        [InlineData("12345")] // Too short
        [InlineData("54829104826")] // 11 digits (missing check digit)
        [InlineData("1234567890123")] // Too long (13)
        [InlineData("5482910482631")] // 13 digits
        [InlineData("ABC123456789")] // Non-numeric
        [InlineData("5482A1048263")] // Alpha character inside
        [InlineData("5482#9104#826")] // Special characters
        public void ValidateAadhaar_InvalidLengthOrChars_ReturnsInvalid(string input)
        {
            var (isValid, errorMessage) = KycFormatHelper.ValidateAadhaar(input);
            Assert.False(isValid);
            Assert.Contains("Please enter a valid 12-digit Aadhaar number", errorMessage);
        }

        [Theory]
        [InlineData("048291048263")] // Starts with 0
        [InlineData("148291048263")] // Starts with 1
        public void ValidateAadhaar_StartsWithZeroOrOne_ReturnsInvalid(string input)
        {
            var (isValid, errorMessage) = KycFormatHelper.ValidateAadhaar(input);
            Assert.False(isValid);
            Assert.Contains("cannot start with 0 or 1", errorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("222222222222")] // Repeated digits
        [InlineData("999999999999")]
        public void ValidateAadhaar_AllSameDigits_ReturnsInvalid(string input)
        {
            var (isValid, errorMessage) = KycFormatHelper.ValidateAadhaar(input);
            Assert.False(isValid);
            Assert.Contains("The Aadhaar checksum is invalid. Please check the number entered", errorMessage);
        }

        [Fact]
        public void ValidateAadhaar_CorruptedCheckDigit_FailsVerhoeffCheck()
        {
            string base11 = "54829104826";
            int checkDigit = KycFormatHelper.GenerateVerhoeffCheckDigit(base11);
            int wrongCheckDigit = (checkDigit + 1) % 10;
            string corrupted = base11 + wrongCheckDigit;

            var (isValid, errorMessage) = KycFormatHelper.ValidateAadhaar(corrupted);

            Assert.False(isValid);
            Assert.Contains("The Aadhaar checksum is invalid. Please check the number entered", errorMessage);
            Assert.Contains("Verhoeff checksum", errorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("548291048261", "•••• •••• 8261")]
        [InlineData("5482 9104 8261", "•••• •••• 8261")]
        public void MaskAadhaar_ReturnsPermittedMaskedFormatOnly(string input, string expectedMask)
        {
            string masked = KycFormatHelper.MaskAadhaar(input);
            Assert.Equal(expectedMask, masked);
            Assert.DoesNotContain("5482", masked); // Raw prefix must never appear
        }

        [Theory]
        [InlineData("ABCDE1234F")] // Valid Individual PAN
        [InlineData("BKRPK1049M")]
        [InlineData("XYZPA9876Q")]
        public void ValidatePan_ValidFormats_ReturnsTrue(string pan)
        {
            var (isValid, errorMessage) = KycFormatHelper.ValidatePan(pan);
            Assert.True(isValid, $"PAN {pan} should be valid. Error: {errorMessage}");
            Assert.Null(errorMessage);
        }

        [Theory]
        [InlineData("ABCDE1234")] // 9 chars
        [InlineData("ABCDE12345F")] // 11 chars
        [InlineData("12345ABCDE")] // Inverted
        [InlineData("ABC1E1234F")] // Digit in first 3 chars
        [InlineData("ABCDE12345")] // Ending with digit instead of letter
        public void ValidatePan_InvalidFormats_ReturnsFalse(string pan)
        {
            var (isValid, errorMessage) = KycFormatHelper.ValidatePan(pan);
            Assert.False(isValid);
            Assert.NotNull(errorMessage);
        }

        [Theory]
        [InlineData("ABCD#1234F")] // Special character in letters
        [InlineData("ABCDE12#4F")] // Special character in numbers
        [InlineData("ABCD 1234F")] // Space inside
        public void ValidatePan_InvalidCharacters_ReturnsFalse(string pan)
        {
            var (isValid, errorMessage) = KycFormatHelper.ValidatePan(pan);
            Assert.False(isValid);
            Assert.Contains("Invalid PAN format", errorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MaskPan_MasksMiddleDigitsCorrectly()
        {
            string pan = "ABCDE1234F";
            string masked = KycFormatHelper.MaskPan(pan);
            Assert.Equal("ABCDE••••F", masked);
            Assert.DoesNotContain("1234", masked);
        }
    }
}
