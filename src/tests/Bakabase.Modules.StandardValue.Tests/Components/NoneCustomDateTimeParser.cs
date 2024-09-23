using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.Modules.StandardValue.Tests.Components
{
    public class NoneCustomDateTimeParser : ICustomDateTimeParser
    {
        public Task<DateTime?> TryToParseDateTime(string? str)
        {
            return Task.FromResult<DateTime?>(default);
        }
    }
}