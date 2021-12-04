using System;
using System.Runtime.Serialization;

namespace NiCloud;

[Serializable]
internal class NiCloudServiceNotActivatedException : Exception
{
    public NiCloudServiceNotActivatedException()
    {
    }

    public NiCloudServiceNotActivatedException(string message) : base(message)
    {
    }

    public NiCloudServiceNotActivatedException(string message, Exception innerException) : base(message, innerException)
    {
    }

    protected NiCloudServiceNotActivatedException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}