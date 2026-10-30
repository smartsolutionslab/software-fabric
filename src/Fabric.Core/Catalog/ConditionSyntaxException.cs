namespace Fabric.Core.Catalog;

public sealed class ConditionSyntaxException : Exception
{
    public ConditionSyntaxException()
    {
    }

    public ConditionSyntaxException(string message)
        : base(message)
    {
    }

    public ConditionSyntaxException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ConditionSyntaxException(string message, int column)
        : base(message)
    {
        Column = column;
    }

    /// <summary>1-based column in the condition text where parsing stopped.</summary>
    public int Column { get; }
}
