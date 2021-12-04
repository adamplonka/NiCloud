using System;
using System.Runtime.Serialization;

namespace NiCloud;

[Serializable]
internal class NiCloudSessionExpired : Exception
{
    public NiCloudSessionExpired()
    {
    }

    public NiCloudSessionExpired(string message) : base(message)
    {
    }

    public NiCloudSessionExpired(string message, Exception innerException) : base(message, innerException)
    {
    }

    protected NiCloudSessionExpired(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}