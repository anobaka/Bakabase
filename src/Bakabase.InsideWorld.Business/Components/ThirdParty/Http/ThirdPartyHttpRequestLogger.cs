using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Logging;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Http
{
    public class ThirdPartyHttpRequestLogger
    {
        private readonly ConcurrentDictionary<ThirdPartyId, ConcurrentBag<ThirdPartyRequestLog>> _logs = new();
        private ILogger<ThirdPartyHttpRequestLogger> _logger;

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
            string key = null,
            Func<HttpResponseMessage, Exception, ThirdPartyRequestResultType> getResultType = null,
            CancellationToken? ct = null, Func<HttpResponseMessage, Exception, string> buildCustomMessage = null)
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
                _logger.LogError(ex, $"[{(int) tp}:{tp}][{id}]An error occurred: {ex.Message}.");
                e = ex;
                throw;
            }
            finally
            {
                sw.Stop();
                var elapsedMs = sw.ElapsedMilliseconds;
                var message = buildCustomMessage?.Invoke(rsp, e) ?? e?.BuildFullInformationText();
                var resultType = getResultType?.Invoke(rsp, e) ?? DefaultGetResultType(rsp, e, ct);

                _logs.GetOrAdd(tp, _ => new ConcurrentBag<ThirdPartyRequestLog>()).Add(new ThirdPartyRequestLog
                {
                    ThirdPartyId = tp,
                    Result = resultType,
                    RequestTime = requestTime,
                    ElapsedMs = elapsedMs,
                    Message = message,
                    Key = key
                });
            }

            return rsp;
        }

        public HttpResponseMessage Capture(ThirdPartyId tp, Func<HttpResponseMessage> request,
            string key = null,
            Func<HttpResponseMessage, Exception, ThirdPartyRequestResultType> getResultType = null,
            CancellationToken? ct = null, Func<HttpResponseMessage, Exception, string> buildCustomMessage = null)
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
                _logger.LogError(ex, $"[{(int) tp}:{tp}][{id}]An error occurred: {ex.Message}.");
                e = ex;
                throw;
            }
            finally
            {
                sw.Stop();

                var elapsedMs = sw.ElapsedMilliseconds;
                var message = buildCustomMessage?.Invoke(rsp, e) ?? e?.BuildFullInformationText();
                var resultType = getResultType?.Invoke(rsp, e) ?? DefaultGetResultType(rsp, e, ct);

                _logs.GetOrAdd(tp, _ => new ConcurrentBag<ThirdPartyRequestLog>()).Add(new ThirdPartyRequestLog
                {
                    ThirdPartyId = tp,
                    Result = resultType,
                    RequestTime = requestTime,
                    ElapsedMs = elapsedMs,
                    Message = message,
                    Key = key
                });
            }

            return rsp;
        }


        public IDictionary<ThirdPartyId, ThirdPartyRequestLog[]> Logs =>
            _logs.ToDictionary(a => a.Key, a => a.Value.ToArray());

        public void Reset()
        {
            _logs.Clear();
        }
    }
}