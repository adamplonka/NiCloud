using System;
using System.Runtime.Serialization;

namespace NiCloud;

[Serializable]
internal class NiCloud2saRequiredException : Exception
{
    public NiCloud2saRequiredException()
    {
    }

    public NiCloud2saRequiredException(string message) : base(message)
    {
    }

    public NiCloud2saRequiredException(string message, Exception innerException) : base(message, innerException)
    {
    }

    protected NiCloud2saRequiredException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}