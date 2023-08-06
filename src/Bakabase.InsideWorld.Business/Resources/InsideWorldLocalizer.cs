using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;

namespace Bakabase.InsideWorld.Business.Resources
{
    /// <summary>
    /// todo: Redirect raw <see cref="IStringLocalizer"/> callings to here
    /// </summary>
    public class InsideWorldLocalizer : IStringLocalizer<Business.SharedResource>
    {
        private readonly IStringLocalizer<Business.SharedResource> _localizer;

        public InsideWorldLocalizer(IStringLocalizer<Business.SharedResource> localizer)
        {
            _localizer = localizer;
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
            _localizer.GetAllStrings(includeParentCultures);

        public LocalizedString this[string name] => _localizer[name];

        public LocalizedString this[string name, params object[] arguments] => _localizer[name, arguments];

        public string Component_NotDeletableWhenUsingByCategories(IEnumerable<string> categoryNames) =>
            this[nameof(Component_NotDeletableWhenUsingByCategories), string.Join(',', categoryNames)];

        public string Category_Invalid((string Name, string Error)[] nameAndErrors) => this[nameof(Category_Invalid),
            string.Join(Environment.NewLine, nameAndErrors.Select(a => $"{a.Name}:{a.Error}"))];

        public string CookieValidation_Fail(string url, string message, string content)
        {
            const int maxContentLength = 1000;
            var shortContent = content.Length > maxContentLength ? content[..maxContentLength] : content;
            return this[nameof(CookieValidation_Fail), url, message, shortContent];
        }

        public string PathsShouldBeInSameDirectory()
        {
            return this[nameof(PathsShouldBeInSameDirectory)];
        }
    }
}