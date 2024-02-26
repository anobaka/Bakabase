/* eslint-disable */
/* tslint:disable */
/*
 * ---------------------------------------------------------------
 * ## THIS FILE WAS GENERATED VIA SWAGGER-TYPESCRIPT-API        ##
 * ##                                                           ##
 * ## AUTHOR: acacode                                           ##
 * ## SOURCE: https://github.com/acacode/swagger-typescript-api ##
 * ---------------------------------------------------------------
 */

export interface BakabaseInfrastructuresComponentsAppModelsRequestModelsAppOptionsPatchRequestModel {
  language?: string | null;
  enablePreReleaseChannel?: boolean | null;
  enableAnonymousDataTracking?: boolean | null;
  /** [0: Prompt, 1: Exit, 2: Minimize, 1000: Cancel] */
  closeBehavior?: BakabaseInfrastructuresComponentsGuiCloseBehavior;
  /** [0: FollowSystem, 1: Light, 2: Dark] */
  uiTheme?: BakabaseInfrastructuresComponentsGuiUiTheme;
}

export interface BakabaseInfrastructuresComponentsAppModelsRequestModelsCoreDataMoveRequestModel {
  /** @minLength 1 */
  dataPath: string;
}

export interface BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo {
  appDataPath?: string | null;
  coreVersion?: string | null;
  logPath?: string | null;
  backupPath?: string | null;
  tempFilesPath?: string | null;
  notAcceptTerms?: boolean;
  needRestart?: boolean;
}

export interface BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo {
  version?: string | null;
  installers?: BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfoInstaller[] | null;
}

export interface BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfoInstaller {
  osPlatform?: SystemRuntimeInteropServicesOSPlatform;
  /** [0: X86, 1: X64, 2: Arm, 3: Arm64, 4: Wasm, 5: S390x] */
  osArchitecture?: SystemRuntimeInteropServicesArchitecture;
  name?: string | null;
  url?: string | null;
  /** @format int64 */
  size?: number;
}

export interface BakabaseInfrastructuresComponentsConfigurationsAppAppOptions {
  language?: string | null;
  version?: string | null;
  enablePreReleaseChannel?: boolean;
  enableAnonymousDataTracking?: boolean;
  wwwRootPath?: string | null;
  dataPath?: string | null;
  prevDataPath?: string | null;
  /** [0: Prompt, 1: Exit, 2: Minimize, 1000: Cancel] */
  closeBehavior?: BakabaseInfrastructuresComponentsGuiCloseBehavior;
  /** [0: FollowSystem, 1: Light, 2: Dark] */
  uiTheme?: BakabaseInfrastructuresComponentsGuiUiTheme;
}

/**
 * [0: Prompt, 1: Exit, 2: Minimize, 1000: Cancel]
 * @format int32
 */
export type BakabaseInfrastructuresComponentsGuiCloseBehavior = 0 | 1 | 2 | 1000;

/**
 * [0: FollowSystem, 1: Light, 2: Dark]
 * @format int32
 */
export type BakabaseInfrastructuresComponentsGuiUiTheme = 0 | 1 | 2;

export interface BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsBulkModificationFilter {
  /** [1: Category, 2: MediaLibrary, 3: Name, 4: FileName, 5: DirectoryPath, 6: ReleaseDt, 7: CreateDt, 8: FileCreateDt, 9: FileModifyDt, 10: Publisher, 11: Language, 12: Volume, 13: Original, 14: Series, 15: Tag, 16: Introduction, 17: Rate, 18: CustomProperty] */
  property?: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsConstantsBulkModificationProperty;
  propertyKey?: string | null;
  /** [1: Equals, 2: NotEquals, 3: Contains, 4: NotContains, 5: StartsWith, 6: NotStartsWith, 7: EndsWith, 8: NotEndsWith, 9: GreaterThan, 10: LessThan, 11: GreaterThanOrEquals, 12: LessThanOrEquals, 13: IsNull, 14: IsNotNull, 15: In, 16: NotIn, 17: Matches, 18: NotMatches] */
  operation?: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsConstantsBulkModificationFilterOperation;
  target?: string | null;
}

export interface BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsBulkModificationFilterGroup {
  /** [1: And, 2: Or] */
  operation?: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsConstantsBulkModificationFilterGroupOperation;
  filters?: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsBulkModificationFilter[] | null;
  groups?: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsBulkModificationFilterGroup[] | null;
}

export interface BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsBulkModificationProcess {
  /** [1: Category, 2: MediaLibrary, 3: Name, 4: FileName, 5: DirectoryPath, 6: ReleaseDt, 7: CreateDt, 8: FileCreateDt, 9: FileModifyDt, 10: Publisher, 11: Language, 12: Volume, 13: Original, 14: Series, 15: Tag, 16: Introduction, 17: Rate, 18: CustomProperty] */
  property?: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsConstantsBulkModificationProperty;
  propertyKey?: string | null;
  value?: string | null;
}

export interface BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsBulkModificationVariable {
  key?: string | null;
  name?: string | null;
  /** [1: None, 2: FileName, 3: FileNameWithoutExtension, 4: FullPath, 5: DirectoryName, 6: Name] */
  source?: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsConstantsBulkModificationVariableSource;
  find?: string | null;
  value?: string | null;
}

/**
 * [0: None, 1: Ignore, 2: Replace, 3: Merge]
 * @format int32
 */
export type BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsConstantsBulkModificationDiffOperation =
  0 | 1 | 2 | 3;

/**
 * [1: Added, 2: Removed, 3: Modified]
 * @format int32
 */
export type BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsConstantsBulkModificationDiffType =
  | 1
  | 2
  | 3;

/**
 * [1: And, 2: Or]
 * @format int32
 */
export type BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsConstantsBulkModificationFilterGroupOperation =
  1 | 2;

/**
 * [1: Equals, 2: NotEquals, 3: Contains, 4: NotContains, 5: StartsWith, 6: NotStartsWith, 7: EndsWith, 8: NotEndsWith, 9: GreaterThan, 10: LessThan, 11: GreaterThanOrEquals, 12: LessThanOrEquals, 13: IsNull, 14: IsNotNull, 15: In, 16: NotIn, 17: Matches, 18: NotMatches]
 * @format int32
 */
export type BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsConstantsBulkModificationFilterOperation =
  1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 | 15 | 16 | 17 | 18;

/**
 * [1: Category, 2: MediaLibrary, 3: Name, 4: FileName, 5: DirectoryPath, 6: ReleaseDt, 7: CreateDt, 8: FileCreateDt, 9: FileModifyDt, 10: Publisher, 11: Language, 12: Volume, 13: Original, 14: Series, 15: Tag, 16: Introduction, 17: Rate, 18: CustomProperty]
 * @format int32
 */
export type BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsConstantsBulkModificationProperty =
  | 1
  | 2
  | 3
  | 4
  | 5
  | 6
  | 7
  | 8
  | 9
  | 10
  | 11
  | 12
  | 13
  | 14
  | 15
  | 16
  | 17
  | 18;

/**
 * [1: Initial, 2: Filtered, 3: Complete, 4: Failed, 5: Cancelled]
 * @format int32
 */
export type BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsConstantsBulkModificationStatus =
  | 1
  | 2
  | 3
  | 4
  | 5;

/**
 * [1: None, 2: FileName, 3: FileNameWithoutExtension, 4: FullPath, 5: DirectoryName, 6: Name]
 * @format int32
 */
export type BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsConstantsBulkModificationVariableSource =
  1 | 2 | 3 | 4 | 5 | 6;

export interface BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationDto {
  /** @format int32 */
  id?: number;
  name?: string | null;
  /** [1: Initial, 2: Filtered, 3: Complete, 4: Failed, 5: Cancelled] */
  status?: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsConstantsBulkModificationStatus;
  /** @format date-time */
  createdAt?: string;
  variables?: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsBulkModificationVariable[] | null;
  filter?: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsBulkModificationFilterGroup;
  processes?: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsBulkModificationProcess[] | null;
  diffs?: BakabaseInsideWorldModelsModelsAosResourceDiff[] | null;
  filteredResourceIds?: number[] | null;
  /** @format date-time */
  calculatedAt?: string | null;
}

export interface BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationPutRequestModel {
  name?: string | null;
  filter?: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsBulkModificationFilterGroup;
  processes?: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsBulkModificationProcess[] | null;
  variables?: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsBulkModificationVariable[] | null;
}

export interface BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationResourceDiffs {
  /** @format int32 */
  id?: number;
  path?: string | null;
  diffs?:
    | BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationResourceDiffsDiff[]
    | null;
}

export interface BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationResourceDiffsDiff {
  /** [1: Category, 2: MediaLibrary, 3: Name, 4: FileName, 5: DirectoryPath, 6: ReleaseDt, 7: CreateDt, 8: FileCreateDt, 9: FileModifyDt, 10: Publisher, 11: Language, 12: Volume, 13: Original, 14: Series, 15: Tag, 16: Introduction, 17: Rate, 18: CustomProperty] */
  property?: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsConstantsBulkModificationProperty;
  propertyKey?: string | null;
  /** [1: Added, 2: Removed, 3: Modified] */
  type?: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsConstantsBulkModificationDiffType;
  currentValue?: string | null;
  newValue?: string | null;
  /** [0: None, 1: Ignore, 2: Replace, 3: Merge] */
  operation?: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsConstantsBulkModificationDiffOperation;
}

export interface BakabaseInsideWorldBusinessComponentsCompressionCompressedFileEntry {
  path?: string | null;
  /** @format int64 */
  size?: number;
  /** @format double */
  sizeInMb?: number;
}

export interface BakabaseInsideWorldBusinessComponentsDependencyAbstractionsDependentComponentVersion {
  version?: string | null;
  description?: string | null;
  canUpdate?: boolean;
}

export interface BakabaseInsideWorldBusinessComponentsFileExplorerEntriesIwFsCompressedFileGroup {
  keyName?: string | null;
  files?: string[] | null;
  extension?: string | null;
  missEntry?: boolean;
  password?: string | null;
  passwordCandidates?: string[] | null;
}

export interface BakabaseInsideWorldBusinessComponentsFileExplorerInformationIwFsEntryLazyInfo {
  /** @format int32 */
  childrenCount?: number;
}

export interface BakabaseInsideWorldBusinessComponentsFileExplorerInformationIwFsTaskInfo {
  path?: string | null;
  /** [1: Decompressing, 2: Moving] */
  type?: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntryTaskType;
  /** @format int32 */
  percentage?: number;
  error?: string | null;
  backgroundTaskId?: string | null;
  name?: string | null;
}

/**
 * [1: Hidden]
 * @format int32
 */
export type BakabaseInsideWorldBusinessComponentsFileExplorerIwFsAttribute = 1;

export interface BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry {
  path?: string | null;
  name?: string | null;
  meaningfulName?: string | null;
  ext?: string | null;
  attributes?: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsAttribute[] | null;
  /** [0: Unknown, 100: Directory, 200: Image, 300: CompressedFileEntry, 400: CompressedFilePart, 500: Symlink, 600: Video, 700: Audio, 10000: Invalid] */
  type?: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsType;
  /** @format int64 */
  size?: number | null;
  /** @format int32 */
  childrenCount?: number | null;
  /** @format date-time */
  creationTime?: string | null;
  /** @format date-time */
  lastWriteTime?: string | null;
  passwordsForDecompressing?: string[] | null;
}

/**
 * [1: Decompressing, 2: Moving]
 * @format int32
 */
export type BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntryTaskType = 1 | 2;

export interface BakabaseInsideWorldBusinessComponentsFileExplorerIwFsPreview {
  entries?: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry[] | null;
  directoryChain?: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry[] | null;
  compressedFileGroups?: BakabaseInsideWorldBusinessComponentsFileExplorerEntriesIwFsCompressedFileGroup[] | null;
}

/**
 * [0: Unknown, 100: Directory, 200: Image, 300: CompressedFileEntry, 400: CompressedFilePart, 500: Symlink, 600: Video, 700: Audio, 10000: Invalid]
 * @format int32
 */
export type BakabaseInsideWorldBusinessComponentsFileExplorerIwFsType =
  | 0
  | 100
  | 200
  | 300
  | 400
  | 500
  | 600
  | 700
  | 10000;

export interface BakabaseInsideWorldBusinessComponentsTasksBackgroundTaskDto {
  id?: string | null;
  name?: string | null;
  /** @format date-time */
  startDt?: string;
  /** [1: Running, 2: Complete, 3: Failed] */
  status?: BakabaseInsideWorldModelsConstantsBackgroundTaskStatus;
  message?: string | null;
  /** @format int32 */
  percentage?: number;
  currentProcess?: string | null;
  /** [1: Default, 2: Critical] */
  level?: BakabaseInsideWorldBusinessComponentsTasksBackgroundTaskLevel;
}

/**
 * [1: Default, 2: Critical]
 * @format int32
 */
export type BakabaseInsideWorldBusinessComponentsTasksBackgroundTaskLevel = 1 | 2;

export interface BakabaseInsideWorldBusinessComponentsThirdPartyBilibiliModelsFavorites {
  /** @format int64 */
  id?: number;
  title?: string | null;
  /** @format int32 */
  mediaCount?: number;
}

export interface BakabaseInsideWorldModelsConfigsBilibiliOptions {
  downloader?: BakabaseInsideWorldModelsConfigsInfrastructuresCommonDownloaderOptions;
  cookie?: string | null;
}

export interface BakabaseInsideWorldModelsConfigsExHentaiOptions {
  downloader?: BakabaseInsideWorldModelsConfigsInfrastructuresCommonDownloaderOptions;
  cookie?: string | null;
  enhancer?: BakabaseInsideWorldModelsConfigsExHentaiOptionsExHentaiEnhancerOptions;
}

export interface BakabaseInsideWorldModelsConfigsExHentaiOptionsExHentaiEnhancerOptions {
  excludedTags?: string[] | null;
}

export interface BakabaseInsideWorldModelsConfigsFileSystemOptions {
  recentMovingDestinations?: string[] | null;
  fileMover?: BakabaseInsideWorldModelsConfigsFileSystemOptionsFileMoverOptions;
  fileProcessor?: BakabaseInsideWorldModelsConfigsFileSystemOptionsFileProcessorOptions;
}

export interface BakabaseInsideWorldModelsConfigsFileSystemOptionsFileMoverOptions {
  targets?: BakabaseInsideWorldModelsConfigsFileSystemOptionsFileMoverOptionsTarget[] | null;
  enabled?: boolean;
  delay?: SystemTimeSpan;
}

export interface BakabaseInsideWorldModelsConfigsFileSystemOptionsFileMoverOptionsTarget {
  path?: string | null;
  sources?: string[] | null;
}

export interface BakabaseInsideWorldModelsConfigsFileSystemOptionsFileProcessorOptions {
  workingDirectory?: string | null;
}

export interface BakabaseInsideWorldModelsConfigsInfrastructuresCommonDownloaderOptions {
  /** @format int32 */
  threads?: number;
  /** @format int32 */
  interval?: number;
  defaultPath?: string | null;
  namingConvention?: string | null;
}

export interface BakabaseInsideWorldModelsConfigsJavLibraryOptions {
  cookie?: string | null;
  collector?: BakabaseInsideWorldModelsConfigsJavLibraryOptionsCollectorOptions;
}

export interface BakabaseInsideWorldModelsConfigsJavLibraryOptionsCollectorOptions {
  path?: string | null;
  /** @uniqueItems true */
  urls?: string[] | null;
  /** @uniqueItems true */
  torrentOrLinkKeywords?: string[] | null;
}

export interface BakabaseInsideWorldModelsConfigsNetworkOptions {
  proxy?: BakabaseInsideWorldModelsConfigsNetworkOptionsProxyOptions;
}

export interface BakabaseInsideWorldModelsConfigsNetworkOptionsProxyOptions {
  address?: string | null;
  credentials?: BakabaseInsideWorldModelsConfigsNetworkOptionsProxyOptionsProxyCredentials;
}

export interface BakabaseInsideWorldModelsConfigsNetworkOptionsProxyOptionsProxyCredentials {
  username?: string | null;
  password?: string | null;
  domain?: string | null;
}

export interface BakabaseInsideWorldModelsConfigsPixivOptions {
  cookie?: string | null;
  downloader?: BakabaseInsideWorldModelsConfigsInfrastructuresCommonDownloaderOptions;
}

export interface BakabaseInsideWorldModelsConfigsResourceResourceOptionsCoverOptionsModel {
  /** [1: ResourceDirectory, 2: TempDirectory] */
  saveLocation?: BakabaseInsideWorldModelsConstantsCoverSaveLocation;
  overwrite?: boolean | null;
}

export interface BakabaseInsideWorldModelsConfigsResourceResourceOptionsDto {
  /** @format date-time */
  lastSyncDt?: string;
  /** @format date-time */
  lastNfoGenerationDt?: string;
  lastSearch?: BakabaseInsideWorldModelsModelsAosResourceSearchDto;
  searchSlots?: BakabaseInsideWorldModelsModelsAosResourceSearchSlotItemDto[] | null;
  coverOptions?: BakabaseInsideWorldModelsConfigsResourceResourceOptionsCoverOptionsModel;
  additionalCoverDiscoveringSources?: BakabaseInsideWorldModelsConstantsAdditionalCoverDiscoveringSource[] | null;
}

export interface BakabaseInsideWorldModelsConfigsResourceResourceSearchOptionsOrderModel {
  /** [1: FileCreateDt, 2: FileModifyDt, 3: Filename, 4: Rate, 5: ReleaseDt, 6: AddDt, 7: Category, 8: MediaLibrary, 9: Name] */
  order?: BakabaseInsideWorldModelsConstantsAosResourceSearchOrder;
  asc?: boolean;
}

export interface BakabaseInsideWorldModelsConfigsThirdPartyOptions {
  simpleSearchEngines?: BakabaseInsideWorldModelsConfigsThirdPartyOptionsSimpleSearchEngineOptions[] | null;
}

export interface BakabaseInsideWorldModelsConfigsThirdPartyOptionsSimpleSearchEngineOptions {
  name?: string | null;
  urlTemplate?: string | null;
}

export interface BakabaseInsideWorldModelsConfigsUIOptions {
  resource?: BakabaseInsideWorldModelsConfigsUIOptionsUIResourceOptions;
  /** [0: Default, 1: Resource] */
  startupPage?: BakabaseInsideWorldModelsConstantsStartupPage;
}

export interface BakabaseInsideWorldModelsConfigsUIOptionsUIResourceOptions {
  /** @format int32 */
  colCount?: number;
  showBiggerCoverWhileHover?: boolean;
  disableMediaPreviewer?: boolean;
  disableCache?: boolean;
}

/**
 * [1: CompressedFile, 2: Video]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsAdditionalCoverDiscoveringSource = 1 | 2;

/**
 * [1: Candidates]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsAdditionalItemsAliasAdditionalItem = 1;

/**
 * [0: None, 1: AssociatedCategories]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsAdditionalItemsComponentDescriptorAdditionalItem = 0 | 1;

/**
 * [0: None, 1: Components, 3: Validation]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsAdditionalItemsResourceCategoryAdditionalItem = 0 | 1 | 3;

/**
 * [0: None, 1: GroupName, 2: PreferredAlias]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsAdditionalItemsTagAdditionalItem = 0 | 1 | 2;

/**
 * [1: Tags, 2: PreferredAlias, 4: TagNamePreferredAlias]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsAdditionalItemsTagGroupAdditionalItem = 1 | 2 | 4;

/**
 * [1: Latest, 2: Frequency]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsAosPasswordSearchOrder = 1 | 2;

/**
 * [1: FileCreateDt, 2: FileModifyDt, 3: Filename, 4: Rate, 5: ReleaseDt, 6: AddDt, 7: Category, 8: MediaLibrary, 9: Name]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsAosResourceSearchOrder = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9;

/**
 * [1: Running, 2: Complete, 3: Failed]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsBackgroundTaskStatus = 1 | 2 | 3;

/**
 * [0: Invalid, 1: Fixed, 2: Configurable, 3: Instance]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsComponentDescriptorType = 0 | 1 | 2 | 3;

/**
 * [1: Enhancer, 2: PlayableFileSelector, 3: Player]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsComponentType = 1 | 2 | 3;

/**
 * [1: BiliBili, 2: ExHentai, 3: Pixiv]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsCookieValidatorTarget = 1 | 2 | 3;

/**
 * [1: ResourceDirectory, 2: TempDirectory]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsCoverSaveLocation = 1 | 2;

/**
 * [1: FilenameAscending, 2: FileModifyDtDescending]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsCoverSelectOrder = 1 | 2;

/**
 * [1: String, 2: DateTime, 3: Number, 4: Enum]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsCustomDataType = 1 | 2 | 3 | 4;

/**
 * [1: StartManually, 2: Restart, 3: Disable, 4: StartAutomatically]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsDownloadTaskAction = 1 | 2 | 3 | 4;

/**
 * [0: NotSet, 1: StopOthers, 2: Ignore]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsDownloadTaskActionOnConflict = 0 | 1 | 2;

/**
 * [100: Idle, 200: InQueue, 300: Starting, 400: Downloading, 500: Stopping, 600: Complete, 700: Failed, 800: Disabled]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsDownloadTaskDtoStatus = 100 | 200 | 300 | 400 | 500 | 600 | 700 | 800;

/**
 * [100: InProgress, 200: Disabled, 300: Complete, 400: Failed]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsDownloadTaskStatus = 100 | 200 | 300 | 400;

/**
 * [1: NotAcceptTerms, 2: NeedRestart]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsInitializationContentType = 1 | 2;

/**
 * [1: InvalidVolume, 2: FreeSpaceNotEnough, 3: Occupied]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsMediaLibraryError = 1 | 2 | 3;

/**
 * [1: Image, 2: Audio, 3: Video, 4: Text, 1000: Unknown]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsMediaType = 1 | 2 | 3 | 4 | 1000;

/**
 * [1: Resource, 2: Video, 3: Image, 4: Audio]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsPlaylistItemType = 1 | 2 | 3 | 4;

/**
 * [0: Category, 1: MediaLibrary, 2: ReleaseDt, 3: Publisher, 4: Name, 5: Language, 6: Volume, 7: Original, 8: Series, 9: Tag, 10: Introduction, 11: Rate, 12: CustomProperty]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsResourceDiffProperty =
  | 0
  | 1
  | 2
  | 3
  | 4
  | 5
  | 6
  | 7
  | 8
  | 9
  | 10
  | 11
  | 12;

/**
 * [1: Added, 2: Removed, 3: Modified]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsResourceDiffType = 1 | 2 | 3;

/**
 * [0: NotSet, 1: Chinese, 2: English, 3: Japanese, 4: Korean, 5: French, 6: German, 7: Spanish, 8: Russian]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsResourceLanguage = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8;

/**
 * [1: Layer, 2: Regex, 3: FixedText]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsResourceMatcherValueType = 1 | 2 | 3;

/**
 * [1: RootPath, 2: ParentResource, 3: Resource, 4: ReleaseDt, 5: Publisher, 6: Name, 7: Language, 8: Volume, 9: Original, 10: Series, 11: Tag, 12: Introduction, 13: Rate, 14: CustomProperty]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsResourceProperty =
  | 1
  | 2
  | 3
  | 4
  | 5
  | 6
  | 7
  | 8
  | 9
  | 10
  | 11
  | 12
  | 13
  | 14;

/**
 * [1: Useless, 2: Language, 3: Wrapper, 4: Standardization, 6: Volume, 7: Trim, 8: DateTime]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsSpecialTextType = 1 | 2 | 3 | 4 | 6 | 7 | 8;

/**
 * [0: Default, 1: Resource]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsStartupPage = 0 | 1;

/**
 * [1: Bilibili, 2: ExHentai, 3: Pixiv]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsThirdPartyId = 1 | 2 | 3;

export interface BakabaseInsideWorldModelsModelsAosDownloaderNamingDefinitions {
  fields?: BakabaseInsideWorldModelsModelsAosDownloaderNamingDefinitionsField[] | null;
  defaultConvention?: string | null;
}

export interface BakabaseInsideWorldModelsModelsAosDownloaderNamingDefinitionsField {
  key?: string | null;
  description?: string | null;
  example?: string | null;
}

export interface BakabaseInsideWorldModelsModelsAosFileEntriesMergeResult {
  rootPath?: string | null;
  currentNames?: string[] | null;
  mergeResult?: Record<string, string[]>;
}

export interface BakabaseInsideWorldModelsModelsAosMatcherValue {
  fixedText?: string | null;
  /** @format int32 */
  layer?: number | null;
  regex?: string | null;
  /** [1: RootPath, 2: ParentResource, 3: Resource, 4: ReleaseDt, 5: Publisher, 6: Name, 7: Language, 8: Volume, 9: Original, 10: Series, 11: Tag, 12: Introduction, 13: Rate, 14: CustomProperty] */
  property?: BakabaseInsideWorldModelsConstantsResourceProperty;
  /** [1: Layer, 2: Regex, 3: FixedText] */
  valueType?: BakabaseInsideWorldModelsConstantsResourceMatcherValueType;
  key?: string | null;
  isValid?: boolean;
}

export interface BakabaseInsideWorldModelsModelsAosPathConfigurationValidateResult {
  rootPath?: string | null;
  entries?: BakabaseInsideWorldModelsModelsAosPathConfigurationValidateResultEntry[] | null;
}

export interface BakabaseInsideWorldModelsModelsAosPathConfigurationValidateResultEntry {
  isDirectory?: boolean;
  relativePath?: string | null;
  segmentAndMatchedValues?:
    | BakabaseInsideWorldModelsModelsAosPathConfigurationValidateResultEntrySegmentMatchResult[]
    | null;
  globalMatchedValues?:
    | BakabaseInsideWorldModelsModelsAosPathConfigurationValidateResultEntryGlobalMatchedValue[]
    | null;
}

export interface BakabaseInsideWorldModelsModelsAosPathConfigurationValidateResultEntryGlobalMatchedValue {
  /** [1: RootPath, 2: ParentResource, 3: Resource, 4: ReleaseDt, 5: Publisher, 6: Name, 7: Language, 8: Volume, 9: Original, 10: Series, 11: Tag, 12: Introduction, 13: Rate, 14: CustomProperty] */
  property?: BakabaseInsideWorldModelsConstantsResourceProperty;
  key?: string | null;
  values?: string[] | null;
}

export interface BakabaseInsideWorldModelsModelsAosPathConfigurationValidateResultEntrySegmentMatchResult {
  value?: string | null;
  properties?:
    | BakabaseInsideWorldModelsModelsAosPathConfigurationValidateResultEntrySegmentMatchResultSegmentPropertyResult[]
    | null;
}

export interface BakabaseInsideWorldModelsModelsAosPathConfigurationValidateResultEntrySegmentMatchResultSegmentPropertyResult {
  /** [1: RootPath, 2: ParentResource, 3: Resource, 4: ReleaseDt, 5: Publisher, 6: Name, 7: Language, 8: Volume, 9: Original, 10: Series, 11: Tag, 12: Introduction, 13: Rate, 14: CustomProperty] */
  property?: BakabaseInsideWorldModelsConstantsResourceProperty;
  keys?: string[] | null;
}

export interface BakabaseInsideWorldModelsModelsAosPreviewerItem {
  filePath?: string | null;
  /** [1: Image, 2: Audio, 3: Video, 4: Text, 1000: Unknown] */
  type?: BakabaseInsideWorldModelsConstantsMediaType;
  /** @format int32 */
  duration?: number;
}

export interface BakabaseInsideWorldModelsModelsAosResourceDiff {
  /** [0: Category, 1: MediaLibrary, 2: ReleaseDt, 3: Publisher, 4: Name, 5: Language, 6: Volume, 7: Original, 8: Series, 9: Tag, 10: Introduction, 11: Rate, 12: CustomProperty] */
  property?: BakabaseInsideWorldModelsConstantsResourceDiffProperty;
  currentValue?: any;
  newValue?: any;
  /** [1: Added, 2: Removed, 3: Modified] */
  type?: BakabaseInsideWorldModelsConstantsResourceDiffType;
  key?: string | null;
  subDiffs?: BakabaseInsideWorldModelsModelsAosResourceDiff[] | null;
}

export interface BakabaseInsideWorldModelsModelsAosResourceSearchDto {
  /** @format int32 */
  pageIndex?: number;
  /** @format int32 */
  skipCount?: number;
  name?: string | null;
  /** @format date-time */
  releaseStartDt?: string | null;
  /** @format date-time */
  releaseEndDt?: string | null;
  /** @format date-time */
  addStartDt?: string | null;
  /** @format date-time */
  addEndDt?: string | null;
  /** @format date-time */
  fileCreateStartDt?: string | null;
  /** @format date-time */
  fileCreateEndDt?: string | null;
  /** @format date-time */
  fileModifyStartDt?: string | null;
  /** @format date-time */
  fileModifyEndDt?: string | null;
  favoritesIds?: number[] | null;
  /** @format int32 */
  categoryId?: number | null;
  publisher?: string | null;
  original?: string | null;
  series?: string | null;
  /** @format double */
  minRate?: number | null;
  languages?: BakabaseInsideWorldModelsConstantsResourceLanguage[] | null;
  mediaLibraryIds?: number[] | null;
  tagIds?: number[] | null;
  excludedTagIds?: number[] | null;
  everything?: string | null;
  orders?: BakabaseInsideWorldModelsConfigsResourceResourceSearchOptionsOrderModel[] | null;
  customProperties?: Record<string, string | null>;
  customPropertyKeys?: string[] | null;
  /** @format int32 */
  parentId?: number | null;
  /** @format int32 */
  pageSize?: number;
  hideChildren?: boolean;
  save?: boolean;
}

export interface BakabaseInsideWorldModelsModelsAosResourceSearchSlotItemDto {
  name?: string | null;
  model?: BakabaseInsideWorldModelsModelsAosResourceSearchDto;
}

export interface BakabaseInsideWorldModelsModelsAosThirdPartyRequestStatistics {
  /** [1: Bilibili, 2: ExHentai, 3: Pixiv] */
  id?: BakabaseInsideWorldModelsConstantsThirdPartyId;
  counts?: Record<string, number>;
}

export interface BakabaseInsideWorldModelsModelsDtosAliasDto {
  /** @format int32 */
  id?: number;
  name?: string | null;
  candidates?: BakabaseInsideWorldModelsModelsDtosAliasDto[] | null;
  /** @format int32 */
  groupId?: number;
  /** @uniqueItems true */
  allNames?: string[] | null;
}

export interface BakabaseInsideWorldModelsModelsDtosComponentDescriptor {
  /** [0: Invalid, 1: Fixed, 2: Configurable, 3: Instance] */
  type?: BakabaseInsideWorldModelsConstantsComponentDescriptorType;
  /** [1: Enhancer, 2: PlayableFileSelector, 3: Player] */
  componentType?: BakabaseInsideWorldModelsConstantsComponentType;
  assemblyQualifiedTypeName?: string | null;
  name?: string | null;
  description?: string | null;
  message?: string | null;
  optionsJson?: string | null;
  /** @format int32 */
  optionsId?: number | null;
  version?: string | null;
  dataVersion?: string | null;
  optionsType?: SystemType;
  optionsJsonSchema?: string | null;
  id?: string | null;
  isInstanceable?: boolean;
}

export interface BakabaseInsideWorldModelsModelsDtosComponentDescriptorDto {
  /** [0: Invalid, 1: Fixed, 2: Configurable, 3: Instance] */
  type?: BakabaseInsideWorldModelsConstantsComponentDescriptorType;
  /** [1: Enhancer, 2: PlayableFileSelector, 3: Player] */
  componentType?: BakabaseInsideWorldModelsConstantsComponentType;
  assemblyQualifiedTypeName?: string | null;
  name?: string | null;
  description?: string | null;
  message?: string | null;
  optionsJson?: string | null;
  /** @format int32 */
  optionsId?: number | null;
  version?: string | null;
  dataVersion?: string | null;
  optionsType?: SystemType;
  optionsJsonSchema?: string | null;
  id?: string | null;
  isInstanceable?: boolean;
  associatedCategories?: BakabaseInsideWorldModelsModelsDtosResourceCategoryDto[] | null;
}

export interface BakabaseInsideWorldModelsModelsDtosDashboardStatistics {
  categoryResourceCounts?: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsTextAndCount[] | null;
  todayAddedCategoryResourceCounts?: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsTextAndCount[] | null;
  thisWeekAddedCategoryResourceCounts?: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsTextAndCount[] | null;
  thisMonthAddedCategoryResourceCounts?: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsTextAndCount[] | null;
  resourceTrending?: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsWeekCount[] | null;
  propertyResourceCounts?: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsPropertyAndCount[] | null;
  tagResourceCounts?: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsTextAndCount[] | null;
  downloaderDataCounts?: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsDownloaderTaskCount[] | null;
  thirdPartyRequestCounts?: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsThirdPartyRequestCount[] | null;
  fileMover?: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsFileMoverInfo;
  otherCounts?: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsTextAndCount[][] | null;
}

export interface BakabaseInsideWorldModelsModelsDtosDashboardStatisticsDownloaderTaskCount {
  /** [1: Bilibili, 2: ExHentai, 3: Pixiv] */
  id?: BakabaseInsideWorldModelsConstantsThirdPartyId;
  statusAndCounts?: Record<string, number>;
}

export interface BakabaseInsideWorldModelsModelsDtosDashboardStatisticsFileMoverInfo {
  /** @format int32 */
  sourceCount?: number;
  /** @format int32 */
  targetCount?: number;
}

export interface BakabaseInsideWorldModelsModelsDtosDashboardStatisticsPropertyAndCount {
  /** [1: RootPath, 2: ParentResource, 3: Resource, 4: ReleaseDt, 5: Publisher, 6: Name, 7: Language, 8: Volume, 9: Original, 10: Series, 11: Tag, 12: Introduction, 13: Rate, 14: CustomProperty] */
  property?: BakabaseInsideWorldModelsConstantsResourceProperty;
  propertyKey?: string | null;
  value?: string | null;
  /** @format int32 */
  count?: number;
}

export interface BakabaseInsideWorldModelsModelsDtosDashboardStatisticsTextAndCount {
  label?: string | null;
  name?: string | null;
  /** @format int32 */
  count?: number;
}

export interface BakabaseInsideWorldModelsModelsDtosDashboardStatisticsThirdPartyRequestCount {
  /** [1: Bilibili, 2: ExHentai, 3: Pixiv] */
  id?: BakabaseInsideWorldModelsConstantsThirdPartyId;
  /** @format int32 */
  resultType?: number;
  /** @format int32 */
  taskCount?: number;
}

export interface BakabaseInsideWorldModelsModelsDtosDashboardStatisticsWeekCount {
  /** @format int32 */
  offset?: number;
  /** @format int32 */
  count?: number;
}

export interface BakabaseInsideWorldModelsModelsDtosDownloadTaskDto {
  /** @format int32 */
  id?: number;
  key?: string | null;
  name?: string | null;
  /** [1: Bilibili, 2: ExHentai, 3: Pixiv] */
  thirdPartyId?: BakabaseInsideWorldModelsConstantsThirdPartyId;
  /** @format int32 */
  type?: number;
  /** @format double */
  progress?: number;
  /** @format date-time */
  downloadStatusUpdateDt?: string;
  /** @format int64 */
  interval?: number | null;
  /** @format int32 */
  startPage?: number | null;
  /** @format int32 */
  endPage?: number | null;
  message?: string | null;
  checkpoint?: string | null;
  /** [100: Idle, 200: InQueue, 300: Starting, 400: Downloading, 500: Stopping, 600: Complete, 700: Failed, 800: Disabled] */
  status?: BakabaseInsideWorldModelsConstantsDownloadTaskDtoStatus;
  downloadPath?: string | null;
  current?: string | null;
  /** @format int32 */
  failureTimes?: number;
  /** @format date-time */
  nextStartDt?: string | null;
  /** @uniqueItems true */
  availableActions?: BakabaseInsideWorldModelsConstantsDownloadTaskAction[] | null;
  displayName?: string | null;
  canStart?: boolean;
}

export interface BakabaseInsideWorldModelsModelsDtosEnhancementRecordDto {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  resourceId?: number;
  resourceRawFullName?: string | null;
  enhancerName?: string | null;
  enhancerDescriptorId?: string | null;
  ruleId?: string | null;
  success?: boolean;
  enhancement?: string | null;
  message?: string | null;
  /** @format date-time */
  createDt?: string;
}

export interface BakabaseInsideWorldModelsModelsDtosFavoritesDto {
  /** @format int32 */
  id?: number;
  /** @minLength 1 */
  name: string;
  description?: string | null;
  /** @format date-time */
  createDt?: string;
}

export interface BakabaseInsideWorldModelsModelsDtosMediaLibraryDto {
  /** @format int32 */
  id?: number;
  /** @minLength 1 */
  name: string;
  /** @format int32 */
  categoryId: number;
  pathConfigurationsJson?: string | null;
  /** @format int32 */
  order?: number;
  /** @format int32 */
  resourceCount?: number;
  rootPathInformation?: Record<
    string,
    BakabaseInsideWorldModelsModelsDtosMediaLibraryDtoSingleMediaLibraryRootPathInformation
  >;
  categoryName?: string | null;
  pathConfigurations?: BakabaseInsideWorldModelsModelsDtosMediaLibraryDtoPathConfigurationDto[] | null;
}

export interface BakabaseInsideWorldModelsModelsDtosMediaLibraryDtoPathConfigurationDto {
  path?: string | null;
  /** @deprecated */
  regex?: string | null;
  fixedTagIds?: number[] | null;
  /** @deprecated */
  segments?: BakabaseInsideWorldModelsModelsEntitiesMediaLibraryPathConfigurationSegmentMatcher[] | null;
  rpmValues?: BakabaseInsideWorldModelsModelsAosMatcherValue[] | null;
  fixedTags?: BakabaseInsideWorldModelsModelsDtosTagDto[] | null;
}

export interface BakabaseInsideWorldModelsModelsDtosMediaLibraryDtoSingleMediaLibraryRootPathInformation {
  /** @format int64 */
  totalSize?: number;
  /** @format int64 */
  freeSpace?: number;
  /** @format double */
  usedPercentage?: number;
  /** @format double */
  freePercentage?: number;
  /** @format double */
  freeSpaceInGb?: number;
  /** [1: InvalidVolume, 2: FreeSpaceNotEnough, 3: Occupied] */
  error?: BakabaseInsideWorldModelsConstantsMediaLibraryError;
}

export interface BakabaseInsideWorldModelsModelsDtosOriginalDto {
  /** @format int32 */
  id?: number;
  name?: string | null;
}

export interface BakabaseInsideWorldModelsModelsDtosPlaylistDto {
  /** @format int32 */
  id?: number;
  name?: string | null;
  items?: BakabaseInsideWorldModelsModelsDtosPlaylistItemDto[] | null;
  /** @format int32 */
  interval?: number;
  /** @format int32 */
  order?: number;
}

export interface BakabaseInsideWorldModelsModelsDtosPlaylistItemDto {
  /** [1: Resource, 2: Video, 3: Image, 4: Audio] */
  type?: BakabaseInsideWorldModelsConstantsPlaylistItemType;
  /** @format int32 */
  resourceId?: number | null;
  file?: string | null;
  startTime?: SystemTimeSpan;
  endTime?: SystemTimeSpan;
}

export interface BakabaseInsideWorldModelsModelsDtosPublisherDto {
  /** @format int32 */
  id?: number;
  name?: string | null;
  subPublishers?: BakabaseInsideWorldModelsModelsDtosPublisherDto[] | null;
  /** @format int32 */
  rank?: number;
  favorite?: boolean;
  tags?: BakabaseInsideWorldModelsModelsDtosTagDto[] | null;
}

export interface BakabaseInsideWorldModelsModelsDtosResourceCategoryDto {
  /** @format int32 */
  id?: number;
  name?: string | null;
  color?: string | null;
  /** @format date-time */
  createDt?: string;
  isValid?: boolean;
  message?: string | null;
  /** @format int32 */
  order?: number;
  componentsData?: BakabaseInsideWorldModelsModelsEntitiesCategoryComponent[] | null;
  /** [1: FilenameAscending, 2: FileModifyDtDescending] */
  coverSelectionOrder?: BakabaseInsideWorldModelsConstantsCoverSelectOrder;
  enhancementOptions?: BakabaseInsideWorldModelsModelsDtosResourceCategoryEnhancementOptions;
  generateNfo?: boolean;
}

export interface BakabaseInsideWorldModelsModelsDtosResourceCategoryEnhancementOptions {
  enhancementPriorities?: Record<string, string[]>;
  defaultPriority?: string[] | null;
}

export interface BakabaseInsideWorldModelsModelsDtosResourceDto {
  /** @format int32 */
  id?: number;
  directory?: string | null;
  /** @format date-time */
  releaseDt?: string | null;
  name?: string | null;
  volume?: BakabaseInsideWorldModelsModelsDtosVolumeDto;
  /** [0: NotSet, 1: Chinese, 2: English, 3: Japanese, 4: Korean, 5: French, 6: German, 7: Spanish, 8: Russian] */
  language?: BakabaseInsideWorldModelsConstantsResourceLanguage;
  publishers?: BakabaseInsideWorldModelsModelsDtosPublisherDto[] | null;
  originals?: BakabaseInsideWorldModelsModelsDtosOriginalDto[] | null;
  displayName?: string | null;
  rawName?: string | null;
  /** @format double */
  rate?: number;
  isSingleFile?: boolean;
  /** @format int32 */
  parentId?: number | null;
  hasChildren?: boolean;
  rawFullname?: string | null;
  series?: BakabaseInsideWorldModelsModelsDtosSeriesDto;
  tags?: BakabaseInsideWorldModelsModelsDtosTagDto[] | null;
  introduction?: string | null;
  /** @format int32 */
  mediaLibraryId?: number;
  /** @format int32 */
  categoryId?: number;
  /** @format date-time */
  createDt?: string;
  /** @format date-time */
  updateDt?: string;
  /** @format date-time */
  fileCreateDt?: string;
  /** @format date-time */
  fileModifyDt?: string;
  customProperties?: Record<string, BakabaseInsideWorldModelsModelsEntitiesCustomResourceProperty[] | null>;
  parent?: BakabaseInsideWorldModelsModelsDtosResourceDto;
}

export interface BakabaseInsideWorldModelsModelsDtosSeriesDto {
  /** @format int32 */
  id?: number;
  name?: string | null;
}

export interface BakabaseInsideWorldModelsModelsDtosTagDto {
  /** @format int32 */
  id?: number;
  name?: string | null;
  preferredAlias?: string | null;
  color?: string | null;
  /** @format int32 */
  order?: number;
  /** @format int32 */
  groupId?: number;
  groupName?: string | null;
  groupNamePreferredAlias?: string | null;
  displayName?: string | null;
}

export interface BakabaseInsideWorldModelsModelsDtosTagGroupDto {
  /** @format int32 */
  id?: number;
  name?: string | null;
  /** @format int32 */
  order?: number;
  tags?: BakabaseInsideWorldModelsModelsDtosTagDto[] | null;
  preferredAlias?: string | null;
}

export interface BakabaseInsideWorldModelsModelsDtosVolumeDto {
  /** @format int32 */
  index?: number;
  name?: string | null;
  title?: string | null;
  /** @format int32 */
  serialId?: number;
  /** @format int32 */
  resourceId?: number;
}

export interface BakabaseInsideWorldModelsModelsEntitiesAlias {
  /** @format int32 */
  id?: number;
  /**
   * @minLength 1
   * @maxLength 256
   */
  name: string;
  /** @format int32 */
  groupId?: number;
  isPreferred?: boolean;
}

export interface BakabaseInsideWorldModelsModelsEntitiesCategoryComponent {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  categoryId: number;
  /** @minLength 1 */
  componentKey: string;
  /** [1: Enhancer, 2: PlayableFileSelector, 3: Player] */
  componentType: BakabaseInsideWorldModelsConstantsComponentType;
  descriptor?: BakabaseInsideWorldModelsModelsDtosComponentDescriptor;
}

export interface BakabaseInsideWorldModelsModelsEntitiesComponentOptions {
  /** @format int32 */
  id?: number;
  /** [1: Enhancer, 2: PlayableFileSelector, 3: Player] */
  componentType: BakabaseInsideWorldModelsConstantsComponentType;
  /** @minLength 1 */
  componentAssemblyQualifiedTypeName: string;
  /** @minLength 1 */
  name: string;
  description?: string | null;
  /** @minLength 1 */
  json: string;
}

export interface BakabaseInsideWorldModelsModelsEntitiesCustomResourceProperty {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  resourceId?: number;
  /** @minLength 1 */
  key: string;
  /** @format int32 */
  index?: number | null;
  /** @minLength 1 */
  value: string;
  /** [1: String, 2: DateTime, 3: Number, 4: Enum] */
  valueType?: BakabaseInsideWorldModelsConstantsCustomDataType;
}

export interface BakabaseInsideWorldModelsModelsEntitiesDownloadTask {
  /** @format int32 */
  id?: number;
  /** @minLength 1 */
  key: string;
  name?: string | null;
  /** [1: Bilibili, 2: ExHentai, 3: Pixiv] */
  thirdPartyId?: BakabaseInsideWorldModelsConstantsThirdPartyId;
  /** @format int32 */
  type?: number;
  /** @format double */
  progress?: number;
  /** @format date-time */
  downloadStatusUpdateDt?: string;
  /** @format int64 */
  interval?: number | null;
  /** @format int32 */
  startPage?: number | null;
  /** @format int32 */
  endPage?: number | null;
  message?: string | null;
  checkpoint?: string | null;
  /** [100: InProgress, 200: Disabled, 300: Complete, 400: Failed] */
  status?: BakabaseInsideWorldModelsConstantsDownloadTaskStatus;
  /** @minLength 1 */
  downloadPath: string;
  displayName?: string | null;
}

export interface BakabaseInsideWorldModelsModelsEntitiesMediaLibraryPathConfiguration {
  path?: string | null;
  /** @deprecated */
  regex?: string | null;
  fixedTagIds?: number[] | null;
  /** @deprecated */
  segments?: BakabaseInsideWorldModelsModelsEntitiesMediaLibraryPathConfigurationSegmentMatcher[] | null;
  rpmValues?: BakabaseInsideWorldModelsModelsAosMatcherValue[] | null;
}

export interface BakabaseInsideWorldModelsModelsEntitiesMediaLibraryPathConfigurationSegmentMatcher {
  fixedText?: string | null;
  /** @format int32 */
  layer?: number | null;
  regex?: string | null;
  /** [1: RootPath, 2: ParentResource, 3: Resource, 4: ReleaseDt, 5: Publisher, 6: Name, 7: Language, 8: Volume, 9: Original, 10: Series, 11: Tag, 12: Introduction, 13: Rate, 14: CustomProperty] */
  property?: BakabaseInsideWorldModelsConstantsResourceProperty;
  /** [1: Layer, 2: Regex, 3: FixedText] */
  valueType?: BakabaseInsideWorldModelsConstantsResourceMatcherValueType;
  key?: string | null;
  isValid?: boolean;
  /** @deprecated */
  isReverse?: boolean;
  /** [1: RootPath, 2: ParentResource, 3: Resource, 4: ReleaseDt, 5: Publisher, 6: Name, 7: Language, 8: Volume, 9: Original, 10: Series, 11: Tag, 12: Introduction, 13: Rate, 14: CustomProperty] */
  type?: BakabaseInsideWorldModelsConstantsResourceProperty;
}

export interface BakabaseInsideWorldModelsModelsEntitiesPassword {
  /** @maxLength 64 */
  text?: string | null;
  /** @format int32 */
  usedTimes?: number;
  /** @format date-time */
  lastUsedAt?: string;
}

export interface BakabaseInsideWorldModelsModelsEntitiesSpecialText {
  /** @format int32 */
  id?: number;
  /**
   * @minLength 1
   * @maxLength 64
   */
  value1: string;
  /** @maxLength 64 */
  value2?: string | null;
  /** [1: Useless, 2: Language, 3: Wrapper, 4: Standardization, 6: Volume, 7: Trim, 8: DateTime] */
  type?: BakabaseInsideWorldModelsConstantsSpecialTextType;
}

export interface BakabaseInsideWorldModelsRequestModelsAliasCreateRequestModel {
  /** @minLength 1 */
  name: string;
  /** @format int32 */
  groupId?: number | null;
}

export interface BakabaseInsideWorldModelsRequestModelsAliasGroupUpdateRequestModel {
  /** @format int32 */
  targetGroupId?: number;
}

export interface BakabaseInsideWorldModelsRequestModelsAliasUpdateRequestModel {
  name?: string | null;
  isPreferred?: boolean;
}

export interface BakabaseInsideWorldModelsRequestModelsCategorySetupWizardRequestModel {
  category?: BakabaseInsideWorldModelsRequestModelsResourceCategoryAddRequestModel;
  mediaLibraries?: BakabaseInsideWorldModelsModelsDtosMediaLibraryDto[] | null;
  syncAfterSaving?: boolean;
}

export interface BakabaseInsideWorldModelsRequestModelsComponentOptionsAddRequestModel {
  /** @minLength 1 */
  name: string;
  description?: string | null;
  /** @minLength 1 */
  componentAssemblyQualifiedTypeName: string;
  /** @minLength 1 */
  json: string;
}

export interface BakabaseInsideWorldModelsRequestModelsCoverSaveRequestModel {
  /** @minLength 1 */
  base64Image: string;
  overwrite?: boolean;
  /** [1: ResourceDirectory, 2: TempDirectory] */
  saveLocation?: BakabaseInsideWorldModelsConstantsCoverSaveLocation;
}

export interface BakabaseInsideWorldModelsRequestModelsDownloadTaskCreateRequestModel {
  /** [1: Bilibili, 2: ExHentai, 3: Pixiv] */
  thirdPartyId: BakabaseInsideWorldModelsConstantsThirdPartyId;
  /** @format int32 */
  type: number;
  keyAndNames?: Record<string, string>;
  /** @format int64 */
  interval?: number | null;
  /** @format int32 */
  startPage?: number | null;
  /** @format int32 */
  endPage?: number | null;
  checkpoint?: string | null;
  forceCreating?: boolean;
  /** @minLength 1 */
  downloadPath: string;
}

export interface BakabaseInsideWorldModelsRequestModelsDownloadTaskStartRequestModel {
  ids?: number[] | null;
  /** [0: NotSet, 1: StopOthers, 2: Ignore] */
  actionOnConflict?: BakabaseInsideWorldModelsConstantsDownloadTaskActionOnConflict;
}

export interface BakabaseInsideWorldModelsRequestModelsFavoritesAddOrUpdateRequestModel {
  /** @format int32 */
  id?: number;
  /** @minLength 1 */
  name: string;
  description?: string | null;
  /** @format date-time */
  createDt?: string;
}

export interface BakabaseInsideWorldModelsRequestModelsFavoritesResourceMappingAddOrRemoveRequestModel {
  /** @format int32 */
  resourceId?: number;
}

export interface BakabaseInsideWorldModelsRequestModelsFileDecompressRequestModel {
  paths?: string[] | null;
  password?: string | null;
}

export interface BakabaseInsideWorldModelsRequestModelsFileMoveRequestModel {
  destDir?: string | null;
  entryPaths?: string[] | null;
}

export interface BakabaseInsideWorldModelsRequestModelsFileRemoveRequestModel {
  paths?: string[] | null;
}

export interface BakabaseInsideWorldModelsRequestModelsFileRenameRequestModel {
  fullname?: string | null;
  newName?: string | null;
}

export interface BakabaseInsideWorldModelsRequestModelsIdBasedSortRequestModel {
  ids?: number[] | null;
}

export interface BakabaseInsideWorldModelsRequestModelsMediaLibraryCreateRequestModel {
  /** @minLength 1 */
  name: string;
  /** @format int32 */
  categoryId: number;
  pathConfigurations?: BakabaseInsideWorldModelsModelsEntitiesMediaLibraryPathConfiguration[] | null;
}

export interface BakabaseInsideWorldModelsRequestModelsMediaLibraryUpdateRequestModel {
  name?: string | null;
  pathConfigurations?: BakabaseInsideWorldModelsModelsEntitiesMediaLibraryPathConfiguration[] | null;
  /** @format int32 */
  order?: number | null;
}

export interface BakabaseInsideWorldModelsRequestModelsOptionsResourceOptionsPatchRequestModel {
  /** @format date-time */
  lastSyncDt?: string | null;
  /** @format date-time */
  lastNfoGenerationDt?: string | null;
  lastSearch?: BakabaseInsideWorldModelsModelsAosResourceSearchDto;
  searchSlots?: BakabaseInsideWorldModelsModelsAosResourceSearchSlotItemDto[] | null;
  additionalCoverDiscoveringSources?: BakabaseInsideWorldModelsConstantsAdditionalCoverDiscoveringSource[] | null;
  coverOptions?: BakabaseInsideWorldModelsConfigsResourceResourceOptionsCoverOptionsModel;
}

export interface BakabaseInsideWorldModelsRequestModelsPathConfigurationRemoveRequestModel {
  /** @format int32 */
  index: number;
}

export interface BakabaseInsideWorldModelsRequestModelsPublisherUpdateModel {
  name?: string | null;
  /** @format int32 */
  rank?: number | null;
  favorite?: boolean | null;
  tagIds?: number[] | null;
}

export interface BakabaseInsideWorldModelsRequestModelsRemoveSameEntryInWorkingDirectoryRequestModel {
  workingDir?: string | null;
  entryPath?: string | null;
}

export interface BakabaseInsideWorldModelsRequestModelsResourceCategoryAddRequestModel {
  /** @format int32 */
  id?: number;
  name?: string | null;
  color?: string | null;
  /** [1: FilenameAscending, 2: FileModifyDtDescending] */
  coverSelectionOrder?: BakabaseInsideWorldModelsConstantsCoverSelectOrder;
  /** @format int32 */
  order?: number | null;
  generateNfo?: boolean | null;
  componentsData?:
    | BakabaseInsideWorldModelsRequestModelsResourceCategoryAddRequestModelSimpleCategoryComponent[]
    | null;
  enhancementOptions?: BakabaseInsideWorldModelsModelsDtosResourceCategoryEnhancementOptions;
}

export interface BakabaseInsideWorldModelsRequestModelsResourceCategoryAddRequestModelSimpleCategoryComponent {
  /** @minLength 1 */
  componentKey: string;
  /** [1: Enhancer, 2: PlayableFileSelector, 3: Player] */
  componentType: BakabaseInsideWorldModelsConstantsComponentType;
}

export interface BakabaseInsideWorldModelsRequestModelsResourceCategoryComponentConfigureRequestModel {
  /** [1: Enhancer, 2: PlayableFileSelector, 3: Player] */
  type: BakabaseInsideWorldModelsConstantsComponentType;
  componentKeys: string[];
  enhancementOptions?: BakabaseInsideWorldModelsModelsDtosResourceCategoryEnhancementOptions;
}

export interface BakabaseInsideWorldModelsRequestModelsResourceCategoryUpdateRequestModel {
  /** @format int32 */
  id?: number;
  name?: string | null;
  color?: string | null;
  /** [1: FilenameAscending, 2: FileModifyDtDescending] */
  coverSelectionOrder?: BakabaseInsideWorldModelsConstantsCoverSelectOrder;
  /** @format int32 */
  order?: number | null;
  generateNfo?: boolean | null;
}

export interface BakabaseInsideWorldModelsRequestModelsResourceMoveRequestModel {
  ids: number[];
  /** @format int32 */
  mediaLibraryId?: number | null;
  /** @minLength 1 */
  path: string;
}

export interface BakabaseInsideWorldModelsRequestModelsResourceRawNameUpdateRequestModel {
  /** @minLength 1 */
  rawName: string;
}

export interface BakabaseInsideWorldModelsRequestModelsResourceTagUpdateRequestModel {
  resourceTagIds: Record<string, number[]>;
}

export interface BakabaseInsideWorldModelsRequestModelsResourceUpdateRequestModel {
  publishers?: BakabaseInsideWorldModelsModelsDtosPublisherDto[] | null;
  name?: string | null;
  originals?: string[] | null;
  series?: string | null;
  /** @format date-time */
  releaseDt?: string | null;
  /** [0: NotSet, 1: Chinese, 2: English, 3: Japanese, 4: Korean, 5: French, 6: German, 7: Spanish, 8: Russian] */
  language?: BakabaseInsideWorldModelsConstantsResourceLanguage;
  /** @format double */
  rate?: number | null;
  tags?: BakabaseInsideWorldModelsModelsDtosTagDto[] | null;
  introduction?: string | null;
}

export interface BakabaseInsideWorldModelsRequestModelsSpecialTextCreateRequestModel {
  /** [1: Useless, 2: Language, 3: Wrapper, 4: Standardization, 6: Volume, 7: Trim, 8: DateTime] */
  type?: BakabaseInsideWorldModelsConstantsSpecialTextType;
  value1?: string | null;
  value2?: string | null;
}

export interface BakabaseInsideWorldModelsRequestModelsSpecialTextUpdateRequestModel {
  value1?: string | null;
  value2?: string | null;
}

export interface BakabaseInsideWorldModelsRequestModelsSubdirectoriesExtractRequestModel {
  /** @minLength 1 */
  path: string;
}

export interface BakabaseInsideWorldModelsRequestModelsTagGroupAddRequestModel {
  names: string[];
}

export interface BakabaseInsideWorldModelsRequestModelsTagGroupUpdateRequestModel {
  name?: string | null;
  /** @format int32 */
  order?: number | null;
}

export interface BakabaseInsideWorldModelsRequestModelsTagMoveRequestModel {
  /** @format int32 */
  targetTagId?: number | null;
  /** @format int32 */
  targetGroupId?: number | null;
}

export interface BakabaseInsideWorldModelsRequestModelsTagNameUpdateRequestModel {
  /** @minLength 1 */
  name: string;
}

export interface BakabaseInsideWorldModelsRequestModelsTagUpdateRequestModel {
  color?: string | null;
  /** @format int32 */
  groupId?: number | null;
  /** @format int32 */
  order?: number | null;
}

export interface BakabaseInsideWorldModelsRequestModelsUIOptionsPatchRequestModel {
  resource?: BakabaseInsideWorldModelsConfigsUIOptionsUIResourceOptions;
  /** [0: Default, 1: Resource] */
  startupPage?: BakabaseInsideWorldModelsConstantsStartupPage;
}

export interface BakabaseInsideWorldModelsResponseModelsEverythingExtractionStatus {
  running?: boolean;
  current?: string | null;
  /** @format int32 */
  doneCount?: number;
  /** @format int32 */
  failedCount?: number;
  /** @format int32 */
  totalCount?: number;
  /** @format int32 */
  percent?: number;
  /** @format date-time */
  startDt?: string;
  failures?: BakabaseInsideWorldModelsResponseModelsEverythingExtractionStatusFailure[] | null;
}

export interface BakabaseInsideWorldModelsResponseModelsEverythingExtractionStatusFailure {
  fullnameList?: string[] | null;
  error?: string | null;
}

export interface BootstrapComponentsLoggingLogServiceModelsEntitiesLog {
  /** @format int32 */
  id?: number;
  /** @format date-time */
  dateTime?: string;
  /** [0: Trace, 1: Debug, 2: Information, 3: Warning, 4: Error, 5: Critical, 6: None] */
  level?: MicrosoftExtensionsLoggingLogLevel;
  logger?: string | null;
  event?: string | null;
  message?: string | null;
  read?: boolean;
}

export interface BootstrapModelsResponseModelsBaseResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationDto[] | null;
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationResourceDiffs {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?:
    | BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationResourceDiffs[]
    | null;
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldBusinessComponentsCompressionCompressedFileEntry {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldBusinessComponentsCompressionCompressedFileEntry[] | null;
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldBusinessComponentsTasksBackgroundTaskDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldBusinessComponentsTasksBackgroundTaskDto[] | null;
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldBusinessComponentsThirdPartyBilibiliModelsFavorites {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldBusinessComponentsThirdPartyBilibiliModelsFavorites[] | null;
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsAosPreviewerItem {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsAosPreviewerItem[] | null;
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosComponentDescriptorDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsDtosComponentDescriptorDto[] | null;
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosDownloadTaskDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsDtosDownloadTaskDto[] | null;
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosEnhancementRecordDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsDtosEnhancementRecordDto[] | null;
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosFavoritesDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsDtosFavoritesDto[] | null;
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosMediaLibraryDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsDtosMediaLibraryDto[] | null;
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosOriginalDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsDtosOriginalDto[] | null;
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosPlaylistDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsDtosPlaylistDto[] | null;
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosPublisherDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsDtosPublisherDto[] | null;
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosResourceCategoryDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsDtosResourceCategoryDto[] | null;
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosResourceDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsDtosResourceDto[] | null;
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosTagDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsDtosTagDto[] | null;
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosTagGroupDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsDtosTagGroupDto[] | null;
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsEntitiesPassword {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsEntitiesPassword[] | null;
}

export interface BootstrapModelsResponseModelsListResponse1BootstrapComponentsLoggingLogServiceModelsEntitiesLog {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BootstrapComponentsLoggingLogServiceModelsEntitiesLog[] | null;
}

export interface BootstrapModelsResponseModelsListResponse1SystemCollectionsGenericList1SystemString {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: string[][] | null;
}

export interface BootstrapModelsResponseModelsListResponse1SystemInt32 {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: number[] | null;
}

export interface BootstrapModelsResponseModelsListResponse1SystemString {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: string[] | null;
}

export interface BootstrapModelsResponseModelsSearchResponse1BakabaseInsideWorldModelsModelsDtosAliasDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsDtosAliasDto[] | null;
  /** @format int32 */
  totalCount?: number;
  /** @format int32 */
  pageIndex?: number;
  /** @format int32 */
  pageSize?: number;
}

export interface BootstrapModelsResponseModelsSearchResponse1BakabaseInsideWorldModelsModelsDtosEnhancementRecordDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsDtosEnhancementRecordDto[] | null;
  /** @format int32 */
  totalCount?: number;
  /** @format int32 */
  pageIndex?: number;
  /** @format int32 */
  pageSize?: number;
}

export interface BootstrapModelsResponseModelsSearchResponse1BakabaseInsideWorldModelsModelsDtosResourceDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsDtosResourceDto[] | null;
  /** @format int32 */
  totalCount?: number;
  /** @format int32 */
  pageIndex?: number;
  /** @format int32 */
  pageSize?: number;
}

export interface BootstrapModelsResponseModelsSearchResponse1BakabaseInsideWorldModelsModelsEntitiesPassword {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsEntitiesPassword[] | null;
  /** @format int32 */
  totalCount?: number;
  /** @format int32 */
  pageIndex?: number;
  /** @format int32 */
  pageSize?: number;
}

export interface BootstrapModelsResponseModelsSearchResponse1BootstrapComponentsLoggingLogServiceModelsEntitiesLog {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BootstrapComponentsLoggingLogServiceModelsEntitiesLog[] | null;
  /** @format int32 */
  totalCount?: number;
  /** @format int32 */
  pageIndex?: number;
  /** @format int32 */
  pageSize?: number;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInfrastructuresComponentsConfigurationsAppAppOptions {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInfrastructuresComponentsConfigurationsAppAppOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationDto;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsDependencyAbstractionsDependentComponentVersion {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldBusinessComponentsDependencyAbstractionsDependentComponentVersion;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsFileExplorerInformationIwFsEntryLazyInfo {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldBusinessComponentsFileExplorerInformationIwFsEntryLazyInfo;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsFileExplorerInformationIwFsTaskInfo {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldBusinessComponentsFileExplorerInformationIwFsTaskInfo;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsFileExplorerIwFsPreview {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsPreview;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsTasksBackgroundTaskDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldBusinessComponentsTasksBackgroundTaskDto;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsBilibiliOptions {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsConfigsBilibiliOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsExHentaiOptions {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsConfigsExHentaiOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsFileSystemOptions {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsConfigsFileSystemOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsJavLibraryOptions {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsConfigsJavLibraryOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsNetworkOptions {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsConfigsNetworkOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsPixivOptions {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsConfigsPixivOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsResourceResourceOptionsDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsConfigsResourceResourceOptionsDto;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsThirdPartyOptions {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsConfigsThirdPartyOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsUIOptions {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsConfigsUIOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConstantsInitializationContentType {
  /** @format int32 */
  code?: number;
  message?: string | null;
  /** [1: NotAcceptTerms, 2: NeedRestart] */
  data?: BakabaseInsideWorldModelsConstantsInitializationContentType;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsAosFileEntriesMergeResult {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsAosFileEntriesMergeResult;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsAosPathConfigurationValidateResult {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsAosPathConfigurationValidateResult;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsAosThirdPartyRequestStatistics {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsAosThirdPartyRequestStatistics[] | null;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsDtosComponentDescriptorDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsDtosComponentDescriptorDto;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsDtosDashboardStatistics {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsDtosDashboardStatistics;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsDtosDownloadTaskDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsDtosDownloadTaskDto;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsDtosPlaylistDto {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsDtosPlaylistDto;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsEntitiesAlias {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsEntitiesAlias;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsEntitiesComponentOptions {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsEntitiesComponentOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsEntitiesSpecialText {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsModelsEntitiesSpecialText;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsResponseModelsEverythingExtractionStatus {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BakabaseInsideWorldModelsResponseModelsEverythingExtractionStatus;
}

export interface BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2BakabaseInsideWorldModelsConstantsSpecialTextTypeSystemCollectionsGenericList1BakabaseInsideWorldModelsModelsEntitiesSpecialText {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Record<string, BakabaseInsideWorldModelsModelsEntitiesSpecialText[] | null>;
}

export interface BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2SystemInt32BakabaseInsideWorldModelsModelsAosDownloaderNamingDefinitions {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Record<string, BakabaseInsideWorldModelsModelsAosDownloaderNamingDefinitions>;
}

export interface BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2SystemInt32SystemInt32 {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Record<string, number[] | null>;
}

export interface BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2SystemStringBakabaseInsideWorldModelsConstantsMediaType {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Record<string, BakabaseInsideWorldModelsConstantsMediaType>;
}

export interface BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2SystemStringSystemInt32 {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Record<string, number | null>;
}

export interface BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2SystemStringSystemString {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Record<string, string[] | null>;
}

export interface BootstrapModelsResponseModelsSingletonResponse1SystemInt32 {
  /** @format int32 */
  code?: number;
  message?: string | null;
  /** @format int32 */
  data?: number;
}

export interface BootstrapModelsResponseModelsSingletonResponse1SystemString {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: string | null;
}

/**
 * [0: Trace, 1: Debug, 2: Information, 3: Warning, 4: Error, 5: Critical, 6: None]
 * @format int32
 */
export type MicrosoftExtensionsLoggingLogLevel = 0 | 1 | 2 | 3 | 4 | 5 | 6;

export type SystemIntPtr = object;

export interface SystemModuleHandle {
  /** @format int32 */
  mdStreamVersion?: number;
}

export interface SystemReflectionAssembly {
  definedTypes?: SystemReflectionTypeInfo[] | null;
  exportedTypes?: SystemType[] | null;
  codeBase?: string | null;
  entryPoint?: SystemReflectionMethodInfo;
  fullName?: string | null;
  imageRuntimeVersion?: string | null;
  isDynamic?: boolean;
  location?: string | null;
  reflectionOnly?: boolean;
  isCollectible?: boolean;
  isFullyTrusted?: boolean;
  customAttributes?: SystemReflectionCustomAttributeData[] | null;
  escapedCodeBase?: string | null;
  manifestModule?: SystemReflectionModule;
  modules?: SystemReflectionModule[] | null;
  /** @deprecated */
  globalAssemblyCache?: boolean;
  /** @format int64 */
  hostContext?: number;
  /** [0: None, 1: Level1, 2: Level2] */
  securityRuleSet?: SystemSecuritySecurityRuleSet;
}

/**
 * [1: Standard, 2: VarArgs, 3: Any, 32: HasThis, 64: ExplicitThis]
 * @format int32
 */
export type SystemReflectionCallingConventions = 1 | 2 | 3 | 32 | 64;

export interface SystemReflectionConstructorInfo {
  name?: string | null;
  declaringType?: SystemType;
  reflectedType?: SystemType;
  module?: SystemReflectionModule;
  customAttributes?: SystemReflectionCustomAttributeData[] | null;
  isCollectible?: boolean;
  /** @format int32 */
  metadataToken?: number;
  /** [0: ReuseSlot, 0: ReuseSlot, 1: Private, 2: FamANDAssem, 3: Assembly, 4: Family, 5: FamORAssem, 6: Public, 7: MemberAccessMask, 8: UnmanagedExport, 16: Static, 32: Final, 64: Virtual, 128: HideBySig, 256: VtableLayoutMask, 256: VtableLayoutMask, 512: CheckAccessOnOverride, 1024: Abstract, 2048: SpecialName, 4096: RTSpecialName, 8192: PinvokeImpl, 16384: HasSecurity, 32768: RequireSecObject, 53248: ReservedMask] */
  attributes?: SystemReflectionMethodAttributes;
  /** [0: Managed, 0: Managed, 1: Native, 2: OPTIL, 3: CodeTypeMask, 3: CodeTypeMask, 4: Unmanaged, 4: Unmanaged, 8: NoInlining, 16: ForwardRef, 32: Synchronized, 64: NoOptimization, 128: PreserveSig, 256: AggressiveInlining, 512: AggressiveOptimization, 4096: InternalCall, 65535: MaxMethodImplVal] */
  methodImplementationFlags?: SystemReflectionMethodImplAttributes;
  /** [1: Standard, 2: VarArgs, 3: Any, 32: HasThis, 64: ExplicitThis] */
  callingConvention?: SystemReflectionCallingConventions;
  isAbstract?: boolean;
  isConstructor?: boolean;
  isFinal?: boolean;
  isHideBySig?: boolean;
  isSpecialName?: boolean;
  isStatic?: boolean;
  isVirtual?: boolean;
  isAssembly?: boolean;
  isFamily?: boolean;
  isFamilyAndAssembly?: boolean;
  isFamilyOrAssembly?: boolean;
  isPrivate?: boolean;
  isPublic?: boolean;
  isConstructedGenericMethod?: boolean;
  isGenericMethod?: boolean;
  isGenericMethodDefinition?: boolean;
  containsGenericParameters?: boolean;
  methodHandle?: SystemRuntimeMethodHandle;
  isSecurityCritical?: boolean;
  isSecuritySafeCritical?: boolean;
  isSecurityTransparent?: boolean;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType?: SystemReflectionMemberTypes;
}

export interface SystemReflectionCustomAttributeData {
  attributeType?: SystemType;
  constructor?: SystemReflectionConstructorInfo;
  constructorArguments?: SystemReflectionCustomAttributeTypedArgument[] | null;
  namedArguments?: SystemReflectionCustomAttributeNamedArgument[] | null;
}

export interface SystemReflectionCustomAttributeNamedArgument {
  memberInfo?: SystemReflectionMemberInfo;
  typedValue?: SystemReflectionCustomAttributeTypedArgument;
  memberName?: string | null;
  isField?: boolean;
}

export interface SystemReflectionCustomAttributeTypedArgument {
  argumentType?: SystemType;
  value?: any;
}

/**
 * [0: None, 512: SpecialName, 1024: ReservedMask, 1024: ReservedMask]
 * @format int32
 */
export type SystemReflectionEventAttributes = 0 | 512 | 1024;

export interface SystemReflectionEventInfo {
  name?: string | null;
  declaringType?: SystemType;
  reflectedType?: SystemType;
  module?: SystemReflectionModule;
  customAttributes?: SystemReflectionCustomAttributeData[] | null;
  isCollectible?: boolean;
  /** @format int32 */
  metadataToken?: number;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType?: SystemReflectionMemberTypes;
  /** [0: None, 512: SpecialName, 1024: ReservedMask, 1024: ReservedMask] */
  attributes?: SystemReflectionEventAttributes;
  isSpecialName?: boolean;
  addMethod?: SystemReflectionMethodInfo;
  removeMethod?: SystemReflectionMethodInfo;
  raiseMethod?: SystemReflectionMethodInfo;
  isMulticast?: boolean;
  eventHandlerType?: SystemType;
}

/**
 * [0: PrivateScope, 1: Private, 2: FamANDAssem, 3: Assembly, 4: Family, 5: FamORAssem, 6: Public, 7: FieldAccessMask, 16: Static, 32: InitOnly, 64: Literal, 128: NotSerialized, 256: HasFieldRVA, 512: SpecialName, 1024: RTSpecialName, 4096: HasFieldMarshal, 8192: PinvokeImpl, 32768: HasDefault, 38144: ReservedMask]
 * @format int32
 */
export type SystemReflectionFieldAttributes =
  | 0
  | 1
  | 2
  | 3
  | 4
  | 5
  | 6
  | 7
  | 16
  | 32
  | 64
  | 128
  | 256
  | 512
  | 1024
  | 4096
  | 8192
  | 32768
  | 38144;

export interface SystemReflectionFieldInfo {
  name?: string | null;
  declaringType?: SystemType;
  reflectedType?: SystemType;
  module?: SystemReflectionModule;
  customAttributes?: SystemReflectionCustomAttributeData[] | null;
  isCollectible?: boolean;
  /** @format int32 */
  metadataToken?: number;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType?: SystemReflectionMemberTypes;
  /** [0: PrivateScope, 1: Private, 2: FamANDAssem, 3: Assembly, 4: Family, 5: FamORAssem, 6: Public, 7: FieldAccessMask, 16: Static, 32: InitOnly, 64: Literal, 128: NotSerialized, 256: HasFieldRVA, 512: SpecialName, 1024: RTSpecialName, 4096: HasFieldMarshal, 8192: PinvokeImpl, 32768: HasDefault, 38144: ReservedMask] */
  attributes?: SystemReflectionFieldAttributes;
  fieldType?: SystemType;
  isInitOnly?: boolean;
  isLiteral?: boolean;
  isNotSerialized?: boolean;
  isPinvokeImpl?: boolean;
  isSpecialName?: boolean;
  isStatic?: boolean;
  isAssembly?: boolean;
  isFamily?: boolean;
  isFamilyAndAssembly?: boolean;
  isFamilyOrAssembly?: boolean;
  isPrivate?: boolean;
  isPublic?: boolean;
  isSecurityCritical?: boolean;
  isSecuritySafeCritical?: boolean;
  isSecurityTransparent?: boolean;
  fieldHandle?: SystemRuntimeFieldHandle;
}

/**
 * [0: None, 1: Covariant, 2: Contravariant, 3: VarianceMask, 4: ReferenceTypeConstraint, 8: NotNullableValueTypeConstraint, 16: DefaultConstructorConstraint, 28: SpecialConstraintMask]
 * @format int32
 */
export type SystemReflectionGenericParameterAttributes = 0 | 1 | 2 | 3 | 4 | 8 | 16 | 28;

export type SystemReflectionICustomAttributeProvider = object;

export interface SystemReflectionMemberInfo {
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType?: SystemReflectionMemberTypes;
  declaringType?: SystemType;
  reflectedType?: SystemType;
  name?: string | null;
  module?: SystemReflectionModule;
  customAttributes?: SystemReflectionCustomAttributeData[] | null;
  isCollectible?: boolean;
  /** @format int32 */
  metadataToken?: number;
}

/**
 * [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All]
 * @format int32
 */
export type SystemReflectionMemberTypes = 1 | 2 | 4 | 8 | 16 | 32 | 64 | 128 | 191;

/**
 * [0: ReuseSlot, 0: ReuseSlot, 1: Private, 2: FamANDAssem, 3: Assembly, 4: Family, 5: FamORAssem, 6: Public, 7: MemberAccessMask, 8: UnmanagedExport, 16: Static, 32: Final, 64: Virtual, 128: HideBySig, 256: VtableLayoutMask, 256: VtableLayoutMask, 512: CheckAccessOnOverride, 1024: Abstract, 2048: SpecialName, 4096: RTSpecialName, 8192: PinvokeImpl, 16384: HasSecurity, 32768: RequireSecObject, 53248: ReservedMask]
 * @format int32
 */
export type SystemReflectionMethodAttributes =
  | 0
  | 1
  | 2
  | 3
  | 4
  | 5
  | 6
  | 7
  | 8
  | 16
  | 32
  | 64
  | 128
  | 256
  | 512
  | 1024
  | 2048
  | 4096
  | 8192
  | 16384
  | 32768
  | 53248;

export interface SystemReflectionMethodBase {
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType?: SystemReflectionMemberTypes;
  name?: string | null;
  declaringType?: SystemType;
  reflectedType?: SystemType;
  module?: SystemReflectionModule;
  customAttributes?: SystemReflectionCustomAttributeData[] | null;
  isCollectible?: boolean;
  /** @format int32 */
  metadataToken?: number;
  /** [0: ReuseSlot, 0: ReuseSlot, 1: Private, 2: FamANDAssem, 3: Assembly, 4: Family, 5: FamORAssem, 6: Public, 7: MemberAccessMask, 8: UnmanagedExport, 16: Static, 32: Final, 64: Virtual, 128: HideBySig, 256: VtableLayoutMask, 256: VtableLayoutMask, 512: CheckAccessOnOverride, 1024: Abstract, 2048: SpecialName, 4096: RTSpecialName, 8192: PinvokeImpl, 16384: HasSecurity, 32768: RequireSecObject, 53248: ReservedMask] */
  attributes?: SystemReflectionMethodAttributes;
  /** [0: Managed, 0: Managed, 1: Native, 2: OPTIL, 3: CodeTypeMask, 3: CodeTypeMask, 4: Unmanaged, 4: Unmanaged, 8: NoInlining, 16: ForwardRef, 32: Synchronized, 64: NoOptimization, 128: PreserveSig, 256: AggressiveInlining, 512: AggressiveOptimization, 4096: InternalCall, 65535: MaxMethodImplVal] */
  methodImplementationFlags?: SystemReflectionMethodImplAttributes;
  /** [1: Standard, 2: VarArgs, 3: Any, 32: HasThis, 64: ExplicitThis] */
  callingConvention?: SystemReflectionCallingConventions;
  isAbstract?: boolean;
  isConstructor?: boolean;
  isFinal?: boolean;
  isHideBySig?: boolean;
  isSpecialName?: boolean;
  isStatic?: boolean;
  isVirtual?: boolean;
  isAssembly?: boolean;
  isFamily?: boolean;
  isFamilyAndAssembly?: boolean;
  isFamilyOrAssembly?: boolean;
  isPrivate?: boolean;
  isPublic?: boolean;
  isConstructedGenericMethod?: boolean;
  isGenericMethod?: boolean;
  isGenericMethodDefinition?: boolean;
  containsGenericParameters?: boolean;
  methodHandle?: SystemRuntimeMethodHandle;
  isSecurityCritical?: boolean;
  isSecuritySafeCritical?: boolean;
  isSecurityTransparent?: boolean;
}

/**
 * [0: Managed, 0: Managed, 1: Native, 2: OPTIL, 3: CodeTypeMask, 3: CodeTypeMask, 4: Unmanaged, 4: Unmanaged, 8: NoInlining, 16: ForwardRef, 32: Synchronized, 64: NoOptimization, 128: PreserveSig, 256: AggressiveInlining, 512: AggressiveOptimization, 4096: InternalCall, 65535: MaxMethodImplVal]
 * @format int32
 */
export type SystemReflectionMethodImplAttributes =
  | 0
  | 1
  | 2
  | 3
  | 4
  | 8
  | 16
  | 32
  | 64
  | 128
  | 256
  | 512
  | 4096
  | 65535;

export interface SystemReflectionMethodInfo {
  name?: string | null;
  declaringType?: SystemType;
  reflectedType?: SystemType;
  module?: SystemReflectionModule;
  customAttributes?: SystemReflectionCustomAttributeData[] | null;
  isCollectible?: boolean;
  /** @format int32 */
  metadataToken?: number;
  /** [0: ReuseSlot, 0: ReuseSlot, 1: Private, 2: FamANDAssem, 3: Assembly, 4: Family, 5: FamORAssem, 6: Public, 7: MemberAccessMask, 8: UnmanagedExport, 16: Static, 32: Final, 64: Virtual, 128: HideBySig, 256: VtableLayoutMask, 256: VtableLayoutMask, 512: CheckAccessOnOverride, 1024: Abstract, 2048: SpecialName, 4096: RTSpecialName, 8192: PinvokeImpl, 16384: HasSecurity, 32768: RequireSecObject, 53248: ReservedMask] */
  attributes?: SystemReflectionMethodAttributes;
  /** [0: Managed, 0: Managed, 1: Native, 2: OPTIL, 3: CodeTypeMask, 3: CodeTypeMask, 4: Unmanaged, 4: Unmanaged, 8: NoInlining, 16: ForwardRef, 32: Synchronized, 64: NoOptimization, 128: PreserveSig, 256: AggressiveInlining, 512: AggressiveOptimization, 4096: InternalCall, 65535: MaxMethodImplVal] */
  methodImplementationFlags?: SystemReflectionMethodImplAttributes;
  /** [1: Standard, 2: VarArgs, 3: Any, 32: HasThis, 64: ExplicitThis] */
  callingConvention?: SystemReflectionCallingConventions;
  isAbstract?: boolean;
  isConstructor?: boolean;
  isFinal?: boolean;
  isHideBySig?: boolean;
  isSpecialName?: boolean;
  isStatic?: boolean;
  isVirtual?: boolean;
  isAssembly?: boolean;
  isFamily?: boolean;
  isFamilyAndAssembly?: boolean;
  isFamilyOrAssembly?: boolean;
  isPrivate?: boolean;
  isPublic?: boolean;
  isConstructedGenericMethod?: boolean;
  isGenericMethod?: boolean;
  isGenericMethodDefinition?: boolean;
  containsGenericParameters?: boolean;
  methodHandle?: SystemRuntimeMethodHandle;
  isSecurityCritical?: boolean;
  isSecuritySafeCritical?: boolean;
  isSecurityTransparent?: boolean;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType?: SystemReflectionMemberTypes;
  returnParameter?: SystemReflectionParameterInfo;
  returnType?: SystemType;
  returnTypeCustomAttributes?: SystemReflectionICustomAttributeProvider;
}

export interface SystemReflectionModule {
  assembly?: SystemReflectionAssembly;
  fullyQualifiedName?: string | null;
  name?: string | null;
  /** @format int32 */
  mdStreamVersion?: number;
  /** @format uuid */
  moduleVersionId?: string;
  scopeName?: string | null;
  moduleHandle?: SystemModuleHandle;
  customAttributes?: SystemReflectionCustomAttributeData[] | null;
  /** @format int32 */
  metadataToken?: number;
}

/**
 * [0: None, 1: In, 2: Out, 4: Lcid, 8: Retval, 16: Optional, 4096: HasDefault, 8192: HasFieldMarshal, 16384: Reserved3, 32768: Reserved4, 61440: ReservedMask]
 * @format int32
 */
export type SystemReflectionParameterAttributes = 0 | 1 | 2 | 4 | 8 | 16 | 4096 | 8192 | 16384 | 32768 | 61440;

export interface SystemReflectionParameterInfo {
  /** [0: None, 1: In, 2: Out, 4: Lcid, 8: Retval, 16: Optional, 4096: HasDefault, 8192: HasFieldMarshal, 16384: Reserved3, 32768: Reserved4, 61440: ReservedMask] */
  attributes?: SystemReflectionParameterAttributes;
  member?: SystemReflectionMemberInfo;
  name?: string | null;
  parameterType?: SystemType;
  /** @format int32 */
  position?: number;
  isIn?: boolean;
  isLcid?: boolean;
  isOptional?: boolean;
  isOut?: boolean;
  isRetval?: boolean;
  defaultValue?: any;
  rawDefaultValue?: any;
  hasDefaultValue?: boolean;
  customAttributes?: SystemReflectionCustomAttributeData[] | null;
  /** @format int32 */
  metadataToken?: number;
}

/**
 * [0: None, 512: SpecialName, 1024: RTSpecialName, 4096: HasDefault, 8192: Reserved2, 16384: Reserved3, 32768: Reserved4, 62464: ReservedMask]
 * @format int32
 */
export type SystemReflectionPropertyAttributes = 0 | 512 | 1024 | 4096 | 8192 | 16384 | 32768 | 62464;

export interface SystemReflectionPropertyInfo {
  name?: string | null;
  declaringType?: SystemType;
  reflectedType?: SystemType;
  module?: SystemReflectionModule;
  customAttributes?: SystemReflectionCustomAttributeData[] | null;
  isCollectible?: boolean;
  /** @format int32 */
  metadataToken?: number;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType?: SystemReflectionMemberTypes;
  propertyType?: SystemType;
  /** [0: None, 512: SpecialName, 1024: RTSpecialName, 4096: HasDefault, 8192: Reserved2, 16384: Reserved3, 32768: Reserved4, 62464: ReservedMask] */
  attributes?: SystemReflectionPropertyAttributes;
  isSpecialName?: boolean;
  canRead?: boolean;
  canWrite?: boolean;
  getMethod?: SystemReflectionMethodInfo;
  setMethod?: SystemReflectionMethodInfo;
}

/**
 * [0: NotPublic, 0: NotPublic, 0: NotPublic, 0: NotPublic, 1: Public, 2: NestedPublic, 3: NestedPrivate, 4: NestedFamily, 5: NestedAssembly, 6: NestedFamANDAssem, 7: VisibilityMask, 7: VisibilityMask, 8: SequentialLayout, 16: ExplicitLayout, 24: LayoutMask, 32: ClassSemanticsMask, 32: ClassSemanticsMask, 128: Abstract, 256: Sealed, 1024: SpecialName, 2048: RTSpecialName, 4096: Import, 8192: Serializable, 16384: WindowsRuntime, 65536: UnicodeClass, 131072: AutoClass, 196608: StringFormatMask, 196608: StringFormatMask, 262144: HasSecurity, 264192: ReservedMask, 1048576: BeforeFieldInit, 12582912: CustomFormatMask]
 * @format int32
 */
export type SystemReflectionTypeAttributes =
  | 0
  | 1
  | 2
  | 3
  | 4
  | 5
  | 6
  | 7
  | 8
  | 16
  | 24
  | 32
  | 128
  | 256
  | 1024
  | 2048
  | 4096
  | 8192
  | 16384
  | 65536
  | 131072
  | 196608
  | 262144
  | 264192
  | 1048576
  | 12582912;

export interface SystemReflectionTypeInfo {
  name?: string | null;
  customAttributes?: SystemReflectionCustomAttributeData[] | null;
  isCollectible?: boolean;
  /** @format int32 */
  metadataToken?: number;
  isInterface?: boolean;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType?: SystemReflectionMemberTypes;
  namespace?: string | null;
  assemblyQualifiedName?: string | null;
  fullName?: string | null;
  assembly?: SystemReflectionAssembly;
  module?: SystemReflectionModule;
  isNested?: boolean;
  declaringType?: SystemType;
  declaringMethod?: SystemReflectionMethodBase;
  reflectedType?: SystemType;
  underlyingSystemType?: SystemType;
  isTypeDefinition?: boolean;
  isArray?: boolean;
  isByRef?: boolean;
  isPointer?: boolean;
  isConstructedGenericType?: boolean;
  isGenericParameter?: boolean;
  isGenericTypeParameter?: boolean;
  isGenericMethodParameter?: boolean;
  isGenericType?: boolean;
  isGenericTypeDefinition?: boolean;
  isSZArray?: boolean;
  isVariableBoundArray?: boolean;
  isByRefLike?: boolean;
  hasElementType?: boolean;
  genericTypeArguments?: SystemType[] | null;
  /** @format int32 */
  genericParameterPosition?: number;
  /** [0: None, 1: Covariant, 2: Contravariant, 3: VarianceMask, 4: ReferenceTypeConstraint, 8: NotNullableValueTypeConstraint, 16: DefaultConstructorConstraint, 28: SpecialConstraintMask] */
  genericParameterAttributes?: SystemReflectionGenericParameterAttributes;
  /** [0: NotPublic, 0: NotPublic, 0: NotPublic, 0: NotPublic, 1: Public, 2: NestedPublic, 3: NestedPrivate, 4: NestedFamily, 5: NestedAssembly, 6: NestedFamANDAssem, 7: VisibilityMask, 7: VisibilityMask, 8: SequentialLayout, 16: ExplicitLayout, 24: LayoutMask, 32: ClassSemanticsMask, 32: ClassSemanticsMask, 128: Abstract, 256: Sealed, 1024: SpecialName, 2048: RTSpecialName, 4096: Import, 8192: Serializable, 16384: WindowsRuntime, 65536: UnicodeClass, 131072: AutoClass, 196608: StringFormatMask, 196608: StringFormatMask, 262144: HasSecurity, 264192: ReservedMask, 1048576: BeforeFieldInit, 12582912: CustomFormatMask] */
  attributes?: SystemReflectionTypeAttributes;
  isAbstract?: boolean;
  isImport?: boolean;
  isSealed?: boolean;
  isSpecialName?: boolean;
  isClass?: boolean;
  isNestedAssembly?: boolean;
  isNestedFamANDAssem?: boolean;
  isNestedFamily?: boolean;
  isNestedFamORAssem?: boolean;
  isNestedPrivate?: boolean;
  isNestedPublic?: boolean;
  isNotPublic?: boolean;
  isPublic?: boolean;
  isAutoLayout?: boolean;
  isExplicitLayout?: boolean;
  isLayoutSequential?: boolean;
  isAnsiClass?: boolean;
  isAutoClass?: boolean;
  isUnicodeClass?: boolean;
  isCOMObject?: boolean;
  isContextful?: boolean;
  isEnum?: boolean;
  isMarshalByRef?: boolean;
  isPrimitive?: boolean;
  isValueType?: boolean;
  isSignatureType?: boolean;
  isSecurityCritical?: boolean;
  isSecuritySafeCritical?: boolean;
  isSecurityTransparent?: boolean;
  structLayoutAttribute?: SystemRuntimeInteropServicesStructLayoutAttribute;
  typeInitializer?: SystemReflectionConstructorInfo;
  typeHandle?: SystemRuntimeTypeHandle;
  /** @format uuid */
  guid?: string;
  baseType?: SystemType;
  isSerializable?: boolean;
  containsGenericParameters?: boolean;
  isVisible?: boolean;
  genericTypeParameters?: SystemType[] | null;
  declaredConstructors?: SystemReflectionConstructorInfo[] | null;
  declaredEvents?: SystemReflectionEventInfo[] | null;
  declaredFields?: SystemReflectionFieldInfo[] | null;
  declaredMembers?: SystemReflectionMemberInfo[] | null;
  declaredMethods?: SystemReflectionMethodInfo[] | null;
  declaredNestedTypes?: SystemReflectionTypeInfo[] | null;
  declaredProperties?: SystemReflectionPropertyInfo[] | null;
  implementedInterfaces?: SystemType[] | null;
}

/**
 * [0: X86, 1: X64, 2: Arm, 3: Arm64, 4: Wasm, 5: S390x]
 * @format int32
 */
export type SystemRuntimeInteropServicesArchitecture = 0 | 1 | 2 | 3 | 4 | 5;

/**
 * [0: Sequential, 2: Explicit, 3: Auto]
 * @format int32
 */
export type SystemRuntimeInteropServicesLayoutKind = 0 | 2 | 3;

export type SystemRuntimeInteropServicesOSPlatform = object;

export interface SystemRuntimeInteropServicesStructLayoutAttribute {
  typeId?: any;
  /** [0: Sequential, 2: Explicit, 3: Auto] */
  value?: SystemRuntimeInteropServicesLayoutKind;
}

export interface SystemRuntimeFieldHandle {
  value?: SystemIntPtr;
}

export interface SystemRuntimeMethodHandle {
  value?: SystemIntPtr;
}

export interface SystemRuntimeTypeHandle {
  value?: SystemIntPtr;
}

/**
 * [0: None, 1: Level1, 2: Level2]
 * @format int32
 */
export type SystemSecuritySecurityRuleSet = 0 | 1 | 2;

export interface SystemTimeSpan {
  /** @format int64 */
  ticks?: number;
  /** @format int32 */
  days?: number;
  /** @format int32 */
  hours?: number;
  /** @format int32 */
  milliseconds?: number;
  /** @format int32 */
  minutes?: number;
  /** @format int32 */
  seconds?: number;
  /** @format double */
  totalDays?: number;
  /** @format double */
  totalHours?: number;
  /** @format double */
  totalMilliseconds?: number;
  /** @format double */
  totalMinutes?: number;
  /** @format double */
  totalSeconds?: number;
}

export interface SystemType {
  name?: string | null;
  customAttributes?: SystemReflectionCustomAttributeData[] | null;
  isCollectible?: boolean;
  /** @format int32 */
  metadataToken?: number;
  isInterface?: boolean;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType?: SystemReflectionMemberTypes;
  namespace?: string | null;
  assemblyQualifiedName?: string | null;
  fullName?: string | null;
  assembly?: SystemReflectionAssembly;
  module?: SystemReflectionModule;
  isNested?: boolean;
  declaringType?: SystemType;
  declaringMethod?: SystemReflectionMethodBase;
  reflectedType?: SystemType;
  underlyingSystemType?: SystemType;
  isTypeDefinition?: boolean;
  isArray?: boolean;
  isByRef?: boolean;
  isPointer?: boolean;
  isConstructedGenericType?: boolean;
  isGenericParameter?: boolean;
  isGenericTypeParameter?: boolean;
  isGenericMethodParameter?: boolean;
  isGenericType?: boolean;
  isGenericTypeDefinition?: boolean;
  isSZArray?: boolean;
  isVariableBoundArray?: boolean;
  isByRefLike?: boolean;
  hasElementType?: boolean;
  genericTypeArguments?: SystemType[] | null;
  /** @format int32 */
  genericParameterPosition?: number;
  /** [0: None, 1: Covariant, 2: Contravariant, 3: VarianceMask, 4: ReferenceTypeConstraint, 8: NotNullableValueTypeConstraint, 16: DefaultConstructorConstraint, 28: SpecialConstraintMask] */
  genericParameterAttributes?: SystemReflectionGenericParameterAttributes;
  /** [0: NotPublic, 0: NotPublic, 0: NotPublic, 0: NotPublic, 1: Public, 2: NestedPublic, 3: NestedPrivate, 4: NestedFamily, 5: NestedAssembly, 6: NestedFamANDAssem, 7: VisibilityMask, 7: VisibilityMask, 8: SequentialLayout, 16: ExplicitLayout, 24: LayoutMask, 32: ClassSemanticsMask, 32: ClassSemanticsMask, 128: Abstract, 256: Sealed, 1024: SpecialName, 2048: RTSpecialName, 4096: Import, 8192: Serializable, 16384: WindowsRuntime, 65536: UnicodeClass, 131072: AutoClass, 196608: StringFormatMask, 196608: StringFormatMask, 262144: HasSecurity, 264192: ReservedMask, 1048576: BeforeFieldInit, 12582912: CustomFormatMask] */
  attributes?: SystemReflectionTypeAttributes;
  isAbstract?: boolean;
  isImport?: boolean;
  isSealed?: boolean;
  isSpecialName?: boolean;
  isClass?: boolean;
  isNestedAssembly?: boolean;
  isNestedFamANDAssem?: boolean;
  isNestedFamily?: boolean;
  isNestedFamORAssem?: boolean;
  isNestedPrivate?: boolean;
  isNestedPublic?: boolean;
  isNotPublic?: boolean;
  isPublic?: boolean;
  isAutoLayout?: boolean;
  isExplicitLayout?: boolean;
  isLayoutSequential?: boolean;
  isAnsiClass?: boolean;
  isAutoClass?: boolean;
  isUnicodeClass?: boolean;
  isCOMObject?: boolean;
  isContextful?: boolean;
  isEnum?: boolean;
  isMarshalByRef?: boolean;
  isPrimitive?: boolean;
  isValueType?: boolean;
  isSignatureType?: boolean;
  isSecurityCritical?: boolean;
  isSecuritySafeCritical?: boolean;
  isSecurityTransparent?: boolean;
  structLayoutAttribute?: SystemRuntimeInteropServicesStructLayoutAttribute;
  typeInitializer?: SystemReflectionConstructorInfo;
  typeHandle?: SystemRuntimeTypeHandle;
  /** @format uuid */
  guid?: string;
  baseType?: SystemType;
  isSerializable?: boolean;
  containsGenericParameters?: boolean;
  isVisible?: boolean;
}

export type QueryParamsType = Record<string | number, any>;
export type ResponseFormat = keyof Omit<Body, "body" | "bodyUsed">;

export interface FullRequestParams extends Omit<RequestInit, "body"> {
  /** set parameter to `true` for call `securityWorker` for this request */
  secure?: boolean;
  /** request path */
  path: string;
  /** content type of request body */
  type?: ContentType;
  /** query params */
  query?: QueryParamsType;
  /** format of response (i.e. response.json() -> format: "json") */
  format?: ResponseFormat;
  /** request body */
  body?: unknown;
  /** base url */
  baseUrl?: string;
  /** request cancellation token */
  cancelToken?: CancelToken;
}

export type RequestParams = Omit<FullRequestParams, "body" | "method" | "query" | "path">;

export interface ApiConfig<SecurityDataType = unknown> {
  baseUrl?: string;
  baseApiParams?: Omit<RequestParams, "baseUrl" | "cancelToken" | "signal">;
  securityWorker?: (securityData: SecurityDataType | null) => Promise<RequestParams | void> | RequestParams | void;
  customFetch?: typeof fetch;
}

export interface HttpResponse<D extends unknown, E extends unknown = unknown> extends Response {
  data: D;
  error: E;
}

type CancelToken = Symbol | string | number;

export enum ContentType {
  Json = "application/json",
  FormData = "multipart/form-data",
  UrlEncoded = "application/x-www-form-urlencoded",
  Text = "text/plain",
}

export class HttpClient<SecurityDataType = unknown> {
  public baseUrl: string = "";
  private securityData: SecurityDataType | null = null;
  private securityWorker?: ApiConfig<SecurityDataType>["securityWorker"];
  private abortControllers = new Map<CancelToken, AbortController>();
  private customFetch = (...fetchParams: Parameters<typeof fetch>) => fetch(...fetchParams);

  private baseApiParams: RequestParams = {
    credentials: "same-origin",
    headers: {},
    redirect: "follow",
    referrerPolicy: "no-referrer",
  };

  constructor(apiConfig: ApiConfig<SecurityDataType> = {}) {
    Object.assign(this, apiConfig);
  }

  public setSecurityData = (data: SecurityDataType | null) => {
    this.securityData = data;
  };

  protected encodeQueryParam(key: string, value: any) {
    const encodedKey = encodeURIComponent(key);
    return `${encodedKey}=${encodeURIComponent(typeof value === "number" ? value : `${value}`)}`;
  }

  protected addQueryParam(query: QueryParamsType, key: string) {
    return this.encodeQueryParam(key, query[key]);
  }

  protected addArrayQueryParam(query: QueryParamsType, key: string) {
    const value = query[key];
    return value.map((v: any) => this.encodeQueryParam(key, v)).join("&");
  }

  protected toQueryString(rawQuery?: QueryParamsType): string {
    const query = rawQuery || {};
    const keys = Object.keys(query).filter((key) => "undefined" !== typeof query[key]);
    return keys
      .map((key) => (Array.isArray(query[key]) ? this.addArrayQueryParam(query, key) : this.addQueryParam(query, key)))
      .join("&");
  }

  protected addQueryParams(rawQuery?: QueryParamsType): string {
    const queryString = this.toQueryString(rawQuery);
    return queryString ? `?${queryString}` : "";
  }

  private contentFormatters: Record<ContentType, (input: any) => any> = {
    [ContentType.Json]: (input: any) =>
      input !== null && (typeof input === "object" || typeof input === "string") ? JSON.stringify(input) : input,
    [ContentType.Text]: (input: any) => (input !== null && typeof input !== "string" ? JSON.stringify(input) : input),
    [ContentType.FormData]: (input: any) =>
      Object.keys(input || {}).reduce((formData, key) => {
        const property = input[key];
        formData.append(
          key,
          property instanceof Blob
            ? property
            : typeof property === "object" && property !== null
            ? JSON.stringify(property)
            : `${property}`,
        );
        return formData;
      }, new FormData()),
    [ContentType.UrlEncoded]: (input: any) => this.toQueryString(input),
  };

  protected mergeRequestParams(params1: RequestParams, params2?: RequestParams): RequestParams {
    return {
      ...this.baseApiParams,
      ...params1,
      ...(params2 || {}),
      headers: {
        ...(this.baseApiParams.headers || {}),
        ...(params1.headers || {}),
        ...((params2 && params2.headers) || {}),
      },
    };
  }

  protected createAbortSignal = (cancelToken: CancelToken): AbortSignal | undefined => {
    if (this.abortControllers.has(cancelToken)) {
      const abortController = this.abortControllers.get(cancelToken);
      if (abortController) {
        return abortController.signal;
      }
      return void 0;
    }

    const abortController = new AbortController();
    this.abortControllers.set(cancelToken, abortController);
    return abortController.signal;
  };

  public abortRequest = (cancelToken: CancelToken) => {
    const abortController = this.abortControllers.get(cancelToken);

    if (abortController) {
      abortController.abort();
      this.abortControllers.delete(cancelToken);
    }
  };

  public request = async <T = any, E = any>({
    body,
    secure,
    path,
    type,
    query,
    format,
    baseUrl,
    cancelToken,
    ...params
  }: FullRequestParams): Promise<T> => {
    const secureParams =
      ((typeof secure === "boolean" ? secure : this.baseApiParams.secure) &&
        this.securityWorker &&
        (await this.securityWorker(this.securityData))) ||
      {};
    const requestParams = this.mergeRequestParams(params, secureParams);
    const queryString = query && this.toQueryString(query);
    const payloadFormatter = this.contentFormatters[type || ContentType.Json];
    const responseFormat = format || requestParams.format;

    return this.customFetch(`${baseUrl || this.baseUrl || ""}${path}${queryString ? `?${queryString}` : ""}`, {
      ...requestParams,
      headers: {
        ...(requestParams.headers || {}),
        ...(type && type !== ContentType.FormData ? { "Content-Type": type } : {}),
      },
      signal: cancelToken ? this.createAbortSignal(cancelToken) : requestParams.signal,
      body: typeof body === "undefined" || body === null ? null : payloadFormatter(body),
    }).then(async (response) => {
      const r = response as HttpResponse<T, E>;
      r.data = null as unknown as T;
      r.error = null as unknown as E;

      const data = !responseFormat
        ? r
        : await response[responseFormat]()
            .then((data) => {
              if (r.ok) {
                r.data = data;
              } else {
                r.error = data;
              }
              return r;
            })
            .catch((e) => {
              r.error = e;
              return r;
            });

      if (cancelToken) {
        this.abortControllers.delete(cancelToken);
      }

      if (!response.ok) throw data;
      return data.data;
    });
  };
}

/**
 * @title API
 * @version v1
 */
export class Api<SecurityDataType extends unknown> extends HttpClient<SecurityDataType> {
  alias = {
    /**
     * No description
     *
     * @tags Alias
     * @name GetAlias
     * @request GET:/alias/{id}
     */
    getAlias: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsEntitiesAlias, any>({
        path: `/alias/${id}`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Alias
     * @name UpdateAlias
     * @request PUT:/alias/{id}
     */
    updateAlias: (
      id: number,
      data: BakabaseInsideWorldModelsRequestModelsAliasUpdateRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/alias/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Alias
     * @name RemoveAlias
     * @request DELETE:/alias/{id}
     */
    removeAlias: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/alias/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Alias
     * @name SearchAliases
     * @request GET:/alias
     */
    searchAliases: (
      query?: {
        names?: string[];
        name?: string;
        exactly?: boolean;
        /** [1: Candidates] */
        additionalItems?: BakabaseInsideWorldModelsConstantsAdditionalItemsAliasAdditionalItem;
        /** @format int32 */
        pageIndex?: number;
        /**
         * @format int32
         * @min 0
         * @max 100
         */
        pageSize?: number;
        /** @format int32 */
        skipCount?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsSearchResponse1BakabaseInsideWorldModelsModelsDtosAliasDto, any>({
        path: `/alias`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Alias
     * @name CreateAlias
     * @request POST:/alias
     */
    createAlias: (data: BakabaseInsideWorldModelsRequestModelsAliasCreateRequestModel, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/alias`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Alias
     * @name ExportAliases
     * @request POST:/alias/export
     */
    exportAliases: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/alias/export`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Alias
     * @name ImportAliases
     * @request POST:/alias/import
     */
    importAliases: (
      data: {
        /** @format binary */
        file?: File;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/alias/import`,
        method: "POST",
        body: data,
        type: ContentType.FormData,
        format: "json",
        ...params,
      }),
  };
  aliasGroup = {
    /**
     * No description
     *
     * @tags AliasGroup
     * @name RemoveAliasGroup
     * @request DELETE:/alias-group/{id}
     */
    removeAliasGroup: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/alias-group/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags AliasGroup
     * @name MergeAliasGroup
     * @request PUT:/alias-group/{id}
     */
    mergeAliasGroup: (
      id: number,
      data: BakabaseInsideWorldModelsRequestModelsAliasGroupUpdateRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/alias-group/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  app = {
    /**
     * No description
     *
     * @tags App
     * @name CheckAppInitialized
     * @request GET:/app/initialized
     */
    checkAppInitialized: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConstantsInitializationContentType,
        any
      >({
        path: `/app/initialized`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags App
     * @name GetAppInfo
     * @request GET:/app/info
     */
    getAppInfo: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo,
        any
      >({
        path: `/app/info`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags App
     * @name AcceptTerms
     * @request POST:/app/terms
     */
    acceptTerms: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/app/terms`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags App
     * @name MoveCoreData
     * @request PUT:/app/data-path
     */
    moveCoreData: (
      data: BakabaseInfrastructuresComponentsAppModelsRequestModelsCoreDataMoveRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/app/data-path`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  backgroundTask = {
    /**
     * No description
     *
     * @tags BackgroundTask
     * @name GetAllBackgroundTasks
     * @request GET:/background-task
     */
    getAllBackgroundTasks: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldBusinessComponentsTasksBackgroundTaskDto,
        any
      >({
        path: `/background-task`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BackgroundTask
     * @name ClearInactiveBackgroundTasks
     * @request DELETE:/background-task
     */
    clearInactiveBackgroundTasks: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/background-task`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BackgroundTask
     * @name GetBackgroundTaskByName
     * @request GET:/background-task/by-name
     */
    getBackgroundTaskByName: (
      query?: {
        name?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsTasksBackgroundTaskDto,
        any
      >({
        path: `/background-task/by-name`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BackgroundTask
     * @name StopBackgroundTask
     * @request DELETE:/background-task/{id}/stop
     */
    stopBackgroundTask: (id: string, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/background-task/${id}/stop`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BackgroundTask
     * @name RemoveBackgroundTask
     * @request DELETE:/background-task/{id}
     */
    removeBackgroundTask: (id: string, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/background-task/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),
  };
  bilibili = {
    /**
     * No description
     *
     * @tags BiliBili
     * @name GetBiliBiliFavorites
     * @request GET:/bilibili/favorites
     */
    getBiliBiliFavorites: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldBusinessComponentsThirdPartyBilibiliModelsFavorites,
        any
      >({
        path: `/bilibili/favorites`,
        method: "GET",
        format: "json",
        ...params,
      }),
  };
  bulkModification = {
    /**
     * No description
     *
     * @tags BulkModification
     * @name GetBulkModificationById
     * @request GET:/bulk-modification/{id}
     */
    getBulkModificationById: (id: number, params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationDto,
        any
      >({
        path: `/bulk-modification/${id}`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BulkModification
     * @name PutBulkModification
     * @request PUT:/bulk-modification/{id}
     */
    putBulkModification: (
      id: number,
      data: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationPutRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/bulk-modification/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BulkModification
     * @name RemoveBulkModification
     * @request DELETE:/bulk-modification/{id}
     */
    removeBulkModification: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/bulk-modification/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BulkModification
     * @name GetAllBulkModifications
     * @request GET:/bulk-modification
     */
    getAllBulkModifications: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationDto,
        any
      >({
        path: `/bulk-modification`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BulkModification
     * @name CreateBulkModification
     * @request POST:/bulk-modification
     */
    createBulkModification: (
      data: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationPutRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationDto,
        any
      >({
        path: `/bulk-modification`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BulkModification
     * @name PerformBulkModificationFiltering
     * @request PUT:/bulk-modification/{id}/filter
     */
    performBulkModificationFiltering: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsListResponse1SystemInt32, any>({
        path: `/bulk-modification/${id}/filter`,
        method: "PUT",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BulkModification
     * @name GetBulkModificationFilteredResources
     * @request GET:/bulk-modification/{id}/filtered-resources
     */
    getBulkModificationFilteredResources: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosResourceDto, any>({
        path: `/bulk-modification/${id}/filtered-resources`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BulkModification
     * @name GetBulkModificationResourceDiffs
     * @request GET:/bulk-modification/{bmId}/diffs
     */
    getBulkModificationResourceDiffs: (bmId: number, params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationResourceDiffs,
        any
      >({
        path: `/bulk-modification/${bmId}/diffs`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BulkModification
     * @name CalculateBulkModificationResourceDiffs
     * @request POST:/bulk-modification/{id}/diffs
     */
    calculateBulkModificationResourceDiffs: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/bulk-modification/${id}/diffs`,
        method: "POST",
        format: "json",
        ...params,
      }),
  };
  component = {
    /**
     * No description
     *
     * @tags Component
     * @name GetComponentDescriptors
     * @request GET:/component
     */
    getComponentDescriptors: (
      query?: {
        /** [1: Enhancer, 2: PlayableFileSelector, 3: Player] */
        type?: BakabaseInsideWorldModelsConstantsComponentType;
        /** [0: None, 1: AssociatedCategories] */
        additionalItems?: BakabaseInsideWorldModelsConstantsAdditionalItemsComponentDescriptorAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosComponentDescriptorDto,
        any
      >({
        path: `/component`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Component
     * @name GetComponentDescriptorByKey
     * @request GET:/component/key
     */
    getComponentDescriptorByKey: (
      query?: {
        key?: string;
        /** [0: None, 1: AssociatedCategories] */
        additionalItems?: BakabaseInsideWorldModelsConstantsAdditionalItemsComponentDescriptorAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsDtosComponentDescriptorDto,
        any
      >({
        path: `/component/key`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Component
     * @name DiscoverDependentComponent
     * @request GET:/component/dependency/discovery
     */
    discoverDependentComponent: (
      query?: {
        id?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/component/dependency/discovery`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Component
     * @name GetDependentComponentLatestVersion
     * @request GET:/component/dependency/latest-version
     */
    getDependentComponentLatestVersion: (
      query?: {
        id?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsDependencyAbstractionsDependentComponentVersion,
        any
      >({
        path: `/component/dependency/latest-version`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Component
     * @name InstallDependentComponent
     * @request POST:/component/dependency
     */
    installDependentComponent: (
      query?: {
        id?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/component/dependency`,
        method: "POST",
        query: query,
        format: "json",
        ...params,
      }),
  };
  componentOptions = {
    /**
     * No description
     *
     * @tags ComponentOptions
     * @name AddComponentOptions
     * @request POST:/component-options
     */
    addComponentOptions: (
      data: BakabaseInsideWorldModelsRequestModelsComponentOptionsAddRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsEntitiesComponentOptions,
        any
      >({
        path: `/component-options`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags ComponentOptions
     * @name PutComponentOptions
     * @request PUT:/component-options/{id}
     */
    putComponentOptions: (
      id: number,
      data: BakabaseInsideWorldModelsRequestModelsComponentOptionsAddRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/component-options/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags ComponentOptions
     * @name RemoveComponentOptions
     * @request DELETE:/component-options/{id}
     */
    removeComponentOptions: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/component-options/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),
  };
  api = {
    /**
     * No description
     *
     * @tags Constant
     * @name GetAllExtensionMediaTypes
     * @request GET:/api/constant/extension-media-types
     */
    getAllExtensionMediaTypes: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2SystemStringBakabaseInsideWorldModelsConstantsMediaType,
        any
      >({
        path: `/api/constant/extension-media-types`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Constant
     * @name ConstantList
     * @request GET:/api/constant
     */
    constantList: (params: RequestParams = {}) =>
      this.request<string, any>({
        path: `/api/constant`,
        method: "GET",
        format: "json",
        ...params,
      }),
  };
  dashboard = {
    /**
     * No description
     *
     * @tags Dashboard
     * @name GetStatistics
     * @request GET:/dashboard
     */
    getStatistics: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsDtosDashboardStatistics,
        any
      >({
        path: `/dashboard`,
        method: "GET",
        format: "json",
        ...params,
      }),
  };
  downloadTask = {
    /**
     * No description
     *
     * @tags DownloadTask
     * @name GetAllDownloaderNamingDefinitions
     * @request GET:/download-task/downloader/naming-definitions
     */
    getAllDownloaderNamingDefinitions: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2SystemInt32BakabaseInsideWorldModelsModelsAosDownloaderNamingDefinitions,
        any
      >({
        path: `/download-task/downloader/naming-definitions`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags DownloadTask
     * @name GetAllDownloadTasks
     * @request GET:/download-task
     */
    getAllDownloadTasks: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosDownloadTaskDto, any>({
        path: `/download-task`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags DownloadTask
     * @name CreateDownloadTask
     * @request POST:/download-task
     */
    createDownloadTask: (
      data: BakabaseInsideWorldModelsRequestModelsDownloadTaskCreateRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/download-task`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags DownloadTask
     * @name GetDownloadTask
     * @request GET:/download-task/{id}
     */
    getDownloadTask: (id: number, params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsDtosDownloadTaskDto,
        any
      >({
        path: `/download-task/${id}`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags DownloadTask
     * @name RemoveDownloadTask
     * @request DELETE:/download-task/{id}
     */
    removeDownloadTask: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/download-task/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags DownloadTask
     * @name PutDownloadTask
     * @request PUT:/download-task/{id}
     */
    putDownloadTask: (
      id: number,
      data: BakabaseInsideWorldModelsModelsEntitiesDownloadTask,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/download-task/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags DownloadTask
     * @name RemoveDownloadTasksByIds
     * @request DELETE:/download-task/ids
     */
    removeDownloadTasksByIds: (data: number[], params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/download-task/ids`,
        method: "DELETE",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags DownloadTask
     * @name StartDownloadTasks
     * @request POST:/download-task/download
     */
    startDownloadTasks: (
      data: BakabaseInsideWorldModelsRequestModelsDownloadTaskStartRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/download-task/download`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags DownloadTask
     * @name StopDownloadTasks
     * @request DELETE:/download-task/download
     */
    stopDownloadTasks: (data: number[], params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/download-task/download`,
        method: "DELETE",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  resource = {
    /**
     * No description
     *
     * @tags Enhancement
     * @name GetResourceEnhancementRecords
     * @request GET:/resource/{id}/enhancement
     */
    getResourceEnhancementRecords: (id: number, params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosEnhancementRecordDto,
        any
      >({
        path: `/resource/${id}/enhancement`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Enhancement
     * @name EnhanceResource
     * @request POST:/resource/{id}/enhancement
     */
    enhanceResource: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/${id}/enhancement`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Enhancement
     * @name RemoveResourceEnhancementRecords
     * @request DELETE:/resource/{id}/enhancement
     */
    removeResourceEnhancementRecords: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/${id}/enhancement`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name SearchResources
     * @request POST:/resource/search
     */
    searchResources: (data: BakabaseInsideWorldModelsModelsAosResourceSearchDto, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsSearchResponse1BakabaseInsideWorldModelsModelsDtosResourceDto, any>({
        path: `/resource/search`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name GetResourcesByKeys
     * @request GET:/resource/keys
     */
    getResourcesByKeys: (
      query?: {
        ids?: number[];
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosResourceDto, any>({
        path: `/resource/keys`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name PatchResource
     * @request PUT:/resource/{id}
     */
    patchResource: (
      id: number,
      data: BakabaseInsideWorldModelsRequestModelsResourceUpdateRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name RemoveResource
     * @request DELETE:/resource/{id}
     */
    removeResource: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name UpdateResourceTags
     * @request PUT:/resource/tag
     */
    updateResourceTags: (
      data: BakabaseInsideWorldModelsRequestModelsResourceTagUpdateRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/tag`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name OpenResourceDirectory
     * @request GET:/resource/directory
     */
    openResourceDirectory: (
      query?: {
        /** @format int32 */
        id?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/directory`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name UpdateResourceRawName
     * @request DELETE:/resource/{id}/raw-name
     */
    updateResourceRawName: (
      id: number,
      data: BakabaseInsideWorldModelsRequestModelsResourceRawNameUpdateRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/${id}/raw-name`,
        method: "DELETE",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name GetResourceCover
     * @request GET:/resource/{id}/cover
     */
    getResourceCover: (id: number, params: RequestParams = {}) =>
      this.request<void, any>({
        path: `/resource/${id}/cover`,
        method: "GET",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name SaveCover
     * @request POST:/resource/{id}/cover
     */
    saveCover: (
      id: number,
      data: BakabaseInsideWorldModelsRequestModelsCoverSaveRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/${id}/cover`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name GetResourcePlayableFiles
     * @request GET:/resource/{id}/playable-files
     */
    getResourcePlayableFiles: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsListResponse1SystemString, any>({
        path: `/resource/${id}/playable-files`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name GetAllCustomPropertyKeys
     * @request GET:/resource/custom-property-keys
     */
    getAllCustomPropertyKeys: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsListResponse1SystemString, any>({
        path: `/resource/custom-property-keys`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name GetAllCustomPropertiesAndCandidates
     * @request GET:/resource/custom-properties-and-candidates
     */
    getAllCustomPropertiesAndCandidates: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2SystemStringSystemString,
        any
      >({
        path: `/resource/custom-properties-and-candidates`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name GetAllReservedPropertiesAndCandidates
     * @request GET:/resource/reserved-properties-and-candidates
     */
    getAllReservedPropertiesAndCandidates: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2SystemStringSystemString,
        any
      >({
        path: `/resource/reserved-properties-and-candidates`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name RemoveResourceCustomProperty
     * @request DELETE:/resource/resource/{id}/custom-property/{propertyKey}
     */
    removeResourceCustomProperty: (id: number, propertyKey: string, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/resource/${id}/custom-property/${propertyKey}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name MoveResources
     * @request PUT:/resource/move
     */
    moveResources: (data: BakabaseInsideWorldModelsRequestModelsResourceMoveRequestModel, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/move`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name ClearResourceTask
     * @request DELETE:/resource/{id}/task
     */
    clearResourceTask: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/${id}/task`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name GetFavoritesResourcesMappings
     * @request GET:/resource/favorites-mappings
     */
    getFavoritesResourcesMappings: (
      query?: {
        ids?: number[];
      },
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2SystemInt32SystemInt32,
        any
      >({
        path: `/resource/favorites-mappings`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name StartResourceNfoGenerationTask
     * @request POST:/resource/nfo
     */
    startResourceNfoGenerationTask: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/nfo`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name GetResourceDataForPreviewer
     * @request GET:/resource/{id}/previewer
     */
    getResourceDataForPreviewer: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsAosPreviewerItem, any>({
        path: `/resource/${id}/previewer`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name GetAllOriginals
     * @request GET:/resource/original/all
     */
    getAllOriginals: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosOriginalDto, any>({
        path: `/resource/original/all`,
        method: "GET",
        format: "json",
        ...params,
      }),
  };
  mediaLibrary = {
    /**
     * No description
     *
     * @tags Enhancement
     * @name RemoveMediaLibraryEnhancementRecords
     * @request DELETE:/media-library/{id}/enhancement
     */
    removeMediaLibraryEnhancementRecords: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library/${id}/enhancement`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibrary
     * @name GetAllMediaLibraries
     * @request GET:/media-library
     */
    getAllMediaLibraries: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosMediaLibraryDto, any>({
        path: `/media-library`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibrary
     * @name AddMediaLibrary
     * @request POST:/media-library
     */
    addMediaLibrary: (
      data: BakabaseInsideWorldModelsRequestModelsMediaLibraryCreateRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibrary
     * @name RemoveMediaLibrary
     * @request DELETE:/media-library/{id}
     */
    removeMediaLibrary: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibrary
     * @name PatchMediaLibrary
     * @request PUT:/media-library/{id}
     */
    patchMediaLibrary: (
      id: number,
      data: BakabaseInsideWorldModelsRequestModelsMediaLibraryUpdateRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibrary
     * @name GetMediaLibrarySyncStatus
     * @request GET:/media-library/sync/status
     */
    getMediaLibrarySyncStatus: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsTasksBackgroundTaskDto,
        any
      >({
        path: `/media-library/sync/status`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibrary
     * @name SyncMediaLibrary
     * @request PUT:/media-library/sync
     */
    syncMediaLibrary: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library/sync`,
        method: "PUT",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibrary
     * @name StopSyncMediaLibrary
     * @request DELETE:/media-library/sync
     */
    stopSyncMediaLibrary: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library/sync`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibrary
     * @name ValidatePathConfiguration
     * @request POST:/media-library/path-configuration-validation
     */
    validatePathConfiguration: (
      data: BakabaseInsideWorldModelsModelsEntitiesMediaLibraryPathConfiguration,
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsAosPathConfigurationValidateResult,
        any
      >({
        path: `/media-library/path-configuration-validation`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibrary
     * @name SortMediaLibrariesInCategory
     * @request PUT:/media-library/orders-in-category
     */
    sortMediaLibrariesInCategory: (
      data: BakabaseInsideWorldModelsRequestModelsIdBasedSortRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library/orders-in-category`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibrary
     * @name AddMediaLibraryPathConfiguration
     * @request POST:/media-library/{id}/path-configuration
     */
    addMediaLibraryPathConfiguration: (
      id: number,
      data: BakabaseInsideWorldModelsModelsEntitiesMediaLibraryPathConfiguration,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library/${id}/path-configuration`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibrary
     * @name RemoveMediaLibraryPathConfiguration
     * @request DELETE:/media-library/{id}/path-configuration
     */
    removeMediaLibraryPathConfiguration: (
      id: number,
      data: BakabaseInsideWorldModelsRequestModelsPathConfigurationRemoveRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library/${id}/path-configuration`,
        method: "DELETE",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibrary
     * @name GetPathRelatedLibraries
     * @request GET:/media-library/path-related-libraries
     */
    getPathRelatedLibraries: (
      query?: {
        /** @format int32 */
        libraryId?: number;
        currentPath?: string;
        newPath?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosMediaLibraryDto, any>({
        path: `/media-library/path-related-libraries`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
  };
  category = {
    /**
     * No description
     *
     * @tags Enhancement
     * @name RemoveCategoryEnhancementRecords
     * @request DELETE:/category/{id}/enhancement
     */
    removeCategoryEnhancementRecords: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/category/${id}/enhancement`,
        method: "DELETE",
        format: "json",
        ...params,
      }),
  };
  enhancementRecord = {
    /**
     * No description
     *
     * @tags EnhancementRecord
     * @name SearchEnhancementRecords
     * @request GET:/enhancement-record
     */
    searchEnhancementRecords: (
      query?: {
        /** @format int32 */
        resourceId?: number;
        success?: boolean;
        enhancerDescriptorId?: string;
        /** @format int32 */
        pageIndex?: number;
        /**
         * @format int32
         * @min 0
         * @max 100
         */
        pageSize?: number;
        /** @format int32 */
        skipCount?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsSearchResponse1BakabaseInsideWorldModelsModelsDtosEnhancementRecordDto,
        any
      >({
        path: `/enhancement-record`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
  };
  favorites = {
    /**
     * No description
     *
     * @tags Favorites
     * @name GetAllFavorites
     * @request GET:/favorites
     */
    getAllFavorites: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosFavoritesDto, any>({
        path: `/favorites`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Favorites
     * @name AddFavorites
     * @request POST:/favorites
     */
    addFavorites: (
      data: BakabaseInsideWorldModelsRequestModelsFavoritesAddOrUpdateRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/favorites`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Favorites
     * @name PutFavorites
     * @request PATCH:/favorites/{id}
     */
    putFavorites: (
      id: number,
      data: BakabaseInsideWorldModelsRequestModelsFavoritesAddOrUpdateRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/favorites/${id}`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Favorites
     * @name DeleteFavorites
     * @request DELETE:/favorites/{id}
     */
    deleteFavorites: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/favorites/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Favorites
     * @name AddResourceToFavorites
     * @request POST:/favorites/{id}/resource
     */
    addResourceToFavorites: (
      id: number,
      data: BakabaseInsideWorldModelsRequestModelsFavoritesResourceMappingAddOrRemoveRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/favorites/${id}/resource`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Favorites
     * @name DeleteResourceFromFavorites
     * @request DELETE:/favorites/{id}/resource
     */
    deleteResourceFromFavorites: (
      id: number,
      data: BakabaseInsideWorldModelsRequestModelsFavoritesResourceMappingAddOrRemoveRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/favorites/${id}/resource`,
        method: "DELETE",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Favorites
     * @name PutResourcesFavorites
     * @request PUT:/favorites/resource-mapping
     */
    putResourcesFavorites: (data: Record<string, number[]>, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/favorites/resource-mapping`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  file = {
    /**
     * No description
     *
     * @tags File
     * @name GetEntryTaskInfo
     * @request GET:/file/task-info
     */
    getEntryTaskInfo: (
      query?: {
        path?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsFileExplorerInformationIwFsTaskInfo,
        any
      >({
        path: `/file/task-info`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name GetIwFsInfo
     * @request GET:/file/iwfs-info
     */
    getIwFsInfo: (
      query?: {
        path?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsFileExplorerInformationIwFsEntryLazyInfo,
        any
      >({
        path: `/file/iwfs-info`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name GetIwFsEntry
     * @request GET:/file/iwfs-entry
     */
    getIwFsEntry: (
      query?: {
        path?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry,
        any
      >({
        path: `/file/iwfs-entry`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name CreateDirectory
     * @request POST:/file/directory
     */
    createDirectory: (
      query?: {
        parent?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/file/directory`,
        method: "POST",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name GetChildrenIwFsInfo
     * @request GET:/file/children/iwfs-info
     */
    getChildrenIwFsInfo: (
      query?: {
        root?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsFileExplorerIwFsPreview,
        any
      >({
        path: `/file/children/iwfs-info`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name RemoveFiles
     * @request DELETE:/file
     */
    removeFiles: (data: BakabaseInsideWorldModelsRequestModelsFileRemoveRequestModel, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/file`,
        method: "DELETE",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name RenameFile
     * @request PUT:/file/name
     */
    renameFile: (data: BakabaseInsideWorldModelsRequestModelsFileRenameRequestModel, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1SystemString, any>({
        path: `/file/name`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name OpenRecycleBin
     * @request GET:/file/recycle-bin
     */
    openRecycleBin: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/file/recycle-bin`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name ExtractAndRemoveDirectory
     * @request POST:/file/extract-and-remove-directory
     */
    extractAndRemoveDirectory: (
      query?: {
        directory?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/file/extract-and-remove-directory`,
        method: "POST",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name MoveEntries
     * @request POST:/file/move-entries
     */
    moveEntries: (data: BakabaseInsideWorldModelsRequestModelsFileMoveRequestModel, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/file/move-entries`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name GetSameNameEntriesInWorkingDirectory
     * @request POST:/file/same-name-entries-in-working-directory
     */
    getSameNameEntriesInWorkingDirectory: (
      data: BakabaseInsideWorldModelsRequestModelsRemoveSameEntryInWorkingDirectoryRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsListResponse1SystemString, any>({
        path: `/file/same-name-entries-in-working-directory`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name RemoveSameNameEntryInWorkingDirectory
     * @request DELETE:/file/same-name-entry-in-working-directory
     */
    removeSameNameEntryInWorkingDirectory: (
      data: BakabaseInsideWorldModelsRequestModelsRemoveSameEntryInWorkingDirectoryRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsListResponse1SystemString, any>({
        path: `/file/same-name-entry-in-working-directory`,
        method: "DELETE",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name StandardizeEntryName
     * @request PUT:/file/standardize
     */
    standardizeEntryName: (
      query?: {
        path?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/file/standardize`,
        method: "PUT",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name PlayFile
     * @request GET:/file/play
     */
    playFile: (
      query?: {
        fullname?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<void, any>({
        path: `/file/play`,
        method: "GET",
        query: query,
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name DecompressFiles
     * @request POST:/file/decompression
     */
    decompressFiles: (
      data: BakabaseInsideWorldModelsRequestModelsFileDecompressRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/file/decompression`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name GetIconData
     * @request GET:/file/icon
     */
    getIconData: (
      query?: {
        path?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1SystemString, any>({
        path: `/file/icon`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name GetAllFiles
     * @request GET:/file/all-files
     */
    getAllFiles: (
      query?: {
        path?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsListResponse1SystemString, any>({
        path: `/file/all-files`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name GetCompressedFileEntries
     * @request GET:/file/compressed-file/entries
     */
    getCompressedFileEntries: (
      query?: {
        compressedFilePath?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldBusinessComponentsCompressionCompressedFileEntry,
        any
      >({
        path: `/file/compressed-file/entries`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name GetFileExtensionCounts
     * @request GET:/file/file-extension-counts
     */
    getFileExtensionCounts: (
      query?: {
        sampleFile?: string;
        rootPath?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2SystemStringSystemInt32,
        any
      >({
        path: `/file/file-extension-counts`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name PreviewFileEntriesMergeResult
     * @request PUT:/file/merge-preview
     */
    previewFileEntriesMergeResult: (data: string[], params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsAosFileEntriesMergeResult,
        any
      >({
        path: `/file/merge-preview`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name PreviewFileEntriesMergeResultInRootPath
     * @request PUT:/file/merge-preview-in-root-path
     */
    previewFileEntriesMergeResultInRootPath: (data: string, params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsAosFileEntriesMergeResult,
        any
      >({
        path: `/file/merge-preview-in-root-path`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name MergeFileEntries
     * @request PUT:/file/merge
     */
    mergeFileEntries: (data: string[], params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/file/merge`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name MergeFileEntriesInRootPath
     * @request PUT:/file/merge-by
     */
    mergeFileEntriesInRootPath: (data: string, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/file/merge-by`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name GetFileSystemEntriesInDirectory
     * @request GET:/file/directory/file-entries
     */
    getFileSystemEntriesInDirectory: (
      query?: {
        path?: string;
        /** @format int32 */
        maxCount?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsListResponse1SystemString, any>({
        path: `/file/directory/file-entries`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name StartWatchingChangesInFileProcessorWorkspace
     * @request POST:/file/file-processor-watcher
     */
    startWatchingChangesInFileProcessorWorkspace: (
      query?: {
        path?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/file/file-processor-watcher`,
        method: "POST",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags File
     * @name StopWatchingChangesInFileProcessorWorkspace
     * @request DELETE:/file/file-processor-watcher
     */
    stopWatchingChangesInFileProcessorWorkspace: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/file/file-processor-watcher`,
        method: "DELETE",
        format: "json",
        ...params,
      }),
  };
  gui = {
    /**
     * No description
     *
     * @tags Gui
     * @name OpenFilesSelector
     * @request GET:/gui/files-selector
     */
    openFilesSelector: (
      query?: {
        initialDirectory?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsListResponse1SystemString, any>({
        path: `/gui/files-selector`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Gui
     * @name OpenFileSelector
     * @request GET:/gui/file-selector
     */
    openFileSelector: (
      query?: {
        initialDirectory?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1SystemString, any>({
        path: `/gui/file-selector`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Gui
     * @name OpenFolderSelector
     * @request GET:/gui/folder-selector
     */
    openFolderSelector: (
      query?: {
        initialDirectory?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1SystemString, any>({
        path: `/gui/folder-selector`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Gui
     * @name OpenUrlInDefaultBrowser
     * @request GET:/gui/url
     */
    openUrlInDefaultBrowser: (
      query?: {
        url?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/gui/url`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
  };
  log = {
    /**
     * No description
     *
     * @tags Log
     * @name GetAllLogs
     * @request GET:/log
     */
    getAllLogs: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsListResponse1BootstrapComponentsLoggingLogServiceModelsEntitiesLog,
        any
      >({
        path: `/log`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Log
     * @name ClearAllLog
     * @request DELETE:/log
     */
    clearAllLog: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/log`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Log
     * @name SearchLogs
     * @request GET:/log/filtered
     */
    searchLogs: (
      query?: {
        /** [0: Trace, 1: Debug, 2: Information, 3: Warning, 4: Error, 5: Critical, 6: None] */
        level?: MicrosoftExtensionsLoggingLogLevel;
        /** @format date-time */
        startDt?: string;
        /** @format date-time */
        endDt?: string;
        logger?: string;
        event?: string;
        message?: string;
        /** @format int32 */
        pageIndex?: number;
        /**
         * @format int32
         * @min 0
         * @max 100
         */
        pageSize?: number;
        /** @format int32 */
        skipCount?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsSearchResponse1BootstrapComponentsLoggingLogServiceModelsEntitiesLog,
        any
      >({
        path: `/log/filtered`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Log
     * @name GetUnreadLogCount
     * @request GET:/log/unread/count
     */
    getUnreadLogCount: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1SystemInt32, any>({
        path: `/log/unread/count`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Log
     * @name ReadLog
     * @request PATCH:/log/{id}/read
     */
    readLog: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/log/${id}/read`,
        method: "PATCH",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Log
     * @name ReadAllLog
     * @request PATCH:/log/read
     */
    readAllLog: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/log/read`,
        method: "PATCH",
        format: "json",
        ...params,
      }),
  };
  options = {
    /**
     * No description
     *
     * @tags Options
     * @name GetAppOptions
     * @request GET:/options/app
     */
    getAppOptions: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInfrastructuresComponentsConfigurationsAppAppOptions,
        any
      >({
        path: `/options/app`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name PatchAppOptions
     * @request PATCH:/options/app
     */
    patchAppOptions: (
      data: BakabaseInfrastructuresComponentsAppModelsRequestModelsAppOptionsPatchRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/options/app`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name GetUiOptions
     * @request GET:/options/ui
     */
    getUiOptions: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsUIOptions, any>({
        path: `/options/ui`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name PatchUiOptions
     * @request PATCH:/options/ui
     */
    patchUiOptions: (
      data: BakabaseInsideWorldModelsRequestModelsUIOptionsPatchRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/options/ui`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name GetBilibiliOptions
     * @request GET:/options/bilibili
     */
    getBilibiliOptions: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsBilibiliOptions, any>(
        {
          path: `/options/bilibili`,
          method: "GET",
          format: "json",
          ...params,
        },
      ),

    /**
     * No description
     *
     * @tags Options
     * @name PatchBilibiliOptions
     * @request PATCH:/options/bilibili
     */
    patchBilibiliOptions: (data: BakabaseInsideWorldModelsConfigsBilibiliOptions, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/options/bilibili`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name GetExHentaiOptions
     * @request GET:/options/exhentai
     */
    getExHentaiOptions: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsExHentaiOptions, any>(
        {
          path: `/options/exhentai`,
          method: "GET",
          format: "json",
          ...params,
        },
      ),

    /**
     * No description
     *
     * @tags Options
     * @name PatchExHentaiOptions
     * @request PATCH:/options/exhentai
     */
    patchExHentaiOptions: (data: BakabaseInsideWorldModelsConfigsExHentaiOptions, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/options/exhentai`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name GetFileSystemOptions
     * @request GET:/options/filesystem
     */
    getFileSystemOptions: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsFileSystemOptions,
        any
      >({
        path: `/options/filesystem`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name PatchFileSystemOptions
     * @request PATCH:/options/filesystem
     */
    patchFileSystemOptions: (data: BakabaseInsideWorldModelsConfigsFileSystemOptions, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/options/filesystem`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name GetJavLibraryOptions
     * @request GET:/options/javlibrary
     */
    getJavLibraryOptions: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsJavLibraryOptions,
        any
      >({
        path: `/options/javlibrary`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name PatchJavLibraryOptions
     * @request PATCH:/options/javlibrary
     */
    patchJavLibraryOptions: (data: BakabaseInsideWorldModelsConfigsJavLibraryOptions, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/options/javlibrary`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name GetPixivOptions
     * @request GET:/options/pixiv
     */
    getPixivOptions: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsPixivOptions, any>({
        path: `/options/pixiv`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name PatchPixivOptions
     * @request PATCH:/options/pixiv
     */
    patchPixivOptions: (data: BakabaseInsideWorldModelsConfigsPixivOptions, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/options/pixiv`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name GetResourceOptions
     * @request GET:/options/resource
     */
    getResourceOptions: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsResourceResourceOptionsDto,
        any
      >({
        path: `/options/resource`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name PatchResourceOptions
     * @request PATCH:/options/resource
     */
    patchResourceOptions: (
      data: BakabaseInsideWorldModelsRequestModelsOptionsResourceOptionsPatchRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/options/resource`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name GetThirdPartyOptions
     * @request GET:/options/thirdparty
     */
    getThirdPartyOptions: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsThirdPartyOptions,
        any
      >({
        path: `/options/thirdparty`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name PatchThirdPartyOptions
     * @request PATCH:/options/thirdparty
     */
    patchThirdPartyOptions: (data: BakabaseInsideWorldModelsConfigsThirdPartyOptions, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/options/thirdparty`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name GetNetworkOptions
     * @request GET:/options/network
     */
    getNetworkOptions: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsNetworkOptions, any>({
        path: `/options/network`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name PatchNetworkOptions
     * @request PATCH:/options/network
     */
    patchNetworkOptions: (data: BakabaseInsideWorldModelsConfigsNetworkOptions, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/options/network`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  password = {
    /**
     * No description
     *
     * @tags Password
     * @name SearchPasswords
     * @request GET:/password
     */
    searchPasswords: (
      query?: {
        /** [1: Latest, 2: Frequency] */
        order?: BakabaseInsideWorldModelsConstantsAosPasswordSearchOrder;
        /** @format int32 */
        pageIndex?: number;
        /**
         * @format int32
         * @min 0
         * @max 100
         */
        pageSize?: number;
        /** @format int32 */
        skipCount?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsSearchResponse1BakabaseInsideWorldModelsModelsEntitiesPassword, any>({
        path: `/password`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Password
     * @name GetAllPasswords
     * @request GET:/password/all
     */
    getAllPasswords: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsEntitiesPassword, any>({
        path: `/password/all`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Password
     * @name DeletePassword
     * @request DELETE:/password/{password}
     */
    deletePassword: (password: string, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/password/${password}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),
  };
  playlist = {
    /**
     * No description
     *
     * @tags Playlist
     * @name GetPlaylist
     * @request GET:/playlist/{id}
     */
    getPlaylist: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsDtosPlaylistDto, any>({
        path: `/playlist/${id}`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Playlist
     * @name DeletePlaylist
     * @request DELETE:/playlist/{id}
     */
    deletePlaylist: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/playlist/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Playlist
     * @name GetAllPlaylists
     * @request GET:/playlist
     */
    getAllPlaylists: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosPlaylistDto, any>({
        path: `/playlist`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Playlist
     * @name AddPlaylist
     * @request POST:/playlist
     */
    addPlaylist: (data: BakabaseInsideWorldModelsModelsDtosPlaylistDto, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/playlist`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Playlist
     * @name PutPlaylist
     * @request PUT:/playlist
     */
    putPlaylist: (data: BakabaseInsideWorldModelsModelsDtosPlaylistDto, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/playlist`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Playlist
     * @name GetPlaylistFiles
     * @request GET:/playlist/{id}/files
     */
    getPlaylistFiles: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsListResponse1SystemCollectionsGenericList1SystemString, any>({
        path: `/playlist/${id}/files`,
        method: "GET",
        format: "json",
        ...params,
      }),
  };
  publisher = {
    /**
     * No description
     *
     * @tags Publisher
     * @name UpdatePublisher
     * @request PUT:/publisher/{id}
     */
    updatePublisher: (
      id: number,
      data: BakabaseInsideWorldModelsRequestModelsPublisherUpdateModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/publisher/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Publisher
     * @name GetAllPublishers
     * @request GET:/publisher/all
     */
    getAllPublishers: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosPublisherDto, any>({
        path: `/publisher/all`,
        method: "GET",
        format: "json",
        ...params,
      }),
  };
  resourceCategory = {
    /**
     * No description
     *
     * @tags ResourceCategory
     * @name GetAllResourceCategories
     * @request GET:/resource-category
     */
    getAllResourceCategories: (
      query?: {
        /** [0: None, 1: Components, 3: Validation] */
        additionalItems?: BakabaseInsideWorldModelsConstantsAdditionalItemsResourceCategoryAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosResourceCategoryDto,
        any
      >({
        path: `/resource-category`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags ResourceCategory
     * @name AddResourceCategory
     * @request POST:/resource-category
     */
    addResourceCategory: (
      data: BakabaseInsideWorldModelsRequestModelsResourceCategoryAddRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource-category`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags ResourceCategory
     * @name UpdateResourceCategory
     * @request PUT:/resource-category/{id}
     */
    updateResourceCategory: (
      id: number,
      data: BakabaseInsideWorldModelsRequestModelsResourceCategoryUpdateRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource-category/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags ResourceCategory
     * @name RemoveResourceCategory
     * @request DELETE:/resource-category/{id}
     */
    removeResourceCategory: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource-category/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags ResourceCategory
     * @name ConfigureResourceCategoryComponents
     * @request PUT:/resource-category/{id}/component
     */
    configureResourceCategoryComponents: (
      id: number,
      data: BakabaseInsideWorldModelsRequestModelsResourceCategoryComponentConfigureRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource-category/${id}/component`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags ResourceCategory
     * @name SortCategories
     * @request PUT:/resource-category/orders
     */
    sortCategories: (data: BakabaseInsideWorldModelsRequestModelsIdBasedSortRequestModel, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource-category/orders`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags ResourceCategory
     * @name SaveDataFromSetupWizard
     * @request POST:/resource-category/setup-wizard
     */
    saveDataFromSetupWizard: (
      data: BakabaseInsideWorldModelsRequestModelsCategorySetupWizardRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource-category/setup-wizard`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  specialText = {
    /**
     * No description
     *
     * @tags SpecialText
     * @name GetAllSpecialText
     * @request GET:/special-text
     */
    getAllSpecialText: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2BakabaseInsideWorldModelsConstantsSpecialTextTypeSystemCollectionsGenericList1BakabaseInsideWorldModelsModelsEntitiesSpecialText,
        any
      >({
        path: `/special-text`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags SpecialText
     * @name CreateSpecialText
     * @request POST:/special-text
     */
    createSpecialText: (
      data: BakabaseInsideWorldModelsRequestModelsSpecialTextCreateRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsEntitiesSpecialText,
        any
      >({
        path: `/special-text`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags SpecialText
     * @name DeleteSpecialText
     * @request DELETE:/special-text/{id}
     */
    deleteSpecialText: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/special-text/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags SpecialText
     * @name UpdateSpecialText
     * @request PUT:/special-text/{id}
     */
    updateSpecialText: (
      id: number,
      data: BakabaseInsideWorldModelsRequestModelsSpecialTextUpdateRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/special-text/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags SpecialText
     * @name AddSpecialTextPrefabs
     * @request POST:/special-text/prefabs
     */
    addSpecialTextPrefabs: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/special-text/prefabs`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags SpecialText
     * @name PretreatText
     * @request POST:/special-text/pretreatment
     */
    pretreatText: (
      query?: {
        text?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1SystemString, any>({
        path: `/special-text/pretreatment`,
        method: "POST",
        query: query,
        format: "json",
        ...params,
      }),
  };
  tag = {
    /**
     * No description
     *
     * @tags Tag
     * @name GetAllTags
     * @request GET:/tag
     */
    getAllTags: (
      query?: {
        /** [0: None, 1: GroupName, 2: PreferredAlias] */
        additionalItems?: BakabaseInsideWorldModelsConstantsAdditionalItemsTagAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosTagDto, any>({
        path: `/tag`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Tag
     * @name AddTags
     * @request POST:/tag
     */
    addTags: (data: Record<string, string[]>, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/tag`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Tag
     * @name GetTagByIds
     * @request GET:/tag/ids
     */
    getTagByIds: (
      query?: {
        ids?: number[];
        /** [0: None, 1: GroupName, 2: PreferredAlias] */
        additionalItems?: BakabaseInsideWorldModelsConstantsAdditionalItemsTagAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosTagDto, any>({
        path: `/tag/ids`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Tag
     * @name UpdateTagName
     * @request PUT:/tag/{id}/name
     */
    updateTagName: (
      id: number,
      data: BakabaseInsideWorldModelsRequestModelsTagNameUpdateRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/tag/${id}/name`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Tag
     * @name UpdateTag
     * @request PATCH:/tag/{id}
     */
    updateTag: (
      id: number,
      data: BakabaseInsideWorldModelsRequestModelsTagUpdateRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/tag/${id}`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Tag
     * @name RemoveTag
     * @request DELETE:/tag/{id}
     */
    removeTag: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/tag/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Tag
     * @name MoveTag
     * @request PUT:/tag/{id}/move
     */
    moveTag: (
      id: number,
      data: BakabaseInsideWorldModelsRequestModelsTagMoveRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/tag/${id}/move`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Tag
     * @name BulkDeleteTags
     * @request DELETE:/tag/bulk
     */
    bulkDeleteTags: (data: number[], params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/tag/bulk`,
        method: "DELETE",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  tagGroup = {
    /**
     * No description
     *
     * @tags TagGroup
     * @name GetAllTagGroups
     * @request GET:/TagGroup
     */
    getAllTagGroups: (
      query?: {
        /** [1: Tags, 2: PreferredAlias, 4: TagNamePreferredAlias] */
        additionalItems?: BakabaseInsideWorldModelsConstantsAdditionalItemsTagGroupAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosTagGroupDto, any>({
        path: `/TagGroup`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TagGroup
     * @name AddTagGroups
     * @request POST:/TagGroup
     */
    addTagGroups: (data: BakabaseInsideWorldModelsRequestModelsTagGroupAddRequestModel, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/TagGroup`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TagGroup
     * @name UpdateTagGroup
     * @request PUT:/TagGroup/{id}
     */
    updateTagGroup: (
      id: number,
      data: BakabaseInsideWorldModelsRequestModelsTagGroupUpdateRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/TagGroup/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TagGroup
     * @name RemoveTagGroup
     * @request DELETE:/TagGroup/{id}
     */
    removeTagGroup: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/TagGroup/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TagGroup
     * @name SortTagGroups
     * @request PUT:/TagGroup/orders
     */
    sortTagGroups: (data: BakabaseInsideWorldModelsRequestModelsIdBasedSortRequestModel, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/TagGroup/orders`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  thirdParty = {
    /**
     * No description
     *
     * @tags ThirdParty
     * @name GetAllThirdPartyRequestStatistics
     * @request GET:/third-party/request-statistics
     */
    getAllThirdPartyRequestStatistics: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsAosThirdPartyRequestStatistics,
        any
      >({
        path: `/third-party/request-statistics`,
        method: "GET",
        format: "json",
        ...params,
      }),
  };
  tool = {
    /**
     * No description
     *
     * @tags Tool
     * @name ExtraSubdirectories
     * @request POST:/tool/extra-subdirectories
     */
    extraSubdirectories: (
      data: BakabaseInsideWorldModelsRequestModelsSubdirectoriesExtractRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/tool/extra-subdirectories`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Tool
     * @name OpenFileOrDirectory
     * @request GET:/tool/open
     */
    openFileOrDirectory: (
      query?: {
        path?: string;
        openInDirectory?: boolean;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/tool/open`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Tool
     * @name RemoveRelayDirectories
     * @request POST:/tool/remove-relay-directories
     */
    removeRelayDirectories: (
      query?: {
        root?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/tool/remove-relay-directories`,
        method: "POST",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Tool
     * @name GroupFilesToDirectories
     * @request POST:/tool/group-files-into-directories
     */
    groupFilesToDirectories: (
      query?: {
        root?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/tool/group-files-into-directories`,
        method: "POST",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Tool
     * @name StartExtractEverything
     * @request POST:/tool/everything-extraction
     */
    startExtractEverything: (
      query?: {
        root?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/tool/everything-extraction`,
        method: "POST",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Tool
     * @name StopExtractingEverything
     * @request DELETE:/tool/everything-extraction
     */
    stopExtractingEverything: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/tool/everything-extraction`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Tool
     * @name GetEverythingExtractionStatus
     * @request GET:/tool/everything-extraction/status
     */
    getEverythingExtractionStatus: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsResponseModelsEverythingExtractionStatus,
        any
      >({
        path: `/tool/everything-extraction/status`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Tool
     * @name PlayResourceFile
     * @request GET:/tool/{id}/play
     */
    playResourceFile: (
      id: number,
      query?: {
        file?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/tool/${id}/play`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Tool
     * @name TestList
     * @summary Test
     * @request GET:/tool/test
     */
    testList: (params: RequestParams = {}) =>
      this.request<void, any>({
        path: `/tool/test`,
        method: "GET",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Tool
     * @name ValidateCookie
     * @request GET:/tool/cookie-validation
     */
    validateCookie: (
      query?: {
        /** [1: BiliBili, 2: ExHentai, 3: Pixiv] */
        target?: BakabaseInsideWorldModelsConstantsCookieValidatorTarget;
        cookie?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/tool/cookie-validation`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
  };
  updater = {
    /**
     * No description
     *
     * @tags Updater
     * @name GetNewAppVersion
     * @request GET:/updater/app/new-version
     */
    getNewAppVersion: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo,
        any
      >({
        path: `/updater/app/new-version`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Updater
     * @name StartUpdatingApp
     * @request POST:/updater/app/update
     */
    startUpdatingApp: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/updater/app/update`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Updater
     * @name StopUpdatingApp
     * @request DELETE:/updater/app/update
     */
    stopUpdatingApp: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/updater/app/update`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Updater
     * @name RestartAndUpdateApp
     * @request POST:/updater/app/restart
     */
    restartAndUpdateApp: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/updater/app/restart`,
        method: "POST",
        format: "json",
        ...params,
      }),
  };
}
