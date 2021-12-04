namespace NiCloud;

public record NiCloudDirectoryServiceInfo(string Dsid)
{
    public string LastName { get; set; }
    public bool HsaEnabled { get; set; }
    public string Locale { get; set; }
    public bool IsManagedAppleID { get; set; }
    public object[] AppleIdAliases { get; set; }
    public int HsaVersion { get; set; }
    public string CountryCode { get; set; }
    public bool PrimaryEmailVerified { get; set; }
    public bool Locked { get; set; }
    public string PrimaryEmail { get; set; }
    public string FullName { get; set; }
    public string LanguageCode { get; set; }
    public string AppleId { get; set; }
    public string FirstName { get; set; }
    public string ICloudAppleIdAlias { get; set; }
    public int StatusCode { get; set; }
}