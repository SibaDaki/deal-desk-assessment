namespace DealDesk.Domain.Common
{
    /// <summary>
    /// Result pattern used across the domain and application layers so business
    /// rules can fail with typed errors instead of exceptions.
    /// </summary>
    public class Result
    {
        protected Result(bool isSuccess, Error? error)
        {
            if (isSuccess && error is not null)
                throw new InvalidOperationException("A successful result cannot carry an error.");
            if (!isSuccess && error is null)
                throw new InvalidOperationException("A failed result requires an error.");

            IsSuccess = isSuccess;
            Error = error;
        }

        public bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;
        public Error? Error { get; }

        public static Result Success() => new(true, null);
        public static Result Failure(Error error) => new(false, error);
        public static Result<T> Success<T>(T value) => new(value, true, null);
        public static Result<T> Failure<T>(Error error) => new(default, false, error);
    }

    public sealed class Result<T> : Result
    {
        private readonly T? _value;

        internal Result(T? value, bool isSuccess, Error? error) : base(isSuccess, error)
        {
            _value = value;
        }

        public T Value => IsSuccess
            ? _value!
            : throw new InvalidOperationException("Cannot read the value of a failed result.");
    }
}
