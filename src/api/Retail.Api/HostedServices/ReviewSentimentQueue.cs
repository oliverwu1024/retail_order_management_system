using System.Threading.Channels;

namespace Retail.Api.HostedServices;

/// <summary>
/// In-process queue of review ids awaiting sentiment scoring (REQUIREMENTS §6.3). A singleton wrapper
/// over an unbounded <see cref="Channel{T}"/>: <c>ReviewService</c> enqueues on insert (a direct write
/// — no MediatR, ADR-0002), and <c>ReviewSentimentHostedService</c> is the single reader.
/// </summary>
/// <remarks>
/// The channel is in-process, so on a crash/restart anything queued-but-unprocessed is lost — the
/// hosted service's slow re-scan of <c>ProcessedAt IS NULL</c> reviews is the restart-safety net.
/// The durable cross-instance version (Service Bus) is Phase 8.
/// </remarks>
public sealed class ReviewSentimentQueue
{
    private readonly Channel<Guid> _channel =
        Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Queues a review for scoring. Non-blocking; safe to call from the request path.</summary>
    public void Enqueue(Guid reviewId) => _channel.Writer.TryWrite(reviewId);

    /// <summary>The reader the hosted service drains.</summary>
    public ChannelReader<Guid> Reader => _channel.Reader;
}
