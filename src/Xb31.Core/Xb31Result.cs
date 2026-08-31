namespace Xb31.Core;

public sealed record Xb31Result(Xb31Status Status, Exception? Diagnostic = null)
{
    public bool IsSuccess => Status == Xb31Status.Success;

    public static Xb31Result Success { get; } = new(Xb31Status.Success);
}
