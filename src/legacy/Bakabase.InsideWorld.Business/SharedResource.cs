namespace Bakabase.InsideWorld.Business
{
    /// <summary>
    /// todo: why this class is required?
    /// </summary>
    public class SharedResource
    {
        public const string Downloader_KeyIsMissing = nameof(Downloader_KeyIsMissing);
        public const string Downloader_MayBeDuplicate = nameof(Downloader_MayBeDuplicate);
        public const string Downloader_BilibiliFavoritesIsMissing = nameof(Downloader_BilibiliFavoritesIsMissing);
        public const string Downloader_BilibiliFavoritesDoesNotExist = nameof(Downloader_BilibiliFavoritesDoesNotExist);
        public const string Downloader_BilibiliCookieIsInvalid = nameof(Downloader_BilibiliCookieIsInvalid);
        public const string Downloader_DownloaderCountExceeded = nameof(Downloader_DownloaderCountExceeded);
        public const string Downloader_DownloaderIsNotFound = nameof(Downloader_DownloaderIsNotFound);

        public const string Downloader_DownloaderOptionsValidatorNotRegistered =
            nameof(Downloader_DownloaderOptionsValidatorNotRegistered);

        public const string Downloader_UnknownThirdPartyId = nameof(Downloader_UnknownThirdPartyId);
        public const string Downloader_UnknownType = nameof(Downloader_UnknownType);

        public const string FileMover_DuplicateSourcesAreFound = nameof(FileMover_DuplicateSourcesAreFound);
        public const string FileMover_DuplicateTargetsAreFound = nameof(FileMover_DuplicateTargetsAreFound);
        public const string FileMover_TargetsCantBeSubOfSources = nameof(FileMover_TargetsCantBeSubOfSources);
        public const string FileMover_SourcesCantBeSubOfSources = nameof(FileMover_SourcesCantBeSubOfSources);

        public const string TypeIsNotFound = nameof(TypeIsNotFound);

        public const string Component_OptionsAreNotConfigured = nameof(Component_OptionsAreNotConfigured);
        public const string Component_Invalid = nameof(Component_Invalid);
        public const string Component_KeyCanNotBeEmpty = nameof(Component_KeyCanNotBeEmpty);
        public const string Component_TypeMismatch = nameof(Component_TypeMismatch);
        public const string Component_DuplicatesAreFound = nameof(Component_DuplicatesAreFound);
        public const string Component_OptionsWithIdAreNotFound = nameof(Component_OptionsWithIdAreNotFound);

        public const string Component_OptionsAppliedToWrongTypeDescriptor =
            nameof(Component_OptionsAppliedToWrongTypeDescriptor);

        public const string Category_ComponentWithTypeHasNotBeenConfigured =
            nameof(Category_ComponentWithTypeHasNotBeenConfigured);

        public const string Category_MediaLibraryExistsOnDeletion = nameof(Category_MediaLibraryExistsOnDeletion);
    }
}