namespace MakeItMeta.Tools.Results;

public record Unit
{
    public static Unit Default { get; } = new Unit();
}

public record Result
{
    public static Result<T> Success<T>(T value) => new Success<T>(value);

    public static Result Success() => new Success<Unit>(Unit.Default);

    public static Result Error(params Error[] errors) => new ErrorResult<Unit>(errors);

    public static implicit operator Result(Error[] value)
        => new ErrorResult<Unit>(value);
    
    public static implicit operator Result(Error value)
        => new ErrorResult<Unit>(value);
    
    public static implicit operator Result(UnwrapErrors errors)
        => new ErrorResult<Unit>(errors.Errors);
}

public record Result<T> : Result
{
    public static implicit operator Result<T>(T value)
        => new Success<T>(value);

    public static implicit operator Result<T>(Error value)
        => new ErrorResult<T>(value);

    public static implicit operator Result<T>(Error[] value)
        => new ErrorResult<T>(value);
    
    public static implicit operator Result<T>(UnwrapErrors errors)
        => new ErrorResult<T>(errors.Errors);
}

public record Success<T>(T Value) : Result<T>;

public record Error(string Code, string Message)
{
    public Error WithMessage(string message) => new(Code, message);
}

public readonly record struct UnwrapErrors(Error[] Errors)
{
    public static implicit operator bool(UnwrapErrors result)
        => result != default;
}

public record ErrorResult<T>(params Error[] Errors) : Result<T>, IErrorResult;

public interface IErrorResult
{
    Error[] Errors { get; }
}

public static class ResultExtensions
{
    public static (T Value, UnwrapErrors) Unwrap<T>(this Result<T> result)
        => result switch
        {
            Success<T> success => (success.Value, default),
            ErrorResult<T> errorResult => (default!, new UnwrapErrors(errorResult.Errors)),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
        };
    
    public static UnwrapErrors Unwrap(this Result result)
        => result switch
        {
            IErrorResult errorResult => new UnwrapErrors(errorResult.Errors),
            _ => default
        };
}