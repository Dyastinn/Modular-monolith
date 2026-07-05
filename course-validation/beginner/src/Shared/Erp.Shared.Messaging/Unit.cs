namespace Erp.Shared.Messaging;

// A stand-in for "void" that still satisfies IRequest<TResponse>.
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}
