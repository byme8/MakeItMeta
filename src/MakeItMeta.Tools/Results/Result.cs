namespace MakeItMeta.Tools.Results;

public record Unit
{
    public static Unit Default { get; } = new Unit();
}

public class Result
{
    public static Result<T> Success<T>(T value) => new Success<T>(value);

    public static Result Success() => new Success<Unit>(Unit.Default);

    public static Result Error(params Error[] errors) => new ErrorResult<Unit>(errors);

    public static Result<T> Error<T>(params Error[] errors) => new ErrorResult<T>(errors);

    public static implicit operator Result(Error[] value)
        => new ErrorResult<Unit>(value);

    public static implicit operator Result(Error value)
        => new ErrorResult<Unit>(value);

    public static implicit operator Result(UnwrapErrors errors)
        => new ErrorResult<Unit>(errors.Errors);
}

public class Result<T> : Result
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

public class Success<T> : Result<T>
{
    public Success(T value)
    {
        Value = value;
    }

    public T Value { get; }
}

public class Error
{
    public Error(string code, string message)
    {
        Code = code;
        Message = message;
    }

    public Error WithMessage(string message) => new(Code, message);
    public string Code { get; }
    public string Message { get; }
}

public readonly struct UnwrapErrors
{
    public UnwrapErrors(Error[] errors)
    {
        Errors = errors;
    }

    public static implicit operator bool(UnwrapErrors? result)
        => result?.Errors != default;

    public Error[] Errors { get; }

    public void Deconstruct(out Error[] errors)
    {
        errors = Errors;
    }
}

public class ErrorResult<T> : Result<T>, IErrorResult
{
    public ErrorResult(params Error[] errors)
    {
        Errors = errors;
    }

    public Error[] Errors { get; }
}

public interface IErrorResult
{
    Error[] Errors { get; }
}

public static class ResultExtensions
{
    public static (T Value, UnwrapErrors Error) Unwrap<T>(this Result<T> result)
        => result switch
        {
            Success<T> success => (success.Value, default),
            ErrorResult<T> errorResult => (default!, new UnwrapErrors(errorResult.Errors)),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
        };

    public static (T[] Value, UnwrapErrors Error) Unwrap<T>(this IEnumerable<Result<T>> result)
    {
        var unwraps = result
            .Select(o => o.Unwrap())
            .ToArray();

        if (unwraps.Any(o => o.Error))
        {
            var errors = unwraps
                .Where(o => o.Error)
                .SelectMany(o => o.Error.Errors)
                .ToArray();

            return (default!, new UnwrapErrors(errors));
        }

        var results = unwraps
            .Select(o => o.Value)
            .ToArray();
        
        return (results, default);
    }

    public static UnwrapErrors Unwrap(this Result result)
        => result switch
        {
            IErrorResult errorResult => new UnwrapErrors(errorResult.Errors),
            _ => default
        };
}