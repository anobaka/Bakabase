using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Enhancer.Components.Enhancers;

public abstract class
    AbstractKeywordEnhancer<TEnumTarget, TContext, TEnhancerOptions>(
        ILoggerFactory loggerFactory,
        IFileManager fileManager,
        IStandardValueService standardValueService,
        ISpecialTextService specialTextService)
    : AbstractEnhancer<TEnumTarget, TContext,
        TEnhancerOptions>(loggerFactory,
        fileManager)
    where TEnumTarget : Enum
    where TEnhancerOptions : class?
    where TContext : class?
{
    protected override async Task<TContext?> BuildContext(Resource resource, EnhancerFullOptions options,
        CancellationToken ct)
    {
        string? keyword = null;
        if (options.KeywordProperty != null)
        {
            if (options.KeywordProperty.Pool == PropertyPool.Internal)
            {
                switch ((InternalProperty)options.KeywordProperty.Id)
                {
                    case InternalProperty.Filename:
                        break;
                    case InternalProperty.RootPath:
                    case InternalProperty.ParentResource:
                    case InternalProperty.Resource:
                    case InternalProperty.DirectoryPath:
                    case InternalProperty.CreatedAt:
                    case InternalProperty.FileCreatedAt:
                    case InternalProperty.FileModifiedAt:
                    case InternalProperty.Category:
                    case InternalProperty.MediaLibrary:
                    case InternalProperty.MediaLibraryV2:
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            else
            {
                var hit = resource.Properties?.GetValueOrDefault((int)options.KeywordProperty.Pool)
                    ?.GetValueOrDefault(options.KeywordProperty.Id);
                if (hit != null)
                {
                    var bv = hit.Values?.FirstOrDefault(x => x.Scope == (int)options.KeywordProperty.Scope)?.BizValue;
                    var val =
                        await standardValueService.Convert(bv, hit.BizValueType, StandardValueType.String) as string;
                    keyword = val;
                }
            }
            if (keyword.IsNullOrEmpty())
            {
                Logger.LogWarning($"Keyword property has been set to [{options.KeywordProperty.Pool}]{options.KeywordProperty.Id} from scope [{(PropertyValueScope)options.KeywordProperty.Scope}] but got empty value, use file/folder name as fallback");
            }
        }

        if (keyword.IsNullOrEmpty())
        {
            keyword = resource.IsFile ? Path.GetFileNameWithoutExtension(resource.FileName) : resource.FileName;
        }

        if (options.PretreatKeyword == true)
        {
            keyword = await specialTextService.Pretreatment(keyword);
        }

        return await BuildContextInternal(keyword, resource, options, ct);
    }

    protected abstract Task<TContext?> BuildContextInternal(string keyword, Resource resource,
        EnhancerFullOptions options,
        CancellationToken ct);
}