namespace VideoOzet.Business.Exceptions;

public class ContentGenerationException : PipelineException
{
    public ContentGenerationException(string message) : base(message) { }
    public ContentGenerationException(string message, Exception innerException) : base(message, innerException) { }
}
