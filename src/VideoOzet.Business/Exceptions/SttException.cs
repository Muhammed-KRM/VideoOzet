namespace VideoOzet.Business.Exceptions;

public class SttException : PipelineException
{
    public SttException(string message) : base(message) { }
    public SttException(string message, Exception innerException) : base(message, innerException) { }
}
