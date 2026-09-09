namespace VideoOzet.Business.Exceptions;

public class EmbeddingException : PipelineException
{
    public EmbeddingException(string message) : base(message) { }
    public EmbeddingException(string message, Exception innerException) : base(message, innerException) { }
}
