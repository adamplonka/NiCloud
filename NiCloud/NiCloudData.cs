using System.Collections.Generic;

namespace NiCloud;

public record NiCloudData
{
    public static readonly NiCloudData Empty = new() { IsEmpty = true };

    public bool IsEmpty { get; init; } = false;
    public IReadOnlyDictionary<string, NiCloudWebService> Webservices { get; init; }
    public IReadOnlyDictionary<string, NiCloudApp> Apps { get; init; }
    public NiCloudDirectoryServiceInfo DsInfo { get; init; }
    public bool? HsaTrustedBrowser { get; init; }
    public bool? HsaChallengeRequired { get; init; }
    public bool HasMinimumDeviceForPhotosWeb { get; set; }
    public bool ICDPEnabled { get; set; }
    public bool PcsEnabled { get; set; }
    public bool TermsUpdateNeeded { get; set; }
    public int Version { get; set; }
    public bool IsExtendedLogin { get; set; }
    public bool PcsServiceIdentitiesIncluded { get; set; }
    public bool IsRepairNeeded { get; set; }
}
