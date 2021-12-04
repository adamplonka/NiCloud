using System;

namespace NiCloud;

[Serializable]
internal class NiCloudFailedLoginException : Exception
{
    public NiCloudFailedLoginException(string message) : base(message)
    {
    }

    public NiCloudFailedLoginException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public NiCloudFailedLoginException()
    {
    }
}
