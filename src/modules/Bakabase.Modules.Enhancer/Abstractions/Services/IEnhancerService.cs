namespace Bakabase.Modules.Enhancer.Abstractions.Services;

public interface IEnhancerService
{
    Task EnhanceResource(int resourceId);
    Task EnhanceAll();
    Task ReapplyEnhancementsToResources(int categoryId);
}ryId);
}