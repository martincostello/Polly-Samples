﻿using PollyDemos.OutputHelpers;

namespace PollyDemos.Async
{
    /// <summary>
    /// Demonstrates Retry strategy with calculated retry delays to back off.
    /// Loops through a series of HTTP requests, keeping track of each requested
    /// item and reporting server failures when encountering exceptions.
    ///
    /// Observations: All calls still succeed!  Yay!
    /// But we didn't hammer the underlying server so hard - we backed off.
    /// That's healthier for it, if it might be struggling ...
    /// ... and if a lot of clients might be doing this simultaneously.
    ///
    /// ... What if the underlying system was totally down tho?
    /// ... Keeping trying forever would be counterproductive (so, see Demo06)
    /// </summary>
    public class AsyncDemo05_WaitAndRetryWithExponentialBackoff : AsyncDemo
    {
        public override string Description =>
            "This demonstrates exponential back-off. We have enough retries to ensure success. But we don't hammer the server so hard: we increase the delay between each try.";

        public override async Task ExecuteAsync(CancellationToken cancellationToken, IProgress<DemoProgress> progress)
        {
            ArgumentNullException.ThrowIfNull(progress);

            EventualSuccesses = 0;
            Retries = 0;
            EventualFailures = 0;
            TotalRequests = 0;

            PrintHeader(progress);

            var strategy = new ResiliencePipelineBuilder().AddRetry(new()
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                MaxRetryAttempts = 6, // We could also retry indefinitely by using int.MaxValue
                BackoffType = DelayBackoffType.Exponential, // Back off: 1s, 2s, 4s, 8s, ... + jitter
                OnRetry = args =>
                {
                    var exception = args.Outcome.Exception!;
                    progress.Report(ProgressWithMessage($"Strategy logging: {exception.Message}", Color.Yellow));
                    progress.Report(ProgressWithMessage($" ... automatically delaying for {args.RetryDelay.TotalMilliseconds}ms.", Color.Yellow));
                    Retries++;
                    return default;
                }
            }).Build();

            var client = new HttpClient();
            var internalCancel = false;

            while (!(internalCancel || cancellationToken.IsCancellationRequested))
            {
                TotalRequests++;

                try
                {
                    await strategy.ExecuteAsync(async token =>
                    {
                        var responseBody = await IssueRequestAndProcessResponseAsync(client, token);
                        progress.Report(ProgressWithMessage($"Response : {responseBody}", Color.Green));
                        EventualSuccesses++;

                    }, cancellationToken);
                }
                catch (Exception e)
                {
                    progress.Report(ProgressWithMessage($"Request {TotalRequests} eventually failed with: {e.Message}", Color.Red));
                    EventualFailures++;
                }

                await Task.Delay(TimeSpan.FromSeconds(0.5), cancellationToken);
                internalCancel = ShouldTerminateByKeyPress();
            }
        }

        public override Statistic[] LatestStatistics => new Statistic[]
        {
            new("Total requests made", TotalRequests),
            new("Requests which eventually succeeded", EventualSuccesses, Color.Green),
            new("Retries made to help achieve success", Retries, Color.Yellow),
            new("Requests which eventually failed", EventualFailures, Color.Red),
        };
    }
}
