using System;

namespace NiCloud;

[Serializable]
internal class NiCloudApiResponseException : Exception
{
    public int Code { get; init; }

    public bool Retry { get; init; }

    public NiCloudApiResponseException(string message) : base(message)
    {
    }

    public NiCloudApiResponseException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public NiCloudApiResponseException()
    {
    }
}
