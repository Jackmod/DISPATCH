using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Resilience;

/// <summary>Why an attempt failed, which decides whether retrying is worth it.</summary>
public enum FailureClass
{
    /// <summary>Network, timeout, a locked file, a 5xx — things that fix themselves.</summary>
    Transient,

    /// <summary>A corrupt archive, a permission error — retrying changes nothing.</summary>
    Permanent,
}

/// <summary>Raised to tell the retry policy how to treat a failure.</summary>
public sealed class RetryableException(string message, FailureClass failureClass, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>How this failure should be treated.</summary>
    public FailureClass FailureClass { get; } = failureClass;
}

/// <summary>
/// The one retry policy the whole app uses.
/// </summary>
/// <remarks>
/// Three attempts, backing off 1s / 4s / 12s with jitter. Only transient
/// failures retry — a corrupt archive or a permission error will not fix itself,
/// and retrying it just wastes the user's time before the same failure. Every
/// attempt is logged with its reason, and after the final failure the caller is
/// expected to mark the item <em>needs attention</em> and carry on rather than
/// letting one mod stop forty.
///
/// <para>
/// The delay is injectable so tests run instantly rather than actually waiting
/// seventeen seconds to prove the backoff schedule.
/// </para>
/// </remarks>
public sealed class RetryPolicy
{
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(12),
    ];

    private readonly ILogger<RetryPolicy> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<double> _jitter;

    /// <summary>Constructs the policy.</summary>
    /// <param name="logger">Diagnostics.</param>
    /// <param name="delay">How to wait between attempts. Defaults to Task.Delay; tests pass a no-op.</param>
    /// <param name="jitter">Returns 0..1 for the jitter fraction. Defaults to Random.Shared.</param>
    public RetryPolicy(
        ILogger<RetryPolicy> logger,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<double>? jitter = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _delay = delay ?? Task.Delay;
        _jitter = jitter ?? (() => Random.Shared.NextDouble());
    }

    /// <summary>Total attempts before giving up.</summary>
    public int MaxAttempts => Backoff.Length;

    /// <summary>
    /// Runs an operation, retrying transient failures on the backoff schedule.
    /// </summary>
    /// <param name="operation">
    /// The work. Throw <see cref="RetryableException"/> to classify a failure;
    /// any other exception is treated as permanent and not retried.
    /// </param>
    /// <param name="description">What this is, for the log.</param>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string description,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var failureClass = ex is RetryableException retryable ? retryable.FailureClass : FailureClass.Permanent;

                // A permanent failure, or the last attempt, ends it now.
                if (failureClass == FailureClass.Permanent || attempt >= MaxAttempts)
                {
                    _logger.LogWarning(ex,
                        "{Description} failed on attempt {Attempt} ({Class}); giving up",
                        description, attempt, failureClass);
                    throw;
                }

                var wait = WithJitter(Backoff[attempt - 1]);
                _logger.LogInformation(
                    "{Description} failed on attempt {Attempt} ({Class}); retrying in {Delay}",
                    description, attempt, failureClass, wait);

                await _delay(wait, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>The void-returning overload.</summary>
    public Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        string description,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<object?>(async ct => { await operation(ct).ConfigureAwait(false); return null; },
            description, cancellationToken);

    private TimeSpan WithJitter(TimeSpan baseDelay)
    {
        // Up to 25% added, so a fleet of retries does not thunder in lockstep.
        var extra = baseDelay.TotalMilliseconds * 0.25 * _jitter();
        return baseDelay + TimeSpan.FromMilliseconds(extra);
    }
}
