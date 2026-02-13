using System.Diagnostics.CodeAnalysis;

namespace Hmp.Devops.Tools.EnvironmentRemover
{
#nullable enable
    public record Result<T>(
        bool IsSuccess,
        T? Value,
        string? ErrorMessage)
    {
        [MemberNotNullWhen(true, nameof(Value))]
        [MemberNotNullWhen(false, nameof(ErrorMessage))]
        public bool IsSuccessful => IsSuccess;
        public static Result<T> Success(T value) => new Result<T>(true, value, null);
        public static Result<T> Failure(string errorMessage) => new Result<T>(false, default, errorMessage);
    }
#nullable restore
}
