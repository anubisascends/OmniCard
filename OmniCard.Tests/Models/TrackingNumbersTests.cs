using OmniCard.Shared.Sales;

namespace OmniCard.Tests.Models;

public class TrackingNumbersTests
{
    private const string UspsTracking = "9400111899223456789012";

    [Theory]
    [InlineData(" 1z 999-aa1 0123456784 ", "1Z999AA10123456784")]
    [InlineData("(420)90210", "42090210")]
    [InlineData("\u001d4209021094001", "4209021094001")] // GS1 FNC1 separator
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Normalize_KeepsUpperCaseAlphanumericsOnly(string? input, string expected) =>
        Assert.Equal(expected, TrackingNumbers.Normalize(input));

    [Theory]
    [InlineData("42090210" + UspsTracking, UspsTracking)]         // USPS IMpb, ZIP5
    [InlineData("420902101234" + UspsTracking, UspsTracking)]     // USPS IMpb, ZIP+4
    [InlineData("9612019123456789012345", "123456789012345")]      // FedEx Ground "96" barcode
    [InlineData("1001901781000001000400123456789012", "123456789012")] // FedEx Express 34-digit
    [InlineData("1Z999AA10123456784", "1Z999AA10123456784")]       // UPS: as-is
    [InlineData(UspsTracking, UspsTracking)]                        // already bare
    public void Extract_StripsCarrierRoutingData(string scanned, string expected) =>
        Assert.Equal(expected, TrackingNumbers.Extract(scanned));

    [Theory]
    [InlineData(UspsTracking, "42090210" + UspsTracking)]
    [InlineData(UspsTracking, "420902101234" + UspsTracking)]
    [InlineData("123456789012", "1001901781000001000400123456789012")]
    [InlineData("1Z999AA10123456784", "1z999aa10123456784")]
    [InlineData("9400 1118 9922 3456 7890 12", UspsTracking)]
    [InlineData("42090210" + UspsTracking, UspsTracking)] // stored the raw barcode, typed the bare number
    public void Matches_LabelBarcodeToStoredTrackingNumber(string stored, string scanned) =>
        Assert.True(TrackingNumbers.Matches(stored, scanned));

    [Theory]
    [InlineData("12345", "9999912345")]             // too short to match by suffix
    [InlineData(UspsTracking, "9400111899223456789013")]
    [InlineData("", UspsTracking)]
    [InlineData(UspsTracking, "")]
    public void Matches_RejectsDifferentOrTooShortValues(string stored, string scanned) =>
        Assert.False(TrackingNumbers.Matches(stored, scanned));
}
