using System.Collections.Concurrent;
using System.Diagnostics;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Logging;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;

namespace Bakabase.Modules.ThirdParty.Abstractions.Http
{
    public class ThirdPartyHttpRequestLogger
    {
        private readonly ConcurrentDictionary<ThirdPartyId, ConcurrentBag<ThirdPartyRequestLog>> _logs = new();
        private ILogger<ThirdPartyHttpRequestLogger> _logger;
        
        public event EventHandler<ThirdPartyRequestCompletedEventArgs>? OnRequestCompleted;
        public event EventHandler? OnTrafficChanged;

        public ThirdPartyHttpRequestLogger(ILogger<ThirdPartyHttpRequestLogger> logger)
        {
            _logger = logger;
        }

        private static ThirdPartyRequestResultType DefaultGetResultType(HttpResponseMessage rsp, Exception e,
            CancellationToken? ct = null)
        {
            if (e is OperationCanceledException oce)
            {
                return oce.CancellationToken == ct
                    ? ThirdPartyRequestResultType.Canceled
                    : ThirdPartyRequestResultType.TimedOut;
            }

            return rsp is not {IsSuccessStatusCode: true}
                ? ThirdPartyRequestResultType.Failed
                : ThirdPartyRequestResultType.Succeed;
        }

        public async Task<HttpResponseMessage> CaptureAsync(ThirdPartyId tp, Func<Task<HttpResponseMessage>> request,
            string? key = null,
            Func<HttpResponseMessage, Exception, ThirdPartyRequestResultType>? getResultType = null,
            CancellationToken? ct = null, Func<HttpResponseMessage, Exception, string>? buildCustomMessage = null,
            bool redactExceptionDetails = false)
        {
            var id = Guid.NewGuid().ToString("N")[..6];
            _logger.LogInformation($"[{(int) tp}:{tp}][{id}]Sending request to {key}.");
            var sw = Stopwatch.StartNew();
            var requestTime = DateTime.Now;
            HttpResponseMessage rsp = null;
            Exception e = null;
            try
            {
                rsp = await request();
                sw.Stop();
                _logger.LogInformation(
                    $"[{(int) tp}:{tp}][{id}]Request has been done successfully in {sw.ElapsedMilliseconds}ms.");
            }
            catch (Exception ex)
            {
                LogRequestFailure(tp, id, ex, redactExceptionDetails);
                e = ex;
                throw;
            }
            finally
            {
                sw.Stop();
                var elapsedMs = sw.ElapsedMilliseconds;
                var message = redactExceptionDetails ? RedactedExceptionDetails(e) :
                    buildCustomMessage?.Invoke(rsp, e) ?? e?.BuildFullInformationText();
                var resultType = getResultType?.Invoke(rsp, e) ?? DefaultGetResultType(rsp, e, ct);

                RecordCompletedRequest(tp, rsp, resultType, requestTime, elapsedMs, message, key);
            }

            return rsp;
        }

        public HttpResponseMessage Capture(ThirdPartyId tp, Func<HttpResponseMessage> request,
            string? key = null,
            Func<HttpResponseMessage, Exception, ThirdPartyRequestResultType>? getResultType = null,
            CancellationToken? ct = null, Func<HttpResponseMessage, Exception, string>? buildCustomMessage = null,
            bool redactExceptionDetails = false)
        {
            var id = Guid.NewGuid().ToString("N")[..6];
            _logger.LogInformation($"[{(int) tp}:{tp}][{id}]Sending request to {key}.");
            var sw = Stopwatch.StartNew();
            var requestTime = DateTime.Now;
            HttpResponseMessage rsp = null;
            Exception e = null;
            try
            {
                rsp = request();
                sw.Stop();
                _logger.LogInformation(
                    $"[{(int) tp}:{tp}][{id}]Request has been done successfully in {sw.ElapsedMilliseconds}ms.");
            }
            catch (Exception ex)
            {
                LogRequestFailure(tp, id, ex, redactExceptionDetails);
                e = ex;
                throw;
            }
            finally
            {
                sw.Stop();

                var elapsedMs = sw.ElapsedMilliseconds;
                var message = redactExceptionDetails ? RedactedExceptionDetails(e) :
                    buildCustomMessage?.Invoke(rsp, e) ?? e?.BuildFullInformationText();
                var resultType = getResultType?.Invoke(rsp, e) ?? DefaultGetResultType(rsp, e, ct);

                RecordCompletedRequest(tp, rsp, resultType, requestTime, elapsedMs, message, key);
            }

            return rsp;
        }

        private void LogRequestFailure(ThirdPartyId tp, string id, Exception error, bool redactDetails)
        {
            if (redactDetails)
            {
                // Transport exceptions can contain the real URI or a response echoing its token.
                // Do not pass the original exception to logging providers, including its inner errors.
                _logger.LogError($"[{(int) tp}:{tp}][{id}]An error occurred: {RedactedExceptionDetails(error)}.");
            }
            else
            {
                _logger.LogError(error, $"[{(int) tp}:{tp}][{id}]An error occurred: {error.Message}.");
            }
        }

        private static string? RedactedExceptionDetails(Exception? error) => error switch
        {
            HttpRequestException requestError => $"HttpRequestException ({requestError.HttpRequestError}" +
                (requestError.StatusCode is { } status ? $", HTTP {(int) status})" : ")"),
            HttpIOException ioError => $"HttpIOException ({ioError.HttpRequestError})",
            null => null,
            _ => error.GetType().Name
        };


        public IDictionary<ThirdPartyId, ThirdPartyRequestLog[]> Logs =>
            _logs.ToDictionary(a => a.Key, a => a.Value.ToArray());

        public void Reset()
        {
            _logs.Clear();
        }

        private void RecordCompletedRequest(ThirdPartyId tp, HttpResponseMessage? response,
            ThirdPartyRequestResultType resultType, DateTime requestTime, long elapsedMs, string? message,
            string? key)
        {
            var log = new ThirdPartyRequestLog
            {
                ThirdPartyId = tp,
                Result = resultType,
                RequestTime = requestTime,
                ElapsedMs = elapsedMs,
                Message = message,
                Key = key
            };
            _logs.GetOrAdd(tp, _ => new ConcurrentBag<ThirdPartyRequestLog>()).Add(log);

            if (response?.Content is { } content)
            {
                // Send/SendAsync can return before any response body is read. Count reads rather
                // than Content-Length so partial and chunked responses report the actual payload.
                var notificationGate = new object();
                long bytesSinceNotification = 0;
                var lastNotification = Stopwatch.GetTimestamp();

                response.Content = new ResponseTrafficTrackingContent(content, (bytes, finished) =>
                {
                    var notify = false;
                    lock (notificationGate)
                    {
                        if (bytes > 0)
                        {
                            log.AddReceivedBytes(bytes);
                            bytesSinceNotification += bytes;
                        }

                        if (bytesSinceNotification > 0 &&
                            (finished || Stopwatch.GetElapsedTime(lastNotification) >= TimeSpan.FromSeconds(1)))
                        {
                            bytesSinceNotification = 0;
                            lastNotification = Stopwatch.GetTimestamp();
                            notify = true;
                        }
                    }

                    if (notify)
                    {
                        OnTrafficChanged?.Invoke(this, EventArgs.Empty);
                    }
                });
            }

            _onRequestCompleted(tp, resultType);
        }
        
        private void _onRequestCompleted(ThirdPartyId thirdPartyId, ThirdPartyRequestResultType resultType)
        {
            OnRequestCompleted?.Invoke(this, new ThirdPartyRequestCompletedEventArgs(thirdPartyId, resultType));
        }
    }
}
