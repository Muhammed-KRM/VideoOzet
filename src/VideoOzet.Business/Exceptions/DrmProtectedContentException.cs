using System;

namespace VideoOzet.Business.Exceptions;

public class DrmProtectedContentException : BusinessException
{
    public const string DefaultMessage =
        "Bu içerik DRM korumalı olduğu için indirilemez; lütfen yetkili API/orijinal dosya kaynağını kullanın.";

    public DrmProtectedContentException() : base(DefaultMessage)
    {
    }

    public DrmProtectedContentException(string message)
        : base(string.IsNullOrWhiteSpace(message) ? DefaultMessage : message)
    {
    }

    public DrmProtectedContentException(string message, Exception innerException)
        : base(string.IsNullOrWhiteSpace(message) ? DefaultMessage : message, innerException)
    {
    }
}
