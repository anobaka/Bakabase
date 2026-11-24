using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Service.Components.Acquisition.ProcessingPlans;
using Bakabase.Service.Components.Acquisition.Handoffs;
using Bakabase.Modules.Acquisition.Models.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bakabase.Service.Components.Acquisition.Planning;

public static class AcquisitionPlanRegistration
{
    public static IServiceCollection AddAcquisitionPlanning(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<AcquisitionOptions>, AcquisitionOptionsBinding>());
        services.AddScoped<IAcquisitionPlanBuilder, ExHentaiAcquisitionPlanBuilder>();
        services.AddScoped<AcquisitionPlanService>();
        services.AddScoped<AcquisitionArchivePasswordEvidence>();
        services.AddScoped<AcquisitionFileProcessingEvidence>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowRunSchedulingPolicy, AcquisitionWorkflowSchedulingPolicy>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowRunSourceSchedulingPolicy, AcquisitionWorkflowSchedulingPolicy>());
        services.AddScoped<Bakabase.Service.Components.FileProcessing.IArtifactCleanupOwnerScopeProvider, AcquisitionArtifactCleanupOwnerScope>();
        services.AddScoped<AcquisitionCreationOriginService>();
        services.AddScoped<IAcquisitionCreationOriginResolver>(provider => provider.GetRequiredService<AcquisitionCreationOriginService>());
        services.AddScoped<IAcquisitionCreationOriginProvider, PostParserCreationOriginProvider>();
        services.AddScoped<IAcquisitionRuntimeDownloadReader, AcquisitionRuntimeDownloadReader>();
        services.AddScoped(provider => new AcquisitionDownloadActivityProjection(
            provider.GetRequiredService<Bakabase.InsideWorld.Business.BakabaseDbContext>(),
            () => provider.GetRequiredService<IAcquisitionRuntimeDownloadReader>()));
        services.AddScoped<AcquisitionRuntimeOverviewService>();
        services.AddSingleton<AcquisitionThroughputHistory>();
        services.AddScoped<IAcquisitionThroughputDownloadSource, AcquisitionThroughputDownloadSource>();
        services.AddHostedService<AcquisitionThroughputSampler>();
        services.AddSingleton<AcquisitionConfirmationRuntime>();
        services.AddScoped<AcquisitionSourceAttentionProjection>();
        services.AddScoped<AcquisitionTaskSearchService>();
        services.AddSingleton<AcquisitionTaskDeletePreviewStore>();
        services.AddSingleton<AcquisitionRecoveryPreviewStore>();
        services.AddSingleton<AcquisitionRecoveryDiagnosisSessions>();
        services.AddSingleton<AcquisitionContinuationPreviewStore>();
        services.AddScoped<AcquisitionContinuationService>();
        services.AddScoped<AcquisitionAdaptiveMigrationService>();
        services.AddHostedService<AcquisitionContinuationWorker>();
        services.AddSingleton<AcquisitionStorageOverridePreviewStore>();
        services.AddScoped<AcquisitionStorageOverrideService>();
        services.AddScoped<AcquisitionTransferredDownloadRecovery>();
        services.AddScoped<IAcquisitionRecoveryDiagnostics, AcquisitionRecoveryDiagnostics>();
        services.AddScoped<AcquisitionRecoveryService>();
        services.AddSingleton<AcquisitionLocalRecoveryBatchPreviewStore>();
        services.AddSingleton<IAcquisitionBatchCapacityReader, AcquisitionBatchCapacityReader>();
        services.AddScoped<IAcquisitionBatchActionProvider, AcquisitionTransferReplayAction>();
        services.AddScoped<IAcquisitionBatchActionProvider, AcquisitionReanalysisAction>();
        services.AddScoped<AcquisitionLocalRecoveryBatchService>();
        services.AddHostedService<AcquisitionLocalRecoveryBatchWorker>();
        services.AddScoped<AcquisitionLocalFilesRecoveryService>();
        services.AddScoped<AcquisitionDownloadContinuationService>();
        services.AddScoped<IWorkflowRunStartupPolicy, AcquisitionDownloadContinuationStartupPolicy>();
        services.AddScoped<AcquisitionDownloadCompletionCoordinator>();
        services.AddSingleton<AcquisitionDownloadCompletionWakeup>();
        services.AddSingleton<IDownloadTaskExecutionLifecycle, AcquisitionDownloadExecutionLifecycle>();
        services.AddHostedService<AcquisitionDownloadCompletionWorker>();
        services.AddScoped<IAcquisitionDownloadOnlyRecoveryService, AcquisitionDownloadOnlyRecoveryService>();
        services.AddScoped<IAcquisitionOriginalDownloadStarter, AcquisitionOriginalDownloadStarter>();
        services.AddSingleton<AcquisitionDownloadDestinationPreviewStore>();
        services.AddScoped<AcquisitionDownloadDestinationService>();
        services.AddScoped<AcquisitionSourceCorrectionService>();
        services.AddSingleton<AcquisitionSourceRevisionPreviewStore>();
        services.AddScoped<IAcquisitionSourceRevisionCandidates, PostSourceRevisionCandidates>();
        services.AddScoped<IAcquisitionSourceRevisionSafety, AcquisitionSourceRevisionSafety>();
        services.AddScoped<AcquisitionSourceRevisionService>();
        services.AddScoped<IAcquisitionDeletionActivityReader, AcquisitionDeletionActivityReader>();
        services.AddScoped<AcquisitionTaskDeletionService>();
        services.AddScoped<AcquisitionTaskDeletionCleanupService>();
        services.AddScoped<AcquisitionSupersededRunCleanup>();
        services.AddScoped<Bakabase.Service.Components.FileProcessing.IArtifactCleanupOwnerPreparation, AcquisitionVerifiedDeliveryRunRetirement>();
        services.AddScoped<IWorkflowRunMutationGuard, AcquisitionWorkflowRunMutationGuard>();
        services.AddScoped<IDownloadTaskStartGuard, WorkflowOwnedDownloadStartGuard>();
        services.AddScoped<AcquisitionOutputDirectoryResolver>();
        services.AddScoped<AcquisitionDeliveryDirectoryResolver>();
        services.AddScoped<AcquisitionStoragePolicyDefaults>();
        services.AddScoped<AcquisitionSupersededDeliveryService>();
        services.AddSingleton<AcquisitionStoragePolicyPreviewStore>();
        services.AddScoped<AcquisitionDraftImportService>();
        services.AddScoped<SourceProcessingPlanService>();
        services.AddScoped<IProcessingPlanSourceReader, PostProcessingPlanSourceReader>();
        services.AddSingleton<IWorkflowActivity, AcquisitionPlanProcessingActivity>();
        services.AddSingleton<IWorkflowActivity, AcquisitionPlanContinueFilesActivity>();
        services.AddScoped<IWorkflowRunCompletionObserver, PostAcquisitionCompletionObserver>();
        services.AddScoped<PostAcquisitionHandoffService>();
        services.AddHostedService<AcquisitionHandoffWorker>();
        services.AddSingleton<AcquisitionDeliveryRetirementQueue>();
        services.AddHostedService<AcquisitionDeliveryRetirementWorker>();
        return services;
    }
}
