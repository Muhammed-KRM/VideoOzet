namespace VideoOzet.Business.Exceptions;

public class NotFoundException : BusinessException
{
    public NotFoundException(string entityName, object key)
        : base($"'{entityName}' bulunamadı. Aranan anahtar: {key}") { }
}
