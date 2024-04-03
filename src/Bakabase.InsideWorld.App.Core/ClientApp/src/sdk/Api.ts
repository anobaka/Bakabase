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

export interface A {
  /** @format int32 */
  x?: number;
}

/**
 * [1: CompressedFile, 2: Video]
 * @format int32
 */
export type AdditionalCoverDiscoveringSource = 1 | 2;

export interface Alias {
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

/**
 * [1: Candidates]
 * @format int32
 */
export type AliasAdditionalItem = 1;

export interface AliasCreateRequestModel {
  /** @minLength 1 */
  name: string;
  /** @format int32 */
  groupId?: number | null;
}

export interface AliasDto {
  /** @format int32 */
  id?: number;
  name?: string | null;
  candidates?: AliasDto[] | null;
  /** @format int32 */
  groupId?: number;
  /** @uniqueItems true */
  allNames?: string[] | null;
}

export interface AliasDtoSearchResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: AliasDto[] | null;
  /** @format int32 */
  totalCount?: number;
  /** @format int32 */
  pageIndex?: number;
  /** @format int32 */
  pageSize?: number;
}

export interface AliasGroupUpdateRequestModel {
  /** @format int32 */
  targetGroupId?: number;
}

export interface AliasSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Alias;
}

export interface AliasUpdateRequestModel {
  name?: string | null;
  isPreferred?: boolean;
}

export interface AppInfo {
  appDataPath?: string | null;
  coreVersion?: string | null;
  logPath?: string | null;
  backupPath?: string | null;
  tempFilesPath?: string | null;
  notAcceptTerms?: boolean;
  needRestart?: boolean;
}

export interface AppInfoSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: AppInfo;
}

export interface AppOptions {
  language?: string | null;
  version?: string | null;
  enablePreReleaseChannel?: boolean;
  enableAnonymousDataTracking?: boolean;
  wwwRootPath?: string | null;
  dataPath?: string | null;
  prevDataPath?: string | null;
  /** [0: Prompt, 1: Exit, 2: Minimize, 1000: Cancel] */
  closeBehavior?: CloseBehavior;
  /** [0: FollowSystem, 1: Light, 2: Dark] */
  uiTheme?: UiTheme;
}

export interface AppOptionsPatchRequestModel {
  language?: string | null;
  enablePreReleaseChannel?: boolean | null;
  enableAnonymousDataTracking?: boolean | null;
  /** [0: Prompt, 1: Exit, 2: Minimize, 1000: Cancel] */
  closeBehavior?: CloseBehavior;
  /** [0: FollowSystem, 1: Light, 2: Dark] */
  uiTheme?: UiTheme;
}

export interface AppOptionsSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: AppOptions;
}

export interface AppVersionInfo {
  version?: string | null;
  installers?: Installer[] | null;
}

export interface AppVersionInfoSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: AppVersionInfo;
}

/**
 * [0: X86, 1: X64, 2: Arm, 3: Arm64, 4: Wasm, 5: S390x]
 * @format int32
 */
export type Architecture = 0 | 1 | 2 | 3 | 4 | 5;

export interface Assembly {
  definedTypes?: TypeInfo[] | null;
  exportedTypes?: Type[] | null;
  codeBase?: string | null;
  entryPoint?: MethodInfo;
  fullName?: string | null;
  imageRuntimeVersion?: string | null;
  isDynamic?: boolean;
  location?: string | null;
  reflectionOnly?: boolean;
  isCollectible?: boolean;
  isFullyTrusted?: boolean;
  customAttributes?: CustomAttributeData[] | null;
  escapedCodeBase?: string | null;
  manifestModule?: Module;
  modules?: Module[] | null;
  /** @deprecated */
  globalAssemblyCache?: boolean;
  /** @format int64 */
  hostContext?: number;
  /** [0: None, 1: Level1, 2: Level2] */
  securityRuleSet?: SecurityRuleSet;
}

export interface BackgroundTaskDto {
  id?: string | null;
  name?: string | null;
  /** @format date-time */
  startDt?: string;
  /** [1: Running, 2: Complete, 3: Failed] */
  status?: BackgroundTaskStatus;
  message?: string | null;
  /** @format int32 */
  percentage?: number;
  currentProcess?: string | null;
  /** [1: Default, 2: Critical] */
  level?: BackgroundTaskLevel;
}

export interface BackgroundTaskDtoListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BackgroundTaskDto[] | null;
}

export interface BackgroundTaskDtoSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BackgroundTaskDto;
}

/**
 * [1: Default, 2: Critical]
 * @format int32
 */
export type BackgroundTaskLevel = 1 | 2;

/**
 * [1: Running, 2: Complete, 3: Failed]
 * @format int32
 */
export type BackgroundTaskStatus = 1 | 2 | 3;

export interface BaseResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
}

export interface BilibiliOptions {
  downloader?: CommonDownloaderOptions;
  cookie?: string | null;
}

export interface BilibiliOptionsSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BilibiliOptions;
}

/**
 * [0: None, 1: Ignore, 2: Replace, 3: Merge]
 * @format int32
 */
export type BulkModificationDiffOperation = 0 | 1 | 2 | 3;

/**
 * [1: Added, 2: Removed, 3: Modified]
 * @format int32
 */
export type BulkModificationDiffType = 1 | 2 | 3;

export interface BulkModificationDto {
  /** @format int32 */
  id?: number;
  name?: string | null;
  /** [1: Processing, 2: Closed] */
  status?: BulkModificationStatus;
  /** @format date-time */
  createdAt?: string;
  variables?: BulkModificationVariable[] | null;
  filter?: BulkModificationFilterGroup;
  processes?: BulkModificationProcess[] | null;
  diffs?: ResourceDiff[] | null;
  filteredResourceIds?: number[] | null;
  /** @format date-time */
  filteredAt?: string | null;
  /** @format date-time */
  calculatedAt?: string | null;
  /** @format date-time */
  appliedAt?: string | null;
  /** @format date-time */
  revertedAt?: string | null;
}

export interface BulkModificationDtoListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BulkModificationDto[] | null;
}

export interface BulkModificationDtoSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BulkModificationDto;
}

export interface BulkModificationFilter {
  /** [1: Category, 2: MediaLibrary, 4: FileName, 5: DirectoryPath, 7: CreateDt, 8: FileCreateDt, 9: FileModifyDt, 15: Tag] */
  property?: BulkModificationFilterableProperty;
  propertyKey?: string | null;
  /** [1: Equals, 2: NotEquals, 3: Contains, 4: NotContains, 5: StartsWith, 6: NotStartsWith, 7: EndsWith, 8: NotEndsWith, 9: GreaterThan, 10: LessThan, 11: GreaterThanOrEquals, 12: LessThanOrEquals, 13: IsNull, 14: IsNotNull, 15: In, 16: NotIn, 17: Matches, 18: NotMatches] */
  operation?: BulkModificationFilterOperation;
  target?: string | null;
}

export interface BulkModificationFilterGroup {
  /** [1: And, 2: Or] */
  operation?: BulkModificationFilterGroupOperation;
  filters?: BulkModificationFilter[] | null;
  groups?: BulkModificationFilterGroup[] | null;
}

/**
 * [1: And, 2: Or]
 * @format int32
 */
export type BulkModificationFilterGroupOperation = 1 | 2;

/**
 * [1: Equals, 2: NotEquals, 3: Contains, 4: NotContains, 5: StartsWith, 6: NotStartsWith, 7: EndsWith, 8: NotEndsWith, 9: GreaterThan, 10: LessThan, 11: GreaterThanOrEquals, 12: LessThanOrEquals, 13: IsNull, 14: IsNotNull, 15: In, 16: NotIn, 17: Matches, 18: NotMatches]
 * @format int32
 */
export type BulkModificationFilterOperation =
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
 * [1: Category, 2: MediaLibrary, 4: FileName, 5: DirectoryPath, 7: CreateDt, 8: FileCreateDt, 9: FileModifyDt, 15: Tag]
 * @format int32
 */
export type BulkModificationFilterableProperty = 1 | 2 | 4 | 5 | 7 | 8 | 9 | 15;

export interface BulkModificationProcess {
  /** [1: Category, 2: MediaLibrary, 4: FileName, 5: DirectoryPath, 7: CreateDt, 8: FileCreateDt, 9: FileModifyDt, 15: Tag] */
  property?: BulkModificationFilterableProperty;
  propertyKey?: string | null;
  value?: string | null;
}

export interface BulkModificationPutRequestModel {
  name?: string | null;
  filter?: BulkModificationFilterGroup;
  processes?: BulkModificationProcess[] | null;
  variables?: BulkModificationVariable[] | null;
}

export interface BulkModificationResourceDiffs {
  /** @format int32 */
  id?: number;
  path?: string | null;
  diffs?: Diff[] | null;
}

export interface BulkModificationResourceDiffsListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: BulkModificationResourceDiffs[] | null;
}

/**
 * [1: Processing, 2: Closed]
 * @format int32
 */
export type BulkModificationStatus = 1 | 2;

export interface BulkModificationVariable {
  key?: string | null;
  name?: string | null;
  /** [1: None, 2: FileName, 3: FileNameWithoutExtension, 4: FullPath, 5: DirectoryName] */
  source?: BulkModificationVariableSource;
  find?: string | null;
  value?: string | null;
}

/**
 * [1: None, 2: FileName, 3: FileNameWithoutExtension, 4: FullPath, 5: DirectoryName]
 * @format int32
 */
export type BulkModificationVariableSource = 1 | 2 | 3 | 4 | 5;

/**
 * [1: Standard, 2: VarArgs, 3: Any, 32: HasThis, 64: ExplicitThis]
 * @format int32
 */
export type CallingConventions = 1 | 2 | 3 | 32 | 64;

export interface Category {
  /** @format int32 */
  id?: number;
  name?: string | null;
  color?: string | null;
  /** @format date-time */
  createDt?: string;
  isValid?: boolean;
  /** @deprecated */
  message?: string | null;
  /** @format int32 */
  order?: number;
  componentsData?: CategoryComponent[] | null;
  /** [1: FilenameAscending, 2: FileModifyDtDescending] */
  coverSelectionOrder?: CoverSelectOrder;
  enhancementOptions?: ResourceCategoryEnhancementOptions;
  generateNfo?: boolean;
  customProperties?: CustomProperty[] | null;
  /** @format int32 */
  resourceDisplayNamePropertyId?: number | null;
}

export interface CategoryComponent {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  categoryId: number;
  /** @minLength 1 */
  componentKey: string;
  /** [1: Enhancer, 2: PlayableFileSelector, 3: Player] */
  componentType: ComponentType;
  descriptor?: ComponentDescriptor;
}

export interface CategoryListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Category[] | null;
}

export interface CategorySetupWizardInputModel {
  category?: ResourceCategoryAddRequestModel;
  mediaLibraries?: MediaLibrary[] | null;
  syncAfterSaving?: boolean;
}

/**
 * [0: Prompt, 1: Exit, 2: Minimize, 1000: Cancel]
 * @format int32
 */
export type CloseBehavior = 0 | 1 | 2 | 1000;

export interface CollectorOptions {
  path?: string | null;
  /** @uniqueItems true */
  urls?: string[] | null;
  /** @uniqueItems true */
  torrentOrLinkKeywords?: string[] | null;
}

/**
 * [1: And, 2: Or]
 * @format int32
 */
export type Combinator = 1 | 2;

export interface CommonDownloaderOptions {
  /** @format int32 */
  threads?: number;
  /** @format int32 */
  interval?: number;
  defaultPath?: string | null;
  namingConvention?: string | null;
}

export interface ComponentDescriptor {
  /** [0: Invalid, 1: Fixed, 2: Configurable, 3: Instance] */
  type?: ComponentDescriptorType;
  /** [1: Enhancer, 2: PlayableFileSelector, 3: Player] */
  componentType?: ComponentType;
  assemblyQualifiedTypeName?: string | null;
  name?: string | null;
  description?: string | null;
  message?: string | null;
  optionsJson?: string | null;
  /** @format int32 */
  optionsId?: number | null;
  version?: string | null;
  dataVersion?: string | null;
  optionsType?: Type;
  optionsJsonSchema?: string | null;
  id?: string | null;
  isInstanceable?: boolean;
  associatedCategories?: Category[] | null;
}

/**
 * [0: None, 1: AssociatedCategories]
 * @format int32
 */
export type ComponentDescriptorAdditionalItem = 0 | 1;

export interface ComponentDescriptorListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: ComponentDescriptor[] | null;
}

export interface ComponentDescriptorSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: ComponentDescriptor;
}

/**
 * [0: Invalid, 1: Fixed, 2: Configurable, 3: Instance]
 * @format int32
 */
export type ComponentDescriptorType = 0 | 1 | 2 | 3;

export interface ComponentOptions {
  /** @format int32 */
  id?: number;
  /** [1: Enhancer, 2: PlayableFileSelector, 3: Player] */
  componentType: ComponentType;
  /** @minLength 1 */
  componentAssemblyQualifiedTypeName: string;
  /** @minLength 1 */
  name: string;
  description?: string | null;
  /** @minLength 1 */
  json: string;
}

export interface ComponentOptionsAddRequestModel {
  /** @minLength 1 */
  name: string;
  description?: string | null;
  /** @minLength 1 */
  componentAssemblyQualifiedTypeName: string;
  /** @minLength 1 */
  json: string;
}

export interface ComponentOptionsSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: ComponentOptions;
}

/**
 * [1: Enhancer, 2: PlayableFileSelector, 3: Player]
 * @format int32
 */
export type ComponentType = 1 | 2 | 3;

export interface CompressedFileEntry {
  path?: string | null;
  /** @format int64 */
  size?: number;
  /** @format double */
  sizeInMb?: number;
}

export interface CompressedFileEntryListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: CompressedFileEntry[] | null;
}

export interface ConstructorInfo {
  name?: string | null;
  declaringType?: Type;
  reflectedType?: Type;
  module?: Module;
  customAttributes?: CustomAttributeData[] | null;
  isCollectible?: boolean;
  /** @format int32 */
  metadataToken?: number;
  /** [0: ReuseSlot, 0: ReuseSlot, 1: Private, 2: FamANDAssem, 3: Assembly, 4: Family, 5: FamORAssem, 6: Public, 7: MemberAccessMask, 8: UnmanagedExport, 16: Static, 32: Final, 64: Virtual, 128: HideBySig, 256: VtableLayoutMask, 256: VtableLayoutMask, 512: CheckAccessOnOverride, 1024: Abstract, 2048: SpecialName, 4096: RTSpecialName, 8192: PinvokeImpl, 16384: HasSecurity, 32768: RequireSecObject, 53248: ReservedMask] */
  attributes?: MethodAttributes;
  /** [0: Managed, 0: Managed, 1: Native, 2: OPTIL, 3: CodeTypeMask, 3: CodeTypeMask, 4: Unmanaged, 4: Unmanaged, 8: NoInlining, 16: ForwardRef, 32: Synchronized, 64: NoOptimization, 128: PreserveSig, 256: AggressiveInlining, 512: AggressiveOptimization, 4096: InternalCall, 65535: MaxMethodImplVal] */
  methodImplementationFlags?: MethodImplAttributes;
  /** [1: Standard, 2: VarArgs, 3: Any, 32: HasThis, 64: ExplicitThis] */
  callingConvention?: CallingConventions;
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
  methodHandle?: RuntimeMethodHandle;
  isSecurityCritical?: boolean;
  isSecuritySafeCritical?: boolean;
  isSecurityTransparent?: boolean;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType?: MemberTypes;
}

/**
 * [1: BiliBili, 2: ExHentai, 3: Pixiv]
 * @format int32
 */
export type CookieValidatorTarget = 1 | 2 | 3;

export interface CoreDataMoveRequestModel {
  /** @minLength 1 */
  dataPath: string;
}

export interface CoverOptionsModel {
  /** [1: ResourceDirectory, 2: TempDirectory] */
  saveLocation?: CoverSaveLocation;
  overwrite?: boolean | null;
}

/**
 * [1: ResourceDirectory, 2: TempDirectory]
 * @format int32
 */
export type CoverSaveLocation = 1 | 2;

export interface CoverSaveRequestModel {
  /** @minLength 1 */
  base64Image: string;
  overwrite?: boolean;
  /** [1: ResourceDirectory, 2: TempDirectory] */
  saveLocation?: CoverSaveLocation;
}

/**
 * [1: FilenameAscending, 2: FileModifyDtDescending]
 * @format int32
 */
export type CoverSelectOrder = 1 | 2;

export interface CustomAttributeData {
  attributeType?: Type;
  constructor?: ConstructorInfo;
  constructorArguments?: CustomAttributeTypedArgument[] | null;
  namedArguments?: CustomAttributeNamedArgument[] | null;
}

export interface CustomAttributeNamedArgument {
  memberInfo?: MemberInfo;
  typedValue?: CustomAttributeTypedArgument;
  memberName?: string | null;
  isField?: boolean;
}

export interface CustomAttributeTypedArgument {
  argumentType?: Type;
  value?: any;
}

/**
 * [1: String, 2: DateTime, 3: Number, 4: Enum]
 * @format int32
 */
export type CustomDataType = 1 | 2 | 3 | 4;

export interface CustomProperty {
  /** @format int32 */
  id?: number;
  name?: string | null;
  /** [1: SingleLineText, 2: MultilineText, 3: SingleChoice, 4: MultipleChoice, 5: Number, 6: Percentage, 7: Rating, 8: Boolean, 9: Link, 10: Attachment, 11: Date, 12: DateTime, 13: Time, 14: Formula, 15: Multilevel] */
  type?: CustomPropertyType;
  /** @format date-time */
  createdAt?: string;
  categories?: Category[] | null;
}

export interface CustomPropertyAddOrPutDto {
  name?: string | null;
  /** [1: SingleLineText, 2: MultilineText, 3: SingleChoice, 4: MultipleChoice, 5: Number, 6: Percentage, 7: Rating, 8: Boolean, 9: Link, 10: Attachment, 11: Date, 12: DateTime, 13: Time, 14: Formula, 15: Multilevel] */
  type?: CustomPropertyType;
  options?: string | null;
}

/**
 * [0: None, 1: Category]
 * @format int32
 */
export type CustomPropertyAdditionalItem = 0 | 1;

export interface CustomPropertyListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: CustomProperty[] | null;
}

export interface CustomPropertySingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: CustomProperty;
}

/**
 * [1: SingleLineText, 2: MultilineText, 3: SingleChoice, 4: MultipleChoice, 5: Number, 6: Percentage, 7: Rating, 8: Boolean, 9: Link, 10: Attachment, 11: Date, 12: DateTime, 13: Time, 14: Formula, 15: Multilevel]
 * @format int32
 */
export type CustomPropertyType = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 | 15;

export interface CustomPropertyValue {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  propertyId?: number;
  /** @format int32 */
  resourceId?: number;
  property?: CustomProperty;
  value?: any;
}

export interface CustomResourceProperty {
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
  valueType?: CustomDataType;
}

export interface DashboardStatistics {
  categoryResourceCounts?: TextAndCount[] | null;
  todayAddedCategoryResourceCounts?: TextAndCount[] | null;
  thisWeekAddedCategoryResourceCounts?: TextAndCount[] | null;
  thisMonthAddedCategoryResourceCounts?: TextAndCount[] | null;
  resourceTrending?: WeekCount[] | null;
  propertyResourceCounts?: PropertyAndCount[] | null;
  tagResourceCounts?: TextAndCount[] | null;
  downloaderDataCounts?: DownloaderTaskCount[] | null;
  thirdPartyRequestCounts?: ThirdPartyRequestCount[] | null;
  fileMover?: FileMoverInfo;
  otherCounts?: TextAndCount[][] | null;
}

export interface DashboardStatisticsSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: DashboardStatistics;
}

export interface DependentComponentVersion {
  version?: string | null;
  description?: string | null;
  canUpdate?: boolean;
}

export interface DependentComponentVersionSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: DependentComponentVersion;
}

export interface Diff {
  /** [1: Category, 2: MediaLibrary, 4: FileName, 5: DirectoryPath, 7: CreateDt, 8: FileCreateDt, 9: FileModifyDt, 15: Tag] */
  property?: BulkModificationFilterableProperty;
  propertyKey?: string | null;
  /** [1: Added, 2: Removed, 3: Modified] */
  type?: BulkModificationDiffType;
  currentValue?: string | null;
  newValue?: string | null;
  /** [0: None, 1: Ignore, 2: Replace, 3: Merge] */
  operation?: BulkModificationDiffOperation;
}

export interface DownloadTask {
  /** @format int32 */
  id?: number;
  /** @minLength 1 */
  key: string;
  name?: string | null;
  /** [1: Bilibili, 2: ExHentai, 3: Pixiv] */
  thirdPartyId?: ThirdPartyId;
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
  status?: DownloadTaskStatus;
  /** @minLength 1 */
  downloadPath: string;
  displayName?: string | null;
}

/**
 * [1: StartManually, 2: Restart, 3: Disable, 4: StartAutomatically]
 * @format int32
 */
export type DownloadTaskAction = 1 | 2 | 3 | 4;

/**
 * [0: NotSet, 1: StopOthers, 2: Ignore]
 * @format int32
 */
export type DownloadTaskActionOnConflict = 0 | 1 | 2;

export interface DownloadTaskCreateRequestModel {
  /** [1: Bilibili, 2: ExHentai, 3: Pixiv] */
  thirdPartyId: ThirdPartyId;
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

export interface DownloadTaskDto {
  /** @format int32 */
  id?: number;
  key?: string | null;
  name?: string | null;
  /** [1: Bilibili, 2: ExHentai, 3: Pixiv] */
  thirdPartyId?: ThirdPartyId;
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
  status?: DownloadTaskDtoStatus;
  downloadPath?: string | null;
  current?: string | null;
  /** @format int32 */
  failureTimes?: number;
  /** @format date-time */
  nextStartDt?: string | null;
  /** @uniqueItems true */
  availableActions?: DownloadTaskAction[] | null;
  displayName?: string | null;
  canStart?: boolean;
}

export interface DownloadTaskDtoListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: DownloadTaskDto[] | null;
}

export interface DownloadTaskDtoSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: DownloadTaskDto;
}

/**
 * [100: Idle, 200: InQueue, 300: Starting, 400: Downloading, 500: Stopping, 600: Complete, 700: Failed, 800: Disabled]
 * @format int32
 */
export type DownloadTaskDtoStatus = 100 | 200 | 300 | 400 | 500 | 600 | 700 | 800;

export interface DownloadTaskStartRequestModel {
  ids?: number[] | null;
  /** [0: NotSet, 1: StopOthers, 2: Ignore] */
  actionOnConflict?: DownloadTaskActionOnConflict;
}

/**
 * [100: InProgress, 200: Disabled, 300: Complete, 400: Failed]
 * @format int32
 */
export type DownloadTaskStatus = 100 | 200 | 300 | 400;

export interface DownloaderNamingDefinitions {
  fields?: Field[] | null;
  defaultConvention?: string | null;
}

export interface DownloaderTaskCount {
  /** [1: Bilibili, 2: ExHentai, 3: Pixiv] */
  id?: ThirdPartyId;
  statusAndCounts?: Record<string, number>;
}

export interface EnhancementRecordDto {
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

export interface EnhancementRecordDtoListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: EnhancementRecordDto[] | null;
}

export interface EnhancementRecordDtoSearchResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: EnhancementRecordDto[] | null;
  /** @format int32 */
  totalCount?: number;
  /** @format int32 */
  pageIndex?: number;
  /** @format int32 */
  pageSize?: number;
}

export interface Entry {
  isDirectory?: boolean;
  relativePath?: string | null;
  segmentAndMatchedValues?: SegmentMatchResult[] | null;
  globalMatchedValues?: GlobalMatchedValue[] | null;
}

/**
 * [0: None, 512: SpecialName, 1024: ReservedMask, 1024: ReservedMask]
 * @format int32
 */
export type EventAttributes = 0 | 512 | 1024;

export interface EventInfo {
  name?: string | null;
  declaringType?: Type;
  reflectedType?: Type;
  module?: Module;
  customAttributes?: CustomAttributeData[] | null;
  isCollectible?: boolean;
  /** @format int32 */
  metadataToken?: number;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType?: MemberTypes;
  /** [0: None, 512: SpecialName, 1024: ReservedMask, 1024: ReservedMask] */
  attributes?: EventAttributes;
  isSpecialName?: boolean;
  addMethod?: MethodInfo;
  removeMethod?: MethodInfo;
  raiseMethod?: MethodInfo;
  isMulticast?: boolean;
  eventHandlerType?: Type;
}

export interface EverythingExtractionStatus {
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
  failures?: Failure[] | null;
}

export interface EverythingExtractionStatusSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: EverythingExtractionStatus;
}

export interface ExHentaiEnhancerOptions {
  excludedTags?: string[] | null;
}

export interface ExHentaiOptions {
  downloader?: CommonDownloaderOptions;
  cookie?: string | null;
  enhancer?: ExHentaiEnhancerOptions;
}

export interface ExHentaiOptionsSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: ExHentaiOptions;
}

export interface Failure {
  fullnameList?: string[] | null;
  error?: string | null;
}

export interface Favorites {
  /** @format int64 */
  id?: number;
  title?: string | null;
  /** @format int32 */
  mediaCount?: number;
}

export interface FavoritesAddOrUpdateRequestModel {
  /** @format int32 */
  id?: number;
  /** @minLength 1 */
  name: string;
  description?: string | null;
  /** @format date-time */
  createDt?: string;
}

export interface FavoritesDto {
  /** @format int32 */
  id?: number;
  /** @minLength 1 */
  name: string;
  description?: string | null;
  /** @format date-time */
  createDt?: string;
}

export interface FavoritesDtoListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: FavoritesDto[] | null;
}

export interface FavoritesListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Favorites[] | null;
}

export interface FavoritesResourceMappingAddOrRemoveRequestModel {
  /** @format int32 */
  resourceId?: number;
}

export interface Field {
  key?: string | null;
  description?: string | null;
  example?: string | null;
}

/**
 * [0: PrivateScope, 1: Private, 2: FamANDAssem, 3: Assembly, 4: Family, 5: FamORAssem, 6: Public, 7: FieldAccessMask, 16: Static, 32: InitOnly, 64: Literal, 128: NotSerialized, 256: HasFieldRVA, 512: SpecialName, 1024: RTSpecialName, 4096: HasFieldMarshal, 8192: PinvokeImpl, 32768: HasDefault, 38144: ReservedMask]
 * @format int32
 */
export type FieldAttributes =
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

export interface FieldInfo {
  name?: string | null;
  declaringType?: Type;
  reflectedType?: Type;
  module?: Module;
  customAttributes?: CustomAttributeData[] | null;
  isCollectible?: boolean;
  /** @format int32 */
  metadataToken?: number;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType?: MemberTypes;
  /** [0: PrivateScope, 1: Private, 2: FamANDAssem, 3: Assembly, 4: Family, 5: FamORAssem, 6: Public, 7: FieldAccessMask, 16: Static, 32: InitOnly, 64: Literal, 128: NotSerialized, 256: HasFieldRVA, 512: SpecialName, 1024: RTSpecialName, 4096: HasFieldMarshal, 8192: PinvokeImpl, 32768: HasDefault, 38144: ReservedMask] */
  attributes?: FieldAttributes;
  fieldType?: Type;
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
  fieldHandle?: RuntimeFieldHandle;
}

export interface FileDecompressRequestModel {
  paths?: string[] | null;
  password?: string | null;
}

export interface FileEntriesMergeResult {
  rootPath?: string | null;
  currentNames?: string[] | null;
  mergeResult?: Record<string, string[]>;
}

export interface FileEntriesMergeResultSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: FileEntriesMergeResult;
}

export interface FileMoveRequestModel {
  destDir?: string | null;
  entryPaths?: string[] | null;
}

export interface FileMoverInfo {
  /** @format int32 */
  sourceCount?: number;
  /** @format int32 */
  targetCount?: number;
}

export interface FileMoverOptions {
  targets?: Target[] | null;
  enabled?: boolean;
  delay?: TimeSpan;
}

export interface FileProcessorOptions {
  workingDirectory?: string | null;
}

export interface FileRemoveRequestModel {
  paths?: string[] | null;
}

export interface FileRenameRequestModel {
  fullname?: string | null;
  newName?: string | null;
}

export interface FileSystemOptions {
  recentMovingDestinations?: string[] | null;
  fileMover?: FileMoverOptions;
  fileProcessor?: FileProcessorOptions;
}

export interface FileSystemOptionsSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: FileSystemOptions;
}

/**
 * [0: None, 1: Covariant, 2: Contravariant, 3: VarianceMask, 4: ReferenceTypeConstraint, 8: NotNullableValueTypeConstraint, 16: DefaultConstructorConstraint, 28: SpecialConstraintMask]
 * @format int32
 */
export type GenericParameterAttributes = 0 | 1 | 2 | 3 | 4 | 8 | 16 | 28;

export interface GlobalMatchedValue {
  /** [1: RootPath, 2: ParentResource, 3: Resource, 4: ReleaseDt, 5: Publisher, 6: Name, 7: Language, 8: Volume, 9: Original, 10: Series, 11: Tag, 12: Introduction, 13: Rate, 14: CustomProperty, 15: FileName, 16: DirectoryPath, 17: CreatedAt, 18: FileCreatedAt, 19: FileModifiedAt, 20: Category, 21: MediaLibrary, 22: Favorites] */
  property?: ResourceProperty;
  key?: string | null;
  values?: string[] | null;
}

export type ICustomAttributeProvider = object;

export interface IdBasedSortRequestModel {
  ids?: number[] | null;
}

/**
 * [1: NotAcceptTerms, 2: NeedRestart]
 * @format int32
 */
export type InitializationContentType = 1 | 2;

export interface InitializationContentTypeSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  /** [1: NotAcceptTerms, 2: NeedRestart] */
  data?: InitializationContentType;
}

export interface Installer {
  osPlatform?: OSPlatform;
  /** [0: X86, 1: X64, 2: Arm, 3: Arm64, 4: Wasm, 5: S390x] */
  osArchitecture?: Architecture;
  name?: string | null;
  url?: string | null;
  /** @format int64 */
  size?: number;
}

export interface Int32DownloaderNamingDefinitionsDictionarySingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Record<string, DownloaderNamingDefinitions>;
}

export interface Int32Int32ArrayDictionarySingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Record<string, number[] | null>;
}

export interface Int32ListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: number[] | null;
}

export interface Int32SingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  /** @format int32 */
  data?: number;
}

export type IntPtr = object;

/**
 * [1: Hidden]
 * @format int32
 */
export type IwFsAttribute = 1;

export interface IwFsCompressedFileGroup {
  keyName?: string | null;
  files?: string[] | null;
  extension?: string | null;
  missEntry?: boolean;
  password?: string | null;
  passwordCandidates?: string[] | null;
}

export interface IwFsEntry {
  path?: string | null;
  name?: string | null;
  meaningfulName?: string | null;
  ext?: string | null;
  attributes?: IwFsAttribute[] | null;
  /** [0: Unknown, 100: Directory, 200: Image, 300: CompressedFileEntry, 400: CompressedFilePart, 500: Symlink, 600: Video, 700: Audio, 10000: Invalid] */
  type?: IwFsType;
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

export interface IwFsEntryLazyInfo {
  /** @format int32 */
  childrenCount?: number;
}

export interface IwFsEntryLazyInfoSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: IwFsEntryLazyInfo;
}

export interface IwFsEntrySingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: IwFsEntry;
}

/**
 * [1: Decompressing, 2: Moving]
 * @format int32
 */
export type IwFsEntryTaskType = 1 | 2;

export interface IwFsPreview {
  entries?: IwFsEntry[] | null;
  directoryChain?: IwFsEntry[] | null;
  compressedFileGroups?: IwFsCompressedFileGroup[] | null;
}

export interface IwFsPreviewSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: IwFsPreview;
}

export interface IwFsTaskInfo {
  path?: string | null;
  /** [1: Decompressing, 2: Moving] */
  type?: IwFsEntryTaskType;
  /** @format int32 */
  percentage?: number;
  error?: string | null;
  backgroundTaskId?: string | null;
  name?: string | null;
}

export interface IwFsTaskInfoSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: IwFsTaskInfo;
}

/**
 * [0: Unknown, 100: Directory, 200: Image, 300: CompressedFileEntry, 400: CompressedFilePart, 500: Symlink, 600: Video, 700: Audio, 10000: Invalid]
 * @format int32
 */
export type IwFsType = 0 | 100 | 200 | 300 | 400 | 500 | 600 | 700 | 10000;

export interface JavLibraryOptions {
  cookie?: string | null;
  collector?: CollectorOptions;
}

export interface JavLibraryOptionsSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: JavLibraryOptions;
}

/**
 * [0: Sequential, 2: Explicit, 3: Auto]
 * @format int32
 */
export type LayoutKind = 0 | 2 | 3;

export interface Log {
  /** @format int32 */
  id?: number;
  /** @format date-time */
  dateTime?: string;
  /** [0: Trace, 1: Debug, 2: Information, 3: Warning, 4: Error, 5: Critical, 6: None] */
  level?: LogLevel;
  logger?: string | null;
  event?: string | null;
  message?: string | null;
  read?: boolean;
}

/**
 * [0: Trace, 1: Debug, 2: Information, 3: Warning, 4: Error, 5: Critical, 6: None]
 * @format int32
 */
export type LogLevel = 0 | 1 | 2 | 3 | 4 | 5 | 6;

export interface LogListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Log[] | null;
}

export interface LogSearchResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Log[] | null;
  /** @format int32 */
  totalCount?: number;
  /** @format int32 */
  pageIndex?: number;
  /** @format int32 */
  pageSize?: number;
}

export interface MatcherValue {
  fixedText?: string | null;
  /** @format int32 */
  layer?: number | null;
  regex?: string | null;
  /** [1: RootPath, 2: ParentResource, 3: Resource, 4: ReleaseDt, 5: Publisher, 6: Name, 7: Language, 8: Volume, 9: Original, 10: Series, 11: Tag, 12: Introduction, 13: Rate, 14: CustomProperty, 15: FileName, 16: DirectoryPath, 17: CreatedAt, 18: FileCreatedAt, 19: FileModifiedAt, 20: Category, 21: MediaLibrary, 22: Favorites] */
  property?: ResourceProperty;
  /** [1: Layer, 2: Regex, 3: FixedText] */
  valueType?: ResourceMatcherValueType;
  key?: string | null;
  isValid?: boolean;
}

export interface MediaLibrary {
  /** @format int32 */
  id?: number;
  /** @minLength 1 */
  name: string;
  /** @format int32 */
  categoryId: number;
  /** @format int32 */
  order?: number;
  /** @format int32 */
  resourceCount?: number;
  fileSystemInformation?: Record<string, MediaLibraryFileSystemInformation>;
  category?: Category;
  pathConfigurations?: PathConfiguration[] | null;
}

export interface MediaLibraryAddInBulkRequestModel {
  nameAndPaths: Record<string, string[] | null>;
}

/**
 * [0: None, 1: Category, 2: FileSystemInfo, 4: FixedTags]
 * @format int32
 */
export type MediaLibraryAdditionalItem = 0 | 1 | 2 | 4;

export interface MediaLibraryCreateDto {
  /** @minLength 1 */
  name: string;
  /** @format int32 */
  categoryId: number;
  pathConfigurations?: PathConfiguration[] | null;
}

/**
 * [1: InvalidVolume, 2: FreeSpaceNotEnough, 3: Occupied]
 * @format int32
 */
export type MediaLibraryFileSystemError = 1 | 2 | 3;

export interface MediaLibraryFileSystemInformation {
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
  error?: MediaLibraryFileSystemError;
}

export interface MediaLibraryListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: MediaLibrary[] | null;
}

export interface MediaLibraryPatchDto {
  name?: string | null;
  pathConfigurations?: PathConfiguration[] | null;
  /** @format int32 */
  order?: number | null;
}

export interface MediaLibraryPathConfigurationCreateRequestModel {
  /** @minLength 1 */
  path: string;
}

export interface MediaLibraryRootPathsAddInBulkRequestModel {
  rootPaths?: string[] | null;
}

/**
 * [1: Image, 2: Audio, 3: Video, 4: Text, 1000: Unknown]
 * @format int32
 */
export type MediaType = 1 | 2 | 3 | 4 | 1000;

export interface MemberInfo {
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType?: MemberTypes;
  declaringType?: Type;
  reflectedType?: Type;
  name?: string | null;
  module?: Module;
  customAttributes?: CustomAttributeData[] | null;
  isCollectible?: boolean;
  /** @format int32 */
  metadataToken?: number;
}

/**
 * [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All]
 * @format int32
 */
export type MemberTypes = 1 | 2 | 4 | 8 | 16 | 32 | 64 | 128 | 191;

/**
 * [0: ReuseSlot, 0: ReuseSlot, 1: Private, 2: FamANDAssem, 3: Assembly, 4: Family, 5: FamORAssem, 6: Public, 7: MemberAccessMask, 8: UnmanagedExport, 16: Static, 32: Final, 64: Virtual, 128: HideBySig, 256: VtableLayoutMask, 256: VtableLayoutMask, 512: CheckAccessOnOverride, 1024: Abstract, 2048: SpecialName, 4096: RTSpecialName, 8192: PinvokeImpl, 16384: HasSecurity, 32768: RequireSecObject, 53248: ReservedMask]
 * @format int32
 */
export type MethodAttributes =
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

export interface MethodBase {
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType?: MemberTypes;
  name?: string | null;
  declaringType?: Type;
  reflectedType?: Type;
  module?: Module;
  customAttributes?: CustomAttributeData[] | null;
  isCollectible?: boolean;
  /** @format int32 */
  metadataToken?: number;
  /** [0: ReuseSlot, 0: ReuseSlot, 1: Private, 2: FamANDAssem, 3: Assembly, 4: Family, 5: FamORAssem, 6: Public, 7: MemberAccessMask, 8: UnmanagedExport, 16: Static, 32: Final, 64: Virtual, 128: HideBySig, 256: VtableLayoutMask, 256: VtableLayoutMask, 512: CheckAccessOnOverride, 1024: Abstract, 2048: SpecialName, 4096: RTSpecialName, 8192: PinvokeImpl, 16384: HasSecurity, 32768: RequireSecObject, 53248: ReservedMask] */
  attributes?: MethodAttributes;
  /** [0: Managed, 0: Managed, 1: Native, 2: OPTIL, 3: CodeTypeMask, 3: CodeTypeMask, 4: Unmanaged, 4: Unmanaged, 8: NoInlining, 16: ForwardRef, 32: Synchronized, 64: NoOptimization, 128: PreserveSig, 256: AggressiveInlining, 512: AggressiveOptimization, 4096: InternalCall, 65535: MaxMethodImplVal] */
  methodImplementationFlags?: MethodImplAttributes;
  /** [1: Standard, 2: VarArgs, 3: Any, 32: HasThis, 64: ExplicitThis] */
  callingConvention?: CallingConventions;
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
  methodHandle?: RuntimeMethodHandle;
  isSecurityCritical?: boolean;
  isSecuritySafeCritical?: boolean;
  isSecurityTransparent?: boolean;
}

/**
 * [0: Managed, 0: Managed, 1: Native, 2: OPTIL, 3: CodeTypeMask, 3: CodeTypeMask, 4: Unmanaged, 4: Unmanaged, 8: NoInlining, 16: ForwardRef, 32: Synchronized, 64: NoOptimization, 128: PreserveSig, 256: AggressiveInlining, 512: AggressiveOptimization, 4096: InternalCall, 65535: MaxMethodImplVal]
 * @format int32
 */
export type MethodImplAttributes = 0 | 1 | 2 | 3 | 4 | 8 | 16 | 32 | 64 | 128 | 256 | 512 | 4096 | 65535;

export interface MethodInfo {
  name?: string | null;
  declaringType?: Type;
  reflectedType?: Type;
  module?: Module;
  customAttributes?: CustomAttributeData[] | null;
  isCollectible?: boolean;
  /** @format int32 */
  metadataToken?: number;
  /** [0: ReuseSlot, 0: ReuseSlot, 1: Private, 2: FamANDAssem, 3: Assembly, 4: Family, 5: FamORAssem, 6: Public, 7: MemberAccessMask, 8: UnmanagedExport, 16: Static, 32: Final, 64: Virtual, 128: HideBySig, 256: VtableLayoutMask, 256: VtableLayoutMask, 512: CheckAccessOnOverride, 1024: Abstract, 2048: SpecialName, 4096: RTSpecialName, 8192: PinvokeImpl, 16384: HasSecurity, 32768: RequireSecObject, 53248: ReservedMask] */
  attributes?: MethodAttributes;
  /** [0: Managed, 0: Managed, 1: Native, 2: OPTIL, 3: CodeTypeMask, 3: CodeTypeMask, 4: Unmanaged, 4: Unmanaged, 8: NoInlining, 16: ForwardRef, 32: Synchronized, 64: NoOptimization, 128: PreserveSig, 256: AggressiveInlining, 512: AggressiveOptimization, 4096: InternalCall, 65535: MaxMethodImplVal] */
  methodImplementationFlags?: MethodImplAttributes;
  /** [1: Standard, 2: VarArgs, 3: Any, 32: HasThis, 64: ExplicitThis] */
  callingConvention?: CallingConventions;
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
  methodHandle?: RuntimeMethodHandle;
  isSecurityCritical?: boolean;
  isSecuritySafeCritical?: boolean;
  isSecurityTransparent?: boolean;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType?: MemberTypes;
  returnParameter?: ParameterInfo;
  returnType?: Type;
  returnTypeCustomAttributes?: ICustomAttributeProvider;
}

export interface Module {
  assembly?: Assembly;
  fullyQualifiedName?: string | null;
  name?: string | null;
  /** @format int32 */
  mdStreamVersion?: number;
  /** @format uuid */
  moduleVersionId?: string;
  scopeName?: string | null;
  moduleHandle?: ModuleHandle;
  customAttributes?: CustomAttributeData[] | null;
  /** @format int32 */
  metadataToken?: number;
}

export interface ModuleHandle {
  /** @format int32 */
  mdStreamVersion?: number;
}

export interface NetworkOptions {
  proxy?: ProxyOptions;
}

export interface NetworkOptionsSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: NetworkOptions;
}

export type OSPlatform = object;

export interface OriginalDto {
  /** @format int32 */
  id?: number;
  name?: string | null;
}

export interface OriginalDtoListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: OriginalDto[] | null;
}

/**
 * [0: None, 1: In, 2: Out, 4: Lcid, 8: Retval, 16: Optional, 4096: HasDefault, 8192: HasFieldMarshal, 16384: Reserved3, 32768: Reserved4, 61440: ReservedMask]
 * @format int32
 */
export type ParameterAttributes = 0 | 1 | 2 | 4 | 8 | 16 | 4096 | 8192 | 16384 | 32768 | 61440;

export interface ParameterInfo {
  /** [0: None, 1: In, 2: Out, 4: Lcid, 8: Retval, 16: Optional, 4096: HasDefault, 8192: HasFieldMarshal, 16384: Reserved3, 32768: Reserved4, 61440: ReservedMask] */
  attributes?: ParameterAttributes;
  member?: MemberInfo;
  name?: string | null;
  parameterType?: Type;
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
  customAttributes?: CustomAttributeData[] | null;
  /** @format int32 */
  metadataToken?: number;
}

export interface Password {
  /** @maxLength 64 */
  text?: string | null;
  /** @format int32 */
  usedTimes?: number;
  /** @format date-time */
  lastUsedAt?: string;
}

export interface PasswordListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Password[] | null;
}

/**
 * [1: Latest, 2: Frequency]
 * @format int32
 */
export type PasswordSearchOrder = 1 | 2;

export interface PasswordSearchResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Password[] | null;
  /** @format int32 */
  totalCount?: number;
  /** @format int32 */
  pageIndex?: number;
  /** @format int32 */
  pageSize?: number;
}

export interface PathConfiguration {
  path?: string | null;
  fixedTagIds?: number[] | null;
  rpmValues?: MatcherValue[] | null;
  fixedTags?: TagDto[] | null;
}

export interface PathConfigurationRemoveRequestModel {
  /** @format int32 */
  index: number;
}

export interface PathConfigurationValidateResult {
  rootPath?: string | null;
  entries?: Entry[] | null;
}

export interface PathConfigurationValidateResultSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: PathConfigurationValidateResult;
}

export interface PixivOptions {
  cookie?: string | null;
  downloader?: CommonDownloaderOptions;
}

export interface PixivOptionsSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: PixivOptions;
}

export interface PlaylistDto {
  /** @format int32 */
  id?: number;
  name?: string | null;
  items?: PlaylistItemDto[] | null;
  /** @format int32 */
  interval?: number;
  /** @format int32 */
  order?: number;
}

export interface PlaylistDtoListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: PlaylistDto[] | null;
}

export interface PlaylistDtoSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: PlaylistDto;
}

export interface PlaylistItemDto {
  /** [1: Resource, 2: Video, 3: Image, 4: Audio] */
  type?: PlaylistItemType;
  /** @format int32 */
  resourceId?: number | null;
  file?: string | null;
  startTime?: TimeSpan;
  endTime?: TimeSpan;
}

/**
 * [1: Resource, 2: Video, 3: Image, 4: Audio]
 * @format int32
 */
export type PlaylistItemType = 1 | 2 | 3 | 4;

export interface PreviewerItem {
  filePath?: string | null;
  /** [1: Image, 2: Audio, 3: Video, 4: Text, 1000: Unknown] */
  type?: MediaType;
  /** @format int32 */
  duration?: number;
}

export interface PreviewerItemListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: PreviewerItem[] | null;
}

export interface PropertyAndCount {
  /** [1: RootPath, 2: ParentResource, 3: Resource, 4: ReleaseDt, 5: Publisher, 6: Name, 7: Language, 8: Volume, 9: Original, 10: Series, 11: Tag, 12: Introduction, 13: Rate, 14: CustomProperty, 15: FileName, 16: DirectoryPath, 17: CreatedAt, 18: FileCreatedAt, 19: FileModifiedAt, 20: Category, 21: MediaLibrary, 22: Favorites] */
  property?: ResourceProperty;
  propertyKey?: string | null;
  value?: string | null;
  /** @format int32 */
  count?: number;
}

/**
 * [0: None, 512: SpecialName, 1024: RTSpecialName, 4096: HasDefault, 8192: Reserved2, 16384: Reserved3, 32768: Reserved4, 62464: ReservedMask]
 * @format int32
 */
export type PropertyAttributes = 0 | 512 | 1024 | 4096 | 8192 | 16384 | 32768 | 62464;

export interface PropertyInfo {
  name?: string | null;
  declaringType?: Type;
  reflectedType?: Type;
  module?: Module;
  customAttributes?: CustomAttributeData[] | null;
  isCollectible?: boolean;
  /** @format int32 */
  metadataToken?: number;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType?: MemberTypes;
  propertyType?: Type;
  /** [0: None, 512: SpecialName, 1024: RTSpecialName, 4096: HasDefault, 8192: Reserved2, 16384: Reserved3, 32768: Reserved4, 62464: ReservedMask] */
  attributes?: PropertyAttributes;
  isSpecialName?: boolean;
  canRead?: boolean;
  canWrite?: boolean;
  getMethod?: MethodInfo;
  setMethod?: MethodInfo;
}

export interface ProxyCredentials {
  username?: string | null;
  password?: string | null;
  domain?: string | null;
}

export interface ProxyOptions {
  address?: string | null;
  credentials?: ProxyCredentials;
}

export interface PublisherDto {
  /** @format int32 */
  id?: number;
  name?: string | null;
  subPublishers?: PublisherDto[] | null;
  /** @format int32 */
  rank?: number;
  favorite?: boolean;
  tags?: TagDto[] | null;
}

export interface PublisherDtoListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: PublisherDto[] | null;
}

export interface PublisherUpdateModel {
  name?: string | null;
  /** @format int32 */
  rank?: number | null;
  favorite?: boolean | null;
  tagIds?: number[] | null;
}

export interface RemoveSameEntryInWorkingDirectoryRequestModel {
  workingDir?: string | null;
  entryPath?: string | null;
}

export interface Resource {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  mediaLibraryId?: number;
  /** @format int32 */
  categoryId?: number;
  fileName?: string | null;
  directory?: string | null;
  path?: string | null;
  /** @format int32 */
  parentId?: number | null;
  hasChildren?: boolean;
  isFile?: boolean;
  /** @format date-time */
  createDt?: string;
  /** @format date-time */
  updateDt?: string;
  /** @format date-time */
  fileCreateDt?: string;
  /** @format date-time */
  fileModifyDt?: string;
  tags?: TagDto[] | null;
  parent?: Resource;
  customPropertiesV2?: CustomProperty[] | null;
  customPropertyValues?: CustomPropertyValue[] | null;
  /** @deprecated */
  customProperties?: Record<string, CustomResourceProperty[] | null>;
  /** @deprecated */
  introduction?: string | null;
  series?: SeriesDto;
  /**
   * @deprecated
   * @format double
   */
  rate?: number;
  /** @deprecated */
  name?: string | null;
  volume?: VolumeDto;
  /** [0: NotSet, 1: Chinese, 2: English, 3: Japanese, 4: Korean, 5: French, 6: German, 7: Spanish, 8: Russian] */
  language?: ResourceLanguage;
  /** @deprecated */
  publishers?: PublisherDto[] | null;
  /** @deprecated */
  originals?: OriginalDto[] | null;
  /**
   * @deprecated
   * @format date-time
   */
  releaseDt?: string | null;
}

export type ResourceCategory = object;

export interface ResourceCategoryAddRequestModel {
  /** @format int32 */
  id?: number;
  name?: string | null;
  color?: string | null;
  /** [1: FilenameAscending, 2: FileModifyDtDescending] */
  coverSelectionOrder?: CoverSelectOrder;
  /** @format int32 */
  order?: number | null;
  generateNfo?: boolean | null;
  componentsData?: SimpleCategoryComponent[] | null;
  enhancementOptions?: ResourceCategoryEnhancementOptions;
}

/**
 * [0: None, 1: Components, 3: Validation, 4: CustomProperties]
 * @format int32
 */
export type ResourceCategoryAdditionalItem = 0 | 1 | 3 | 4;

export interface ResourceCategoryComponentConfigureRequestModel {
  /** [1: Enhancer, 2: PlayableFileSelector, 3: Player] */
  type: ComponentType;
  componentKeys: string[];
  enhancementOptions?: ResourceCategoryEnhancementOptions;
}

export interface ResourceCategoryCustomPropertyBindRequestModel {
  customPropertyIds?: number[] | null;
}

export interface ResourceCategoryDuplicateRequestModel {
  name?: string | null;
}

export interface ResourceCategoryEnhancementOptions {
  enhancementPriorities?: Record<string, string[]>;
  defaultPriority?: string[] | null;
}

export interface ResourceCategoryUpdateRequestModel {
  /** @format int32 */
  id?: number;
  name?: string | null;
  color?: string | null;
  /** [1: FilenameAscending, 2: FileModifyDtDescending] */
  coverSelectionOrder?: CoverSelectOrder;
  /** @format int32 */
  order?: number | null;
  generateNfo?: boolean | null;
}

export interface ResourceCustomPropertyValuePutRequestModel {
  value?: string | null;
}

export interface ResourceDiff {
  /** [0: Category, 1: MediaLibrary, 2: ReleaseDt, 3: Publisher, 4: Name, 5: Language, 6: Volume, 7: Original, 8: Series, 9: Tag, 10: Introduction, 11: Rate, 12: CustomProperty] */
  property?: ResourceDiffProperty;
  currentValue?: any;
  newValue?: any;
  /** [1: Added, 2: Removed, 3: Modified] */
  type?: ResourceDiffType;
  key?: string | null;
  subDiffs?: ResourceDiff[] | null;
}

/**
 * [0: Category, 1: MediaLibrary, 2: ReleaseDt, 3: Publisher, 4: Name, 5: Language, 6: Volume, 7: Original, 8: Series, 9: Tag, 10: Introduction, 11: Rate, 12: CustomProperty]
 * @format int32
 */
export type ResourceDiffProperty = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12;

/**
 * [1: Added, 2: Removed, 3: Modified]
 * @format int32
 */
export type ResourceDiffType = 1 | 2 | 3;

/**
 * [0: NotSet, 1: Chinese, 2: English, 3: Japanese, 4: Korean, 5: French, 6: German, 7: Spanish, 8: Russian]
 * @format int32
 */
export type ResourceLanguage = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8;

export interface ResourceListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Resource[] | null;
}

/**
 * [1: Layer, 2: Regex, 3: FixedText]
 * @format int32
 */
export type ResourceMatcherValueType = 1 | 2 | 3;

export interface ResourceMoveRequestModel {
  ids: number[];
  /** @format int32 */
  mediaLibraryId?: number | null;
  /** @minLength 1 */
  path: string;
}

export interface ResourceOptionsDto {
  /** @format date-time */
  lastSyncDt?: string;
  /** @format date-time */
  lastNfoGenerationDt?: string;
  lastSearchV2?: ResourceSearchDto;
  coverOptions?: CoverOptionsModel;
  additionalCoverDiscoveringSources?: AdditionalCoverDiscoveringSource[] | null;
}

export interface ResourceOptionsDtoSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: ResourceOptionsDto;
}

export interface ResourceOptionsPatchInputModel {
  additionalCoverDiscoveringSources?: AdditionalCoverDiscoveringSource[] | null;
  coverOptions?: CoverOptionsModel;
}

/**
 * [1: RootPath, 2: ParentResource, 3: Resource, 4: ReleaseDt, 5: Publisher, 6: Name, 7: Language, 8: Volume, 9: Original, 10: Series, 11: Tag, 12: Introduction, 13: Rate, 14: CustomProperty, 15: FileName, 16: DirectoryPath, 17: CreatedAt, 18: FileCreatedAt, 19: FileModifiedAt, 20: Category, 21: MediaLibrary, 22: Favorites]
 * @format int32
 */
export type ResourceProperty =
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
  | 18
  | 19
  | 20
  | 21
  | 22;

export interface ResourceSearchDto {
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
  group?: ResourceSearchFilterGroup;
  orders?: ResourceSearchOrderInputModel[] | null;
}

export interface ResourceSearchFilter {
  /** @format int32 */
  propertyId?: number;
  isReservedProperty?: boolean;
  /** [1: Equals, 2: NotEquals, 3: Contains, 4: NotContains, 5: StartsWith, 6: NotStartsWith, 7: EndsWith, 8: NotEndsWith, 9: GreaterThan, 10: LessThan, 11: GreaterThanOrEquals, 12: LessThanOrEquals, 13: IsNull, 14: IsNotNull, 15: In, 16: NotIn, 17: Matches, 18: NotMatches] */
  operation?: SearchOperation;
  value?: string | null;
}

export interface ResourceSearchFilterGroup {
  /** [1: And, 2: Or] */
  combinator?: Combinator;
  groups?: ResourceSearchFilterGroup[] | null;
  filters?: ResourceSearchFilter[] | null;
}

export interface ResourceSearchInputModel {
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
  group?: ResourceSearchFilterGroup;
  orders?: ResourceSearchOrderInputModel[] | null;
  save?: boolean;
}

export interface ResourceSearchOrderInputModel {
  /** [1: FileCreateDt, 2: FileModifyDt, 3: Filename, 5: ReleaseDt, 6: AddDt, 7: Category, 8: MediaLibrary] */
  property?: ResourceSearchSortableProperty;
  asc?: boolean;
}

export interface ResourceSearchResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Resource[] | null;
  /** @format int32 */
  totalCount?: number;
  /** @format int32 */
  pageIndex?: number;
  /** @format int32 */
  pageSize?: number;
}

/**
 * [1: FileCreateDt, 2: FileModifyDt, 3: Filename, 5: ReleaseDt, 6: AddDt, 7: Category, 8: MediaLibrary]
 * @format int32
 */
export type ResourceSearchSortableProperty = 1 | 2 | 3 | 5 | 6 | 7 | 8;

export interface ResourceTagUpdateRequestModel {
  resourceTagIds: Record<string, number[]>;
}

export interface ResourceUpdateRequestModel {
  publishers?: PublisherDto[] | null;
  name?: string | null;
  originals?: string[] | null;
  series?: string | null;
  /** @format date-time */
  releaseDt?: string | null;
  /** [0: NotSet, 1: Chinese, 2: English, 3: Japanese, 4: Korean, 5: French, 6: German, 7: Spanish, 8: Russian] */
  language?: ResourceLanguage;
  /** @format double */
  rate?: number | null;
  tags?: TagDto[] | null;
  introduction?: string | null;
}

export interface RuntimeFieldHandle {
  value?: IntPtr;
}

export interface RuntimeMethodHandle {
  value?: IntPtr;
}

export interface RuntimeTypeHandle {
  value?: IntPtr;
}

/**
 * [1: Equals, 2: NotEquals, 3: Contains, 4: NotContains, 5: StartsWith, 6: NotStartsWith, 7: EndsWith, 8: NotEndsWith, 9: GreaterThan, 10: LessThan, 11: GreaterThanOrEquals, 12: LessThanOrEquals, 13: IsNull, 14: IsNotNull, 15: In, 16: NotIn, 17: Matches, 18: NotMatches]
 * @format int32
 */
export type SearchOperation = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 | 15 | 16 | 17 | 18;

/**
 * [0: None, 1: Level1, 2: Level2]
 * @format int32
 */
export type SecurityRuleSet = 0 | 1 | 2;

export interface SegmentMatchResult {
  value?: string | null;
  properties?: SegmentPropertyResult[] | null;
}

export interface SegmentPropertyResult {
  /** [1: RootPath, 2: ParentResource, 3: Resource, 4: ReleaseDt, 5: Publisher, 6: Name, 7: Language, 8: Volume, 9: Original, 10: Series, 11: Tag, 12: Introduction, 13: Rate, 14: CustomProperty, 15: FileName, 16: DirectoryPath, 17: CreatedAt, 18: FileCreatedAt, 19: FileModifiedAt, 20: Category, 21: MediaLibrary, 22: Favorites] */
  property?: ResourceProperty;
  keys?: string[] | null;
}

export interface SeriesDto {
  /** @format int32 */
  id?: number;
  name?: string | null;
}

export interface SeriesDtoListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: SeriesDto[] | null;
}

export interface SimpleCategoryComponent {
  /** @minLength 1 */
  componentKey: string;
  /** [1: Enhancer, 2: PlayableFileSelector, 3: Player] */
  componentType: ComponentType;
}

export interface SimpleSearchEngineOptions {
  name?: string | null;
  urlTemplate?: string | null;
}

export interface SpecialText {
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
  type?: SpecialTextType;
}

export interface SpecialTextCreateRequestModel {
  /** [1: Useless, 2: Language, 3: Wrapper, 4: Standardization, 6: Volume, 7: Trim, 8: DateTime] */
  type?: SpecialTextType;
  value1?: string | null;
  value2?: string | null;
}

export interface SpecialTextSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: SpecialText;
}

/**
 * [1: Useless, 2: Language, 3: Wrapper, 4: Standardization, 6: Volume, 7: Trim, 8: DateTime]
 * @format int32
 */
export type SpecialTextType = 1 | 2 | 3 | 4 | 6 | 7 | 8;

export interface SpecialTextTypeSpecialTextListDictionarySingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Record<string, SpecialText[] | null>;
}

export interface SpecialTextUpdateRequestModel {
  value1?: string | null;
  value2?: string | null;
}

/**
 * [0: Default, 1: Resource]
 * @format int32
 */
export type StartupPage = 0 | 1;

export interface StringCustomResourcePropertyListDictionarySingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Record<string, CustomResourceProperty[] | null>;
}

export interface StringInt32DictionarySingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Record<string, number | null>;
}

export interface StringListListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: string[][] | null;
}

export interface StringListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: string[] | null;
}

export interface StringMediaTypeDictionarySingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Record<string, MediaType>;
}

export interface StringSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: string | null;
}

export interface StringStringArrayDictionarySingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: Record<string, string[] | null>;
}

export interface StructLayoutAttribute {
  typeId?: any;
  /** [0: Sequential, 2: Explicit, 3: Auto] */
  value?: LayoutKind;
}

export interface SubdirectoriesExtractRequestModel {
  /** @minLength 1 */
  path: string;
}

/**
 * [0: None, 1: GroupName, 2: PreferredAlias]
 * @format int32
 */
export type TagAdditionalItem = 0 | 1 | 2;

export interface TagDto {
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

export interface TagDtoListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: TagDto[] | null;
}

export interface TagGroupAddRequestModel {
  names: string[];
}

/**
 * [1: Tags, 2: PreferredAlias, 4: TagNamePreferredAlias]
 * @format int32
 */
export type TagGroupAdditionalItem = 1 | 2 | 4;

export interface TagGroupDto {
  /** @format int32 */
  id?: number;
  name?: string | null;
  /** @format int32 */
  order?: number;
  tags?: TagDto[] | null;
  preferredAlias?: string | null;
}

export interface TagGroupDtoListResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: TagGroupDto[] | null;
}

export interface TagGroupUpdateRequestModel {
  name?: string | null;
  /** @format int32 */
  order?: number | null;
}

export interface TagMoveRequestModel {
  /** @format int32 */
  targetTagId?: number | null;
  /** @format int32 */
  targetGroupId?: number | null;
}

export interface TagNameUpdateRequestModel {
  /** @minLength 1 */
  name: string;
}

export interface TagUpdateRequestModel {
  color?: string | null;
  /** @format int32 */
  groupId?: number | null;
  /** @format int32 */
  order?: number | null;
}

export interface Target {
  path?: string | null;
  sources?: string[] | null;
}

export interface TextAndCount {
  label?: string | null;
  name?: string | null;
  /** @format int32 */
  count?: number;
}

/**
 * [1: Bilibili, 2: ExHentai, 3: Pixiv]
 * @format int32
 */
export type ThirdPartyId = 1 | 2 | 3;

export interface ThirdPartyOptions {
  simpleSearchEngines?: SimpleSearchEngineOptions[] | null;
}

export interface ThirdPartyOptionsSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: ThirdPartyOptions;
}

export interface ThirdPartyRequestCount {
  /** [1: Bilibili, 2: ExHentai, 3: Pixiv] */
  id?: ThirdPartyId;
  /** @format int32 */
  resultType?: number;
  /** @format int32 */
  taskCount?: number;
}

export interface ThirdPartyRequestStatistics {
  /** [1: Bilibili, 2: ExHentai, 3: Pixiv] */
  id?: ThirdPartyId;
  counts?: Record<string, number>;
}

export interface ThirdPartyRequestStatisticsArraySingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: ThirdPartyRequestStatistics[] | null;
}

export interface TimeSpan {
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

export interface Type {
  name?: string | null;
  customAttributes?: CustomAttributeData[] | null;
  isCollectible?: boolean;
  /** @format int32 */
  metadataToken?: number;
  isInterface?: boolean;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType?: MemberTypes;
  namespace?: string | null;
  assemblyQualifiedName?: string | null;
  fullName?: string | null;
  assembly?: Assembly;
  module?: Module;
  isNested?: boolean;
  declaringType?: Type;
  declaringMethod?: MethodBase;
  reflectedType?: Type;
  underlyingSystemType?: Type;
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
  genericTypeArguments?: Type[] | null;
  /** @format int32 */
  genericParameterPosition?: number;
  /** [0: None, 1: Covariant, 2: Contravariant, 3: VarianceMask, 4: ReferenceTypeConstraint, 8: NotNullableValueTypeConstraint, 16: DefaultConstructorConstraint, 28: SpecialConstraintMask] */
  genericParameterAttributes?: GenericParameterAttributes;
  /** [0: NotPublic, 0: NotPublic, 0: NotPublic, 0: NotPublic, 1: Public, 2: NestedPublic, 3: NestedPrivate, 4: NestedFamily, 5: NestedAssembly, 6: NestedFamANDAssem, 7: VisibilityMask, 7: VisibilityMask, 8: SequentialLayout, 16: ExplicitLayout, 24: LayoutMask, 32: ClassSemanticsMask, 32: ClassSemanticsMask, 128: Abstract, 256: Sealed, 1024: SpecialName, 2048: RTSpecialName, 4096: Import, 8192: Serializable, 16384: WindowsRuntime, 65536: UnicodeClass, 131072: AutoClass, 196608: StringFormatMask, 196608: StringFormatMask, 262144: HasSecurity, 264192: ReservedMask, 1048576: BeforeFieldInit, 12582912: CustomFormatMask] */
  attributes?: TypeAttributes;
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
  structLayoutAttribute?: StructLayoutAttribute;
  typeInitializer?: ConstructorInfo;
  typeHandle?: RuntimeTypeHandle;
  /** @format uuid */
  guid?: string;
  baseType?: Type;
  isSerializable?: boolean;
  containsGenericParameters?: boolean;
  isVisible?: boolean;
}

/**
 * [0: NotPublic, 0: NotPublic, 0: NotPublic, 0: NotPublic, 1: Public, 2: NestedPublic, 3: NestedPrivate, 4: NestedFamily, 5: NestedAssembly, 6: NestedFamANDAssem, 7: VisibilityMask, 7: VisibilityMask, 8: SequentialLayout, 16: ExplicitLayout, 24: LayoutMask, 32: ClassSemanticsMask, 32: ClassSemanticsMask, 128: Abstract, 256: Sealed, 1024: SpecialName, 2048: RTSpecialName, 4096: Import, 8192: Serializable, 16384: WindowsRuntime, 65536: UnicodeClass, 131072: AutoClass, 196608: StringFormatMask, 196608: StringFormatMask, 262144: HasSecurity, 264192: ReservedMask, 1048576: BeforeFieldInit, 12582912: CustomFormatMask]
 * @format int32
 */
export type TypeAttributes =
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

export interface TypeInfo {
  name?: string | null;
  customAttributes?: CustomAttributeData[] | null;
  isCollectible?: boolean;
  /** @format int32 */
  metadataToken?: number;
  isInterface?: boolean;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType?: MemberTypes;
  namespace?: string | null;
  assemblyQualifiedName?: string | null;
  fullName?: string | null;
  assembly?: Assembly;
  module?: Module;
  isNested?: boolean;
  declaringType?: Type;
  declaringMethod?: MethodBase;
  reflectedType?: Type;
  underlyingSystemType?: Type;
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
  genericTypeArguments?: Type[] | null;
  /** @format int32 */
  genericParameterPosition?: number;
  /** [0: None, 1: Covariant, 2: Contravariant, 3: VarianceMask, 4: ReferenceTypeConstraint, 8: NotNullableValueTypeConstraint, 16: DefaultConstructorConstraint, 28: SpecialConstraintMask] */
  genericParameterAttributes?: GenericParameterAttributes;
  /** [0: NotPublic, 0: NotPublic, 0: NotPublic, 0: NotPublic, 1: Public, 2: NestedPublic, 3: NestedPrivate, 4: NestedFamily, 5: NestedAssembly, 6: NestedFamANDAssem, 7: VisibilityMask, 7: VisibilityMask, 8: SequentialLayout, 16: ExplicitLayout, 24: LayoutMask, 32: ClassSemanticsMask, 32: ClassSemanticsMask, 128: Abstract, 256: Sealed, 1024: SpecialName, 2048: RTSpecialName, 4096: Import, 8192: Serializable, 16384: WindowsRuntime, 65536: UnicodeClass, 131072: AutoClass, 196608: StringFormatMask, 196608: StringFormatMask, 262144: HasSecurity, 264192: ReservedMask, 1048576: BeforeFieldInit, 12582912: CustomFormatMask] */
  attributes?: TypeAttributes;
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
  structLayoutAttribute?: StructLayoutAttribute;
  typeInitializer?: ConstructorInfo;
  typeHandle?: RuntimeTypeHandle;
  /** @format uuid */
  guid?: string;
  baseType?: Type;
  isSerializable?: boolean;
  containsGenericParameters?: boolean;
  isVisible?: boolean;
  genericTypeParameters?: Type[] | null;
  declaredConstructors?: ConstructorInfo[] | null;
  declaredEvents?: EventInfo[] | null;
  declaredFields?: FieldInfo[] | null;
  declaredMembers?: MemberInfo[] | null;
  declaredMethods?: MethodInfo[] | null;
  declaredNestedTypes?: TypeInfo[] | null;
  declaredProperties?: PropertyInfo[] | null;
  implementedInterfaces?: Type[] | null;
}

export interface UIOptions {
  resource?: UIResourceOptions;
  /** [0: Default, 1: Resource] */
  startupPage?: StartupPage;
}

export interface UIOptionsPatchRequestModel {
  resource?: UIResourceOptions;
  /** [0: Default, 1: Resource] */
  startupPage?: StartupPage;
}

export interface UIOptionsSingletonResponse {
  /** @format int32 */
  code?: number;
  message?: string | null;
  data?: UIOptions;
}

export interface UIResourceOptions {
  /** @format int32 */
  colCount?: number;
  showBiggerCoverWhileHover?: boolean;
  disableMediaPreviewer?: boolean;
  disableCache?: boolean;
}

/**
 * [0: FollowSystem, 1: Light, 2: Dark]
 * @format int32
 */
export type UiTheme = 0 | 1 | 2;

export interface VolumeDto {
  /** @format int32 */
  index?: number;
  name?: string | null;
  title?: string | null;
  /** @format int32 */
  serialId?: number;
  /** @format int32 */
  resourceId?: number;
}

export interface WeekCount {
  /** @format int32 */
  offset?: number;
  /** @format int32 */
  count?: number;
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
      this.request<AliasSingletonResponse, any>({
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
    updateAlias: (id: number, data: AliasUpdateRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
        additionalItems?: AliasAdditionalItem;
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
      this.request<AliasDtoSearchResponse, any>({
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
    createAlias: (data: AliasCreateRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
    mergeAliasGroup: (id: number, data: AliasGroupUpdateRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<InitializationContentTypeSingletonResponse, any>({
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
      this.request<AppInfoSingletonResponse, any>({
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
      this.request<BaseResponse, any>({
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
    moveCoreData: (data: CoreDataMoveRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<BackgroundTaskDtoListResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<BackgroundTaskDtoSingletonResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<FavoritesListResponse, any>({
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
      this.request<BulkModificationDtoSingletonResponse, any>({
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
    putBulkModification: (id: number, data: BulkModificationPutRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
     * @name DeleteBulkModification
     * @request DELETE:/bulk-modification/{id}
     */
    deleteBulkModification: (id: number, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<BulkModificationDtoListResponse, any>({
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
    createBulkModification: (data: BulkModificationPutRequestModel, params: RequestParams = {}) =>
      this.request<BulkModificationDtoSingletonResponse, any>({
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
     * @name DuplicateBulkModification
     * @request POST:/bulk-modification/{id}/duplication
     */
    duplicateBulkModification: (id: number, params: RequestParams = {}) =>
      this.request<BulkModificationDtoSingletonResponse, any>({
        path: `/bulk-modification/${id}/duplication`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BulkModification
     * @name CloseBulkModification
     * @request PUT:/bulk-modification/{id}/close
     */
    closeBulkModification: (id: number, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
        path: `/bulk-modification/${id}/close`,
        method: "PUT",
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
      this.request<Int32ListResponse, any>({
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
      this.request<ResourceListResponse, any>({
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
      this.request<BulkModificationResourceDiffsListResponse, any>({
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
      this.request<BaseResponse, any>({
        path: `/bulk-modification/${id}/diffs`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BulkModification
     * @name ApplyBulkModification
     * @request POST:/bulk-modification/{id}/apply
     */
    applyBulkModification: (id: number, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
        path: `/bulk-modification/${id}/apply`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BulkModification
     * @name RevertBulkModification
     * @request POST:/bulk-modification/{id}/revert
     */
    revertBulkModification: (id: number, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
        path: `/bulk-modification/${id}/revert`,
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
        type?: ComponentType;
        /** [0: None, 1: AssociatedCategories] */
        additionalItems?: ComponentDescriptorAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<ComponentDescriptorListResponse, any>({
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
        additionalItems?: ComponentDescriptorAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<ComponentDescriptorSingletonResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<DependentComponentVersionSingletonResponse, any>({
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
      this.request<BaseResponse, any>({
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
    addComponentOptions: (data: ComponentOptionsAddRequestModel, params: RequestParams = {}) =>
      this.request<ComponentOptionsSingletonResponse, any>({
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
    putComponentOptions: (id: number, data: ComponentOptionsAddRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<StringMediaTypeDictionarySingletonResponse, any>({
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
  customProperty = {
    /**
     * No description
     *
     * @tags CustomProperty
     * @name GetAllCustomPropertiesV2
     * @request GET:/custom-property
     */
    getAllCustomPropertiesV2: (
      query?: {
        /** [0: None, 1: Category] */
        additionalItems?: CustomPropertyAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<CustomPropertyListResponse, any>({
        path: `/custom-property`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags CustomProperty
     * @name AddCustomProperty
     * @request POST:/custom-property
     */
    addCustomProperty: (data: CustomPropertyAddOrPutDto, params: RequestParams = {}) =>
      this.request<CustomPropertySingletonResponse, any>({
        path: `/custom-property`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags CustomProperty
     * @name PutCustomProperty
     * @request PUT:/custom-property/{id}
     */
    putCustomProperty: (id: number, data: CustomPropertyAddOrPutDto, params: RequestParams = {}) =>
      this.request<CustomPropertySingletonResponse, any>({
        path: `/custom-property/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags CustomProperty
     * @name RemoveCustomProperty
     * @request DELETE:/custom-property/{id}
     */
    removeCustomProperty: (id: number, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
        path: `/custom-property/${id}`,
        method: "DELETE",
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
      this.request<DashboardStatisticsSingletonResponse, any>({
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
      this.request<Int32DownloaderNamingDefinitionsDictionarySingletonResponse, any>({
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
      this.request<DownloadTaskDtoListResponse, any>({
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
    createDownloadTask: (data: DownloadTaskCreateRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<DownloadTaskDtoSingletonResponse, any>({
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
      this.request<BaseResponse, any>({
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
    putDownloadTask: (id: number, data: DownloadTask, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
    startDownloadTasks: (data: DownloadTaskStartRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<EnhancementRecordDtoListResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
        path: `/resource/${id}/enhancement`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name SearchResourcesV2
     * @request POST:/resource/search/v2
     */
    searchResourcesV2: (data: ResourceSearchInputModel, params: RequestParams = {}) =>
      this.request<ResourceSearchResponse, any>({
        path: `/resource/search/v2`,
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
      this.request<ResourceListResponse, any>({
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
    patchResource: (id: number, data: ResourceUpdateRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
     * @name UpdateResourceTags
     * @request PUT:/resource/tag
     */
    updateResourceTags: (data: ResourceTagUpdateRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
    saveCover: (id: number, data: CoverSaveRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<StringListResponse, any>({
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
      this.request<StringListResponse, any>({
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
      this.request<StringStringArrayDictionarySingletonResponse, any>({
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
      this.request<StringStringArrayDictionarySingletonResponse, any>({
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
      this.request<BaseResponse, any>({
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
    moveResources: (data: ResourceMoveRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<Int32Int32ArrayDictionarySingletonResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<PreviewerItemListResponse, any>({
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
      this.request<OriginalDtoListResponse, any>({
        path: `/resource/original/all`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name GetAllSeries
     * @request GET:/resource/series/all
     */
    getAllSeries: (params: RequestParams = {}) =>
      this.request<SeriesDtoListResponse, any>({
        path: `/resource/series/all`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name GetAllCustomProperties
     * @request GET:/resource/custom-property/all
     */
    getAllCustomProperties: (params: RequestParams = {}) =>
      this.request<StringCustomResourcePropertyListDictionarySingletonResponse, any>({
        path: `/resource/custom-property/all`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name PutResourceCustomPropertyValue
     * @request PUT:/resource/{id}/custom-property/{pId}/value
     */
    putResourceCustomPropertyValue: (
      id: number,
      pId: number,
      data: ResourceCustomPropertyValuePutRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BaseResponse, any>({
        path: `/resource/${id}/custom-property/${pId}/value`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
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
      this.request<BaseResponse, any>({
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
    getAllMediaLibraries: (
      query?: {
        /** [0: None, 1: Category, 2: FileSystemInfo, 4: FixedTags] */
        additionalItems?: MediaLibraryAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<MediaLibraryListResponse, any>({
        path: `/media-library`,
        method: "GET",
        query: query,
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
    addMediaLibrary: (data: MediaLibraryCreateDto, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
    patchMediaLibrary: (id: number, data: MediaLibraryPatchDto, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<BackgroundTaskDtoSingletonResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
    validatePathConfiguration: (data: PathConfiguration, params: RequestParams = {}) =>
      this.request<PathConfigurationValidateResultSingletonResponse, any>({
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
    sortMediaLibrariesInCategory: (data: IdBasedSortRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      data: MediaLibraryPathConfigurationCreateRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BaseResponse, any>({
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
      data: PathConfigurationRemoveRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BaseResponse, any>({
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
     * @name AddMediaLibrariesInBulk
     * @request POST:/media-library/bulk-add/{cId}
     */
    addMediaLibrariesInBulk: (cId: number, data: MediaLibraryAddInBulkRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
        path: `/media-library/bulk-add/${cId}`,
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
     * @name AddMediaLibraryRootPathsInBulk
     * @request POST:/media-library/{mlId}/path-configuration/root-paths
     */
    addMediaLibraryRootPathsInBulk: (
      mlId: number,
      data: MediaLibraryRootPathsAddInBulkRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BaseResponse, any>({
        path: `/media-library/${mlId}/path-configuration/root-paths`,
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
      this.request<MediaLibraryListResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<EnhancementRecordDtoSearchResponse, any>({
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
      this.request<FavoritesDtoListResponse, any>({
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
    addFavorites: (data: FavoritesAddOrUpdateRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
    putFavorites: (id: number, data: FavoritesAddOrUpdateRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
      data: FavoritesResourceMappingAddOrRemoveRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BaseResponse, any>({
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
      data: FavoritesResourceMappingAddOrRemoveRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<IwFsTaskInfoSingletonResponse, any>({
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
      this.request<IwFsEntryLazyInfoSingletonResponse, any>({
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
      this.request<IwFsEntrySingletonResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<IwFsPreviewSingletonResponse, any>({
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
    removeFiles: (data: FileRemoveRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
    renameFile: (data: FileRenameRequestModel, params: RequestParams = {}) =>
      this.request<StringSingletonResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
    moveEntries: (data: FileMoveRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      data: RemoveSameEntryInWorkingDirectoryRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<StringListResponse, any>({
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
      data: RemoveSameEntryInWorkingDirectoryRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<StringListResponse, any>({
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
      this.request<BaseResponse, any>({
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
    decompressFiles: (data: FileDecompressRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<StringSingletonResponse, any>({
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
      this.request<StringListResponse, any>({
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
      this.request<CompressedFileEntryListResponse, any>({
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
      this.request<StringInt32DictionarySingletonResponse, any>({
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
      this.request<FileEntriesMergeResultSingletonResponse, any>({
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
      this.request<FileEntriesMergeResultSingletonResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<StringListResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<StringListResponse, any>({
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
      this.request<StringSingletonResponse, any>({
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
      this.request<StringSingletonResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<LogListResponse, any>({
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
      this.request<BaseResponse, any>({
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
        level?: LogLevel;
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
      this.request<LogSearchResponse, any>({
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
      this.request<Int32SingletonResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<AppOptionsSingletonResponse, any>({
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
    patchAppOptions: (data: AppOptionsPatchRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<UIOptionsSingletonResponse, any>({
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
    patchUiOptions: (data: UIOptionsPatchRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<BilibiliOptionsSingletonResponse, any>({
        path: `/options/bilibili`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name PatchBilibiliOptions
     * @request PATCH:/options/bilibili
     */
    patchBilibiliOptions: (data: BilibiliOptions, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<ExHentaiOptionsSingletonResponse, any>({
        path: `/options/exhentai`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name PatchExHentaiOptions
     * @request PATCH:/options/exhentai
     */
    patchExHentaiOptions: (data: ExHentaiOptions, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<FileSystemOptionsSingletonResponse, any>({
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
    patchFileSystemOptions: (data: FileSystemOptions, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<JavLibraryOptionsSingletonResponse, any>({
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
    patchJavLibraryOptions: (data: JavLibraryOptions, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<PixivOptionsSingletonResponse, any>({
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
    patchPixivOptions: (data: PixivOptions, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<ResourceOptionsDtoSingletonResponse, any>({
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
    patchResourceOptions: (data: ResourceOptionsPatchInputModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<ThirdPartyOptionsSingletonResponse, any>({
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
    patchThirdPartyOptions: (data: ThirdPartyOptions, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<NetworkOptionsSingletonResponse, any>({
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
    patchNetworkOptions: (data: NetworkOptions, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
        order?: PasswordSearchOrder;
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
      this.request<PasswordSearchResponse, any>({
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
      this.request<PasswordListResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<PlaylistDtoSingletonResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<PlaylistDtoListResponse, any>({
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
    addPlaylist: (data: PlaylistDto, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
    putPlaylist: (data: PlaylistDto, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<StringListListResponse, any>({
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
    updatePublisher: (id: number, data: PublisherUpdateModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<PublisherDtoListResponse, any>({
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
        /** [0: None, 1: Components, 3: Validation, 4: CustomProperties] */
        additionalItems?: ResourceCategoryAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<CategoryListResponse, any>({
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
    addResourceCategory: (data: ResourceCategoryAddRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
     * @name DuplicateResourceCategory
     * @request POST:/resource-category/{id}/duplication
     */
    duplicateResourceCategory: (id: number, data: ResourceCategoryDuplicateRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
        path: `/resource-category/${id}/duplication`,
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
    updateResourceCategory: (id: number, data: ResourceCategoryUpdateRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
     * @name DeleteResourceCategoryAndClearAllRelatedData
     * @request DELETE:/resource-category/{id}
     */
    deleteResourceCategoryAndClearAllRelatedData: (id: number, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      data: ResourceCategoryComponentConfigureRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BaseResponse, any>({
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
    sortCategories: (data: IdBasedSortRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
    saveDataFromSetupWizard: (data: CategorySetupWizardInputModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
        path: `/resource-category/setup-wizard`,
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
     * @name BindCustomPropertiesToCategory
     * @request PUT:/resource-category/{id}/custom-properties
     */
    bindCustomPropertiesToCategory: (
      id: number,
      data: ResourceCategoryCustomPropertyBindRequestModel,
      params: RequestParams = {},
    ) =>
      this.request<BaseResponse, any>({
        path: `/resource-category/${id}/custom-properties`,
        method: "PUT",
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
      this.request<SpecialTextTypeSpecialTextListDictionarySingletonResponse, any>({
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
    createSpecialText: (data: SpecialTextCreateRequestModel, params: RequestParams = {}) =>
      this.request<SpecialTextSingletonResponse, any>({
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
      this.request<BaseResponse, any>({
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
    updateSpecialText: (id: number, data: SpecialTextUpdateRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<StringSingletonResponse, any>({
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
        additionalItems?: TagAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<TagDtoListResponse, any>({
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
      this.request<BaseResponse, any>({
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
        additionalItems?: TagAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<TagDtoListResponse, any>({
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
    updateTagName: (id: number, data: TagNameUpdateRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
    updateTag: (id: number, data: TagUpdateRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
    moveTag: (id: number, data: TagMoveRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
        additionalItems?: TagGroupAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<TagGroupDtoListResponse, any>({
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
    addTagGroups: (data: TagGroupAddRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
    updateTagGroup: (id: number, data: TagGroupUpdateRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
    sortTagGroups: (data: IdBasedSortRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<ThirdPartyRequestStatisticsArraySingletonResponse, any>({
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
    extraSubdirectories: (data: SubdirectoriesExtractRequestModel, params: RequestParams = {}) =>
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<EverythingExtractionStatusSingletonResponse, any>({
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
      this.request<BaseResponse, any>({
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
        target?: CookieValidatorTarget;
        cookie?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BaseResponse, any>({
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
      this.request<AppVersionInfoSingletonResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
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
      this.request<BaseResponse, any>({
        path: `/updater/app/restart`,
        method: "POST",
        format: "json",
        ...params,
      }),
  };
}
