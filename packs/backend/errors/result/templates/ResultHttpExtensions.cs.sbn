using Microsoft.AspNetCore.Http.HttpResults;

namespace Microsoft.AspNetCore.Http;

public static class ResultHttpExtensions
{
    /// <summary>200 with the value, or problem details carrying the error's status and code.</summary>
    /// <remarks>The typed union tells the OpenAPI document what a success returns, so clients are generated with its type.</remarks>
    public static Results<Ok<TValue>, ProblemHttpResult> ToHttpResult<TValue, TError>(this Result<TValue, TError> result)
        where TValue : notnull
        where TError : ApiError =>
        result.Match<Results<Ok<TValue>, ProblemHttpResult>>(
            value => TypedResults.Ok(value),
            error => TypedResults.Problem(
                title: error.Message,
                statusCode: (int)error.Status,
                extensions: new Dictionary<string, object?> { ["code"] = error.Code }));
}
