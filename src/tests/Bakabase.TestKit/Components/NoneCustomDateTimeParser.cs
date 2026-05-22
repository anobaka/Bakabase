using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.TestKit.Components
{
    public class NoneCustomDateTimeParser : ICustomDateTimeParser
    {
        public Task<DateTime?> TryToParseDateTime(string? str)
        {
            return Task.FromResult<DateTime?>(default);
        }
    }
}