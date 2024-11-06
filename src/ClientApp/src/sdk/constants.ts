export enum BuiltinPropertyForDisplayName {Filename = 15}
export const builtinPropertyForDisplayNames = Object.keys(BuiltinPropertyForDisplayName).filter(k => typeof BuiltinPropertyForDisplayName[k] === 'number').map(t => ({label: t, value: BuiltinPropertyForDisplayName[t]}));
export enum TextProcessOperation {Remove = 1, SetWithFixedValue = 2, AddToStart = 3, AddToEnd = 4, AddToAnyPosition = 5, RemoveFromStart = 6, RemoveFromEnd = 7, RemoveFromAnyPosition = 8, ReplaceFromStart = 9, ReplaceFromEnd = 10, ReplaceFromAnyPosition = 11, ReplaceWithRegex = 12}
export const textProcessOperations = Object.keys(TextProcessOperation).filter(k => typeof TextProcessOperation[k] === 'number').map(t => ({label: t, value: TextProcessOperation[t]}));
export enum BackgroundTaskLevel {Default = 1, Critical = 2}
export const backgroundTaskLevels = Object.keys(BackgroundTaskLevel).filter(k => typeof BackgroundTaskLevel[k] === 'number').map(t => ({label: t, value: BackgroundTaskLevel[t]}));
export enum EnhancementType {Property = 1, File = 2}
export const enhancementTypes = Object.keys(EnhancementType).filter(k => typeof EnhancementType[k] === 'number').map(t => ({label: t, value: EnhancementType[t]}));
export enum ResourceTaskOperationOnComplete {RemoveOnResourceView = 1}
export const resourceTaskOperationOnCompletes = Object.keys(ResourceTaskOperationOnComplete).filter(k => typeof ResourceTaskOperationOnComplete[k] === 'number').map(t => ({label: t, value: ResourceTaskOperationOnComplete[t]}));
export enum IwFsAttribute {Hidden = 1}
export const iwFsAttributes = Object.keys(IwFsAttribute).filter(k => typeof IwFsAttribute[k] === 'number').map(t => ({label: t, value: IwFsAttribute[t]}));
export enum IwFsEntryChangeType {Created = 1, Renamed = 2, Changed = 3, Deleted = 4, TaskChanged = 5}
export const iwFsEntryChangeTypes = Object.keys(IwFsEntryChangeType).filter(k => typeof IwFsEntryChangeType[k] === 'number').map(t => ({label: t, value: IwFsEntryChangeType[t]}));
export enum IwFsEntryTaskOperationOnComplete {None = 0}
export const iwFsEntryTaskOperationOnCompletes = Object.keys(IwFsEntryTaskOperationOnComplete).filter(k => typeof IwFsEntryTaskOperationOnComplete[k] === 'number').map(t => ({label: t, value: IwFsEntryTaskOperationOnComplete[t]}));
export enum IwFsEntryTaskType {Decompressing = 1, Moving = 2}
export const iwFsEntryTaskTypes = Object.keys(IwFsEntryTaskType).filter(k => typeof IwFsEntryTaskType[k] === 'number').map(t => ({label: t, value: IwFsEntryTaskType[t]}));
export enum IwFsType {Unknown = 0, Directory = 100, Image = 200, CompressedFileEntry = 300, CompressedFilePart = 400, Symlink = 500, Video = 600, Audio = 700, Drive = 1000, Invalid = 10000}
export const iwFsTypes = Object.keys(IwFsType).filter(k => typeof IwFsType[k] === 'number').map(t => ({label: t, value: IwFsType[t]}));
export enum DownloaderStatus {JustCreated = 0, Starting = 100, Downloading = 200, Complete = 300, Failed = 400, Stopping = 500, Stopped = 600}
export const downloaderStatuses = Object.keys(DownloaderStatus).filter(k => typeof DownloaderStatus[k] === 'number').map(t => ({label: t, value: DownloaderStatus[t]}));
export enum DependentComponentStatus {NotInstalled = 1, Installed = 2, Installing = 3}
export const dependentComponentStatuses = Object.keys(DependentComponentStatus).filter(k => typeof DependentComponentStatus[k] === 'number').map(t => ({label: t, value: DependentComponentStatus[t]}));
export enum BmSimpleValueProcessorOperation {SetWithFixedValue = 1, SetWithDynamicValue = 2, Remove = 3}
export const bmSimpleValueProcessorOperations = Object.keys(BmSimpleValueProcessorOperation).filter(k => typeof BmSimpleValueProcessorOperation[k] === 'number').map(t => ({label: t, value: BmSimpleValueProcessorOperation[t]}));
export enum BmMultipleValueProcessorFilterBy {All = 1, Containing = 2, Matching = 3}
export const bmMultipleValueProcessorFilterBies = Object.keys(BmMultipleValueProcessorFilterBy).filter(k => typeof BmMultipleValueProcessorFilterBy[k] === 'number').map(t => ({label: t, value: BmMultipleValueProcessorFilterBy[t]}));
export enum BmMultipleValueProcessorOperation {SetWithFixedValue = 1, Add = 2, Modify = 3, Remove = 4}
export const bmMultipleValueProcessorOperations = Object.keys(BmMultipleValueProcessorOperation).filter(k => typeof BmMultipleValueProcessorOperation[k] === 'number').map(t => ({label: t, value: BmMultipleValueProcessorOperation[t]}));
export enum BulkModificationDiffOperation {None = 0, Ignore = 1, Replace = 2, Merge = 3}
export const bulkModificationDiffOperations = Object.keys(BulkModificationDiffOperation).filter(k => typeof BulkModificationDiffOperation[k] === 'number').map(t => ({label: t, value: BulkModificationDiffOperation[t]}));
export enum BulkModificationDiffType {Added = 1, Removed = 2, Modified = 3}
export const bulkModificationDiffTypes = Object.keys(BulkModificationDiffType).filter(k => typeof BulkModificationDiffType[k] === 'number').map(t => ({label: t, value: BulkModificationDiffType[t]}));
export enum BulkModificationFilterableProperty {Category = 1, MediaLibrary = 2, FileName = 4, DirectoryPath = 5, CreateDt = 7, FileCreateDt = 8, FileModifyDt = 9}
export const bulkModificationFilterableProperties = Object.keys(BulkModificationFilterableProperty).filter(k => typeof BulkModificationFilterableProperty[k] === 'number').map(t => ({label: t, value: BulkModificationFilterableProperty[t]}));
export enum BulkModificationFilterGroupOperation {And = 1, Or = 2}
export const bulkModificationFilterGroupOperations = Object.keys(BulkModificationFilterGroupOperation).filter(k => typeof BulkModificationFilterGroupOperation[k] === 'number').map(t => ({label: t, value: BulkModificationFilterGroupOperation[t]}));
export enum BulkModificationFilterOperation {Equals = 1, NotEquals = 2, Contains = 3, NotContains = 4, StartsWith = 5, NotStartsWith = 6, EndsWith = 7, NotEndsWith = 8, GreaterThan = 9, LessThan = 10, GreaterThanOrEquals = 11, LessThanOrEquals = 12, IsNull = 13, IsNotNull = 14, In = 15, NotIn = 16, Matches = 17, NotMatches = 18}
export const bulkModificationFilterOperations = Object.keys(BulkModificationFilterOperation).filter(k => typeof BulkModificationFilterOperation[k] === 'number').map(t => ({label: t, value: BulkModificationFilterOperation[t]}));
export enum BulkModificationModifiableProperty {FileName = 4, CreateDt = 7}
export const bulkModificationModifiableProperties = Object.keys(BulkModificationModifiableProperty).filter(k => typeof BulkModificationModifiableProperty[k] === 'number').map(t => ({label: t, value: BulkModificationModifiableProperty[t]}));
export enum BulkModificationProcessOperation {Add = 1, Modify = 2, Remove = 3}
export const bulkModificationProcessOperations = Object.keys(BulkModificationProcessOperation).filter(k => typeof BulkModificationProcessOperation[k] === 'number').map(t => ({label: t, value: BulkModificationProcessOperation[t]}));
export enum BulkModificationStatus {Processing = 1, Closed = 2}
export const bulkModificationStatuses = Object.keys(BulkModificationStatus).filter(k => typeof BulkModificationStatus[k] === 'number').map(t => ({label: t, value: BulkModificationStatus[t]}));
export enum BulkModificationVariableSource {None = 1, FileName = 2, FileNameWithoutExtension = 3, FullPath = 4, DirectoryName = 5}
export const bulkModificationVariableSources = Object.keys(BulkModificationVariableSource).filter(k => typeof BulkModificationVariableSource[k] === 'number').map(t => ({label: t, value: BulkModificationVariableSource[t]}));
export enum CloseBehavior {Prompt = 0, Exit = 1, Minimize = 2, Cancel = 1000}
export const closeBehaviors = Object.keys(CloseBehavior).filter(k => typeof CloseBehavior[k] === 'number').map(t => ({label: t, value: CloseBehavior[t]}));
export enum UiTheme {FollowSystem = 0, Light = 1, Dark = 2}
export const uiThemes = Object.keys(UiTheme).filter(k => typeof UiTheme[k] === 'number').map(t => ({label: t, value: UiTheme[t]}));
export enum UpdaterStatus {Idle = 1, Running = 2, PendingRestart = 3, UpToDate = 4, Failed = 5}
export const updaterStatuses = Object.keys(UpdaterStatus).filter(k => typeof UpdaterStatus[k] === 'number').map(t => ({label: t, value: UpdaterStatus[t]}));
export enum AppDistributionType {WindowsApp = 0, MacOsApp = 1, LinuxApp = 2, Android = 3, Ios = 4, WindowsServer = 5, LinuxServer = 6}
export const appDistributionTypes = Object.keys(AppDistributionType).filter(k => typeof AppDistributionType[k] === 'number').map(t => ({label: t, value: AppDistributionType[t]}));
export enum MigrationTiming {BeforeDbMigration = 1, AfterDbMigration = 2}
export const migrationTimings = Object.keys(MigrationTiming).filter(k => typeof MigrationTiming[k] === 'number').map(t => ({label: t, value: MigrationTiming[t]}));
export enum OsPlatform {Unknown = 0, Windows = 1, Osx = 2, Linux = 3, FreeBsd = 4}
export const osPlatforms = Object.keys(OsPlatform).filter(k => typeof OsPlatform[k] === 'number').map(t => ({label: t, value: OsPlatform[t]}));
export enum CategoryResourceDisplayNameSegmentType {StaticText = 1, Property = 2, LeftWrapper = 3, RightWrapper = 4}
export const categoryResourceDisplayNameSegmentTypes = Object.keys(CategoryResourceDisplayNameSegmentType).filter(k => typeof CategoryResourceDisplayNameSegmentType[k] === 'number').map(t => ({label: t, value: CategoryResourceDisplayNameSegmentType[t]}));
export enum EnhancementRecordStatus {ContextCreated = 1, ContextApplied = 2}
export const enhancementRecordStatuses = Object.keys(EnhancementRecordStatus).filter(k => typeof EnhancementRecordStatus[k] === 'number').map(t => ({label: t, value: EnhancementRecordStatus[t]}));
export enum InitializationContentType {NotAcceptTerms = 1, NeedRestart = 2}
export const initializationContentTypes = Object.keys(InitializationContentType).filter(k => typeof InitializationContentType[k] === 'number').map(t => ({label: t, value: InitializationContentType[t]}));
export enum InternalProperty {RootPath = 1, ParentResource = 2, Resource = 3, Filename = 15, DirectoryPath = 16, CreatedAt = 17, FileCreatedAt = 18, FileModifiedAt = 19, Category = 20, MediaLibrary = 21}
export const internalProperties = Object.keys(InternalProperty).filter(k => typeof InternalProperty[k] === 'number').map(t => ({label: t, value: InternalProperty[t]}));
export enum PropertyPool {Internal = 1, Reserved = 2, Custom = 4, All = 7}
export const propertyPools = Object.keys(PropertyPool).filter(k => typeof PropertyPool[k] === 'number').map(t => ({label: t, value: PropertyPool[t]}));
export enum PropertyType {SingleLineText = 1, MultilineText = 2, SingleChoice = 3, MultipleChoice = 4, Number = 5, Percentage = 6, Rating = 7, Boolean = 8, Link = 9, Attachment = 10, Date = 11, DateTime = 12, Time = 13, Formula = 14, Multilevel = 15, Tags = 16}
export const propertyTypes = Object.keys(PropertyType).filter(k => typeof PropertyType[k] === 'number').map(t => ({label: t, value: PropertyType[t]}));
export enum PropertyValueScope {Manual = 0, Synchronization = 1, BakabaseEnhancer = 1000, ExHentaiEnhancer = 1001, BangumiEnhancer = 1002, DLsiteEnhancer = 1003, RegexEnhancer = 1004}
export const propertyValueScopes = Object.keys(PropertyValueScope).filter(k => typeof PropertyValueScope[k] === 'number').map(t => ({label: t, value: PropertyValueScope[t]}));
export enum ReservedProperty {Introduction = 12, Rating = 13}
export const reservedProperties = Object.keys(ReservedProperty).filter(k => typeof ReservedProperty[k] === 'number').map(t => ({label: t, value: ReservedProperty[t]}));
export enum ResourceCacheType {Covers = 0, PlayableFiles = 1}
export const resourceCacheTypes = Object.keys(ResourceCacheType).filter(k => typeof ResourceCacheType[k] === 'number').map(t => ({label: t, value: ResourceCacheType[t]}));
export enum SearchCombinator {And = 1, Or = 2}
export const searchCombinators = Object.keys(SearchCombinator).filter(k => typeof SearchCombinator[k] === 'number').map(t => ({label: t, value: SearchCombinator[t]}));
export enum SearchOperation {Equals = 1, NotEquals = 2, Contains = 3, NotContains = 4, StartsWith = 5, NotStartsWith = 6, EndsWith = 7, NotEndsWith = 8, GreaterThan = 9, LessThan = 10, GreaterThanOrEquals = 11, LessThanOrEquals = 12, IsNull = 13, IsNotNull = 14, In = 15, NotIn = 16, Matches = 17, NotMatches = 18}
export const searchOperations = Object.keys(SearchOperation).filter(k => typeof SearchOperation[k] === 'number').map(t => ({label: t, value: SearchOperation[t]}));
export enum SpecialTextType {Useless = 1, Wrapper = 3, Standardization = 4, Volume = 6, Trim = 7, DateTime = 8, Language = 9}
export const specialTextTypes = Object.keys(SpecialTextType).filter(k => typeof SpecialTextType[k] === 'number').map(t => ({label: t, value: SpecialTextType[t]}));
export enum StandardValueType {String = 1, ListString = 2, Decimal = 3, Link = 4, Boolean = 5, DateTime = 6, Time = 7, ListListString = 8, ListTag = 9}
export const standardValueTypes = Object.keys(StandardValueType).filter(k => typeof StandardValueType[k] === 'number').map(t => ({label: t, value: StandardValueType[t]}));
export enum CoverDiscoverResultType {LocalFile = 1, FromAdditionalSource = 2, Icon = 3}
export const coverDiscoverResultTypes = Object.keys(CoverDiscoverResultType).filter(k => typeof CoverDiscoverResultType[k] === 'number').map(t => ({label: t, value: CoverDiscoverResultType[t]}));
export enum ResourceExistence {Exist = 1, Maybe = 2, New = 3}
export const resourceExistences = Object.keys(ResourceExistence).filter(k => typeof ResourceExistence[k] === 'number').map(t => ({label: t, value: ResourceExistence[t]}));
export enum AdditionalCoverDiscoveringSource {CompressedFile = 1, Video = 2}
export const additionalCoverDiscoveringSources = Object.keys(AdditionalCoverDiscoveringSource).filter(k => typeof AdditionalCoverDiscoveringSource[k] === 'number').map(t => ({label: t, value: AdditionalCoverDiscoveringSource[t]}));
export enum BackgroundTaskStatus {Running = 1, Complete = 2, Failed = 3}
export const backgroundTaskStatuses = Object.keys(BackgroundTaskStatus).filter(k => typeof BackgroundTaskStatus[k] === 'number').map(t => ({label: t, value: BackgroundTaskStatus[t]}));
export enum BilibiliDownloadTaskType {Favorites = 1}
export const bilibiliDownloadTaskTypes = Object.keys(BilibiliDownloadTaskType).filter(k => typeof BilibiliDownloadTaskType[k] === 'number').map(t => ({label: t, value: BilibiliDownloadTaskType[t]}));
export enum ComponentDescriptorType {Invalid = 0, Fixed = 1, Configurable = 2, Instance = 3}
export const componentDescriptorTypes = Object.keys(ComponentDescriptorType).filter(k => typeof ComponentDescriptorType[k] === 'number').map(t => ({label: t, value: ComponentDescriptorType[t]}));
export enum ComponentType {Enhancer = 1, PlayableFileSelector = 2, Player = 3}
export const componentTypes = Object.keys(ComponentType).filter(k => typeof ComponentType[k] === 'number').map(t => ({label: t, value: ComponentType[t]}));
export enum CookieValidatorTarget {BiliBili = 1, ExHentai = 2, Pixiv = 3}
export const cookieValidatorTargets = Object.keys(CookieValidatorTarget).filter(k => typeof CookieValidatorTarget[k] === 'number').map(t => ({label: t, value: CookieValidatorTarget[t]}));
export enum CoverFit {Contain = 1, Cover = 2}
export const coverFits = Object.keys(CoverFit).filter(k => typeof CoverFit[k] === 'number').map(t => ({label: t, value: CoverFit[t]}));
export enum CoverSaveLocation {ResourceDirectory = 1, TempDirectory = 2}
export const coverSaveLocations = Object.keys(CoverSaveLocation).filter(k => typeof CoverSaveLocation[k] === 'number').map(t => ({label: t, value: CoverSaveLocation[t]}));
export enum CoverSelectOrder {FilenameAscending = 1, FileModifyDtDescending = 2}
export const coverSelectOrders = Object.keys(CoverSelectOrder).filter(k => typeof CoverSelectOrder[k] === 'number').map(t => ({label: t, value: CoverSelectOrder[t]}));
export enum CustomDataType {String = 1, DateTime = 2, Number = 3, Enum = 4}
export const customDataTypes = Object.keys(CustomDataType).filter(k => typeof CustomDataType[k] === 'number').map(t => ({label: t, value: CustomDataType[t]}));
export enum DownloaderStopBy {ManuallyStop = 1, AppendToTheQueue = 2}
export const downloaderStopBies = Object.keys(DownloaderStopBy).filter(k => typeof DownloaderStopBy[k] === 'number').map(t => ({label: t, value: DownloaderStopBy[t]}));
export enum DownloadTaskAction {StartManually = 1, Restart = 2, Disable = 3, StartAutomatically = 4}
export const downloadTaskActions = Object.keys(DownloadTaskAction).filter(k => typeof DownloadTaskAction[k] === 'number').map(t => ({label: t, value: DownloadTaskAction[t]}));
export enum DownloadTaskActionOnConflict {NotSet = 0, StopOthers = 1, Ignore = 2}
export const downloadTaskActionOnConflicts = Object.keys(DownloadTaskActionOnConflict).filter(k => typeof DownloadTaskActionOnConflict[k] === 'number').map(t => ({label: t, value: DownloadTaskActionOnConflict[t]}));
export enum DownloadTaskDtoStatus {Idle = 100, InQueue = 200, Starting = 300, Downloading = 400, Stopping = 500, Complete = 600, Failed = 700, Disabled = 800}
export const downloadTaskDtoStatuses = Object.keys(DownloadTaskDtoStatus).filter(k => typeof DownloadTaskDtoStatus[k] === 'number').map(t => ({label: t, value: DownloadTaskDtoStatus[t]}));
export enum DownloadTaskStartMode {AutoStart = 1, ManualStart = 2}
export const downloadTaskStartModes = Object.keys(DownloadTaskStartMode).filter(k => typeof DownloadTaskStartMode[k] === 'number').map(t => ({label: t, value: DownloadTaskStartMode[t]}));
export enum DownloadTaskStatus {InProgress = 100, Disabled = 200, Complete = 300, Failed = 400}
export const downloadTaskStatuses = Object.keys(DownloadTaskStatus).filter(k => typeof DownloadTaskStatus[k] === 'number').map(t => ({label: t, value: DownloadTaskStatus[t]}));
export enum ExHentaiDownloadTaskType {SingleWork = 1, Watched = 2, List = 3}
export const exHentaiDownloadTaskTypes = Object.keys(ExHentaiDownloadTaskType).filter(k => typeof ExHentaiDownloadTaskType[k] === 'number').map(t => ({label: t, value: ExHentaiDownloadTaskType[t]}));
export enum MatchResultType {Layer = 1, Regex = 2}
export const matchResultTypes = Object.keys(MatchResultType).filter(k => typeof MatchResultType[k] === 'number').map(t => ({label: t, value: MatchResultType[t]}));
export enum MediaLibraryFileSystemError {InvalidVolume = 1, FreeSpaceNotEnough = 2, Occupied = 3}
export const mediaLibraryFileSystemErrors = Object.keys(MediaLibraryFileSystemError).filter(k => typeof MediaLibraryFileSystemError[k] === 'number').map(t => ({label: t, value: MediaLibraryFileSystemError[t]}));
export enum MediaLibrarySyncStep {Filtering = 0, AcquireFileSystemInfo = 1, CleanResources = 2, CompareResources = 3, SaveResources = 4}
export const mediaLibrarySyncSteps = Object.keys(MediaLibrarySyncStep).filter(k => typeof MediaLibrarySyncStep[k] === 'number').map(t => ({label: t, value: MediaLibrarySyncStep[t]}));
export enum MediaType {Image = 1, Audio = 2, Video = 3, Text = 4, Unknown = 1000}
export const mediaTypes = Object.keys(MediaType).filter(k => typeof MediaType[k] === 'number').map(t => ({label: t, value: MediaType[t]}));
export enum PixivDownloadTaskType {Search = 1, Ranking = 2, Following = 3}
export const pixivDownloadTaskTypes = Object.keys(PixivDownloadTaskType).filter(k => typeof PixivDownloadTaskType[k] === 'number').map(t => ({label: t, value: PixivDownloadTaskType[t]}));
export enum PlaylistItemType {Resource = 1, Video = 2, Image = 3, Audio = 4}
export const playlistItemTypes = Object.keys(PlaylistItemType).filter(k => typeof PlaylistItemType[k] === 'number').map(t => ({label: t, value: PlaylistItemType[t]}));
export enum ReservedResourceFileType {Cover = 1}
export const reservedResourceFileTypes = Object.keys(ReservedResourceFileType).filter(k => typeof ReservedResourceFileType[k] === 'number').map(t => ({label: t, value: ReservedResourceFileType[t]}));
export enum ResourceDiffProperty {Category = 0, MediaLibrary = 1, ReleaseDt = 2, Publisher = 3, Name = 4, Language = 5, Volume = 6, Original = 7, Series = 8, Tag = 9, Introduction = 10, Rate = 11, CustomProperty = 12}
export const resourceDiffProperties = Object.keys(ResourceDiffProperty).filter(k => typeof ResourceDiffProperty[k] === 'number').map(t => ({label: t, value: ResourceDiffProperty[t]}));
export enum ResourceDiffType {Added = 1, Removed = 2, Modified = 3}
export const resourceDiffTypes = Object.keys(ResourceDiffType).filter(k => typeof ResourceDiffType[k] === 'number').map(t => ({label: t, value: ResourceDiffType[t]}));
export enum ResourceDisplayContent {MediaLibrary = 1, Category = 2, Tags = 4, All = 7}
export const resourceDisplayContents = Object.keys(ResourceDisplayContent).filter(k => typeof ResourceDisplayContent[k] === 'number').map(t => ({label: t, value: ResourceDisplayContent[t]}));
export enum ResourceLanguage {NotSet = 0, Chinese = 1, English = 2, Japanese = 3, Korean = 4, French = 5, German = 6, Spanish = 7, Russian = 8}
export const resourceLanguages = Object.keys(ResourceLanguage).filter(k => typeof ResourceLanguage[k] === 'number').map(t => ({label: t, value: ResourceLanguage[t]}));
export enum ResourceMatcherValueType {Layer = 1, Regex = 2, FixedText = 3}
export const resourceMatcherValueTypes = Object.keys(ResourceMatcherValueType).filter(k => typeof ResourceMatcherValueType[k] === 'number').map(t => ({label: t, value: ResourceMatcherValueType[t]}));
export enum ResourceProperty {RootPath = 1, ParentResource = 2, Resource = 3, Introduction = 12, Rating = 13, CustomProperty = 14, Filename = 15, DirectoryPath = 16, CreatedAt = 17, FileCreatedAt = 18, FileModifiedAt = 19, Category = 20, MediaLibrary = 21}
export const resourceProperties = Object.keys(ResourceProperty).filter(k => typeof ResourceProperty[k] === 'number').map(t => ({label: t, value: ResourceProperty[t]}));
export enum ResourceTaskType {Moving = 1}
export const resourceTaskTypes = Object.keys(ResourceTaskType).filter(k => typeof ResourceTaskType[k] === 'number').map(t => ({label: t, value: ResourceTaskType[t]}));
export enum SearchableReservedProperty {Introduction = 12, Rating = 13, FileName = 15, DirectoryPath = 16, CreatedAt = 17, FileCreatedAt = 18, FileModifiedAt = 19, Category = 20, MediaLibrary = 21}
export const searchableReservedProperties = Object.keys(SearchableReservedProperty).filter(k => typeof SearchableReservedProperty[k] === 'number').map(t => ({label: t, value: SearchableReservedProperty[t]}));
export enum StartupPage {Default = 0, Resource = 1}
export const startupPages = Object.keys(StartupPage).filter(k => typeof StartupPage[k] === 'number').map(t => ({label: t, value: StartupPage[t]}));
export enum SubscriptionType {ExHentai = 1, Jav = 2, SoulPlus = 4}
export const subscriptionTypes = Object.keys(SubscriptionType).filter(k => typeof SubscriptionType[k] === 'number').map(t => ({label: t, value: SubscriptionType[t]}));
export enum ThirdPartyId {Bilibili = 1, ExHentai = 2, Pixiv = 3, Bangumi = 4}
export const thirdPartyIds = Object.keys(ThirdPartyId).filter(k => typeof ThirdPartyId[k] === 'number').map(t => ({label: t, value: ThirdPartyId[t]}));
export enum PasswordSearchOrder {Latest = 1, Frequency = 2}
export const passwordSearchOrders = Object.keys(PasswordSearchOrder).filter(k => typeof PasswordSearchOrder[k] === 'number').map(t => ({label: t, value: PasswordSearchOrder[t]}));
export enum ResourceSearchSortableProperty {FileCreateDt = 1, FileModifyDt = 2, Filename = 3, AddDt = 6}
export const resourceSearchSortableProperties = Object.keys(ResourceSearchSortableProperty).filter(k => typeof ResourceSearchSortableProperty[k] === 'number').map(t => ({label: t, value: ResourceSearchSortableProperty[t]}));
export enum AliasAdditionalItem {Candidates = 1}
export const aliasAdditionalItems = Object.keys(AliasAdditionalItem).filter(k => typeof AliasAdditionalItem[k] === 'number').map(t => ({label: t, value: AliasAdditionalItem[t]}));
export enum CategoryAdditionalItem {None = 0, Components = 1, Validation = 3, CustomProperties = 4, EnhancerOptions = 8}
export const categoryAdditionalItems = Object.keys(CategoryAdditionalItem).filter(k => typeof CategoryAdditionalItem[k] === 'number').map(t => ({label: t, value: CategoryAdditionalItem[t]}));
export enum ComponentDescriptorAdditionalItem {None = 0, AssociatedCategories = 1}
export const componentDescriptorAdditionalItems = Object.keys(ComponentDescriptorAdditionalItem).filter(k => typeof ComponentDescriptorAdditionalItem[k] === 'number').map(t => ({label: t, value: ComponentDescriptorAdditionalItem[t]}));
export enum CustomPropertyAdditionalItem {None = 0, Category = 1, ValueCount = 2}
export const customPropertyAdditionalItems = Object.keys(CustomPropertyAdditionalItem).filter(k => typeof CustomPropertyAdditionalItem[k] === 'number').map(t => ({label: t, value: CustomPropertyAdditionalItem[t]}));
export enum CustomPropertyValueAdditionalItem {None = 0, BizValue = 1}
export const customPropertyValueAdditionalItems = Object.keys(CustomPropertyValueAdditionalItem).filter(k => typeof CustomPropertyValueAdditionalItem[k] === 'number').map(t => ({label: t, value: CustomPropertyValueAdditionalItem[t]}));
export enum MediaLibraryAdditionalItem {None = 0, Category = 1, FileSystemInfo = 2, PathConfigurationBoundProperties = 4}
export const mediaLibraryAdditionalItems = Object.keys(MediaLibraryAdditionalItem).filter(k => typeof MediaLibraryAdditionalItem[k] === 'number').map(t => ({label: t, value: MediaLibraryAdditionalItem[t]}));
export enum ResourceAdditionalItem {None = 0, Alias = 64, Category = 128, CustomProperties = 160, DisplayName = 416, HasChildren = 512, ReservedProperties = 1024, MediaLibraryName = 2048, Cache = 4096, All = 8160}
export const resourceAdditionalItems = Object.keys(ResourceAdditionalItem).filter(k => typeof ResourceAdditionalItem[k] === 'number').map(t => ({label: t, value: ResourceAdditionalItem[t]}));
export enum TagAdditionalItem {None = 0, GroupName = 1, PreferredAlias = 2}
export const tagAdditionalItems = Object.keys(TagAdditionalItem).filter(k => typeof TagAdditionalItem[k] === 'number').map(t => ({label: t, value: TagAdditionalItem[t]}));
export enum TagGroupAdditionalItem {Tags = 1, PreferredAlias = 2, TagNamePreferredAlias = 4}
export const tagGroupAdditionalItems = Object.keys(TagGroupAdditionalItem).filter(k => typeof TagGroupAdditionalItem[k] === 'number').map(t => ({label: t, value: TagGroupAdditionalItem[t]}));
export enum EnhancerId {Bakabase = 1, ExHentai = 2, Bangumi = 3, DLsite = 4, Regex = 5}
export const enhancerIds = Object.keys(EnhancerId).filter(k => typeof EnhancerId[k] === 'number').map(t => ({label: t, value: EnhancerId[t]}));
export enum RegexEnhancerTarget {CaptureGroups = 0}
export const regexEnhancerTargets = Object.keys(RegexEnhancerTarget).filter(k => typeof RegexEnhancerTarget[k] === 'number').map(t => ({label: t, value: RegexEnhancerTarget[t]}));
export enum ExHentaiEnhancerTarget {Name = 1, Introduction = 2, Rating = 3, Tags = 4, Cover = 5}
export const exHentaiEnhancerTargets = Object.keys(ExHentaiEnhancerTarget).filter(k => typeof ExHentaiEnhancerTarget[k] === 'number').map(t => ({label: t, value: ExHentaiEnhancerTarget[t]}));
export enum DLsiteEnhancerTarget {Name = 0, Cover = 1, PropertiesOnTheRightSideOfCover = 2, Introduction = 3, Rating = 4}
export const dLsiteEnhancerTargets = Object.keys(DLsiteEnhancerTarget).filter(k => typeof DLsiteEnhancerTarget[k] === 'number').map(t => ({label: t, value: DLsiteEnhancerTarget[t]}));
export enum BangumiEnhancerTarget {Name = 1, Tags = 2, Introduction = 3, Rating = 4, OtherPropertiesInLeftPanel = 5, Cover = 6}
export const bangumiEnhancerTargets = Object.keys(BangumiEnhancerTarget).filter(k => typeof BangumiEnhancerTarget[k] === 'number').map(t => ({label: t, value: BangumiEnhancerTarget[t]}));
export enum BakabaseEnhancerTarget {Name = 1, Publisher = 2, ReleaseDt = 3, VolumeName = 4, VolumeTitle = 5, Originals = 6, Language = 7, Cover = 8}
export const bakabaseEnhancerTargets = Object.keys(BakabaseEnhancerTarget).filter(k => typeof BakabaseEnhancerTarget[k] === 'number').map(t => ({label: t, value: BakabaseEnhancerTarget[t]}));
export enum EnhancementAdditionalItem {None = 0, GeneratedPropertyValue = 1}
export const enhancementAdditionalItems = Object.keys(EnhancementAdditionalItem).filter(k => typeof EnhancementAdditionalItem[k] === 'number').map(t => ({label: t, value: EnhancementAdditionalItem[t]}));
export enum EnhancerTargetOptionsItem {AutoBindProperty = 1, AutoMatchMultilevelString = 2, CoverSelectOrder = 3}
export const enhancerTargetOptionsItems = Object.keys(EnhancerTargetOptionsItem).filter(k => typeof EnhancerTargetOptionsItem[k] === 'number').map(t => ({label: t, value: EnhancerTargetOptionsItem[t]}));
export enum AliasExceptionType {ConflictAliasGroup = 1}
export const aliasExceptionTypes = Object.keys(AliasExceptionType).filter(k => typeof AliasExceptionType[k] === 'number').map(t => ({label: t, value: AliasExceptionType[t]}));
export enum CaptchaType {Image = 1, SmsMessage = 2}
export const captchaTypes = Object.keys(CaptchaType).filter(k => typeof CaptchaType[k] === 'number').map(t => ({label: t, value: CaptchaType[t]}));
export enum DingSysLevel {Other = 0, MainAdministrator = 1, SubAdministrator = 2, Boss = 100}
export const dingSysLevels = Object.keys(DingSysLevel).filter(k => typeof DingSysLevel[k] === 'number').map(t => ({label: t, value: DingSysLevel[t]}));
export enum ResponseCode {Success = 0, NotModified = 304, InvalidPayloadOrOperation = 400, Unauthenticated = 401, Unauthorized = 403, NotFound = 404, Conflict = 409, SystemError = 500, Timeout = 504, InvalidCaptcha = 100400}
export const responseCodes = Object.keys(ResponseCode).filter(k => typeof ResponseCode[k] === 'number').map(t => ({label: t, value: ResponseCode[t]}));
export enum Operation {DELETE = 0, INSERT = 1, EQUAL = 2}
export const operations = Object.keys(Operation).filter(k => typeof Operation[k] === 'number').map(t => ({label: t, value: Operation[t]}));
export enum ProgressorClientAction {Start = 1, Stop = 2, Initialize = 3}
export const progressorClientActions = Object.keys(ProgressorClientAction).filter(k => typeof ProgressorClientAction[k] === 'number').map(t => ({label: t, value: ProgressorClientAction[t]}));
export enum ProgressorEvent {StateChanged = 1, ProgressChanged = 2, ErrorOccurred = 3}
export const progressorEvents = Object.keys(ProgressorEvent).filter(k => typeof ProgressorEvent[k] === 'number').map(t => ({label: t, value: ProgressorEvent[t]}));
export enum ProgressorStatus {Idle = 1, Running = 2, Complete = 3, Suspended = 4}
export const progressorStatuses = Object.keys(ProgressorStatus).filter(k => typeof ProgressorStatus[k] === 'number').map(t => ({label: t, value: ProgressorStatus[t]}));
export enum FileStorageUploadResponseCode {Success = 0, Error = 500}
export const fileStorageUploadResponseCodes = Object.keys(FileStorageUploadResponseCode).filter(k => typeof FileStorageUploadResponseCode[k] === 'number').map(t => ({label: t, value: FileStorageUploadResponseCode[t]}));
export enum MessageStatus {ToBeSent = 0, Succeed = 1, Failed = 2}
export const messageStatuses = Object.keys(MessageStatus).filter(k => typeof MessageStatus[k] === 'number').map(t => ({label: t, value: MessageStatus[t]}));
export enum NotificationType {Os = 1, Email = 2, OsAndEmail = 3, WeChat = 4, Sms = 8}
export const notificationTypes = Object.keys(NotificationType).filter(k => typeof NotificationType[k] === 'number').map(t => ({label: t, value: NotificationType[t]}));
export enum AdbDeviceState {Device = 1, Offline = 2, NoDevice = 3}
export const adbDeviceStates = Object.keys(AdbDeviceState).filter(k => typeof AdbDeviceState[k] === 'number').map(t => ({label: t, value: AdbDeviceState[t]}));
export enum AdbExceptionCode {Error = 1, InvalidExitCode = 2}
export const adbExceptionCodes = Object.keys(AdbExceptionCode).filter(k => typeof AdbExceptionCode[k] === 'number').map(t => ({label: t, value: AdbExceptionCode[t]}));
export enum AdbInternalError {Error = 1, INSTALL_FAILED_ALREADY_EXISTS = 100, DELETE_FAILED_INTERNAL_ERROR = 101, FailedToConnectDevice = 200}
export const adbInternalErrors = Object.keys(AdbInternalError).filter(k => typeof AdbInternalError[k] === 'number').map(t => ({label: t, value: AdbInternalError[t]}));
export enum ExHentaiCategory {Unknown = 0, Misc = 1, Doushijin = 2, Manga = 4, ArtistCG = 8, GameCG = 16, ImageSet = 32, Cosplay = 64, AsianPorn = 128, NonH = 256, Western = 512}
export const exHentaiCategories = Object.keys(ExHentaiCategory).filter(k => typeof ExHentaiCategory[k] === 'number').map(t => ({label: t, value: ExHentaiCategory[t]}));
export enum ExHentaiConnectionStatus {Ok = 1, InvalidCookie = 2, IpBanned = 3, UnknownError = 4}
export const exHentaiConnectionStatuses = Object.keys(ExHentaiConnectionStatus).filter(k => typeof ExHentaiConnectionStatus[k] === 'number').map(t => ({label: t, value: ExHentaiConnectionStatus[t]}));
export enum ThirdPartyRequestResultType {Succeed = 1, TimedOut = 2, Banned = 3, Canceled = 4, Failed = 1000}
export const thirdPartyRequestResultTypes = Object.keys(ThirdPartyRequestResultType).filter(k => typeof ThirdPartyRequestResultType[k] === 'number').map(t => ({label: t, value: ThirdPartyRequestResultType[t]}));
export enum StandardValueConversionRule {Directly = 1, Incompatible = 2, ValuesWillBeMerged = 4, DateWillBeLost = 8, StringToTag = 16, OnlyFirstValidRemains = 64, StringToDateTime = 128, StringToTime = 256, UrlWillBeLost = 1024, StringToNumber = 2048, Trim = 8192, StringToLink = 16384, ValueWillBeSplit = 32768, BooleanToNumber = 65536, TimeToDateTime = 131072, TagGroupWillBeLost = 262144, ValueToBoolean = 524288}
export const standardValueConversionRules = Object.keys(StandardValueConversionRule).filter(k => typeof StandardValueConversionRule[k] === 'number').map(t => ({label: t, value: StandardValueConversionRule[t]}));
export enum LegacyResourceProperty {ReleaseDt = 4, Publisher = 5, Name = 6, Language = 7, Volume = 8, Original = 9, Series = 10, Tag = 11, CustomProperty = 14, Favorites = 22, Cover = 23}
export const legacyResourceProperties = Object.keys(LegacyResourceProperty).filter(k => typeof LegacyResourceProperty[k] === 'number').map(t => ({label: t, value: LegacyResourceProperty[t]}));
export enum LogLevel {Trace = 0, Debug = 1, Information = 2, Warning = 3, Error = 4, Critical = 5, None = 6}
export const logLevels = Object.keys(LogLevel).filter(k => typeof LogLevel[k] === 'number').map(t => ({label: t, value: LogLevel[t]}));