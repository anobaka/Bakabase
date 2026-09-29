using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Modules.ThirdParty.Abstractions.Logging
{
    public class ThirdPartyRequestLog
    {
        private long _receivedBytes;

        public string Key { get; set; }
        public ThirdPartyId ThirdPartyId { get; set; }
        public DateTime RequestTime { get; set; }
        public long ElapsedMs { get; set; }
        public ThirdPartyRequestResultType Result { get; set; }
        public string Message { get; set; }

        /// <summary>Response-body bytes actually read by the caller.</summary>
        public long ReceivedBytes => Interlocked.Read(ref _receivedBytes);

        public void AddReceivedBytes(long bytes) => Interlocked.Add(ref _receivedBytes, bytes);
    }
}
