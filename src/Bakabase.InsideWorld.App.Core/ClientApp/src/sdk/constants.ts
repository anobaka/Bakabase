export enum ThirdPartyRequestResultType {Succeed = 1, TimedOut = 2, Banned = 3, Canceled = 4, Failed = 1000}
export const thirdPartyRequestResultTypes = Object.keys(ThirdPartyRequestResultType).filter(k => typeof ThirdPartyRequestResultType[k] === 'number').map(t => ({label: t, value: ThirdPartyRequestResultType[t]}));
export enum ExHentaiCategory {Unknown = 0, Misc = 1, Doushijin = 2, Manga = 4, ArtistCG = 8, GameCG = 16, ImageSet = 32, Cosplay = 64, AsianPorn = 128, NonH = 256, Western = 512}
export const exHentaiCategories = Object.keys(ExHentaiCategory).filter(k => typeof ExHentaiCategory[k] === 'number').map(t => ({label: t, value: ExHentaiCategory[t]}));
export enum ExHentaiConnectionStatus {Ok = 1, InvalidCookie = 2, IpBanned = 3, UnknownError = 4}
export const exHentaiConnectionStatuses = Object.keys(ExHentaiConnectionStatus).filter(k => typeof ExHentaiConnectionStatus[k] === 'number').map(t => ({label: t, value: ExHentaiConnectionStatus[t]}));
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
export enum IwFsType {Unknown = 0, Directory = 100, Image = 200, CompressedFileEntry = 300, CompressedFilePart = 400, Symlink = 500, Video = 600, Audio = 700, Invalid = 10000}
export const iwFsTypes = Object.keys(IwFsType).filter(k => typeof IwFsType[k] === 'number').map(t => ({label: t, value: IwFsType[t]}));
export enum DownloaderStatus {JustCreated = 0, Starting = 100, Downloading = 200, Complete = 300, Failed = 400, Stopping = 500, Stopped = 600}
export const downloaderStatuses = Object.keys(DownloaderStatus).filter(k => typeof DownloaderStatus[k] === 'number').map(t => ({label: t, value: DownloaderStatus[t]}));
export enum CloseBehavior {Prompt = 0, Exit = 1, Minimize = 2, Cancel = 1000}
export const closeBehaviors = Object.keys(CloseBehavior).filter(k => typeof CloseBehavior[k] === 'number').map(t => ({label: t, value: CloseBehavior[t]}));
export enum UiTheme {FollowSystem = 0, Light = 1, Dark = 2}
export const uiThemes = Object.keys(UiTheme).filter(k => typeof UiTheme[k] === 'number').map(t => ({label: t, value: UiTheme[t]}));
export enum UpdaterStatus {Idle = 1, Running = 2, PendingRestart = 3, UpToDate = 4, Failed = 5}
export const updaterStatuses = Object.keys(UpdaterStatus).filter(k => typeof UpdaterStatus[k] === 'number').map(t => ({label: t, value: UpdaterStatus[t]}));
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
export enum CoverSelectOrder {FilenameAscending = 1, FileModifyDtDescending = 2}
export const coverSelectOrders = Object.keys(CoverSelectOrder).filter(k => typeof CoverSelectOrder[k] === 'number').map(t => ({label: t, value: CoverSelectOrder[t]}));
export enum CustomDataType {String = 1, DateTime = 2, Number = 3, Enum = 4}
export const customDataTypes = Object.keys(CustomDataType).filter(k => typeof CustomDataType[k] === 'number').map(t => ({label: t, value: CustomDataType[t]}));
export enum DownloadTaskAction {StartManually = 1, Restart = 2, Disable = 3, StartAutomatically = 4}
export const downloadTaskActions = Object.keys(DownloadTaskAction).filter(k => typeof DownloadTaskAction[k] === 'number').map(t => ({label: t, value: DownloadTaskAction[t]}));
export enum DownloadTaskDtoStatus {Idle = 100, InQueue = 200, Starting = 300, Downloading = 400, Stopping = 500, Complete = 600, Failed = 700, Disabled = 800}
export const downloadTaskDtoStatuses = Object.keys(DownloadTaskDtoStatus).filter(k => typeof DownloadTaskDtoStatus[k] === 'number').map(t => ({label: t, value: DownloadTaskDtoStatus[t]}));
export enum DownloadTaskStatus {InProgress = 100, Disabled = 200, Complete = 300, Failed = 400}
export const downloadTaskStatuses = Object.keys(DownloadTaskStatus).filter(k => typeof DownloadTaskStatus[k] === 'number').map(t => ({label: t, value: DownloadTaskStatus[t]}));
export enum ExHentaiDownloadTaskType {SingleWork = 1, Watched = 2, List = 3}
export const exHentaiDownloadTaskTypes = Object.keys(ExHentaiDownloadTaskType).filter(k => typeof ExHentaiDownloadTaskType[k] === 'number').map(t => ({label: t, value: ExHentaiDownloadTaskType[t]}));
export enum InitializationContentType {NotAcceptTerms = 1, NeedRestart = 2}
export const initializationContentTypes = Object.keys(InitializationContentType).filter(k => typeof InitializationContentType[k] === 'number').map(t => ({label: t, value: InitializationContentType[t]}));
export enum MatchResultType {Layer = 1, Regex = 2}
export const matchResultTypes = Object.keys(MatchResultType).filter(k => typeof MatchResultType[k] === 'number').map(t => ({label: t, value: MatchResultType[t]}));
export enum MediaLibraryError {InvalidVolume = 1, FreeSpaceNotEnough = 2, Occupied = 3}
export const mediaLibraryErrors = Object.keys(MediaLibraryError).filter(k => typeof MediaLibraryError[k] === 'number').map(t => ({label: t, value: MediaLibraryError[t]}));
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
export enum ReservedResourceProperty {ReleaseDt = 1, Publisher = 2, Name = 3, Language = 4, Volume = 5, Original = 6, Series = 7, Tag = 8, Introduction = 9, Rate = 10}
export const reservedResourceProperties = Object.keys(ReservedResourceProperty).filter(k => typeof ReservedResourceProperty[k] === 'number').map(t => ({label: t, value: ReservedResourceProperty[t]}));
export enum ResourceLanguage {NotSet = 0, Chinese = 1, English = 2, Japanese = 3, Korean = 4, French = 5, German = 6, Spanish = 7, Russian = 8}
export const resourceLanguages = Object.keys(ResourceLanguage).filter(k => typeof ResourceLanguage[k] === 'number').map(t => ({label: t, value: ResourceLanguage[t]}));
export enum ResourceMatcherValueType {Layer = 1, Regex = 2, FixedText = 3}
export const resourceMatcherValueTypes = Object.keys(ResourceMatcherValueType).filter(k => typeof ResourceMatcherValueType[k] === 'number').map(t => ({label: t, value: ResourceMatcherValueType[t]}));
export enum ResourceProperty {RootPath = 1, ParentResource = 2, Resource = 3, ReleaseDt = 4, Publisher = 5, Name = 6, Language = 7, Volume = 8, Original = 9, Series = 10, Tag = 11, Introduction = 12, Rate = 13, CustomProperty = 14}
export const resourceProperties = Object.keys(ResourceProperty).filter(k => typeof ResourceProperty[k] === 'number').map(t => ({label: t, value: ResourceProperty[t]}));
export enum ResourceTaskType {Moving = 1}
export const resourceTaskTypes = Object.keys(ResourceTaskType).filter(k => typeof ResourceTaskType[k] === 'number').map(t => ({label: t, value: ResourceTaskType[t]}));
export enum SpecialTextType {Useless = 1, Language = 2, Wrapper = 3, Standardization = 4, Volume = 6, Trim = 7, DateTime = 8}
export const specialTextTypes = Object.keys(SpecialTextType).filter(k => typeof SpecialTextType[k] === 'number').map(t => ({label: t, value: SpecialTextType[t]}));
export enum StartupPage {Default = 0, Resource = 1}
export const startupPages = Object.keys(StartupPage).filter(k => typeof StartupPage[k] === 'number').map(t => ({label: t, value: StartupPage[t]}));
export enum SubscriptionType {ExHentai = 1, Jav = 2, SoulPlus = 4}
export const subscriptionTypes = Object.keys(SubscriptionType).filter(k => typeof SubscriptionType[k] === 'number').map(t => ({label: t, value: SubscriptionType[t]}));
export enum ThirdPartyId {Bilibili = 1, ExHentai = 2, Pixiv = 3}
export const thirdPartyIds = Object.keys(ThirdPartyId).filter(k => typeof ThirdPartyId[k] === 'number').map(t => ({label: t, value: ThirdPartyId[t]}));
export enum PasswordSearchOrder {Latest = 1, Frequency = 2}
export const passwordSearchOrders = Object.keys(PasswordSearchOrder).filter(k => typeof PasswordSearchOrder[k] === 'number').map(t => ({label: t, value: PasswordSearchOrder[t]}));
export enum ResourceSearchOrder {FileCreateDt = 1, FileModifyDt = 2, Filename = 3, Rate = 4, ReleaseDt = 5, AddDt = 6, Category = 7, MediaLibrary = 8, Name = 9}
export const resourceSearchOrders = Object.keys(ResourceSearchOrder).filter(k => typeof ResourceSearchOrder[k] === 'number').map(t => ({label: t, value: ResourceSearchOrder[t]}));
export enum AliasAdditionalItem {Candidates = 1}
export const aliasAdditionalItems = Object.keys(AliasAdditionalItem).filter(k => typeof AliasAdditionalItem[k] === 'number').map(t => ({label: t, value: AliasAdditionalItem[t]}));
export enum ComponentDescriptorAdditionalItem {None = 0, AssociatedCategories = 1}
export const componentDescriptorAdditionalItems = Object.keys(ComponentDescriptorAdditionalItem).filter(k => typeof ComponentDescriptorAdditionalItem[k] === 'number').map(t => ({label: t, value: ComponentDescriptorAdditionalItem[t]}));
export enum ResourceAdditionalItem {None = 0, Publishers = 1, Volume = 2, Serial = 6, Originals = 8, Tags = 16, CustomProperties = 32, Alias = 64, All = 127}
export const resourceAdditionalItems = Object.keys(ResourceAdditionalItem).filter(k => typeof ResourceAdditionalItem[k] === 'number').map(t => ({label: t, value: ResourceAdditionalItem[t]}));
export enum ResourceCategoryAdditionalItem {None = 0, Components = 1, Validation = 3}
export const resourceCategoryAdditionalItems = Object.keys(ResourceCategoryAdditionalItem).filter(k => typeof ResourceCategoryAdditionalItem[k] === 'number').map(t => ({label: t, value: ResourceCategoryAdditionalItem[t]}));
export enum TagAdditionalItem {None = 0, GroupName = 1, PreferredAlias = 2}
export const tagAdditionalItems = Object.keys(TagAdditionalItem).filter(k => typeof TagAdditionalItem[k] === 'number').map(t => ({label: t, value: TagAdditionalItem[t]}));
export enum TagGroupAdditionalItem {Tags = 1, PreferredAlias = 2, TagNamePreferredAlias = 4}
export const tagGroupAdditionalItems = Object.keys(TagGroupAdditionalItem).filter(k => typeof TagGroupAdditionalItem[k] === 'number').map(t => ({label: t, value: TagGroupAdditionalItem[t]}));
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
export enum LogLevel {Trace = 0, Debug = 1, Information = 2, Warning = 3, Error = 4, Critical = 5, None = 6}
export const logLevels = Object.keys(LogLevel).filter(k => typeof LogLevel[k] === 'number').map(t => ({label: t, value: LogLevel[t]}));
export enum OpenDialogProperty {openFile = 0, openDirectory = 1, multiSelections = 2, showHiddenFiles = 3, createDirectory = 4, promptToCreate = 5, noResolveAliases = 6, treatPackageAsDirectory = 7}
export const openDialogProperties = Object.keys(OpenDialogProperty).filter(k => typeof OpenDialogProperty[k] === 'number').map(t => ({label: t, value: OpenDialogProperty[t]}));