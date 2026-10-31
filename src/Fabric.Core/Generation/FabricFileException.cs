namespace Fabric.Core.Generation;

public sealed class FabricFileException : Exception
{
    public FabricFileException()
    {
    }

    public FabricFileException(string message)
        : base(message)
    {
    }

    public FabricFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
