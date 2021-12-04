namespace NiCloud;

public record PhoneNumber(
    string NumberWithDialCode,
    string ObfuscatedNumber,
    string PushMode,
    int Id
)
{
    public override string ToString() =>
        ObfuscatedNumber?.Replace('•', '*');
}
