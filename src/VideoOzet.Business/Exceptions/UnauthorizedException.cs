namespace VideoOzet.Business.Exceptions;

public class UnauthorizedException : BusinessException
{
    public UnauthorizedException(string message = "Bu işlem için yetkiniz bulunmamaktadır.")
        : base(message) { }
}
