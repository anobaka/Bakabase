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

export interface BakabaseAbstractionsComponentsConfigurationTaskOptions {
  tasks?: BakabaseAbstractionsModelsDbBTaskDbModel[];
}

export interface BakabaseAbstractionsModelsDbBTaskDbModel {
  id: string;
  /** @format date-span */
  interval: string;
  /** @format date-time */
  enableAfter?: string;
}

export interface BakabaseAbstractionsModelsDbCategoryComponent {
  /** @format int32 */
  id: number;
  /** @format int32 */
  categoryId: number;
  /** @minLength 1 */
  componentKey: string;
  /** [1: Enhancer, 2: PlayableFileSelector, 3: Player] */
  componentType: BakabaseInsideWorldModelsConstantsComponentType;
  descriptor: BakabaseAbstractionsModelsDomainComponentDescriptor;
}

export interface BakabaseAbstractionsModelsDbPlayHistoryDbModel {
  /** @format int32 */
  id: number;
  /** @format int32 */
  resourceId: number;
  item?: string;
  /** @format date-time */
  playedAt: string;
}

export interface BakabaseAbstractionsModelsDomainCategory {
  /** @format int32 */
  id: number;
  name: string;
  color?: string;
  /** @format date-time */
  createDt: string;
  isValid: boolean;
  /** @deprecated */
  message?: string;
  /** @format int32 */
  order: number;
  componentsData?: BakabaseAbstractionsModelsDbCategoryComponent[];
  /** [1: FilenameAscending, 2: FileModifyDtDescending] */
  coverSelectionOrder: BakabaseInsideWorldModelsConstantsCoverSelectOrder;
  enhancementOptions?: BakabaseInsideWorldModelsModelsDtosResourceCategoryEnhancementOptions;
  generateNfo: boolean;
  resourceDisplayNameTemplate?: string;
  customProperties?: BakabaseAbstractionsModelsDomainCustomProperty[];
  enhancerOptions?: BakabaseAbstractionsModelsDomainCategoryEnhancerOptions[];
}

export interface BakabaseAbstractionsModelsDomainCategoryEnhancerOptions {
  /** @format int32 */
  id: number;
  /** @format int32 */
  categoryId: number;
  /** @format int32 */
  enhancerId: number;
  active: boolean;
}

export interface BakabaseAbstractionsModelsDomainComponentDescriptor {
  /** [0: Invalid, 1: Fixed, 2: Configurable, 3: Instance] */
  type: BakabaseInsideWorldModelsConstantsComponentDescriptorType;
  /** [1: Enhancer, 2: PlayableFileSelector, 3: Player] */
  componentType: BakabaseInsideWorldModelsConstantsComponentType;
  assemblyQualifiedTypeName: string;
  name: string;
  description?: string;
  message?: string;
  optionsJson?: string;
  /** @format int32 */
  optionsId?: number;
  version: string;
  dataVersion: string;
  optionsType?: SystemType;
  optionsJsonSchema?: string;
  id?: string;
  canBeInstantiated: boolean;
  associatedCategories?: BakabaseAbstractionsModelsDomainCategory[];
}

/**
 * [1: ContextCreated, 2: ContextApplied]
 * @format int32
 */
export type BakabaseAbstractionsModelsDomainConstantsEnhancementRecordStatus = 1 | 2;

/**
 * [1: NotAcceptTerms, 2: NeedRestart]
 * @format int32
 */
export type BakabaseAbstractionsModelsDomainConstantsInitializationContentType = 1 | 2;

/**
 * [0: None, 1: Template]
 * @format int32
 */
export type BakabaseAbstractionsModelsDomainConstantsMediaLibraryV2AdditionalItem = 0 | 1;

/**
 * [1: File, 2: Directory]
 * @format int32
 */
export type BakabaseAbstractionsModelsDomainConstantsPathFilterFsType = 1 | 2;

/**
 * [1: Layer, 2: Regex]
 * @format int32
 */
export type BakabaseAbstractionsModelsDomainConstantsPathPositioner = 1 | 2;

/**
 * [1: Internal, 2: Reserved, 4: Custom, 7: All]
 * @format int32
 */
export type BakabaseAbstractionsModelsDomainConstantsPropertyPool = 1 | 2 | 4 | 7;

/**
 * [1: SingleLineText, 2: MultilineText, 3: SingleChoice, 4: MultipleChoice, 5: Number, 6: Percentage, 7: Rating, 8: Boolean, 9: Link, 10: Attachment, 11: Date, 12: DateTime, 13: Time, 14: Formula, 15: Multilevel, 16: Tags]
 * @format int32
 */
export type BakabaseAbstractionsModelsDomainConstantsPropertyType =
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
  | 16;

/**
 * [0: Manual, 1: Synchronization, 1000: BakabaseEnhancer, 1001: ExHentaiEnhancer, 1002: BangumiEnhancer, 1003: DLsiteEnhancer, 1004: RegexEnhancer]
 * @format int32
 */
export type BakabaseAbstractionsModelsDomainConstantsPropertyValueScope = 0 | 1 | 1000 | 1001 | 1002 | 1003 | 1004;

/**
 * [12: Introduction, 13: Rating, 22: Cover]
 * @format int32
 */
export type BakabaseAbstractionsModelsDomainConstantsReservedProperty = 12 | 13 | 22;

/**
 * [1: Covers, 2: PlayableFiles]
 * @format int32
 */
export type BakabaseAbstractionsModelsDomainConstantsResourceCacheType = 1 | 2;

/**
 * [1: IsParent, 2: Pinned, 4: PathDoesNotExist, 8: UnknownMediaLibrary]
 * @format int32
 */
export type BakabaseAbstractionsModelsDomainConstantsResourceTag = 1 | 2 | 4 | 8;

/**
 * [1: And, 2: Or]
 * @format int32
 */
export type BakabaseAbstractionsModelsDomainConstantsSearchCombinator = 1 | 2;

/**
 * [1: Equals, 2: NotEquals, 3: Contains, 4: NotContains, 5: StartsWith, 6: NotStartsWith, 7: EndsWith, 8: NotEndsWith, 9: GreaterThan, 10: LessThan, 11: GreaterThanOrEquals, 12: LessThanOrEquals, 13: IsNull, 14: IsNotNull, 15: In, 16: NotIn, 17: Matches, 18: NotMatches]
 * @format int32
 */
export type BakabaseAbstractionsModelsDomainConstantsSearchOperation =
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
 * [1: Useless, 3: Wrapper, 4: Standardization, 6: Volume, 7: Trim, 8: DateTime, 9: Language]
 * @format int32
 */
export type BakabaseAbstractionsModelsDomainConstantsSpecialTextType = 1 | 3 | 4 | 6 | 7 | 8 | 9;

/**
 * [1: String, 2: ListString, 3: Decimal, 4: Link, 5: Boolean, 6: DateTime, 7: Time, 8: ListListString, 9: ListTag]
 * @format int32
 */
export type BakabaseAbstractionsModelsDomainConstantsStandardValueType = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9;

export interface BakabaseAbstractionsModelsDomainCustomProperty {
  /** @format int32 */
  id: number;
  name: string;
  /** [1: SingleLineText, 2: MultilineText, 3: SingleChoice, 4: MultipleChoice, 5: Number, 6: Percentage, 7: Rating, 8: Boolean, 9: Link, 10: Attachment, 11: Date, 12: DateTime, 13: Time, 14: Formula, 15: Multilevel, 16: Tags] */
  type: BakabaseAbstractionsModelsDomainConstantsPropertyType;
  /** @format date-time */
  createdAt: string;
  categories?: BakabaseAbstractionsModelsDomainCategory[];
  options?: any;
  /** @format int32 */
  valueCount?: number;
  /** @format int32 */
  order: number;
}

export interface BakabaseAbstractionsModelsDomainCustomPropertyValue {
  /** @format int32 */
  id: number;
  /** @format int32 */
  propertyId: number;
  /** @format int32 */
  resourceId: number;
  property?: BakabaseAbstractionsModelsDomainCustomProperty;
  value?: any;
  /** @format int32 */
  scope: number;
  bizKey: string;
  bizValue?: any;
}

export interface BakabaseAbstractionsModelsDomainExtensionGroup {
  /** @format int32 */
  id: number;
  name: string;
  /** @uniqueItems true */
  extensions?: string[];
}

export interface BakabaseAbstractionsModelsDomainMediaLibrary {
  /** @format int32 */
  id: number;
  /** @minLength 1 */
  name: string;
  /** @format int32 */
  categoryId: number;
  /** @format int32 */
  order: number;
  /** @format int32 */
  resourceCount: number;
  fileSystemInformation?: Record<string, BakabaseInsideWorldModelsModelsAosMediaLibraryFileSystemInformation>;
  category?: BakabaseAbstractionsModelsDomainCategory;
  pathConfigurations?: BakabaseAbstractionsModelsDomainPathConfiguration[];
}

export interface BakabaseAbstractionsModelsDomainMediaLibraryTemplate {
  /** @format int32 */
  id: number;
  name: string;
  author?: string;
  description?: string;
  /** @format date-time */
  createdAt: string;
  /** @format date-time */
  updatedAt: string;
  resourceFilters?: BakabaseAbstractionsModelsDomainPathFilter[];
  properties?: BakabaseAbstractionsModelsDomainMediaLibraryTemplateProperty[];
  playableFileLocator?: BakabaseAbstractionsModelsDomainMediaLibraryTemplatePlayableFileLocator;
  enhancers?: BakabaseAbstractionsModelsDomainMediaLibraryTemplateEnhancerOptions[];
  displayNameTemplate?: string;
  samplePaths?: string[];
  child?: BakabaseAbstractionsModelsDomainMediaLibraryTemplate;
}

export interface BakabaseAbstractionsModelsDomainMediaLibraryTemplateEnhancerOptions {
  /** @format int32 */
  enhancerId: number;
  targetOptions?: BakabaseAbstractionsModelsDomainMediaLibraryTemplateEnhancerTargetAllInOneOptions[];
}

export interface BakabaseAbstractionsModelsDomainMediaLibraryTemplateEnhancerTargetAllInOneOptions {
  /** @format int32 */
  target: number;
  dynamicTarget?: string;
  /** [1: FilenameAscending, 2: FileModifyDtDescending] */
  coverSelectOrder?: BakabaseInsideWorldModelsConstantsCoverSelectOrder;
  /** [1: Internal, 2: Reserved, 4: Custom, 7: All] */
  propertyPool: BakabaseAbstractionsModelsDomainConstantsPropertyPool;
  /** @format int32 */
  propertyId: number;
  property?: BakabaseAbstractionsModelsDomainProperty;
}

export interface BakabaseAbstractionsModelsDomainMediaLibraryTemplatePlayableFileLocator {
  /** @uniqueItems true */
  extensionGroupIds?: number[];
  extensionGroups?: BakabaseAbstractionsModelsDomainExtensionGroup[];
  /** @uniqueItems true */
  extensions?: string[];
}

export interface BakabaseAbstractionsModelsDomainMediaLibraryTemplateProperty {
  /** [1: Internal, 2: Reserved, 4: Custom, 7: All] */
  pool: BakabaseAbstractionsModelsDomainConstantsPropertyPool;
  /** @format int32 */
  id: number;
  property?: BakabaseAbstractionsModelsDomainProperty;
  valueLocators?: BakabaseAbstractionsModelsDomainPathLocator[];
}

export interface BakabaseAbstractionsModelsDomainMediaLibraryV2 {
  /** @format int32 */
  id: number;
  name: string;
  path: string;
  /** @format int32 */
  templateId?: number;
  /** @format int32 */
  resourceCount: number;
  template?: BakabaseAbstractionsModelsDomainMediaLibraryTemplate;
}

export interface BakabaseAbstractionsModelsDomainPathConfiguration {
  path?: string;
  rpmValues?: BakabaseAbstractionsModelsDomainPropertyPathSegmentMatcherValue[];
}

export interface BakabaseAbstractionsModelsDomainPathConfigurationTestResult {
  rootPath: string;
  resources: BakabaseAbstractionsModelsDomainPathConfigurationTestResultResource[];
  customPropertyMap: Record<string, BakabaseAbstractionsModelsDomainCustomProperty>;
}

export interface BakabaseAbstractionsModelsDomainPathConfigurationTestResultResource {
  isDirectory: boolean;
  relativePath: string;
  segmentAndMatchedValues: BakabaseAbstractionsModelsDomainPathConfigurationTestResultResourceSegmentMatchResult[];
  globalMatchedValues: BakabaseAbstractionsModelsDomainPathConfigurationTestResultResourceGlobalMatchedValue[];
  customPropertyIdValueMap: Record<string, any>;
}

export interface BakabaseAbstractionsModelsDomainPathConfigurationTestResultResourceGlobalMatchedValue {
  propertyKey: BakabaseAbstractionsModelsDomainPathConfigurationTestResultResourceSegmentPropertyKey;
  /** @uniqueItems true */
  textValues: string[];
}

export interface BakabaseAbstractionsModelsDomainPathConfigurationTestResultResourceSegmentMatchResult {
  segmentText: string;
  propertyKeys: BakabaseAbstractionsModelsDomainPathConfigurationTestResultResourceSegmentPropertyKey[];
}

export interface BakabaseAbstractionsModelsDomainPathConfigurationTestResultResourceSegmentPropertyKey {
  /** @format int32 */
  id: number;
  isCustom: boolean;
}

export interface BakabaseAbstractionsModelsDomainPathFilter {
  /** [1: Layer, 2: Regex] */
  positioner: BakabaseAbstractionsModelsDomainConstantsPathPositioner;
  /** @format int32 */
  layer?: number;
  regex?: string;
  /** [1: File, 2: Directory] */
  fsType?: BakabaseAbstractionsModelsDomainConstantsPathFilterFsType;
  /** @uniqueItems true */
  extensionGroupIds?: number[];
  extensionGroups?: BakabaseAbstractionsModelsDomainExtensionGroup[];
  /** @uniqueItems true */
  extensions?: string[];
}

export interface BakabaseAbstractionsModelsDomainPathLocator {
  /** [1: Layer, 2: Regex] */
  positioner: BakabaseAbstractionsModelsDomainConstantsPathPositioner;
  /** @format int32 */
  layer?: number;
  regex?: string;
}

export interface BakabaseAbstractionsModelsDomainProperty {
  /** [1: Internal, 2: Reserved, 4: Custom, 7: All] */
  pool: BakabaseAbstractionsModelsDomainConstantsPropertyPool;
  /** @format int32 */
  id: number;
  name: string;
  /** [1: SingleLineText, 2: MultilineText, 3: SingleChoice, 4: MultipleChoice, 5: Number, 6: Percentage, 7: Rating, 8: Boolean, 9: Link, 10: Attachment, 11: Date, 12: DateTime, 13: Time, 14: Formula, 15: Multilevel, 16: Tags] */
  type: BakabaseAbstractionsModelsDomainConstantsPropertyType;
  options?: any;
  /** @format int32 */
  order: number;
}

export interface BakabaseAbstractionsModelsDomainPropertyPathSegmentMatcherValue {
  fixedText?: string;
  /** @format int32 */
  layer?: number;
  regex?: string;
  /** @format int32 */
  propertyId: number;
  isCustomProperty: boolean;
  /** [1: Layer, 2: Regex, 3: FixedText] */
  valueType: BakabaseInsideWorldModelsConstantsResourceMatcherValueType;
  propertyName?: string;
  isSecondaryProperty: boolean;
  isResourceProperty: boolean;
  isValid: boolean;
}

export interface BakabaseAbstractionsModelsDomainReservedPropertyValue {
  /** @format int32 */
  id: number;
  /** @format int32 */
  resourceId: number;
  /** @format int32 */
  scope: number;
  /** @format double */
  rating?: number;
  introduction?: string;
  coverPaths?: string[];
}

export interface BakabaseAbstractionsModelsDomainResource {
  /** @format int32 */
  id: number;
  /** @format int32 */
  mediaLibraryId: number;
  /** @format int32 */
  categoryId: number;
  fileName: string;
  directory: string;
  path: string;
  displayName: string;
  /** @format int32 */
  parentId?: number;
  hasChildren: boolean;
  isFile: boolean;
  /** @format date-time */
  createdAt: string;
  /** @format date-time */
  updatedAt: string;
  /** @format date-time */
  fileCreatedAt: string;
  /** @format date-time */
  fileModifiedAt: string;
  coverPaths?: string[];
  /** @uniqueItems true */
  tags: BakabaseAbstractionsModelsDomainConstantsResourceTag[];
  parent?: BakabaseAbstractionsModelsDomainResource;
  properties?: Record<string, Record<string, BakabaseAbstractionsModelsDomainResourceProperty>>;
  pinned: boolean;
  /** @format date-time */
  playedAt?: string;
  cache?: BakabaseAbstractionsModelsDomainResourceCache;
  isMediaLibraryV2: boolean;
  category?: BakabaseAbstractionsModelsDomainCategory;
  mediaLibraryName?: string;
}

export interface BakabaseAbstractionsModelsDomainResourceProperty {
  name?: string;
  values?: BakabaseAbstractionsModelsDomainResourcePropertyPropertyValue[];
  /** [1: String, 2: ListString, 3: Decimal, 4: Link, 5: Boolean, 6: DateTime, 7: Time, 8: ListListString, 9: ListTag] */
  dbValueType: BakabaseAbstractionsModelsDomainConstantsStandardValueType;
  /** [1: String, 2: ListString, 3: Decimal, 4: Link, 5: Boolean, 6: DateTime, 7: Time, 8: ListListString, 9: ListTag] */
  bizValueType: BakabaseAbstractionsModelsDomainConstantsStandardValueType;
  /** [1: SingleLineText, 2: MultilineText, 3: SingleChoice, 4: MultipleChoice, 5: Number, 6: Percentage, 7: Rating, 8: Boolean, 9: Link, 10: Attachment, 11: Date, 12: DateTime, 13: Time, 14: Formula, 15: Multilevel, 16: Tags] */
  type: BakabaseAbstractionsModelsDomainConstantsPropertyType;
  visible: boolean;
  /** @format int32 */
  order: number;
}

export interface BakabaseAbstractionsModelsDomainResourcePropertyPropertyValue {
  /** @format int32 */
  scope: number;
  value?: any;
  bizValue?: any;
  aliasAppliedBizValue?: any;
}

export interface BakabaseAbstractionsModelsDomainResourceCache {
  coverPaths?: string[];
  hasMorePlayableFiles: boolean;
  playableFilePaths?: string[];
  cachedTypes: BakabaseAbstractionsModelsDomainConstantsResourceCacheType[];
}

export interface BakabaseAbstractionsModelsDomainSpecialText {
  /** @format int32 */
  id: number;
  value1: string;
  value2?: string;
  /** [1: Useless, 3: Wrapper, 4: Standardization, 6: Volume, 7: Trim, 8: DateTime, 9: Language] */
  type: BakabaseAbstractionsModelsDomainConstantsSpecialTextType;
}

export interface BakabaseAbstractionsModelsDtoCustomPropertyAddOrPutDto {
  name: string;
  /** [1: SingleLineText, 2: MultilineText, 3: SingleChoice, 4: MultipleChoice, 5: Number, 6: Percentage, 7: Rating, 8: Boolean, 9: Link, 10: Attachment, 11: Date, 12: DateTime, 13: Time, 14: Formula, 15: Multilevel, 16: Tags] */
  type: BakabaseAbstractionsModelsDomainConstantsPropertyType;
  options?: string;
}

export interface BakabaseAbstractionsModelsDtoMediaLibraryAddDto {
  /** @minLength 1 */
  name: string;
  /** @format int32 */
  categoryId: number;
  pathConfigurations?: BakabaseAbstractionsModelsDomainPathConfiguration[];
}

export interface BakabaseAbstractionsModelsDtoMediaLibraryPatchDto {
  name?: string;
  pathConfigurations?: BakabaseAbstractionsModelsDomainPathConfiguration[];
  /** @format int32 */
  order?: number;
}

export interface BakabaseAbstractionsModelsInputCategoryAddInputModel {
  name: string;
}

export interface BakabaseAbstractionsModelsInputCategoryComponentConfigureInputModel {
  /** [1: Enhancer, 2: PlayableFileSelector, 3: Player] */
  type: BakabaseInsideWorldModelsConstantsComponentType;
  componentKeys: string[];
  enhancementOptions?: BakabaseInsideWorldModelsModelsDtosResourceCategoryEnhancementOptions;
}

export interface BakabaseAbstractionsModelsInputCategoryCustomPropertyBindInputModel {
  customPropertyIds?: number[];
}

export interface BakabaseAbstractionsModelsInputCategoryDuplicateInputModel {
  name: string;
}

export interface BakabaseAbstractionsModelsInputCategoryPatchInputModel {
  name?: string;
  color?: string;
  /** [1: FilenameAscending, 2: FileModifyDtDescending] */
  coverSelectionOrder?: BakabaseInsideWorldModelsConstantsCoverSelectOrder;
  /** @format int32 */
  order?: number;
  generateNfo?: boolean;
}

export interface BakabaseAbstractionsModelsInputExtensionGroupAddInputModel {
  name: string;
  /** @uniqueItems true */
  extensions?: string[];
}

export interface BakabaseAbstractionsModelsInputExtensionGroupPutInputModel {
  name: string;
  /** @uniqueItems true */
  extensions: string[];
}

export interface BakabaseAbstractionsModelsInputMediaLibraryAddInBulkInputModel {
  nameAndPaths: Record<string, string[] | null>;
}

export interface BakabaseAbstractionsModelsInputMediaLibraryPathConfigurationAddInputModel {
  /** @minLength 1 */
  path: string;
}

export interface BakabaseAbstractionsModelsInputMediaLibraryRootPathsAddInBulkInputModel {
  rootPaths: string[];
}

export interface BakabaseAbstractionsModelsInputMediaLibraryTemplateAddByMediaLibraryV1InputModel {
  /** @format int32 */
  v1Id: number;
  /** @format int32 */
  pcIdx: number;
  name: string;
}

export interface BakabaseAbstractionsModelsInputMediaLibraryTemplateAddInputModel {
  name: string;
}

export interface BakabaseAbstractionsModelsInputMediaLibraryTemplateImportInputModel {
  shareCode: string;
  customPropertyConversionsMap?: Record<
    string,
    BakabaseAbstractionsModelsInputMediaLibraryTemplateImportInputModelTCustomPropertyConversion
  >;
  extensionGroupConversionsMap?: Record<
    string,
    BakabaseAbstractionsModelsInputMediaLibraryTemplateImportInputModelTExtensionGroupConversion
  >;
}

export interface BakabaseAbstractionsModelsInputMediaLibraryTemplateImportInputModelTCustomPropertyConversion {
  /** [1: Internal, 2: Reserved, 4: Custom, 7: All] */
  toPropertyPool: BakabaseAbstractionsModelsDomainConstantsPropertyPool;
  /** @format int32 */
  toPropertyId: number;
}

export interface BakabaseAbstractionsModelsInputMediaLibraryTemplateImportInputModelTExtensionGroupConversion {
  /** @format int32 */
  toExtensionGroupId: number;
}

export interface BakabaseAbstractionsModelsInputMediaLibraryV2AddOrPutInputModel {
  name: string;
  path: string;
}

export interface BakabaseAbstractionsModelsInputResourcePropertyValuePutInputModel {
  /** @format int32 */
  propertyId: number;
  isCustomProperty: boolean;
  value?: string;
}

export interface BakabaseAbstractionsModelsInputResourceSearchOrderInputModel {
  /** [1: FileCreateDt, 2: FileModifyDt, 3: Filename, 6: AddDt, 11: PlayedAt] */
  property: BakabaseInsideWorldModelsConstantsAosResourceSearchSortableProperty;
  asc: boolean;
}

export interface BakabaseAbstractionsModelsInputResourceTransferInputModel {
  items: BakabaseAbstractionsModelsInputResourceTransferInputModelItem[];
  keepMediaLibraryForAll: boolean;
  deleteAllSourceResources: boolean;
}

export interface BakabaseAbstractionsModelsInputResourceTransferInputModelItem {
  /** @format int32 */
  fromId: number;
  /** @format int32 */
  toId: number;
  keepMediaLibrary: boolean;
  deleteSourceResource: boolean;
}

export interface BakabaseAbstractionsModelsInputSpecialTextAddInputModel {
  /** [1: Useless, 3: Wrapper, 4: Standardization, 6: Volume, 7: Trim, 8: DateTime, 9: Language] */
  type: BakabaseAbstractionsModelsDomainConstantsSpecialTextType;
  value1: string;
  value2?: string;
}

export interface BakabaseAbstractionsModelsInputSpecialTextPatchInputModel {
  value1?: string;
  value2?: string;
}

export interface BakabaseAbstractionsModelsViewCacheOverviewViewModel {
  categoryCaches: BakabaseAbstractionsModelsViewCacheOverviewViewModelCategoryCacheViewModel[];
}

export interface BakabaseAbstractionsModelsViewCacheOverviewViewModelCategoryCacheViewModel {
  /** @format int32 */
  categoryId: number;
  categoryName: string;
  resourceCacheCountMap: Record<string, number>;
  /** @format int32 */
  resourceCount: number;
}

export interface BakabaseAbstractionsModelsViewCategoryResourceDisplayNameViewModel {
  /** @format int32 */
  resourceId: number;
  resourcePath: string;
  segments: BakabaseAbstractionsModelsViewCategoryResourceDisplayNameViewModelSegment[];
}

export interface BakabaseAbstractionsModelsViewCategoryResourceDisplayNameViewModelSegment {
  /** [1: StaticText, 2: Property, 3: LeftWrapper, 4: RightWrapper] */
  type: BakabaseAbstractionsModelsViewConstantsCategoryResourceDisplayNameSegmentType;
  text: string;
  wrapperPairId?: string;
}

/**
 * [1: StaticText, 2: Property, 3: LeftWrapper, 4: RightWrapper]
 * @format int32
 */
export type BakabaseAbstractionsModelsViewConstantsCategoryResourceDisplayNameSegmentType = 1 | 2 | 3 | 4;

export interface BakabaseAbstractionsModelsViewMediaLibraryTemplateValidationViewModel {
  passed: boolean;
  unhandledProperties?: BakabaseAbstractionsModelsDomainProperty[];
  unhandledExtensionGroups?: BakabaseAbstractionsModelsDomainExtensionGroup[];
}

export interface BakabaseInfrastructuresComponentsAppModelsRequestModelsAppOptionsPatchRequestModel {
  language?: string;
  enablePreReleaseChannel?: boolean;
  enableAnonymousDataTracking?: boolean;
  /** [0: Prompt, 1: Exit, 2: Minimize, 1000: Cancel] */
  closeBehavior?: BakabaseInfrastructuresComponentsGuiCloseBehavior;
  /** [0: FollowSystem, 1: Light, 2: Dark] */
  uiTheme?: BakabaseInfrastructuresComponentsGuiUiTheme;
  /** @format int32 */
  listeningPort?: number;
}

export interface BakabaseInfrastructuresComponentsAppModelsRequestModelsCoreDataMoveRequestModel {
  /** @minLength 1 */
  dataPath: string;
}

export interface BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo {
  appDataPath?: string;
  coreVersion?: string;
  logPath?: string;
  backupPath?: string;
  tempFilesPath?: string;
  notAcceptTerms: boolean;
  needRestart: boolean;
}

export interface BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo {
  version?: string;
  installers?: BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfoInstaller[];
}

export interface BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfoInstaller {
  osPlatform?: SystemRuntimeInteropServicesOSPlatform;
  /** [0: X86, 1: X64, 2: Arm, 3: Arm64, 4: Wasm, 5: S390x, 6: LoongArch64, 7: Armv6, 8: Ppc64le, 9: RiscV64] */
  osArchitecture: SystemRuntimeInteropServicesArchitecture;
  name?: string;
  url?: string;
  /** @format int64 */
  size: number;
}

export interface BakabaseInfrastructuresComponentsConfigurationsAppAppOptions {
  language?: string;
  version?: string;
  enablePreReleaseChannel: boolean;
  enableAnonymousDataTracking: boolean;
  wwwRootPath?: string;
  dataPath?: string;
  prevDataPath?: string;
  /** [0: Prompt, 1: Exit, 2: Minimize, 1000: Cancel] */
  closeBehavior: BakabaseInfrastructuresComponentsGuiCloseBehavior;
  /** [0: FollowSystem, 1: Light, 2: Dark] */
  uiTheme: BakabaseInfrastructuresComponentsGuiUiTheme;
  /** @format int32 */
  listeningPort?: number;
}

/**
 * [0: Prompt, 1: Exit, 2: Minimize, 1000: Cancel]
 * @format int32
 */
export type BakabaseInfrastructuresComponentsGuiCloseBehavior = 0 | 1 | 2 | 1000;

/**
 * [1: UnknownFile, 2: Directory, 3: Dynamic]
 * @format int32
 */
export type BakabaseInfrastructuresComponentsGuiIconType = 1 | 2 | 3;

/**
 * [0: FollowSystem, 1: Light, 2: Dark]
 * @format int32
 */
export type BakabaseInfrastructuresComponentsGuiUiTheme = 0 | 1 | 2;

export interface BakabaseInsideWorldBusinessComponentsCompressionCompressedFileEntry {
  path: string;
  /** @format int64 */
  size: number;
  /** @format double */
  sizeInMb: number;
}

export interface BakabaseInsideWorldBusinessComponentsDependencyAbstractionsDependentComponentVersion {
  version: string;
  description?: string;
  canUpdate: boolean;
}

export interface BakabaseInsideWorldBusinessComponentsDownloaderModelsDbDownloadTaskDbModel {
  /** @format int32 */
  id: number;
  /** @minLength 1 */
  key: string;
  name?: string;
  /** [1: Bilibili, 2: ExHentai, 3: Pixiv, 4: Bangumi, 5: SoulPlus] */
  thirdPartyId: BakabaseInsideWorldModelsConstantsThirdPartyId;
  /** @format int32 */
  type: number;
  /** @format double */
  progress: number;
  /** @format date-time */
  downloadStatusUpdateDt: string;
  /** @format int64 */
  interval?: number;
  /** @format int32 */
  startPage?: number;
  /** @format int32 */
  endPage?: number;
  message?: string;
  checkpoint?: string;
  /** [100: InProgress, 200: Disabled, 300: Complete, 400: Failed] */
  status: BakabaseInsideWorldBusinessComponentsDownloaderModelsDomainConstantsDownloadTaskStatus;
  autoRetry: boolean;
  /** @minLength 1 */
  downloadPath: string;
}

/**
 * [1: StartManually, 2: Restart, 3: Disable, 4: StartAutomatically]
 * @format int32
 */
export type BakabaseInsideWorldBusinessComponentsDownloaderModelsDomainConstantsDownloadTaskAction = 1 | 2 | 3 | 4;

/**
 * [0: NotSet, 1: StopOthers, 2: Ignore]
 * @format int32
 */
export type BakabaseInsideWorldBusinessComponentsDownloaderModelsDomainConstantsDownloadTaskActionOnConflict =
  | 0
  | 1
  | 2;

/**
 * [100: Idle, 200: InQueue, 300: Starting, 400: Downloading, 500: Stopping, 600: Complete, 700: Failed, 800: Disabled]
 * @format int32
 */
export type BakabaseInsideWorldBusinessComponentsDownloaderModelsDomainConstantsDownloadTaskDtoStatus =
  | 100
  | 200
  | 300
  | 400
  | 500
  | 600
  | 700
  | 800;

/**
 * [100: InProgress, 200: Disabled, 300: Complete, 400: Failed]
 * @format int32
 */
export type BakabaseInsideWorldBusinessComponentsDownloaderModelsDomainConstantsDownloadTaskStatus =
  | 100
  | 200
  | 300
  | 400;

export interface BakabaseInsideWorldBusinessComponentsDownloaderModelsDomainDownloadTask {
  /** @format int32 */
  id: number;
  key: string;
  name?: string;
  /** [1: Bilibili, 2: ExHentai, 3: Pixiv, 4: Bangumi, 5: SoulPlus] */
  thirdPartyId: BakabaseInsideWorldModelsConstantsThirdPartyId;
  /** @format int32 */
  type: number;
  /** @format double */
  progress: number;
  /** @format date-time */
  downloadStatusUpdateDt: string;
  /** @format int64 */
  interval?: number;
  /** @format int32 */
  startPage?: number;
  /** @format int32 */
  endPage?: number;
  message?: string;
  checkpoint?: string;
  /** [100: Idle, 200: InQueue, 300: Starting, 400: Downloading, 500: Stopping, 600: Complete, 700: Failed, 800: Disabled] */
  status: BakabaseInsideWorldBusinessComponentsDownloaderModelsDomainConstantsDownloadTaskDtoStatus;
  downloadPath: string;
  current?: string;
  /** @format int32 */
  failureTimes: number;
  autoRetry: boolean;
  /** @format date-time */
  nextStartDt?: string;
  /** @uniqueItems true */
  availableActions: BakabaseInsideWorldBusinessComponentsDownloaderModelsDomainConstantsDownloadTaskAction[];
  displayName: string;
  canStart: boolean;
}

export interface BakabaseInsideWorldBusinessComponentsDownloaderModelsInputDownloadTaskAddInputModel {
  /** [1: Bilibili, 2: ExHentai, 3: Pixiv, 4: Bangumi, 5: SoulPlus] */
  thirdPartyId: BakabaseInsideWorldModelsConstantsThirdPartyId;
  /** @format int32 */
  type: number;
  keyAndNames?: Record<string, string | null>;
  /** @format int64 */
  interval?: number;
  /** @format int32 */
  startPage?: number;
  /** @format int32 */
  endPage?: number;
  checkpoint?: string;
  autoRetry: boolean;
  /** @minLength 1 */
  downloadPath: string;
  isDuplicateAllowed: boolean;
}

export interface BakabaseInsideWorldBusinessComponentsDownloaderModelsInputDownloadTaskDeleteInputModel {
  ids?: number[];
  /** [1: Bilibili, 2: ExHentai, 3: Pixiv, 4: Bangumi, 5: SoulPlus] */
  thirdPartyId?: BakabaseInsideWorldModelsConstantsThirdPartyId;
}

export interface BakabaseInsideWorldBusinessComponentsDownloaderModelsInputDownloadTaskPutInputModel {
  /** @format int64 */
  interval?: number;
  /** @format int32 */
  startPage?: number;
  /** @format int32 */
  endPage?: number;
  checkpoint?: string;
  autoRetry: boolean;
}

export interface BakabaseInsideWorldBusinessComponentsDownloaderModelsInputDownloadTaskStartRequestModel {
  ids: number[];
  /** [0: NotSet, 1: StopOthers, 2: Ignore] */
  actionOnConflict: BakabaseInsideWorldBusinessComponentsDownloaderModelsDomainConstantsDownloadTaskActionOnConflict;
}

export interface BakabaseInsideWorldBusinessComponentsFileExplorerEntriesIwFsCompressedFileGroup {
  keyName: string;
  files: string[];
  extension: string;
  missEntry: boolean;
  password?: string;
  passwordCandidates: string[];
}

export interface BakabaseInsideWorldBusinessComponentsFileExplorerInformationIwFsEntryLazyInfo {
  /** @format int32 */
  childrenCount: number;
}

/**
 * [1: Hidden]
 * @format int32
 */
export type BakabaseInsideWorldBusinessComponentsFileExplorerIwFsAttribute = 1;

export interface BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry {
  path: string;
  name: string;
  meaningfulName: string;
  ext: string;
  attributes: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsAttribute[];
  /** [0: Unknown, 100: Directory, 200: Image, 300: CompressedFileEntry, 400: CompressedFilePart, 500: Symlink, 600: Video, 700: Audio, 1000: Drive, 10000: Invalid] */
  type: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsType;
  /** @format int64 */
  size?: number;
  /** @format int32 */
  childrenCount?: number;
  /** @format date-time */
  creationTime?: string;
  /** @format date-time */
  lastWriteTime?: string;
  passwordsForDecompressing: string[];
}

export interface BakabaseInsideWorldBusinessComponentsFileExplorerIwFsPreview {
  entries: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry[];
  directoryChain: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry[];
  compressedFileGroups: BakabaseInsideWorldBusinessComponentsFileExplorerEntriesIwFsCompressedFileGroup[];
}

/**
 * [0: Unknown, 100: Directory, 200: Image, 300: CompressedFileEntry, 400: CompressedFilePart, 500: Symlink, 600: Video, 700: Audio, 1000: Drive, 10000: Invalid]
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
  | 1000
  | 10000;

/**
 * [5: SoulPlus]
 * @format int32
 */
export type BakabaseInsideWorldBusinessComponentsPostParserModelsDomainConstantsPostParserSource = 5;

export interface BakabaseInsideWorldBusinessComponentsPostParserModelsDomainPostParserTask {
  /** @format int32 */
  id: number;
  /** [5: SoulPlus] */
  source: BakabaseInsideWorldBusinessComponentsPostParserModelsDomainConstantsPostParserSource;
  link: string;
  title?: string;
  content?: string;
  /** @format date-time */
  parsedAt?: string;
  items?: BakabaseInsideWorldBusinessComponentsPostParserModelsDomainPostParserTaskItem[];
  error?: string;
}

export interface BakabaseInsideWorldBusinessComponentsPostParserModelsDomainPostParserTaskItem {
  title: string;
  link?: string;
  accessCode?: string;
  decompressionPassword?: string;
}

/**
 * [1: SoulPlus, 2: ExHentai]
 * @format int32
 */
export type BakabaseInsideWorldBusinessComponentsTampermonkeyModelsConstantsTampermonkeyScript = 1 | 2;

export interface BakabaseInsideWorldBusinessConfigurationsModelsDomainAiOptions {
  ollamaEndpoint?: string;
}

export interface BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptions {
  /** @format date-time */
  lastSyncDt: string;
  /** @format date-time */
  lastNfoGenerationDt: string;
  lastSearchV2?: BakabaseModulesSearchModelsDbResourceSearchDbModel;
  coverOptions: BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsCoverOptionsModel;
  hideChildren: boolean;
  propertyValueScopePriority: BakabaseAbstractionsModelsDomainConstantsPropertyValueScope[];
  additionalCoverDiscoveringSources: BakabaseInsideWorldModelsConstantsAdditionalCoverDiscoveringSource[];
  savedSearches: BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSavedSearch[];
  idsOfMediaLibraryRecentlyMovedTo?: number[];
  synchronizationOptions?: BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationOptionsModel;
}

export interface BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsCoverOptionsModel {
  /** [1: Replace, 2: Prepend] */
  saveMode?: BakabaseInsideWorldModelsConstantsCoverSaveMode;
}

export interface BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSavedSearch {
  search: BakabaseModulesSearchModelsDbResourceSearchDbModel;
  name: string;
}

export interface BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationCategoryOptions {
  deleteResourcesWithUnknownPath?: boolean;
  enhancerOptionsMap?: Record<
    string,
    BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationEnhancerOptions
  >;
  mediaLibraryOptionsMap?: Record<
    string,
    BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationMediaLibraryOptions
  >;
}

export interface BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationEnhancerOptions {
  reApply?: boolean;
  reEnhance?: boolean;
}

export interface BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationMediaLibraryOptions {
  deleteResourcesWithUnknownPath?: boolean;
  enhancerOptionsMap?: Record<
    string,
    BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationEnhancerOptions
  >;
}

export interface BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationOptionsModel {
  deleteResourcesWithUnknownPath?: boolean;
  deleteResourcesWithUnknownMediaLibrary?: boolean;
  /** @deprecated */
  categoryOptionsMap?: Record<
    string,
    BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationCategoryOptions
  >;
  enhancerOptionsMap?: Record<
    string,
    BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationEnhancerOptions
  >;
  mediaLibraryOptionsMap?: Record<
    string,
    BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationMediaLibraryOptions
  >;
}

export interface BakabaseInsideWorldModelsConfigsBilibiliOptions {
  downloader?: BakabaseInsideWorldModelsConfigsInfrastructuresCommonDownloaderOptions;
  cookie?: string;
}

export interface BakabaseInsideWorldModelsConfigsEnhancerOptions {
  regexEnhancer?: BakabaseInsideWorldModelsConfigsEnhancerOptionsRegexEnhancerModel;
}

export interface BakabaseInsideWorldModelsConfigsEnhancerOptionsRegexEnhancerModel {
  expressions?: string[];
}

export interface BakabaseInsideWorldModelsConfigsExHentaiOptions {
  downloader?: BakabaseInsideWorldModelsConfigsInfrastructuresCommonDownloaderOptions;
  cookie?: string;
  enhancer?: BakabaseInsideWorldModelsConfigsExHentaiOptionsExHentaiEnhancerOptions;
}

export interface BakabaseInsideWorldModelsConfigsExHentaiOptionsExHentaiEnhancerOptions {
  excludedTags: string[];
}

export interface BakabaseInsideWorldModelsConfigsFileSystemOptions {
  recentMovingDestinations?: string[];
  fileMover?: BakabaseInsideWorldModelsConfigsFileSystemOptionsFileMoverOptions;
  fileProcessor?: BakabaseInsideWorldModelsConfigsFileSystemOptionsFileProcessorOptions;
}

export interface BakabaseInsideWorldModelsConfigsFileSystemOptionsFileMoverOptions {
  targets?: BakabaseInsideWorldModelsConfigsFileSystemOptionsFileMoverOptionsTarget[];
  enabled: boolean;
  /** @format date-span */
  delay: string;
}

export interface BakabaseInsideWorldModelsConfigsFileSystemOptionsFileMoverOptionsTarget {
  path: string;
  overwrite: boolean;
  sources: string[];
}

export interface BakabaseInsideWorldModelsConfigsFileSystemOptionsFileProcessorOptions {
  workingDirectory: string;
}

export interface BakabaseInsideWorldModelsConfigsInfrastructuresCommonDownloaderOptions {
  /** @format int32 */
  threads: number;
  /** @format int32 */
  interval: number;
  defaultPath?: string;
  namingConvention?: string;
}

export interface BakabaseInsideWorldModelsConfigsJavLibraryOptions {
  cookie?: string;
  collector?: BakabaseInsideWorldModelsConfigsJavLibraryOptionsCollectorOptions;
}

export interface BakabaseInsideWorldModelsConfigsJavLibraryOptionsCollectorOptions {
  path?: string;
  /** @uniqueItems true */
  urls?: string[];
  /** @uniqueItems true */
  torrentOrLinkKeywords?: string[];
}

export interface BakabaseInsideWorldModelsConfigsNetworkOptions {
  customProxies?: BakabaseInsideWorldModelsConfigsNetworkOptionsProxyOptions[];
  proxy: BakabaseInsideWorldModelsConfigsNetworkOptionsProxyModel;
}

/**
 * [0: DoNotUse, 1: UseSystem, 2: UseCustom]
 * @format int32
 */
export type BakabaseInsideWorldModelsConfigsNetworkOptionsProxyMode = 0 | 1 | 2;

export interface BakabaseInsideWorldModelsConfigsNetworkOptionsProxyModel {
  /** [0: DoNotUse, 1: UseSystem, 2: UseCustom] */
  mode: BakabaseInsideWorldModelsConfigsNetworkOptionsProxyMode;
  customProxyId?: string;
}

export interface BakabaseInsideWorldModelsConfigsNetworkOptionsProxyOptions {
  id: string;
  address: string;
  credentials?: BakabaseInsideWorldModelsConfigsNetworkOptionsProxyOptionsProxyCredentials;
}

export interface BakabaseInsideWorldModelsConfigsNetworkOptionsProxyOptionsProxyCredentials {
  username: string;
  password?: string;
  domain?: string;
}

export interface BakabaseInsideWorldModelsConfigsPixivOptions {
  cookie?: string;
  downloader?: BakabaseInsideWorldModelsConfigsInfrastructuresCommonDownloaderOptions;
}

export interface BakabaseInsideWorldModelsConfigsSoulPlusOptions {
  cookie?: string;
  /** @format int32 */
  autoBuyThreshold: number;
}

export interface BakabaseInsideWorldModelsConfigsThirdPartyOptions {
  simpleSearchEngines?: BakabaseInsideWorldModelsConfigsThirdPartyOptionsSimpleSearchEngineOptions[];
  curlExecutable?: string;
  automaticallyParsingPosts: boolean;
}

export interface BakabaseInsideWorldModelsConfigsThirdPartyOptionsSimpleSearchEngineOptions {
  name: string;
  urlTemplate: string;
}

export interface BakabaseInsideWorldModelsConfigsUIOptions {
  resource: BakabaseInsideWorldModelsConfigsUIOptionsUIResourceOptions;
  /** [0: Default, 1: Resource] */
  startupPage: BakabaseInsideWorldModelsConstantsStartupPage;
}

export interface BakabaseInsideWorldModelsConfigsUIOptionsUIResourceOptions {
  /** @format int32 */
  colCount: number;
  showBiggerCoverWhileHover: boolean;
  disableMediaPreviewer: boolean;
  disableCache: boolean;
  /** [1: Contain, 2: Cover] */
  coverFit: BakabaseInsideWorldModelsConstantsCoverFit;
  /** [1: MediaLibrary, 2: Category, 4: Tags, 7: All] */
  displayContents: BakabaseInsideWorldModelsConstantsResourceDisplayContent;
  disableCoverCarousel: boolean;
  displayResourceId: boolean;
  hideResourceTimeInfo: boolean;
}

/**
 * [1: CompressedFile, 2: Video]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsAdditionalCoverDiscoveringSource = 1 | 2;

/**
 * [0: None, 1: Components, 3: Validation, 4: CustomProperties, 8: EnhancerOptions]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsAdditionalItemsCategoryAdditionalItem = 0 | 1 | 3 | 4 | 8;

/**
 * [0: None, 1: AssociatedCategories]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsAdditionalItemsComponentDescriptorAdditionalItem = 0 | 1;

/**
 * [0: None, 1: Category, 2: ValueCount]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsAdditionalItemsCustomPropertyAdditionalItem = 0 | 1 | 2;

/**
 * [0: None, 1: Category, 2: FileSystemInfo, 4: PathConfigurationBoundProperties]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsAdditionalItemsMediaLibraryAdditionalItem = 0 | 1 | 2 | 4;

/**
 * [0: None, 64: Alias, 128: Category, 160: Properties, 416: DisplayName, 512: HasChildren, 2048: MediaLibraryName, 4096: Cache, 7136: All]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsAdditionalItemsResourceAdditionalItem =
  | 0
  | 64
  | 128
  | 160
  | 416
  | 512
  | 2048
  | 4096
  | 7136;

/**
 * [1: Latest, 2: Frequency]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsAosPasswordSearchOrder = 1 | 2;

/**
 * [1: FileCreateDt, 2: FileModifyDt, 3: Filename, 6: AddDt, 11: PlayedAt]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsAosResourceSearchSortableProperty = 1 | 2 | 3 | 6 | 11;

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
 * [1: Contain, 2: Cover]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsCoverFit = 1 | 2;

/**
 * [1: Replace, 2: Prepend]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsCoverSaveMode = 1 | 2;

/**
 * [1: FilenameAscending, 2: FileModifyDtDescending]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsCoverSelectOrder = 1 | 2;

/**
 * [1: SingleWork, 2: Watched, 3: List, 4: Torrent]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsExHentaiDownloadTaskType = 1 | 2 | 3 | 4;

/**
 * [1: InvalidVolume, 2: FreeSpaceNotEnough, 3: Occupied]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsMediaLibraryFileSystemError = 1 | 2 | 3;

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
 * [1: MediaLibrary, 2: Category, 4: Tags, 7: All]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsResourceDisplayContent = 1 | 2 | 4 | 7;

/**
 * [1: Layer, 2: Regex, 3: FixedText]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsResourceMatcherValueType = 1 | 2 | 3;

/**
 * [0: Default, 1: Resource]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsStartupPage = 0 | 1;

/**
 * [1: Bilibili, 2: ExHentai, 3: Pixiv, 4: Bangumi, 5: SoulPlus]
 * @format int32
 */
export type BakabaseInsideWorldModelsConstantsThirdPartyId = 1 | 2 | 3 | 4 | 5;

export interface BakabaseInsideWorldModelsModelsAosDownloaderNamingDefinitions {
  fields: BakabaseInsideWorldModelsModelsAosDownloaderNamingDefinitionsField[];
  defaultConvention: string;
}

export interface BakabaseInsideWorldModelsModelsAosDownloaderNamingDefinitionsField {
  key: string;
  description: string;
  example: string;
}

export interface BakabaseInsideWorldModelsModelsAosMediaLibraryFileSystemInformation {
  /** @format int64 */
  totalSize: number;
  /** @format int64 */
  freeSpace: number;
  /** @format double */
  usedPercentage: number;
  /** @format double */
  freePercentage: number;
  /** @format double */
  freeSpaceInGb: number;
  /** [1: InvalidVolume, 2: FreeSpaceNotEnough, 3: Occupied] */
  error: BakabaseInsideWorldModelsConstantsMediaLibraryFileSystemError;
}

export interface BakabaseInsideWorldModelsModelsAosPreviewerItem {
  filePath: string;
  /** [1: Image, 2: Audio, 3: Video, 4: Text, 1000: Unknown] */
  type: BakabaseInsideWorldModelsConstantsMediaType;
  /** @format int32 */
  duration: number;
}

export interface BakabaseInsideWorldModelsModelsAosThirdPartyRequestStatistics {
  /** [1: Bilibili, 2: ExHentai, 3: Pixiv, 4: Bangumi, 5: SoulPlus] */
  id: BakabaseInsideWorldModelsConstantsThirdPartyId;
  counts?: Record<string, number>;
}

export interface BakabaseInsideWorldModelsModelsDtosDashboardPropertyStatistics {
  /** @format int32 */
  totalExpectedPropertyValueCount: number;
  /** @format int32 */
  totalFilledPropertyValueCount: number;
  propertyValueCoverages: BakabaseInsideWorldModelsModelsDtosDashboardPropertyStatisticsPropertyValueCoverage[];
}

export interface BakabaseInsideWorldModelsModelsDtosDashboardPropertyStatisticsPropertyValueCoverage {
  /** @format int32 */
  pool: number;
  /** @format int32 */
  id: number;
  name: string;
  /** @format int32 */
  filledCount: number;
  /** @format int32 */
  expectedCount: number;
}

export interface BakabaseInsideWorldModelsModelsDtosDashboardStatistics {
  categoryMediaLibraryCounts: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsCategoryMediaLibraryCount[];
  todayAddedCategoryResourceCounts: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsTextAndCount[];
  thisWeekAddedCategoryResourceCounts: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsTextAndCount[];
  thisMonthAddedCategoryResourceCounts: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsTextAndCount[];
  resourceTrending: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsWeekCount[];
  tagResourceCounts: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsTextAndCount[];
  downloaderDataCounts: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsDownloaderTaskCount[];
  thirdPartyRequestCounts: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsThirdPartyRequestCount[];
  fileMover: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsFileMoverInfo;
  otherCounts: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsTextAndCount[][];
}

export interface BakabaseInsideWorldModelsModelsDtosDashboardStatisticsCategoryMediaLibraryCount {
  categoryName: string;
  mediaLibraryCounts: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsTextAndCount[];
}

export interface BakabaseInsideWorldModelsModelsDtosDashboardStatisticsDownloaderTaskCount {
  /** [1: Bilibili, 2: ExHentai, 3: Pixiv, 4: Bangumi, 5: SoulPlus] */
  id: BakabaseInsideWorldModelsConstantsThirdPartyId;
  statusAndCounts: Record<string, number>;
}

export interface BakabaseInsideWorldModelsModelsDtosDashboardStatisticsFileMoverInfo {
  /** @format int32 */
  sourceCount: number;
  /** @format int32 */
  targetCount: number;
}

export interface BakabaseInsideWorldModelsModelsDtosDashboardStatisticsTextAndCount {
  label?: string;
  name: string;
  /** @format int32 */
  count: number;
}

export interface BakabaseInsideWorldModelsModelsDtosDashboardStatisticsThirdPartyRequestCount {
  /** [1: Bilibili, 2: ExHentai, 3: Pixiv, 4: Bangumi, 5: SoulPlus] */
  id: BakabaseInsideWorldModelsConstantsThirdPartyId;
  /** @format int32 */
  resultType: number;
  /** @format int32 */
  taskCount: number;
}

export interface BakabaseInsideWorldModelsModelsDtosDashboardStatisticsWeekCount {
  /** @format int32 */
  offset: number;
  /** @format int32 */
  count: number;
}

export interface BakabaseInsideWorldModelsModelsDtosPlaylistDto {
  /** @format int32 */
  id: number;
  name: string;
  items?: BakabaseInsideWorldModelsModelsDtosPlaylistItemDto[];
  /** @format int32 */
  interval: number;
  /** @format int32 */
  order: number;
}

export interface BakabaseInsideWorldModelsModelsDtosPlaylistItemDto {
  /** [1: Resource, 2: Video, 3: Image, 4: Audio] */
  type: BakabaseInsideWorldModelsConstantsPlaylistItemType;
  /** @format int32 */
  resourceId?: number;
  file?: string;
  /** @format date-span */
  startTime?: string;
  /** @format date-span */
  endTime?: string;
}

export interface BakabaseInsideWorldModelsModelsDtosResourceCategoryEnhancementOptions {
  enhancementPriorities: Record<string, string[]>;
  defaultPriority: string[];
}

export interface BakabaseInsideWorldModelsModelsEntitiesComponentOptions {
  /** @format int32 */
  id: number;
  /** [1: Enhancer, 2: PlayableFileSelector, 3: Player] */
  componentType: BakabaseInsideWorldModelsConstantsComponentType;
  /** @minLength 1 */
  componentAssemblyQualifiedTypeName: string;
  /** @minLength 1 */
  name: string;
  description?: string;
  /** @minLength 1 */
  json: string;
}

export interface BakabaseInsideWorldModelsModelsEntitiesPassword {
  /** @maxLength 64 */
  text: string;
  /** @format int32 */
  usedTimes: number;
  /** @format date-time */
  lastUsedAt: string;
}

export interface BakabaseInsideWorldModelsRequestModelsComponentOptionsAddRequestModel {
  /** @minLength 1 */
  name: string;
  description?: string;
  /** @minLength 1 */
  componentAssemblyQualifiedTypeName: string;
  /** @minLength 1 */
  json: string;
}

export interface BakabaseInsideWorldModelsRequestModelsExHentaiDownloadTaskAddInputModel {
  /** [1: SingleWork, 2: Watched, 3: List, 4: Torrent] */
  type: BakabaseInsideWorldModelsConstantsExHentaiDownloadTaskType;
  /** @minLength 1 */
  link: string;
}

export interface BakabaseInsideWorldModelsRequestModelsFileDecompressRequestModel {
  paths: string[];
  password?: string;
}

export interface BakabaseInsideWorldModelsRequestModelsFileMoveRequestModel {
  destDir: string;
  entryPaths: string[];
}

export interface BakabaseInsideWorldModelsRequestModelsFileRemoveRequestModel {
  paths: string[];
}

export interface BakabaseInsideWorldModelsRequestModelsFileRenameRequestModel {
  fullname: string;
  newName: string;
}

export interface BakabaseInsideWorldModelsRequestModelsIdBasedSortRequestModel {
  ids: number[];
}

export interface BakabaseInsideWorldModelsRequestModelsOptionsNetworkOptionsPatchInputModel {
  customProxies?: BakabaseInsideWorldModelsRequestModelsOptionsNetworkOptionsPatchInputModelProxyOptions[];
  proxy?: BakabaseInsideWorldModelsConfigsNetworkOptionsProxyModel;
}

export interface BakabaseInsideWorldModelsRequestModelsOptionsNetworkOptionsPatchInputModelProxyOptions {
  id?: string;
  address: string;
  credentials?: BakabaseInsideWorldModelsConfigsNetworkOptionsProxyOptionsProxyCredentials;
}

export interface BakabaseInsideWorldModelsRequestModelsPathConfigurationRemoveRequestModel {
  /** @format int32 */
  index: number;
}

export interface BakabaseInsideWorldModelsRequestModelsRemoveSameEntryInWorkingDirectoryRequestModel {
  workingDir: string;
  entryPaths: string[];
}

export interface BakabaseInsideWorldModelsRequestModelsResourceMoveRequestModel {
  ids: number[];
  /** @format int32 */
  mediaLibraryId: number;
  isLegacyMediaLibrary: boolean;
  path?: string;
}

export interface BakabaseInsideWorldModelsRequestModelsSoulPlusOptionsPatchInputModel {
  cookie?: string;
  /** @format int32 */
  autoBuyThreshold?: number;
}

export interface BakabaseInsideWorldModelsRequestModelsThirdPartyOptionsPatchInput {
  simpleSearchEngines?: BakabaseInsideWorldModelsRequestModelsThirdPartyOptionsPatchInputSimpleSearchEngineOptionsPatchInput[];
  curlExecutable?: string;
  automaticallyParsingPosts?: boolean;
}

export interface BakabaseInsideWorldModelsRequestModelsThirdPartyOptionsPatchInputSimpleSearchEngineOptionsPatchInput {
  name?: string;
  urlTemplate?: string;
}

export interface BakabaseInsideWorldModelsRequestModelsUIOptionsPatchRequestModel {
  resource?: BakabaseInsideWorldModelsConfigsUIOptionsUIResourceOptions;
  /** [0: Default, 1: Resource] */
  startupPage?: BakabaseInsideWorldModelsConstantsStartupPage;
}

export interface BakabaseModulesAliasAbstractionsModelsDomainAlias {
  text: string;
  preferred?: string;
  /** @uniqueItems true */
  candidates?: string[];
}

export interface BakabaseModulesAliasModelsInputAliasAddInputModel {
  /** @minLength 1 */
  text: string;
  preferred?: string;
}

export interface BakabaseModulesAliasModelsInputAliasPatchInputModel {
  text?: string;
  isPreferred: boolean;
}

export type BakabaseModulesEnhancerAbstractionsComponentsIEnhancementConverter = object;

export interface BakabaseModulesEnhancerAbstractionsComponentsIEnhancerDescriptor {
  /** @format int32 */
  id: number;
  name: string;
  description?: string;
  targets: BakabaseModulesEnhancerAbstractionsComponentsIEnhancerTargetDescriptor[];
  /** @format int32 */
  propertyValueScope: number;
}

export interface BakabaseModulesEnhancerAbstractionsComponentsIEnhancerTargetDescriptor {
  /** @format int32 */
  id: number;
  name: string;
  enumId: SystemEnum;
  /** [1: String, 2: ListString, 3: Decimal, 4: Link, 5: Boolean, 6: DateTime, 7: Time, 8: ListListString, 9: ListTag] */
  valueType: BakabaseAbstractionsModelsDomainConstantsStandardValueType;
  /** [1: SingleLineText, 2: MultilineText, 3: SingleChoice, 4: MultipleChoice, 5: Number, 6: Percentage, 7: Rating, 8: Boolean, 9: Link, 10: Attachment, 11: Date, 12: DateTime, 13: Time, 14: Formula, 15: Multilevel, 16: Tags] */
  propertyType: BakabaseAbstractionsModelsDomainConstantsPropertyType;
  isDynamic: boolean;
  description?: string;
  optionsItems?: number[];
  enhancementConverter?: BakabaseModulesEnhancerAbstractionsComponentsIEnhancementConverter;
  /** [12: Introduction, 13: Rating, 22: Cover] */
  reservedPropertyCandidate?: BakabaseAbstractionsModelsDomainConstantsReservedProperty;
}

/**
 * [0: None, 1: GeneratedPropertyValue]
 * @format int32
 */
export type BakabaseModulesEnhancerAbstractionsModelsDomainConstantsEnhancementAdditionalItem = 0 | 1;

export interface BakabaseModulesEnhancerAbstractionsModelsDomainEnhancerFullOptions {
  targetOptions?: BakabaseModulesEnhancerAbstractionsModelsDomainEnhancerTargetFullOptions[];
}

export interface BakabaseModulesEnhancerAbstractionsModelsDomainEnhancerTargetFullOptions {
  /** @format int32 */
  target: number;
  dynamicTarget?: string;
  autoMatchMultilevelString?: boolean;
  autoBindProperty?: boolean;
  /** [1: FilenameAscending, 2: FileModifyDtDescending] */
  coverSelectOrder?: BakabaseInsideWorldModelsConstantsCoverSelectOrder;
  /** [1: Internal, 2: Reserved, 4: Custom, 7: All] */
  propertyPool?: BakabaseAbstractionsModelsDomainConstantsPropertyPool;
  /** @format int32 */
  propertyId?: number;
}

export interface BakabaseModulesEnhancerModelsInputCategoryEnhancerOptionsPatchInputModel {
  options?: BakabaseModulesEnhancerAbstractionsModelsDomainEnhancerFullOptions;
  active?: boolean;
}

export interface BakabaseModulesEnhancerModelsInputCategoryEnhancerTargetOptionsPatchInputModel {
  autoMatchMultilevelString?: boolean;
  autoBindProperty?: boolean;
  /** [1: FilenameAscending, 2: FileModifyDtDescending] */
  coverSelectOrder?: BakabaseInsideWorldModelsConstantsCoverSelectOrder;
  /** @format int32 */
  propertyId?: number;
  /** [1: Internal, 2: Reserved, 4: Custom, 7: All] */
  propertyPool?: BakabaseAbstractionsModelsDomainConstantsPropertyPool;
  dynamicTarget?: string;
}

export interface BakabaseModulesPropertyModelsViewCustomPropertyTypeConversionExampleViewModel {
  results?: BakabaseModulesPropertyModelsViewCustomPropertyTypeConversionExampleViewModelTin[];
}

export interface BakabaseModulesPropertyModelsViewCustomPropertyTypeConversionExampleViewModelTin {
  /** [1: SingleLineText, 2: MultilineText, 3: SingleChoice, 4: MultipleChoice, 5: Number, 6: Percentage, 7: Rating, 8: Boolean, 9: Link, 10: Attachment, 11: Date, 12: DateTime, 13: Time, 14: Formula, 15: Multilevel, 16: Tags] */
  type: BakabaseAbstractionsModelsDomainConstantsPropertyType;
  /** [1: String, 2: ListString, 3: Decimal, 4: Link, 5: Boolean, 6: DateTime, 7: Time, 8: ListListString, 9: ListTag] */
  bizValueType: BakabaseAbstractionsModelsDomainConstantsStandardValueType;
  serializedBizValue?: string;
  outputs?: BakabaseModulesPropertyModelsViewCustomPropertyTypeConversionExampleViewModelTout[];
}

export interface BakabaseModulesPropertyModelsViewCustomPropertyTypeConversionExampleViewModelTout {
  /** [1: SingleLineText, 2: MultilineText, 3: SingleChoice, 4: MultipleChoice, 5: Number, 6: Percentage, 7: Rating, 8: Boolean, 9: Link, 10: Attachment, 11: Date, 12: DateTime, 13: Time, 14: Formula, 15: Multilevel, 16: Tags] */
  type: BakabaseAbstractionsModelsDomainConstantsPropertyType;
  /** [1: String, 2: ListString, 3: Decimal, 4: Link, 5: Boolean, 6: DateTime, 7: Time, 8: ListListString, 9: ListTag] */
  bizValueType: BakabaseAbstractionsModelsDomainConstantsStandardValueType;
  serializedBizValue?: string;
}

export interface BakabaseModulesPropertyModelsViewCustomPropertyTypeConversionPreviewViewModel {
  /** @format int32 */
  dataCount: number;
  changes: BakabaseModulesPropertyModelsViewCustomPropertyTypeConversionPreviewViewModelChange[];
  /** [1: String, 2: ListString, 3: Decimal, 4: Link, 5: Boolean, 6: DateTime, 7: Time, 8: ListListString, 9: ListTag] */
  fromType: BakabaseAbstractionsModelsDomainConstantsStandardValueType;
  /** [1: String, 2: ListString, 3: Decimal, 4: Link, 5: Boolean, 6: DateTime, 7: Time, 8: ListListString, 9: ListTag] */
  toType: BakabaseAbstractionsModelsDomainConstantsStandardValueType;
}

export interface BakabaseModulesPropertyModelsViewCustomPropertyTypeConversionPreviewViewModelChange {
  serializedFromValue?: string;
  serializedToValue?: string;
}

export interface BakabaseModulesPropertyModelsViewPropertyViewModel {
  /** [1: Internal, 2: Reserved, 4: Custom, 7: All] */
  pool: BakabaseAbstractionsModelsDomainConstantsPropertyPool;
  /** @format int32 */
  id: number;
  name: string;
  /** [1: SingleLineText, 2: MultilineText, 3: SingleChoice, 4: MultipleChoice, 5: Number, 6: Percentage, 7: Rating, 8: Boolean, 9: Link, 10: Attachment, 11: Date, 12: DateTime, 13: Time, 14: Formula, 15: Multilevel, 16: Tags] */
  type: BakabaseAbstractionsModelsDomainConstantsPropertyType;
  options?: any;
  /** [1: String, 2: ListString, 3: Decimal, 4: Link, 5: Boolean, 6: DateTime, 7: Time, 8: ListListString, 9: ListTag] */
  dbValueType: BakabaseAbstractionsModelsDomainConstantsStandardValueType;
  /** [1: String, 2: ListString, 3: Decimal, 4: Link, 5: Boolean, 6: DateTime, 7: Time, 8: ListListString, 9: ListTag] */
  bizValueType: BakabaseAbstractionsModelsDomainConstantsStandardValueType;
  poolName: string;
  typeName: string;
  /** @format int32 */
  order: number;
}

export interface BakabaseModulesSearchModelsDbResourceSearchDbModel {
  group?: BakabaseModulesSearchModelsDbResourceSearchFilterGroupDbModel;
  orders?: BakabaseAbstractionsModelsInputResourceSearchOrderInputModel[];
  keyword?: string;
  /** @format int32 */
  page: number;
  /** @format int32 */
  pageSize: number;
  tags?: BakabaseAbstractionsModelsDomainConstantsResourceTag[];
}

export interface BakabaseModulesSearchModelsDbResourceSearchFilterDbModel {
  /** [1: Internal, 2: Reserved, 4: Custom, 7: All] */
  propertyPool?: BakabaseAbstractionsModelsDomainConstantsPropertyPool;
  /** @format int32 */
  propertyId?: number;
  /** [1: Equals, 2: NotEquals, 3: Contains, 4: NotContains, 5: StartsWith, 6: NotStartsWith, 7: EndsWith, 8: NotEndsWith, 9: GreaterThan, 10: LessThan, 11: GreaterThanOrEquals, 12: LessThanOrEquals, 13: IsNull, 14: IsNotNull, 15: In, 16: NotIn, 17: Matches, 18: NotMatches] */
  operation?: BakabaseAbstractionsModelsDomainConstantsSearchOperation;
  value?: string;
  disabled: boolean;
}

export interface BakabaseModulesSearchModelsDbResourceSearchFilterGroupDbModel {
  /** [1: And, 2: Or] */
  combinator: BakabaseAbstractionsModelsDomainConstantsSearchCombinator;
  groups?: BakabaseModulesSearchModelsDbResourceSearchFilterGroupDbModel[];
  filters?: BakabaseModulesSearchModelsDbResourceSearchFilterDbModel[];
  disabled: boolean;
}

/**
 * [1: Directly, 2: Incompatible, 4: ValuesWillBeMerged, 8: DateWillBeLost, 16: StringToTag, 64: OnlyFirstValidRemains, 128: StringToDateTime, 256: StringToTime, 1024: UrlWillBeLost, 2048: StringToNumber, 8192: Trim, 16384: StringToLink, 32768: ValueWillBeSplit, 65536: BooleanToNumber, 131072: TimeToDateTime, 262144: TagGroupWillBeLost, 524288: ValueToBoolean]
 * @format int32
 */
export type BakabaseModulesStandardValueAbstractionsModelsDomainConstantsStandardValueConversionRule =
  | 1
  | 2
  | 4
  | 8
  | 16
  | 64
  | 128
  | 256
  | 1024
  | 2048
  | 8192
  | 16384
  | 32768
  | 65536
  | 131072
  | 262144
  | 524288;

export interface BakabaseModulesStandardValueModelsViewStandardValueConversionRuleViewModel {
  /** [1: Directly, 2: Incompatible, 4: ValuesWillBeMerged, 8: DateWillBeLost, 16: StringToTag, 64: OnlyFirstValidRemains, 128: StringToDateTime, 256: StringToTime, 1024: UrlWillBeLost, 2048: StringToNumber, 8192: Trim, 16384: StringToLink, 32768: ValueWillBeSplit, 65536: BooleanToNumber, 131072: TimeToDateTime, 262144: TagGroupWillBeLost, 524288: ValueToBoolean] */
  rule: BakabaseModulesStandardValueAbstractionsModelsDomainConstantsStandardValueConversionRule;
  name: string;
  description?: string;
}

export interface BakabaseModulesThirdPartyThirdPartiesBilibiliModelsFavorites {
  /** @format int64 */
  id: number;
  title: string;
  /** @format int32 */
  mediaCount: number;
}

export interface BakabaseServiceModelsInputBulkModificationPatchInputModel {
  name?: string;
  isActive?: boolean;
  variables?: BakabaseServiceModelsInputBulkModificationVariableInputModel[];
  filter?: BakabaseServiceModelsInputResourceSearchFilterGroupInputModel;
  processes?: BakabaseServiceModelsInputBulkModificationProcessInputModel[];
}

export interface BakabaseServiceModelsInputBulkModificationProcessInputModel {
  /** [1: Internal, 2: Reserved, 4: Custom, 7: All] */
  propertyPool: BakabaseAbstractionsModelsDomainConstantsPropertyPool;
  /** @format int32 */
  propertyId: number;
  steps?: string;
}

export interface BakabaseServiceModelsInputBulkModificationVariableInputModel {
  key?: string;
  /** [0: Manual, 1: Synchronization, 1000: BakabaseEnhancer, 1001: ExHentaiEnhancer, 1002: BangumiEnhancer, 1003: DLsiteEnhancer, 1004: RegexEnhancer] */
  scope: BakabaseAbstractionsModelsDomainConstantsPropertyValueScope;
  /** [1: Internal, 2: Reserved, 4: Custom, 7: All] */
  propertyPool: BakabaseAbstractionsModelsDomainConstantsPropertyPool;
  /** @format int32 */
  propertyId: number;
  name: string;
  preprocesses?: string;
}

export interface BakabaseServiceModelsInputCategoryCustomPropertySortInputModel {
  orderedPropertyIds: number[];
}

export interface BakabaseServiceModelsInputFileSystemEntryGroupInputModel {
  paths: string[];
  groupInternal: boolean;
}

export interface BakabaseServiceModelsInputIdBasedDataSortInputModel {
  ids: number[];
}

export interface BakabaseServiceModelsInputResourceCoverSaveInputModel {
  base64String: string;
  /** [1: Replace, 2: Prepend] */
  saveMode: BakabaseInsideWorldModelsConstantsCoverSaveMode;
}

export interface BakabaseServiceModelsInputResourceOptionsPatchInputModel {
  additionalCoverDiscoveringSources?: BakabaseInsideWorldModelsConstantsAdditionalCoverDiscoveringSource[];
  coverOptions?: BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsCoverOptionsModel;
  propertyValueScopePriority?: BakabaseAbstractionsModelsDomainConstantsPropertyValueScope[];
  searchCriteria?: BakabaseServiceModelsInputResourceSearchInputModel;
  synchronizationOptions?: BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationOptionsModel;
}

export interface BakabaseServiceModelsInputResourceSearchFilterGroupInputModel {
  /** [1: And, 2: Or] */
  combinator: BakabaseAbstractionsModelsDomainConstantsSearchCombinator;
  groups?: BakabaseServiceModelsInputResourceSearchFilterGroupInputModel[];
  filters?: BakabaseServiceModelsInputResourceSearchFilterInputModel[];
  disabled: boolean;
}

export interface BakabaseServiceModelsInputResourceSearchFilterInputModel {
  /** [1: Internal, 2: Reserved, 4: Custom, 7: All] */
  propertyPool?: BakabaseAbstractionsModelsDomainConstantsPropertyPool;
  /** @format int32 */
  propertyId?: number;
  /** [1: Equals, 2: NotEquals, 3: Contains, 4: NotContains, 5: StartsWith, 6: NotStartsWith, 7: EndsWith, 8: NotEndsWith, 9: GreaterThan, 10: LessThan, 11: GreaterThanOrEquals, 12: LessThanOrEquals, 13: IsNull, 14: IsNotNull, 15: In, 16: NotIn, 17: Matches, 18: NotMatches] */
  operation?: BakabaseAbstractionsModelsDomainConstantsSearchOperation;
  dbValue?: string;
  disabled: boolean;
}

export interface BakabaseServiceModelsInputResourceSearchInputModel {
  group?: BakabaseServiceModelsInputResourceSearchFilterGroupInputModel;
  orders?: BakabaseAbstractionsModelsInputResourceSearchOrderInputModel[];
  keyword?: string;
  /** @format int32 */
  pageSize: number;
  /** @format int32 */
  page: number;
  tags?: BakabaseAbstractionsModelsDomainConstantsResourceTag[];
}

export interface BakabaseServiceModelsInputSavedSearchAddInputModel {
  search: BakabaseServiceModelsInputResourceSearchInputModel;
  name: string;
}

export interface BakabaseServiceModelsViewBulkModificationDiffViewModel {
  /** @format int32 */
  id: number;
  /** @format int32 */
  bulkModificationId: number;
  resourcePath: string;
  /** @format int32 */
  resourceId: number;
  diffs: BakabaseServiceModelsViewResourceDiffViewModel[];
}

export interface BakabaseServiceModelsViewBulkModificationProcessStepViewModel {
  /** @format int32 */
  operation: number;
  options?: any;
}

export interface BakabaseServiceModelsViewBulkModificationProcessViewModel {
  /** [1: Internal, 2: Reserved, 4: Custom, 7: All] */
  propertyPool: BakabaseAbstractionsModelsDomainConstantsPropertyPool;
  /** @format int32 */
  propertyId: number;
  property: BakabaseModulesPropertyModelsViewPropertyViewModel;
  steps?: BakabaseServiceModelsViewBulkModificationProcessStepViewModel[];
}

export interface BakabaseServiceModelsViewBulkModificationVariableViewModel {
  /** [0: Manual, 1: Synchronization, 1000: BakabaseEnhancer, 1001: ExHentaiEnhancer, 1002: BangumiEnhancer, 1003: DLsiteEnhancer, 1004: RegexEnhancer] */
  scope: BakabaseAbstractionsModelsDomainConstantsPropertyValueScope;
  /** [1: Internal, 2: Reserved, 4: Custom, 7: All] */
  propertyPool: BakabaseAbstractionsModelsDomainConstantsPropertyPool;
  /** @format int32 */
  propertyId: number;
  property: BakabaseModulesPropertyModelsViewPropertyViewModel;
  key: string;
  name: string;
  preprocesses?: BakabaseServiceModelsViewBulkModificationProcessStepViewModel[];
}

export interface BakabaseServiceModelsViewBulkModificationViewModel {
  /** @format int32 */
  id: number;
  name: string;
  isActive: boolean;
  /** @format date-time */
  createdAt: string;
  variables?: BakabaseServiceModelsViewBulkModificationVariableViewModel[];
  filter?: BakabaseServiceModelsViewResourceSearchFilterGroupViewModel;
  processes?: BakabaseServiceModelsViewBulkModificationProcessViewModel[];
  filteredResourceIds?: number[];
  /** @format date-time */
  appliedAt?: string;
  /** @format int32 */
  resourceDiffCount: number;
}

export interface BakabaseServiceModelsViewCategoryViewModel {
  /** @format int32 */
  id: number;
  name: string;
  color?: string;
  /** @format date-time */
  createDt: string;
  /** @format int32 */
  order: number;
  componentsData?: BakabaseAbstractionsModelsDbCategoryComponent[];
  /** [1: FilenameAscending, 2: FileModifyDtDescending] */
  coverSelectionOrder: BakabaseInsideWorldModelsConstantsCoverSelectOrder;
  generateNfo: boolean;
  resourceDisplayNameTemplate?: string;
  customProperties?: BakabaseServiceModelsViewCategoryViewModelCustomPropertyViewModel[];
  enhancerOptions?: BakabaseAbstractionsModelsDomainCategoryEnhancerOptions[];
}

export interface BakabaseServiceModelsViewCategoryViewModelCustomPropertyViewModel {
  /** @format int32 */
  id: number;
  name: string;
  /** [1: SingleLineText, 2: MultilineText, 3: SingleChoice, 4: MultipleChoice, 5: Number, 6: Percentage, 7: Rating, 8: Boolean, 9: Link, 10: Attachment, 11: Date, 12: DateTime, 13: Time, 14: Formula, 15: Multilevel, 16: Tags] */
  type: BakabaseAbstractionsModelsDomainConstantsPropertyType;
}

export interface BakabaseServiceModelsViewCustomPropertyViewModel {
  /** [1: Internal, 2: Reserved, 4: Custom, 7: All] */
  pool: BakabaseAbstractionsModelsDomainConstantsPropertyPool;
  /** @format int32 */
  id: number;
  name: string;
  /** [1: SingleLineText, 2: MultilineText, 3: SingleChoice, 4: MultipleChoice, 5: Number, 6: Percentage, 7: Rating, 8: Boolean, 9: Link, 10: Attachment, 11: Date, 12: DateTime, 13: Time, 14: Formula, 15: Multilevel, 16: Tags] */
  type: BakabaseAbstractionsModelsDomainConstantsPropertyType;
  options?: any;
  /** [1: String, 2: ListString, 3: Decimal, 4: Link, 5: Boolean, 6: DateTime, 7: Time, 8: ListListString, 9: ListTag] */
  dbValueType: BakabaseAbstractionsModelsDomainConstantsStandardValueType;
  /** [1: String, 2: ListString, 3: Decimal, 4: Link, 5: Boolean, 6: DateTime, 7: Time, 8: ListListString, 9: ListTag] */
  bizValueType: BakabaseAbstractionsModelsDomainConstantsStandardValueType;
  poolName: string;
  typeName: string;
  /** @format int32 */
  order: number;
  /** @format int32 */
  valueCount?: number;
  categories?: BakabaseAbstractionsModelsDomainCategory[];
}

export interface BakabaseServiceModelsViewEnhancementViewModel {
  /** @format int32 */
  id: number;
  /** @format int32 */
  resourceId: number;
  /** @format int32 */
  enhancerId: number;
  /** [1: String, 2: ListString, 3: Decimal, 4: Link, 5: Boolean, 6: DateTime, 7: Time, 8: ListListString, 9: ListTag] */
  valueType: BakabaseAbstractionsModelsDomainConstantsStandardValueType;
  /** @format int32 */
  target: number;
  dynamicTarget?: string;
  value?: any;
  /** [1: Internal, 2: Reserved, 4: Custom, 7: All] */
  propertyPool?: BakabaseAbstractionsModelsDomainConstantsPropertyPool;
  /** @format int32 */
  propertyId?: number;
  customPropertyValue?: BakabaseAbstractionsModelsDomainCustomPropertyValue;
  reservedPropertyValue?: BakabaseAbstractionsModelsDomainReservedPropertyValue;
  property?: BakabaseModulesPropertyModelsViewPropertyViewModel;
}

export interface BakabaseServiceModelsViewFileSystemEntryGroupResultViewModel {
  rootPath: string;
  groups: BakabaseServiceModelsViewFileSystemEntryGroupResultViewModelGroupViewModel[];
}

export interface BakabaseServiceModelsViewFileSystemEntryGroupResultViewModelGroupViewModel {
  directoryName: string;
  filenames: string[];
}

export interface BakabaseServiceModelsViewFileSystemEntryNameViewModel {
  path: string;
  name: string;
  isDirectory: boolean;
}

export interface BakabaseServiceModelsViewPropertyTypeForManuallySettingValueViewModel {
  /** [1: SingleLineText, 2: MultilineText, 3: SingleChoice, 4: MultipleChoice, 5: Number, 6: Percentage, 7: Rating, 8: Boolean, 9: Link, 10: Attachment, 11: Date, 12: DateTime, 13: Time, 14: Formula, 15: Multilevel, 16: Tags] */
  type: BakabaseAbstractionsModelsDomainConstantsPropertyType;
  /** [1: String, 2: ListString, 3: Decimal, 4: Link, 5: Boolean, 6: DateTime, 7: Time, 8: ListListString, 9: ListTag] */
  dbValueType: BakabaseAbstractionsModelsDomainConstantsStandardValueType;
  /** [1: String, 2: ListString, 3: Decimal, 4: Link, 5: Boolean, 6: DateTime, 7: Time, 8: ListListString, 9: ListTag] */
  bizValueType: BakabaseAbstractionsModelsDomainConstantsStandardValueType;
  isReferenceValueType: boolean;
  properties?: BakabaseModulesPropertyModelsViewPropertyViewModel[];
  unavailableReason?: string;
  isAvailable: boolean;
}

export interface BakabaseServiceModelsViewResourceDiffViewModel {
  property: BakabaseModulesPropertyModelsViewPropertyViewModel;
  value1?: string;
  value2?: string;
}

export interface BakabaseServiceModelsViewResourceEnhancements {
  enhancer: BakabaseModulesEnhancerAbstractionsComponentsIEnhancerDescriptor;
  /** @format date-time */
  contextCreatedAt?: string;
  /** @format date-time */
  contextAppliedAt?: string;
  /** [1: ContextCreated, 2: ContextApplied] */
  status: BakabaseAbstractionsModelsDomainConstantsEnhancementRecordStatus;
  targets: BakabaseServiceModelsViewResourceEnhancementsTargetEnhancement[];
  dynamicTargets: BakabaseServiceModelsViewResourceEnhancementsDynamicTargetEnhancements[];
}

export interface BakabaseServiceModelsViewResourceEnhancementsDynamicTargetEnhancements {
  /** @format int32 */
  target: number;
  targetName: string;
  enhancements?: BakabaseServiceModelsViewEnhancementViewModel[];
}

export interface BakabaseServiceModelsViewResourceEnhancementsTargetEnhancement {
  /** @format int32 */
  target: number;
  targetName: string;
  enhancement?: BakabaseServiceModelsViewEnhancementViewModel;
}

export interface BakabaseServiceModelsViewResourcePathInfoViewModel {
  /** @format int32 */
  id: number;
  path: string;
  fileName: string;
}

export interface BakabaseServiceModelsViewResourceSearchFilterGroupViewModel {
  /** [1: And, 2: Or] */
  combinator: BakabaseAbstractionsModelsDomainConstantsSearchCombinator;
  groups?: BakabaseServiceModelsViewResourceSearchFilterGroupViewModel[];
  filters?: BakabaseServiceModelsViewResourceSearchFilterViewModel[];
  disabled: boolean;
}

export interface BakabaseServiceModelsViewResourceSearchFilterViewModel {
  /** [1: Internal, 2: Reserved, 4: Custom, 7: All] */
  propertyPool?: BakabaseAbstractionsModelsDomainConstantsPropertyPool;
  /** @format int32 */
  propertyId?: number;
  /** [1: Equals, 2: NotEquals, 3: Contains, 4: NotContains, 5: StartsWith, 6: NotStartsWith, 7: EndsWith, 8: NotEndsWith, 9: GreaterThan, 10: LessThan, 11: GreaterThanOrEquals, 12: LessThanOrEquals, 13: IsNull, 14: IsNotNull, 15: In, 16: NotIn, 17: Matches, 18: NotMatches] */
  operation?: BakabaseAbstractionsModelsDomainConstantsSearchOperation;
  dbValue?: string;
  bizValue?: string;
  disabled: boolean;
  availableOperations?: BakabaseAbstractionsModelsDomainConstantsSearchOperation[];
  property?: BakabaseModulesPropertyModelsViewPropertyViewModel;
  valueProperty?: BakabaseModulesPropertyModelsViewPropertyViewModel;
}

export interface BakabaseServiceModelsViewResourceSearchViewModel {
  group?: BakabaseServiceModelsViewResourceSearchFilterGroupViewModel;
  orders?: BakabaseAbstractionsModelsInputResourceSearchOrderInputModel[];
  keyword?: string;
  /** @format int32 */
  page: number;
  /** @format int32 */
  pageSize: number;
  tags?: BakabaseAbstractionsModelsDomainConstantsResourceTag[];
}

export interface BakabaseServiceModelsViewSavedSearchViewModel {
  search: BakabaseServiceModelsViewResourceSearchViewModel;
  name: string;
}

export interface BootstrapComponentsLoggingLogServiceModelsEntitiesLog {
  /** @format int32 */
  id: number;
  /** @format date-time */
  dateTime: string;
  /** [0: Trace, 1: Debug, 2: Information, 3: Warning, 4: Error, 5: Critical, 6: None] */
  level: MicrosoftExtensionsLoggingLogLevel;
  logger?: string;
  event?: string;
  message?: string;
  read: boolean;
}

export interface BootstrapModelsResponseModelsBaseResponse {
  /** @format int32 */
  code: number;
  message?: string;
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseAbstractionsModelsDomainComponentDescriptor {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsModelsDomainComponentDescriptor[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseAbstractionsModelsDomainConstantsSearchOperation {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsModelsDomainConstantsSearchOperation[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseAbstractionsModelsDomainExtensionGroup {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsModelsDomainExtensionGroup[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseAbstractionsModelsDomainMediaLibraryTemplate {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsModelsDomainMediaLibraryTemplate[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseAbstractionsModelsDomainMediaLibraryV2 {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsModelsDomainMediaLibraryV2[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseAbstractionsModelsDomainMediaLibrary {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsModelsDomainMediaLibrary[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseAbstractionsModelsDomainResource {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsModelsDomainResource[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseAbstractionsModelsViewCategoryResourceDisplayNameViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsModelsViewCategoryResourceDisplayNameViewModel[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldBusinessComponentsCompressionCompressedFileEntry {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldBusinessComponentsCompressionCompressedFileEntry[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldBusinessComponentsDownloaderModelsDbDownloadTaskDbModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldBusinessComponentsDownloaderModelsDbDownloadTaskDbModel[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldBusinessComponentsDownloaderModelsDomainDownloadTask {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldBusinessComponentsDownloaderModelsDomainDownloadTask[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldBusinessComponentsPostParserModelsDomainPostParserTask {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldBusinessComponentsPostParserModelsDomainPostParserTask[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsAosPreviewerItem {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldModelsModelsAosPreviewerItem[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsDtosPlaylistDto {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldModelsModelsDtosPlaylistDto[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldModelsModelsEntitiesPassword {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldModelsModelsEntitiesPassword[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseModulesEnhancerAbstractionsComponentsIEnhancerDescriptor {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseModulesEnhancerAbstractionsComponentsIEnhancerDescriptor[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseModulesPropertyModelsViewPropertyViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseModulesPropertyModelsViewPropertyViewModel[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseModulesThirdPartyThirdPartiesBilibiliModelsFavorites {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseModulesThirdPartyThirdPartiesBilibiliModelsFavorites[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseServiceModelsViewBulkModificationViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseServiceModelsViewBulkModificationViewModel[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseServiceModelsViewCategoryViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseServiceModelsViewCategoryViewModel[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseServiceModelsViewCustomPropertyViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseServiceModelsViewCustomPropertyViewModel[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseServiceModelsViewFileSystemEntryGroupResultViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseServiceModelsViewFileSystemEntryGroupResultViewModel[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseServiceModelsViewFileSystemEntryNameViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseServiceModelsViewFileSystemEntryNameViewModel[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseServiceModelsViewPropertyTypeForManuallySettingValueViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseServiceModelsViewPropertyTypeForManuallySettingValueViewModel[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseServiceModelsViewResourceEnhancements {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseServiceModelsViewResourceEnhancements[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseServiceModelsViewResourcePathInfoViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseServiceModelsViewResourcePathInfoViewModel[];
}

export interface BootstrapModelsResponseModelsListResponse1BakabaseServiceModelsViewSavedSearchViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseServiceModelsViewSavedSearchViewModel[];
}

export interface BootstrapModelsResponseModelsListResponse1BootstrapComponentsLoggingLogServiceModelsEntitiesLog {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BootstrapComponentsLoggingLogServiceModelsEntitiesLog[];
}

export interface BootstrapModelsResponseModelsListResponse1SystemCollectionsGenericList1SystemString {
  /** @format int32 */
  code: number;
  message?: string;
  data?: string[][];
}

export interface BootstrapModelsResponseModelsListResponse1SystemString {
  /** @format int32 */
  code: number;
  message?: string;
  data?: string[];
}

export interface BootstrapModelsResponseModelsSearchResponse1BakabaseAbstractionsModelsDbPlayHistoryDbModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsModelsDbPlayHistoryDbModel[];
  /** @format int32 */
  totalCount: number;
  /** @format int32 */
  pageIndex: number;
  /** @format int32 */
  pageSize: number;
}

export interface BootstrapModelsResponseModelsSearchResponse1BakabaseAbstractionsModelsDomainResource {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsModelsDomainResource[];
  /** @format int32 */
  totalCount: number;
  /** @format int32 */
  pageIndex: number;
  /** @format int32 */
  pageSize: number;
}

export interface BootstrapModelsResponseModelsSearchResponse1BakabaseInsideWorldModelsModelsEntitiesPassword {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldModelsModelsEntitiesPassword[];
  /** @format int32 */
  totalCount: number;
  /** @format int32 */
  pageIndex: number;
  /** @format int32 */
  pageSize: number;
}

export interface BootstrapModelsResponseModelsSearchResponse1BakabaseModulesAliasAbstractionsModelsDomainAlias {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseModulesAliasAbstractionsModelsDomainAlias[];
  /** @format int32 */
  totalCount: number;
  /** @format int32 */
  pageIndex: number;
  /** @format int32 */
  pageSize: number;
}

export interface BootstrapModelsResponseModelsSearchResponse1BakabaseServiceModelsViewBulkModificationDiffViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseServiceModelsViewBulkModificationDiffViewModel[];
  /** @format int32 */
  totalCount: number;
  /** @format int32 */
  pageIndex: number;
  /** @format int32 */
  pageSize: number;
}

export interface BootstrapModelsResponseModelsSearchResponse1BootstrapComponentsLoggingLogServiceModelsEntitiesLog {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BootstrapComponentsLoggingLogServiceModelsEntitiesLog[];
  /** @format int32 */
  totalCount: number;
  /** @format int32 */
  pageIndex: number;
  /** @format int32 */
  pageSize: number;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsComponentsConfigurationTaskOptions {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsComponentsConfigurationTaskOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsDomainCategoryEnhancerOptions {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsModelsDomainCategoryEnhancerOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsDomainComponentDescriptor {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsModelsDomainComponentDescriptor;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsDomainConstantsInitializationContentType {
  /** @format int32 */
  code: number;
  message?: string;
  /** [1: NotAcceptTerms, 2: NeedRestart] */
  data: BakabaseAbstractionsModelsDomainConstantsInitializationContentType;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsDomainExtensionGroup {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsModelsDomainExtensionGroup;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsDomainMediaLibraryTemplate {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsModelsDomainMediaLibraryTemplate;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsDomainMediaLibraryV2 {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsModelsDomainMediaLibraryV2;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsDomainMediaLibrary {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsModelsDomainMediaLibrary;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsDomainPathConfigurationTestResult {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsModelsDomainPathConfigurationTestResult;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsDomainSpecialText {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsModelsDomainSpecialText;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsViewCacheOverviewViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsModelsViewCacheOverviewViewModel;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsViewMediaLibraryTemplateValidationViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseAbstractionsModelsViewMediaLibraryTemplateValidationViewModel;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInfrastructuresComponentsConfigurationsAppAppOptions {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInfrastructuresComponentsConfigurationsAppAppOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsDependencyAbstractionsDependentComponentVersion {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldBusinessComponentsDependencyAbstractionsDependentComponentVersion;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsDownloaderModelsDomainDownloadTask {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldBusinessComponentsDownloaderModelsDomainDownloadTask;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsFileExplorerInformationIwFsEntryLazyInfo {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldBusinessComponentsFileExplorerInformationIwFsEntryLazyInfo;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsFileExplorerIwFsPreview {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsPreview;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessConfigurationsModelsDomainAiOptions {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldBusinessConfigurationsModelsDomainAiOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptions {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsBilibiliOptions {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldModelsConfigsBilibiliOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsEnhancerOptions {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldModelsConfigsEnhancerOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsExHentaiOptions {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldModelsConfigsExHentaiOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsFileSystemOptions {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldModelsConfigsFileSystemOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsJavLibraryOptions {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldModelsConfigsJavLibraryOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsNetworkOptions {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldModelsConfigsNetworkOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsPixivOptions {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldModelsConfigsPixivOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsSoulPlusOptions {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldModelsConfigsSoulPlusOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsThirdPartyOptions {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldModelsConfigsThirdPartyOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsUIOptions {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldModelsConfigsUIOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsAosThirdPartyRequestStatistics {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldModelsModelsAosThirdPartyRequestStatistics[];
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsDtosDashboardPropertyStatistics {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldModelsModelsDtosDashboardPropertyStatistics;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsDtosDashboardStatistics {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldModelsModelsDtosDashboardStatistics;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsDtosPlaylistDto {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldModelsModelsDtosPlaylistDto;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsEntitiesComponentOptions {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseInsideWorldModelsModelsEntitiesComponentOptions;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseModulesPropertyModelsViewCustomPropertyTypeConversionExampleViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseModulesPropertyModelsViewCustomPropertyTypeConversionExampleViewModel;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseModulesPropertyModelsViewCustomPropertyTypeConversionPreviewViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseModulesPropertyModelsViewCustomPropertyTypeConversionPreviewViewModel;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseModulesPropertyModelsViewPropertyViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseModulesPropertyModelsViewPropertyViewModel;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseServiceModelsViewBulkModificationViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseServiceModelsViewBulkModificationViewModel;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseServiceModelsViewCategoryViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseServiceModelsViewCategoryViewModel;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseServiceModelsViewCustomPropertyViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseServiceModelsViewCustomPropertyViewModel;
}

export interface BootstrapModelsResponseModelsSingletonResponse1BakabaseServiceModelsViewResourceSearchViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: BakabaseServiceModelsViewResourceSearchViewModel;
}

export interface BootstrapModelsResponseModelsSingletonResponse1SystemBoolean {
  /** @format int32 */
  code: number;
  message?: string;
  data: boolean;
}

export interface BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2SystemInt32BakabaseInsideWorldModelsModelsAosDownloaderNamingDefinitions {
  /** @format int32 */
  code: number;
  message?: string;
  data?: Record<string, BakabaseInsideWorldModelsModelsAosDownloaderNamingDefinitions>;
}

export interface BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2SystemInt32SystemCollectionsGenericDictionary2SystemInt32SystemCollectionsGenericList1BakabaseModulesStandardValueModelsViewStandardValueConversionRuleViewModel {
  /** @format int32 */
  code: number;
  message?: string;
  data?: Record<string, Record<string, BakabaseModulesStandardValueModelsViewStandardValueConversionRuleViewModel[]>>;
}

export interface BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2SystemInt32SystemCollectionsGenericList1BakabaseAbstractionsModelsDomainSpecialText {
  /** @format int32 */
  code: number;
  message?: string;
  data?: Record<string, BakabaseAbstractionsModelsDomainSpecialText[] | null>;
}

export interface BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2SystemStringBakabaseInsideWorldModelsConstantsMediaType {
  /** @format int32 */
  code: number;
  message?: string;
  data?: Record<string, BakabaseInsideWorldModelsConstantsMediaType>;
}

export interface BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2SystemStringSystemCollectionsGenericList1SystemString {
  /** @format int32 */
  code: number;
  message?: string;
  data?: Record<string, string[] | null>;
}

export interface BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2SystemStringSystemInt32 {
  /** @format int32 */
  code: number;
  message?: string;
  data?: Record<string, number>;
}

export interface BootstrapModelsResponseModelsSingletonResponse1SystemInt32 {
  /** @format int32 */
  code: number;
  message?: string;
  /** @format int32 */
  data: number;
}

export interface BootstrapModelsResponseModelsSingletonResponse1SystemString {
  /** @format int32 */
  code: number;
  message?: string;
  data?: string;
}

/**
 * [0: Trace, 1: Debug, 2: Information, 3: Warning, 4: Error, 5: Critical, 6: None]
 * @format int32
 */
export type MicrosoftExtensionsLoggingLogLevel = 0 | 1 | 2 | 3 | 4 | 5 | 6;

export type SystemEnum = object;

export type SystemIntPtr = object;

export interface SystemModuleHandle {
  /** @format int32 */
  mdStreamVersion: number;
}

export interface SystemReflectionAssembly {
  definedTypes: SystemReflectionTypeInfo[];
  exportedTypes: SystemType[];
  /** @deprecated */
  codeBase?: string;
  entryPoint?: SystemReflectionMethodInfo;
  fullName?: string;
  imageRuntimeVersion: string;
  isDynamic: boolean;
  location: string;
  reflectionOnly: boolean;
  isCollectible: boolean;
  isFullyTrusted: boolean;
  customAttributes: SystemReflectionCustomAttributeData[];
  /** @deprecated */
  escapedCodeBase: string;
  manifestModule: SystemReflectionModule;
  modules: SystemReflectionModule[];
  /** @deprecated */
  globalAssemblyCache: boolean;
  /** @format int64 */
  hostContext: number;
  /** [0: None, 1: Level1, 2: Level2] */
  securityRuleSet: SystemSecuritySecurityRuleSet;
}

/**
 * [1: Standard, 2: VarArgs, 3: Any, 32: HasThis, 64: ExplicitThis]
 * @format int32
 */
export type SystemReflectionCallingConventions = 1 | 2 | 3 | 32 | 64;

export interface SystemReflectionConstructorInfo {
  name: string;
  declaringType?: SystemType;
  reflectedType?: SystemType;
  module: SystemReflectionModule;
  customAttributes: SystemReflectionCustomAttributeData[];
  isCollectible: boolean;
  /** @format int32 */
  metadataToken: number;
  /** [0: PrivateScope, 0: PrivateScope, 1: Private, 2: FamANDAssem, 3: Assembly, 4: Family, 5: FamORAssem, 6: Public, 7: MemberAccessMask, 8: UnmanagedExport, 16: Static, 32: Final, 64: Virtual, 128: HideBySig, 256: VtableLayoutMask, 256: VtableLayoutMask, 512: CheckAccessOnOverride, 1024: Abstract, 2048: SpecialName, 4096: RTSpecialName, 8192: PinvokeImpl, 16384: HasSecurity, 32768: RequireSecObject, 53248: ReservedMask] */
  attributes: SystemReflectionMethodAttributes;
  /** [0: IL, 0: IL, 1: Native, 2: OPTIL, 3: CodeTypeMask, 3: CodeTypeMask, 4: ManagedMask, 4: ManagedMask, 8: NoInlining, 16: ForwardRef, 32: Synchronized, 64: NoOptimization, 128: PreserveSig, 256: AggressiveInlining, 512: AggressiveOptimization, 4096: InternalCall, 65535: MaxMethodImplVal] */
  methodImplementationFlags: SystemReflectionMethodImplAttributes;
  /** [1: Standard, 2: VarArgs, 3: Any, 32: HasThis, 64: ExplicitThis] */
  callingConvention: SystemReflectionCallingConventions;
  isAbstract: boolean;
  isConstructor: boolean;
  isFinal: boolean;
  isHideBySig: boolean;
  isSpecialName: boolean;
  isStatic: boolean;
  isVirtual: boolean;
  isAssembly: boolean;
  isFamily: boolean;
  isFamilyAndAssembly: boolean;
  isFamilyOrAssembly: boolean;
  isPrivate: boolean;
  isPublic: boolean;
  isConstructedGenericMethod: boolean;
  isGenericMethod: boolean;
  isGenericMethodDefinition: boolean;
  containsGenericParameters: boolean;
  methodHandle: SystemRuntimeMethodHandle;
  isSecurityCritical: boolean;
  isSecuritySafeCritical: boolean;
  isSecurityTransparent: boolean;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType: SystemReflectionMemberTypes;
}

export interface SystemReflectionCustomAttributeData {
  attributeType: SystemType;
  constructor: SystemReflectionConstructorInfo;
  constructorArguments: SystemReflectionCustomAttributeTypedArgument[];
  namedArguments: SystemReflectionCustomAttributeNamedArgument[];
}

export interface SystemReflectionCustomAttributeNamedArgument {
  memberInfo: SystemReflectionMemberInfo;
  typedValue: SystemReflectionCustomAttributeTypedArgument;
  memberName: string;
  isField: boolean;
}

export interface SystemReflectionCustomAttributeTypedArgument {
  argumentType: SystemType;
  value?: any;
}

/**
 * [0: None, 512: SpecialName, 1024: ReservedMask, 1024: ReservedMask]
 * @format int32
 */
export type SystemReflectionEventAttributes = 0 | 512 | 1024;

export interface SystemReflectionEventInfo {
  name: string;
  declaringType?: SystemType;
  reflectedType?: SystemType;
  module: SystemReflectionModule;
  customAttributes: SystemReflectionCustomAttributeData[];
  isCollectible: boolean;
  /** @format int32 */
  metadataToken: number;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType: SystemReflectionMemberTypes;
  /** [0: None, 512: SpecialName, 1024: ReservedMask, 1024: ReservedMask] */
  attributes: SystemReflectionEventAttributes;
  isSpecialName: boolean;
  addMethod?: SystemReflectionMethodInfo;
  removeMethod?: SystemReflectionMethodInfo;
  raiseMethod?: SystemReflectionMethodInfo;
  isMulticast: boolean;
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
  name: string;
  declaringType?: SystemType;
  reflectedType?: SystemType;
  module: SystemReflectionModule;
  customAttributes: SystemReflectionCustomAttributeData[];
  isCollectible: boolean;
  /** @format int32 */
  metadataToken: number;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType: SystemReflectionMemberTypes;
  /** [0: PrivateScope, 1: Private, 2: FamANDAssem, 3: Assembly, 4: Family, 5: FamORAssem, 6: Public, 7: FieldAccessMask, 16: Static, 32: InitOnly, 64: Literal, 128: NotSerialized, 256: HasFieldRVA, 512: SpecialName, 1024: RTSpecialName, 4096: HasFieldMarshal, 8192: PinvokeImpl, 32768: HasDefault, 38144: ReservedMask] */
  attributes: SystemReflectionFieldAttributes;
  fieldType: SystemType;
  isInitOnly: boolean;
  isLiteral: boolean;
  /** @deprecated */
  isNotSerialized: boolean;
  isPinvokeImpl: boolean;
  isSpecialName: boolean;
  isStatic: boolean;
  isAssembly: boolean;
  isFamily: boolean;
  isFamilyAndAssembly: boolean;
  isFamilyOrAssembly: boolean;
  isPrivate: boolean;
  isPublic: boolean;
  isSecurityCritical: boolean;
  isSecuritySafeCritical: boolean;
  isSecurityTransparent: boolean;
  fieldHandle: SystemRuntimeFieldHandle;
}

/**
 * [0: None, 1: Covariant, 2: Contravariant, 3: VarianceMask, 4: ReferenceTypeConstraint, 8: NotNullableValueTypeConstraint, 16: DefaultConstructorConstraint, 28: SpecialConstraintMask, 32: AllowByRefLike]
 * @format int32
 */
export type SystemReflectionGenericParameterAttributes = 0 | 1 | 2 | 3 | 4 | 8 | 16 | 28 | 32;

export type SystemReflectionICustomAttributeProvider = object;

export interface SystemReflectionMemberInfo {
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType: SystemReflectionMemberTypes;
  name: string;
  declaringType?: SystemType;
  reflectedType?: SystemType;
  module: SystemReflectionModule;
  customAttributes: SystemReflectionCustomAttributeData[];
  isCollectible: boolean;
  /** @format int32 */
  metadataToken: number;
}

/**
 * [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All]
 * @format int32
 */
export type SystemReflectionMemberTypes = 1 | 2 | 4 | 8 | 16 | 32 | 64 | 128 | 191;

/**
 * [0: PrivateScope, 0: PrivateScope, 1: Private, 2: FamANDAssem, 3: Assembly, 4: Family, 5: FamORAssem, 6: Public, 7: MemberAccessMask, 8: UnmanagedExport, 16: Static, 32: Final, 64: Virtual, 128: HideBySig, 256: VtableLayoutMask, 256: VtableLayoutMask, 512: CheckAccessOnOverride, 1024: Abstract, 2048: SpecialName, 4096: RTSpecialName, 8192: PinvokeImpl, 16384: HasSecurity, 32768: RequireSecObject, 53248: ReservedMask]
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
  memberType: SystemReflectionMemberTypes;
  name: string;
  declaringType?: SystemType;
  reflectedType?: SystemType;
  module: SystemReflectionModule;
  customAttributes: SystemReflectionCustomAttributeData[];
  isCollectible: boolean;
  /** @format int32 */
  metadataToken: number;
  /** [0: PrivateScope, 0: PrivateScope, 1: Private, 2: FamANDAssem, 3: Assembly, 4: Family, 5: FamORAssem, 6: Public, 7: MemberAccessMask, 8: UnmanagedExport, 16: Static, 32: Final, 64: Virtual, 128: HideBySig, 256: VtableLayoutMask, 256: VtableLayoutMask, 512: CheckAccessOnOverride, 1024: Abstract, 2048: SpecialName, 4096: RTSpecialName, 8192: PinvokeImpl, 16384: HasSecurity, 32768: RequireSecObject, 53248: ReservedMask] */
  attributes: SystemReflectionMethodAttributes;
  /** [0: IL, 0: IL, 1: Native, 2: OPTIL, 3: CodeTypeMask, 3: CodeTypeMask, 4: ManagedMask, 4: ManagedMask, 8: NoInlining, 16: ForwardRef, 32: Synchronized, 64: NoOptimization, 128: PreserveSig, 256: AggressiveInlining, 512: AggressiveOptimization, 4096: InternalCall, 65535: MaxMethodImplVal] */
  methodImplementationFlags: SystemReflectionMethodImplAttributes;
  /** [1: Standard, 2: VarArgs, 3: Any, 32: HasThis, 64: ExplicitThis] */
  callingConvention: SystemReflectionCallingConventions;
  isAbstract: boolean;
  isConstructor: boolean;
  isFinal: boolean;
  isHideBySig: boolean;
  isSpecialName: boolean;
  isStatic: boolean;
  isVirtual: boolean;
  isAssembly: boolean;
  isFamily: boolean;
  isFamilyAndAssembly: boolean;
  isFamilyOrAssembly: boolean;
  isPrivate: boolean;
  isPublic: boolean;
  isConstructedGenericMethod: boolean;
  isGenericMethod: boolean;
  isGenericMethodDefinition: boolean;
  containsGenericParameters: boolean;
  methodHandle: SystemRuntimeMethodHandle;
  isSecurityCritical: boolean;
  isSecuritySafeCritical: boolean;
  isSecurityTransparent: boolean;
}

/**
 * [0: IL, 0: IL, 1: Native, 2: OPTIL, 3: CodeTypeMask, 3: CodeTypeMask, 4: ManagedMask, 4: ManagedMask, 8: NoInlining, 16: ForwardRef, 32: Synchronized, 64: NoOptimization, 128: PreserveSig, 256: AggressiveInlining, 512: AggressiveOptimization, 4096: InternalCall, 65535: MaxMethodImplVal]
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
  name: string;
  declaringType?: SystemType;
  reflectedType?: SystemType;
  module: SystemReflectionModule;
  customAttributes: SystemReflectionCustomAttributeData[];
  isCollectible: boolean;
  /** @format int32 */
  metadataToken: number;
  /** [0: PrivateScope, 0: PrivateScope, 1: Private, 2: FamANDAssem, 3: Assembly, 4: Family, 5: FamORAssem, 6: Public, 7: MemberAccessMask, 8: UnmanagedExport, 16: Static, 32: Final, 64: Virtual, 128: HideBySig, 256: VtableLayoutMask, 256: VtableLayoutMask, 512: CheckAccessOnOverride, 1024: Abstract, 2048: SpecialName, 4096: RTSpecialName, 8192: PinvokeImpl, 16384: HasSecurity, 32768: RequireSecObject, 53248: ReservedMask] */
  attributes: SystemReflectionMethodAttributes;
  /** [0: IL, 0: IL, 1: Native, 2: OPTIL, 3: CodeTypeMask, 3: CodeTypeMask, 4: ManagedMask, 4: ManagedMask, 8: NoInlining, 16: ForwardRef, 32: Synchronized, 64: NoOptimization, 128: PreserveSig, 256: AggressiveInlining, 512: AggressiveOptimization, 4096: InternalCall, 65535: MaxMethodImplVal] */
  methodImplementationFlags: SystemReflectionMethodImplAttributes;
  /** [1: Standard, 2: VarArgs, 3: Any, 32: HasThis, 64: ExplicitThis] */
  callingConvention: SystemReflectionCallingConventions;
  isAbstract: boolean;
  isConstructor: boolean;
  isFinal: boolean;
  isHideBySig: boolean;
  isSpecialName: boolean;
  isStatic: boolean;
  isVirtual: boolean;
  isAssembly: boolean;
  isFamily: boolean;
  isFamilyAndAssembly: boolean;
  isFamilyOrAssembly: boolean;
  isPrivate: boolean;
  isPublic: boolean;
  isConstructedGenericMethod: boolean;
  isGenericMethod: boolean;
  isGenericMethodDefinition: boolean;
  containsGenericParameters: boolean;
  methodHandle: SystemRuntimeMethodHandle;
  isSecurityCritical: boolean;
  isSecuritySafeCritical: boolean;
  isSecurityTransparent: boolean;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType: SystemReflectionMemberTypes;
  returnParameter: SystemReflectionParameterInfo;
  returnType: SystemType;
  returnTypeCustomAttributes: SystemReflectionICustomAttributeProvider;
}

export interface SystemReflectionModule {
  assembly: SystemReflectionAssembly;
  fullyQualifiedName: string;
  name: string;
  /** @format int32 */
  mdStreamVersion: number;
  /** @format uuid */
  moduleVersionId: string;
  scopeName: string;
  moduleHandle: SystemModuleHandle;
  customAttributes: SystemReflectionCustomAttributeData[];
  /** @format int32 */
  metadataToken: number;
}

/**
 * [0: None, 1: In, 2: Out, 4: Lcid, 8: Retval, 16: Optional, 4096: HasDefault, 8192: HasFieldMarshal, 16384: Reserved3, 32768: Reserved4, 61440: ReservedMask]
 * @format int32
 */
export type SystemReflectionParameterAttributes = 0 | 1 | 2 | 4 | 8 | 16 | 4096 | 8192 | 16384 | 32768 | 61440;

export interface SystemReflectionParameterInfo {
  /** [0: None, 1: In, 2: Out, 4: Lcid, 8: Retval, 16: Optional, 4096: HasDefault, 8192: HasFieldMarshal, 16384: Reserved3, 32768: Reserved4, 61440: ReservedMask] */
  attributes: SystemReflectionParameterAttributes;
  member: SystemReflectionMemberInfo;
  name?: string;
  parameterType: SystemType;
  /** @format int32 */
  position: number;
  isIn: boolean;
  isLcid: boolean;
  isOptional: boolean;
  isOut: boolean;
  isRetval: boolean;
  defaultValue?: any;
  rawDefaultValue?: any;
  hasDefaultValue: boolean;
  customAttributes: SystemReflectionCustomAttributeData[];
  /** @format int32 */
  metadataToken: number;
}

/**
 * [0: None, 512: SpecialName, 1024: RTSpecialName, 4096: HasDefault, 8192: Reserved2, 16384: Reserved3, 32768: Reserved4, 62464: ReservedMask]
 * @format int32
 */
export type SystemReflectionPropertyAttributes = 0 | 512 | 1024 | 4096 | 8192 | 16384 | 32768 | 62464;

export interface SystemReflectionPropertyInfo {
  name: string;
  declaringType?: SystemType;
  reflectedType?: SystemType;
  module: SystemReflectionModule;
  customAttributes: SystemReflectionCustomAttributeData[];
  isCollectible: boolean;
  /** @format int32 */
  metadataToken: number;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType: SystemReflectionMemberTypes;
  propertyType: SystemType;
  /** [0: None, 512: SpecialName, 1024: RTSpecialName, 4096: HasDefault, 8192: Reserved2, 16384: Reserved3, 32768: Reserved4, 62464: ReservedMask] */
  attributes: SystemReflectionPropertyAttributes;
  isSpecialName: boolean;
  canRead: boolean;
  canWrite: boolean;
  getMethod?: SystemReflectionMethodInfo;
  setMethod?: SystemReflectionMethodInfo;
}

/**
 * [0: NotPublic, 0: NotPublic, 0: NotPublic, 0: NotPublic, 1: Public, 2: NestedPublic, 3: NestedPrivate, 4: NestedFamily, 5: NestedAssembly, 6: NestedFamANDAssem, 7: NestedFamORAssem, 7: NestedFamORAssem, 8: SequentialLayout, 16: ExplicitLayout, 24: LayoutMask, 32: ClassSemanticsMask, 32: ClassSemanticsMask, 128: Abstract, 256: Sealed, 1024: SpecialName, 2048: RTSpecialName, 4096: Import, 8192: Serializable, 16384: WindowsRuntime, 65536: UnicodeClass, 131072: AutoClass, 196608: CustomFormatClass, 196608: CustomFormatClass, 262144: HasSecurity, 264192: ReservedMask, 1048576: BeforeFieldInit, 12582912: CustomFormatMask]
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
  name: string;
  customAttributes: SystemReflectionCustomAttributeData[];
  isCollectible: boolean;
  /** @format int32 */
  metadataToken: number;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType: SystemReflectionMemberTypes;
  namespace?: string;
  assemblyQualifiedName?: string;
  fullName?: string;
  assembly: SystemReflectionAssembly;
  module: SystemReflectionModule;
  isInterface: boolean;
  isNested: boolean;
  declaringType?: SystemType;
  declaringMethod?: SystemReflectionMethodBase;
  reflectedType?: SystemType;
  underlyingSystemType: SystemType;
  isTypeDefinition: boolean;
  isArray: boolean;
  isByRef: boolean;
  isPointer: boolean;
  isConstructedGenericType: boolean;
  isGenericParameter: boolean;
  isGenericTypeParameter: boolean;
  isGenericMethodParameter: boolean;
  isGenericType: boolean;
  isGenericTypeDefinition: boolean;
  isSZArray: boolean;
  isVariableBoundArray: boolean;
  isByRefLike: boolean;
  isFunctionPointer: boolean;
  isUnmanagedFunctionPointer: boolean;
  hasElementType: boolean;
  genericTypeArguments: SystemType[];
  /** @format int32 */
  genericParameterPosition: number;
  /** [0: None, 1: Covariant, 2: Contravariant, 3: VarianceMask, 4: ReferenceTypeConstraint, 8: NotNullableValueTypeConstraint, 16: DefaultConstructorConstraint, 28: SpecialConstraintMask, 32: AllowByRefLike] */
  genericParameterAttributes: SystemReflectionGenericParameterAttributes;
  /** [0: NotPublic, 0: NotPublic, 0: NotPublic, 0: NotPublic, 1: Public, 2: NestedPublic, 3: NestedPrivate, 4: NestedFamily, 5: NestedAssembly, 6: NestedFamANDAssem, 7: NestedFamORAssem, 7: NestedFamORAssem, 8: SequentialLayout, 16: ExplicitLayout, 24: LayoutMask, 32: ClassSemanticsMask, 32: ClassSemanticsMask, 128: Abstract, 256: Sealed, 1024: SpecialName, 2048: RTSpecialName, 4096: Import, 8192: Serializable, 16384: WindowsRuntime, 65536: UnicodeClass, 131072: AutoClass, 196608: CustomFormatClass, 196608: CustomFormatClass, 262144: HasSecurity, 264192: ReservedMask, 1048576: BeforeFieldInit, 12582912: CustomFormatMask] */
  attributes: SystemReflectionTypeAttributes;
  isAbstract: boolean;
  isImport: boolean;
  isSealed: boolean;
  isSpecialName: boolean;
  isClass: boolean;
  isNestedAssembly: boolean;
  isNestedFamANDAssem: boolean;
  isNestedFamily: boolean;
  isNestedFamORAssem: boolean;
  isNestedPrivate: boolean;
  isNestedPublic: boolean;
  isNotPublic: boolean;
  isPublic: boolean;
  isAutoLayout: boolean;
  isExplicitLayout: boolean;
  isLayoutSequential: boolean;
  isAnsiClass: boolean;
  isAutoClass: boolean;
  isUnicodeClass: boolean;
  isCOMObject: boolean;
  isContextful: boolean;
  isEnum: boolean;
  isMarshalByRef: boolean;
  isPrimitive: boolean;
  isValueType: boolean;
  isSignatureType: boolean;
  isSecurityCritical: boolean;
  isSecuritySafeCritical: boolean;
  isSecurityTransparent: boolean;
  structLayoutAttribute?: SystemRuntimeInteropServicesStructLayoutAttribute;
  typeInitializer?: SystemReflectionConstructorInfo;
  typeHandle: SystemRuntimeTypeHandle;
  /** @format uuid */
  guid: string;
  baseType?: SystemType;
  /** @deprecated */
  isSerializable: boolean;
  containsGenericParameters: boolean;
  isVisible: boolean;
  genericTypeParameters: SystemType[];
  declaredConstructors: SystemReflectionConstructorInfo[];
  declaredEvents: SystemReflectionEventInfo[];
  declaredFields: SystemReflectionFieldInfo[];
  declaredMembers: SystemReflectionMemberInfo[];
  declaredMethods: SystemReflectionMethodInfo[];
  declaredNestedTypes: SystemReflectionTypeInfo[];
  declaredProperties: SystemReflectionPropertyInfo[];
  implementedInterfaces: SystemType[];
}

/**
 * [0: X86, 1: X64, 2: Arm, 3: Arm64, 4: Wasm, 5: S390x, 6: LoongArch64, 7: Armv6, 8: Ppc64le, 9: RiscV64]
 * @format int32
 */
export type SystemRuntimeInteropServicesArchitecture = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9;

/**
 * [0: Sequential, 2: Explicit, 3: Auto]
 * @format int32
 */
export type SystemRuntimeInteropServicesLayoutKind = 0 | 2 | 3;

export type SystemRuntimeInteropServicesOSPlatform = object;

export interface SystemRuntimeInteropServicesStructLayoutAttribute {
  typeId: any;
  /** [0: Sequential, 2: Explicit, 3: Auto] */
  value: SystemRuntimeInteropServicesLayoutKind;
}

export interface SystemRuntimeFieldHandle {
  value: SystemIntPtr;
}

export interface SystemRuntimeMethodHandle {
  value: SystemIntPtr;
}

export interface SystemRuntimeTypeHandle {
  value: SystemIntPtr;
}

/**
 * [0: None, 1: Level1, 2: Level2]
 * @format int32
 */
export type SystemSecuritySecurityRuleSet = 0 | 1 | 2;

export interface SystemType {
  name: string;
  customAttributes: SystemReflectionCustomAttributeData[];
  isCollectible: boolean;
  /** @format int32 */
  metadataToken: number;
  /** [1: Constructor, 2: Event, 4: Field, 8: Method, 16: Property, 32: TypeInfo, 64: Custom, 128: NestedType, 191: All] */
  memberType: SystemReflectionMemberTypes;
  namespace?: string;
  assemblyQualifiedName?: string;
  fullName?: string;
  assembly: SystemReflectionAssembly;
  module: SystemReflectionModule;
  isInterface: boolean;
  isNested: boolean;
  declaringType?: SystemType;
  declaringMethod?: SystemReflectionMethodBase;
  reflectedType?: SystemType;
  underlyingSystemType: SystemType;
  isTypeDefinition: boolean;
  isArray: boolean;
  isByRef: boolean;
  isPointer: boolean;
  isConstructedGenericType: boolean;
  isGenericParameter: boolean;
  isGenericTypeParameter: boolean;
  isGenericMethodParameter: boolean;
  isGenericType: boolean;
  isGenericTypeDefinition: boolean;
  isSZArray: boolean;
  isVariableBoundArray: boolean;
  isByRefLike: boolean;
  isFunctionPointer: boolean;
  isUnmanagedFunctionPointer: boolean;
  hasElementType: boolean;
  genericTypeArguments: SystemType[];
  /** @format int32 */
  genericParameterPosition: number;
  /** [0: None, 1: Covariant, 2: Contravariant, 3: VarianceMask, 4: ReferenceTypeConstraint, 8: NotNullableValueTypeConstraint, 16: DefaultConstructorConstraint, 28: SpecialConstraintMask, 32: AllowByRefLike] */
  genericParameterAttributes: SystemReflectionGenericParameterAttributes;
  /** [0: NotPublic, 0: NotPublic, 0: NotPublic, 0: NotPublic, 1: Public, 2: NestedPublic, 3: NestedPrivate, 4: NestedFamily, 5: NestedAssembly, 6: NestedFamANDAssem, 7: NestedFamORAssem, 7: NestedFamORAssem, 8: SequentialLayout, 16: ExplicitLayout, 24: LayoutMask, 32: ClassSemanticsMask, 32: ClassSemanticsMask, 128: Abstract, 256: Sealed, 1024: SpecialName, 2048: RTSpecialName, 4096: Import, 8192: Serializable, 16384: WindowsRuntime, 65536: UnicodeClass, 131072: AutoClass, 196608: CustomFormatClass, 196608: CustomFormatClass, 262144: HasSecurity, 264192: ReservedMask, 1048576: BeforeFieldInit, 12582912: CustomFormatMask] */
  attributes: SystemReflectionTypeAttributes;
  isAbstract: boolean;
  isImport: boolean;
  isSealed: boolean;
  isSpecialName: boolean;
  isClass: boolean;
  isNestedAssembly: boolean;
  isNestedFamANDAssem: boolean;
  isNestedFamily: boolean;
  isNestedFamORAssem: boolean;
  isNestedPrivate: boolean;
  isNestedPublic: boolean;
  isNotPublic: boolean;
  isPublic: boolean;
  isAutoLayout: boolean;
  isExplicitLayout: boolean;
  isLayoutSequential: boolean;
  isAnsiClass: boolean;
  isAutoClass: boolean;
  isUnicodeClass: boolean;
  isCOMObject: boolean;
  isContextful: boolean;
  isEnum: boolean;
  isMarshalByRef: boolean;
  isPrimitive: boolean;
  isValueType: boolean;
  isSignatureType: boolean;
  isSecurityCritical: boolean;
  isSecuritySafeCritical: boolean;
  isSecurityTransparent: boolean;
  structLayoutAttribute?: SystemRuntimeInteropServicesStructLayoutAttribute;
  typeInitializer?: SystemReflectionConstructorInfo;
  typeHandle: SystemRuntimeTypeHandle;
  /** @format uuid */
  guid: string;
  baseType?: SystemType;
  /** @deprecated */
  isSerializable: boolean;
  containsGenericParameters: boolean;
  isVisible: boolean;
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
     * @name SearchAliasGroups
     * @request GET:/alias
     */
    searchAliasGroups: (
      query?: {
        /** @uniqueItems true */
        texts?: string[];
        text?: string;
        fuzzyText?: string;
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
      this.request<BootstrapModelsResponseModelsSearchResponse1BakabaseModulesAliasAbstractionsModelsDomainAlias, any>({
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
     * @name PatchAlias
     * @request PUT:/alias
     */
    patchAlias: (
      data: BakabaseModulesAliasModelsInputAliasPatchInputModel,
      query?: {
        text?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/alias`,
        method: "PUT",
        query: query,
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Alias
     * @name AddAlias
     * @request POST:/alias
     */
    addAlias: (data: BakabaseModulesAliasModelsInputAliasAddInputModel, params: RequestParams = {}) =>
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
     * @name DeleteAlias
     * @request DELETE:/alias
     */
    deleteAlias: (
      query?: {
        text?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/alias`,
        method: "DELETE",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Alias
     * @name DeleteAliasGroups
     * @request DELETE:/alias/groups
     */
    deleteAliasGroups: (
      query?: {
        preferredTexts?: string[];
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/alias/groups`,
        method: "DELETE",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Alias
     * @name MergeAliasGroups
     * @request PUT:/alias/merge
     */
    mergeAliasGroups: (
      query?: {
        preferredTexts?: string[];
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/alias/merge`,
        method: "PUT",
        query: query,
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
      query?: {
        path?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/alias/import`,
        method: "POST",
        query: query,
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
        BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsDomainConstantsInitializationContentType,
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
     * @name StartBackgroundTask
     * @request POST:/background-task/{id}/run
     */
    startBackgroundTask: (id: string, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/background-task/${id}/run`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BackgroundTask
     * @name StopBackgroundTask
     * @request DELETE:/background-task/{id}/run
     */
    stopBackgroundTask: (
      id: string,
      query?: {
        /** @default false */
        confirm?: boolean;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/background-task/${id}/run`,
        method: "DELETE",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BackgroundTask
     * @name PauseBackgroundTask
     * @request POST:/background-task/{id}/pause
     */
    pauseBackgroundTask: (id: string, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/background-task/${id}/pause`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BackgroundTask
     * @name ResumeBackgroundTask
     * @request DELETE:/background-task/{id}/pause
     */
    resumeBackgroundTask: (id: string, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/background-task/${id}/pause`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BackgroundTask
     * @name CleanInactiveBackgroundTasks
     * @request DELETE:/background-task
     */
    cleanInactiveBackgroundTasks: (params: RequestParams = {}) =>
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
     * @name CleanBackgroundTask
     * @request DELETE:/background-task/{id}
     */
    cleanBackgroundTask: (id: string, params: RequestParams = {}) =>
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
        BootstrapModelsResponseModelsListResponse1BakabaseModulesThirdPartyThirdPartiesBilibiliModelsFavorites,
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
     * @name GetBulkModification
     * @request GET:/bulk-modification/{id}
     */
    getBulkModification: (id: number, params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseServiceModelsViewBulkModificationViewModel,
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
     * @name DuplicateBulkModification
     * @request POST:/bulk-modification/{id}
     */
    duplicateBulkModification: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/bulk-modification/${id}`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BulkModification
     * @name PatchBulkModification
     * @request PATCH:/bulk-modification/{id}
     */
    patchBulkModification: (
      id: number,
      data: BakabaseServiceModelsInputBulkModificationPatchInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/bulk-modification/${id}`,
        method: "PATCH",
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
     * @request GET:/bulk-modification/all
     */
    getAllBulkModifications: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseServiceModelsViewBulkModificationViewModel, any>({
        path: `/bulk-modification/all`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BulkModification
     * @name AddBulkModification
     * @request POST:/bulk-modification
     */
    addBulkModification: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/bulk-modification`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BulkModification
     * @name FilterResourcesInBulkModification
     * @request PUT:/bulk-modification/{id}/filtered-resources
     */
    filterResourcesInBulkModification: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/bulk-modification/${id}/filtered-resources`,
        method: "PUT",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BulkModification
     * @name PreviewBulkModification
     * @request PUT:/bulk-modification/{id}/preview
     */
    previewBulkModification: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/bulk-modification/${id}/preview`,
        method: "PUT",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags BulkModification
     * @name SearchBulkModificationDiffs
     * @request GET:/bulk-modification/{bmId}/diffs
     */
    searchBulkModificationDiffs: (
      bmId: number,
      query?: {
        path?: string;
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
        BootstrapModelsResponseModelsSearchResponse1BakabaseServiceModelsViewBulkModificationDiffViewModel,
        any
      >({
        path: `/bulk-modification/${bmId}/diffs`,
        method: "GET",
        query: query,
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
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
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
     * @request DELETE:/bulk-modification/{id}/apply
     */
    revertBulkModification: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/bulk-modification/${id}/apply`,
        method: "DELETE",
        format: "json",
        ...params,
      }),
  };
  cache = {
    /**
     * No description
     *
     * @tags Cache
     * @name GetCacheOverview
     * @request GET:/cache
     */
    getCacheOverview: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsViewCacheOverviewViewModel,
        any
      >({
        path: `/cache`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Cache
     * @name DeleteResourceCacheByCategoryIdAndCacheType
     * @request DELETE:/cache/category/{categoryId}/type/{type}
     */
    deleteResourceCacheByCategoryIdAndCacheType: (
      categoryId: number,
      type: BakabaseAbstractionsModelsDomainConstantsResourceCacheType,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/cache/category/${categoryId}/type/${type}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),
  };
  category = {
    /**
     * No description
     *
     * @tags Category
     * @name GetCategory
     * @request GET:/category/{id}
     */
    getCategory: (
      id: number,
      query?: {
        /** [0: None, 1: Components, 3: Validation, 4: CustomProperties, 8: EnhancerOptions] */
        additionalItems?: BakabaseInsideWorldModelsConstantsAdditionalItemsCategoryAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1BakabaseServiceModelsViewCategoryViewModel, any>({
        path: `/category/${id}`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Category
     * @name PatchCategory
     * @request PATCH:/category/{id}
     */
    patchCategory: (
      id: number,
      data: BakabaseAbstractionsModelsInputCategoryPatchInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/category/${id}`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Category
     * @name DeleteCategory
     * @request DELETE:/category/{id}
     */
    deleteCategory: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/category/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Category
     * @name GetAllCategories
     * @request GET:/category
     */
    getAllCategories: (
      query?: {
        /** [0: None, 1: Components, 3: Validation, 4: CustomProperties, 8: EnhancerOptions] */
        additionalItems?: BakabaseInsideWorldModelsConstantsAdditionalItemsCategoryAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseServiceModelsViewCategoryViewModel, any>({
        path: `/category`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Category
     * @name AddCategory
     * @request POST:/category
     */
    addCategory: (data: BakabaseAbstractionsModelsInputCategoryAddInputModel, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/category`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Category
     * @name DuplicateCategory
     * @request POST:/category/{id}/duplication
     */
    duplicateCategory: (
      id: number,
      data: BakabaseAbstractionsModelsInputCategoryDuplicateInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/category/${id}/duplication`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Category
     * @name PutCategoryResourceDisplayNameTemplate
     * @request PUT:/category/{id}/resource-display-name-template
     */
    putCategoryResourceDisplayNameTemplate: (id: number, data: string, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/category/${id}/resource-display-name-template`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Category
     * @name ConfigureCategoryComponents
     * @request PUT:/category/{id}/component
     */
    configureCategoryComponents: (
      id: number,
      data: BakabaseAbstractionsModelsInputCategoryComponentConfigureInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/category/${id}/component`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Category
     * @name SortCategories
     * @request PUT:/category/orders
     */
    sortCategories: (data: BakabaseInsideWorldModelsRequestModelsIdBasedSortRequestModel, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/category/orders`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Category
     * @name BindCustomPropertiesToCategory
     * @request PUT:/category/{id}/custom-properties
     */
    bindCustomPropertiesToCategory: (
      id: number,
      data: BakabaseAbstractionsModelsInputCategoryCustomPropertyBindInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/category/${id}/custom-properties`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Category
     * @name BindCustomPropertyToCategory
     * @request POST:/category/{categoryId}/custom-property/{customPropertyId}
     */
    bindCustomPropertyToCategory: (categoryId: number, customPropertyId: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/category/${categoryId}/custom-property/${customPropertyId}`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Category
     * @name UnlinkCustomPropertyFromCategory
     * @request DELETE:/category/{categoryId}/custom-property/{customPropertyId}
     */
    unlinkCustomPropertyFromCategory: (categoryId: number, customPropertyId: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/category/${categoryId}/custom-property/${customPropertyId}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Category
     * @name SortCustomPropertiesInCategory
     * @request PUT:/category/{categoryId}/custom-property/order
     */
    sortCustomPropertiesInCategory: (
      categoryId: number,
      data: BakabaseServiceModelsInputCategoryCustomPropertySortInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/category/${categoryId}/custom-property/order`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Category
     * @name PreviewCategoryDisplayNameTemplate
     * @request GET:/category/{id}/resource/resource-display-name-template/preview
     */
    previewCategoryDisplayNameTemplate: (
      id: number,
      query?: {
        template?: string;
        /**
         * @format int32
         * @default 100
         */
        maxCount?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsListResponse1BakabaseAbstractionsModelsViewCategoryResourceDisplayNameViewModel,
        any
      >({
        path: `/category/${id}/resource/resource-display-name-template/preview`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Category
     * @name GetCategoryEnhancerOptions
     * @request GET:/category/{id}/enhancer/{enhancerId}/options
     */
    getCategoryEnhancerOptions: (id: number, enhancerId: number, params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsDomainCategoryEnhancerOptions,
        any
      >({
        path: `/category/${id}/enhancer/${enhancerId}/options`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Category
     * @name PatchCategoryEnhancerOptions
     * @request PATCH:/category/{id}/enhancer/{enhancerId}/options
     */
    patchCategoryEnhancerOptions: (
      id: number,
      enhancerId: number,
      data: BakabaseModulesEnhancerModelsInputCategoryEnhancerOptionsPatchInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/category/${id}/enhancer/${enhancerId}/options`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Category
     * @name DeleteCategoryEnhancerTargetOptions
     * @request DELETE:/category/{id}/enhancer/{enhancerId}/options/target
     */
    deleteCategoryEnhancerTargetOptions: (
      id: number,
      enhancerId: number,
      query?: {
        /** @format int32 */
        target?: number;
        dynamicTarget?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/category/${id}/enhancer/${enhancerId}/options/target`,
        method: "DELETE",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Category
     * @name PatchCategoryEnhancerTargetOptions
     * @request PATCH:/category/{id}/enhancer/{enhancerId}/options/target
     */
    patchCategoryEnhancerTargetOptions: (
      id: number,
      enhancerId: number,
      query: {
        /** @format int32 */
        target: number;
        dynamicTarget?: string;
      },
      data: BakabaseModulesEnhancerModelsInputCategoryEnhancerTargetOptionsPatchInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/category/${id}/enhancer/${enhancerId}/options/target`,
        method: "PATCH",
        query: query,
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Category
     * @name UnbindCategoryEnhancerTargetProperty
     * @request DELETE:/category/{id}/enhancer/{enhancerId}/options/target/property
     */
    unbindCategoryEnhancerTargetProperty: (
      id: number,
      enhancerId: number,
      query: {
        /** @format int32 */
        target: number;
        dynamicTarget?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/category/${id}/enhancer/${enhancerId}/options/target/property`,
        method: "DELETE",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Category
     * @name StartSyncingCategoryResources
     * @request PUT:/category/{id}/synchronization
     */
    startSyncingCategoryResources: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/category/${id}/synchronization`,
        method: "PUT",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Enhancement
     * @name ApplyEnhancementContextDataByEnhancerAndCategory
     * @request POST:/category/{categoryId}/enhancer/{enhancerId}/enhancement/apply
     */
    applyEnhancementContextDataByEnhancerAndCategory: (
      categoryId: number,
      enhancerId: number,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/category/${categoryId}/enhancer/${enhancerId}/enhancement/apply`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Enhancement
     * @name DeleteEnhancementsByCategory
     * @request DELETE:/category/{categoryId}/enhancement
     */
    deleteEnhancementsByCategory: (
      categoryId: number,
      query?: {
        deleteEmptyOnly?: boolean;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/category/${categoryId}/enhancement`,
        method: "DELETE",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Enhancement
     * @name DeleteEnhancementsByCategoryAndEnhancer
     * @request DELETE:/category/{categoryId}/enhancer/{enhancerId}/enhancements
     */
    deleteEnhancementsByCategoryAndEnhancer: (
      categoryId: number,
      enhancerId: number,
      query?: {
        deleteEmptyOnly?: boolean;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/category/${categoryId}/enhancer/${enhancerId}/enhancements`,
        method: "DELETE",
        query: query,
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
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseAbstractionsModelsDomainComponentDescriptor, any>({
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
        BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsDomainComponentDescriptor,
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
        /** @default true */
        fromCache?: boolean;
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
  customProperty = {
    /**
     * No description
     *
     * @tags CustomProperty
     * @name GetAllCustomProperties
     * @request GET:/custom-property/all
     */
    getAllCustomProperties: (
      query?: {
        /** [0: None, 1: Category, 2: ValueCount] */
        additionalItems?: BakabaseInsideWorldModelsConstantsAdditionalItemsCustomPropertyAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseServiceModelsViewCustomPropertyViewModel, any>({
        path: `/custom-property/all`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags CustomProperty
     * @name GetCustomPropertyByKeys
     * @request GET:/custom-property/ids
     */
    getCustomPropertyByKeys: (
      query?: {
        ids?: number[];
        /** [0: None, 1: Category, 2: ValueCount] */
        additionalItems?: BakabaseInsideWorldModelsConstantsAdditionalItemsCustomPropertyAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseServiceModelsViewCustomPropertyViewModel, any>({
        path: `/custom-property/ids`,
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
    addCustomProperty: (data: BakabaseAbstractionsModelsDtoCustomPropertyAddOrPutDto, params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseServiceModelsViewCustomPropertyViewModel,
        any
      >({
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
    putCustomProperty: (
      id: number,
      data: BakabaseAbstractionsModelsDtoCustomPropertyAddOrPutDto,
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseServiceModelsViewCustomPropertyViewModel,
        any
      >({
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
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/custom-property/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags CustomProperty
     * @name SortCustomProperties
     * @request PUT:/custom-property/order
     */
    sortCustomProperties: (data: BakabaseServiceModelsInputIdBasedDataSortInputModel, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/custom-property/order`,
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
     * @name PreviewCustomPropertyTypeConversion
     * @request POST:/custom-property/{sourceCustomPropertyId}/{targetType}/conversion-preview
     */
    previewCustomPropertyTypeConversion: (
      sourceCustomPropertyId: number,
      targetType: BakabaseAbstractionsModelsDomainConstantsPropertyType,
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseModulesPropertyModelsViewCustomPropertyTypeConversionPreviewViewModel,
        any
      >({
        path: `/custom-property/${sourceCustomPropertyId}/${targetType}/conversion-preview`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags CustomProperty
     * @name GetCustomPropertyConversionRules
     * @request GET:/custom-property/conversion-rule
     */
    getCustomPropertyConversionRules: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2SystemInt32SystemCollectionsGenericDictionary2SystemInt32SystemCollectionsGenericList1BakabaseModulesStandardValueModelsViewStandardValueConversionRuleViewModel,
        any
      >({
        path: `/custom-property/conversion-rule`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags CustomProperty
     * @name ChangeCustomPropertyType
     * @request PUT:/custom-property/{id}/{type}
     */
    changeCustomPropertyType: (
      id: number,
      type: BakabaseAbstractionsModelsDomainConstantsPropertyType,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/custom-property/${id}/${type}`,
        method: "PUT",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags CustomProperty
     * @name GetCustomPropertyValueUsage
     * @request GET:/custom-property/{id}/value-usage
     */
    getCustomPropertyValueUsage: (
      id: number,
      query?: {
        value?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1SystemInt32, any>({
        path: `/custom-property/${id}/value-usage`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags CustomProperty
     * @name TestCustomPropertyTypeConversion
     * @request GET:/custom-property/type-conversion-overview
     */
    testCustomPropertyTypeConversion: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseModulesPropertyModelsViewCustomPropertyTypeConversionExampleViewModel,
        any
      >({
        path: `/custom-property/type-conversion-overview`,
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

    /**
     * No description
     *
     * @tags Dashboard
     * @name GetPropertyStatistics
     * @request GET:/dashboard/property
     */
    getPropertyStatistics: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsModelsDtosDashboardPropertyStatistics,
        any
      >({
        path: `/dashboard/property`,
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
      this.request<
        BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldBusinessComponentsDownloaderModelsDomainDownloadTask,
        any
      >({
        path: `/download-task`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags DownloadTask
     * @name AddDownloadTask
     * @request POST:/download-task
     */
    addDownloadTask: (
      data: BakabaseInsideWorldBusinessComponentsDownloaderModelsInputDownloadTaskAddInputModel,
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldBusinessComponentsDownloaderModelsDbDownloadTaskDbModel,
        any
      >({
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
     * @name DeleteDownloadTasks
     * @request DELETE:/download-task
     */
    deleteDownloadTasks: (
      data: BakabaseInsideWorldBusinessComponentsDownloaderModelsInputDownloadTaskDeleteInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/download-task`,
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
     * @name GetDownloadTask
     * @request GET:/download-task/{id}
     */
    getDownloadTask: (id: number, params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessComponentsDownloaderModelsDomainDownloadTask,
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
     * @name DeleteDownloadTask
     * @request DELETE:/download-task/{id}
     */
    deleteDownloadTask: (id: number, params: RequestParams = {}) =>
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
      data: BakabaseInsideWorldBusinessComponentsDownloaderModelsInputDownloadTaskPutInputModel,
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
     * @name StartDownloadTasks
     * @request POST:/download-task/download
     */
    startDownloadTasks: (
      data: BakabaseInsideWorldBusinessComponentsDownloaderModelsInputDownloadTaskStartRequestModel,
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

    /**
     * No description
     *
     * @tags DownloadTask
     * @name ExportAllDownloadTasks
     * @request GET:/download-task/xlsx
     */
    exportAllDownloadTasks: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/download-task/xlsx`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags DownloadTask
     * @name AddExHentaiDownloadTask
     * @request POST:/download-task/exhentai
     */
    addExHentaiDownloadTask: (
      data: BakabaseInsideWorldModelsRequestModelsExHentaiDownloadTaskAddInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/download-task/exhentai`,
        method: "POST",
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
     * @name GetResourceEnhancements
     * @request GET:/resource/{resourceId}/enhancement
     */
    getResourceEnhancements: (
      resourceId: number,
      query?: {
        /** [0: None, 1: GeneratedPropertyValue] */
        additionalItem?: BakabaseModulesEnhancerAbstractionsModelsDomainConstantsEnhancementAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseServiceModelsViewResourceEnhancements, any>({
        path: `/resource/${resourceId}/enhancement`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Enhancement
     * @name DeleteResourceEnhancement
     * @request DELETE:/resource/{resourceId}/enhancer/{enhancerId}/enhancement
     */
    deleteResourceEnhancement: (resourceId: number, enhancerId: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/${resourceId}/enhancer/${enhancerId}/enhancement`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Enhancement
     * @name EnhanceResourceByEnhancer
     * @request POST:/resource/{resourceId}/enhancer/{enhancerId}/enhancement
     */
    enhanceResourceByEnhancer: (resourceId: number, enhancerId: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/${resourceId}/enhancer/${enhancerId}/enhancement`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Enhancement
     * @name ApplyEnhancementContextDataForResourceByEnhancer
     * @request POST:/resource/{resourceId}/enhancer/{enhancerId}/enhancement/apply
     */
    applyEnhancementContextDataForResourceByEnhancer: (
      resourceId: number,
      enhancerId: number,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/${resourceId}/enhancer/${enhancerId}/enhancement/apply`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name GetSearchOperationsForProperty
     * @request GET:/resource/search-operation
     */
    getSearchOperationsForProperty: (
      query?: {
        /** [1: Internal, 2: Reserved, 4: Custom, 7: All] */
        propertyPool?: BakabaseAbstractionsModelsDomainConstantsPropertyPool;
        /** @format int32 */
        propertyId?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsListResponse1BakabaseAbstractionsModelsDomainConstantsSearchOperation,
        any
      >({
        path: `/resource/search-operation`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name GetFilterValueProperty
     * @request GET:/resource/filter-value-property
     */
    getFilterValueProperty: (
      query?: {
        /** [1: Internal, 2: Reserved, 4: Custom, 7: All] */
        propertyPool?: BakabaseAbstractionsModelsDomainConstantsPropertyPool;
        /** @format int32 */
        propertyId?: number;
        /** [1: Equals, 2: NotEquals, 3: Contains, 4: NotContains, 5: StartsWith, 6: NotStartsWith, 7: EndsWith, 8: NotEndsWith, 9: GreaterThan, 10: LessThan, 11: GreaterThanOrEquals, 12: LessThanOrEquals, 13: IsNull, 14: IsNotNull, 15: In, 16: NotIn, 17: Matches, 18: NotMatches] */
        operation?: BakabaseAbstractionsModelsDomainConstantsSearchOperation;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseModulesPropertyModelsViewPropertyViewModel,
        any
      >({
        path: `/resource/filter-value-property`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name GetLastResourceSearch
     * @request GET:/resource/last-search
     */
    getLastResourceSearch: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseServiceModelsViewResourceSearchViewModel,
        any
      >({
        path: `/resource/last-search`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name SaveNewResourceSearch
     * @request POST:/resource/saved-search
     */
    saveNewResourceSearch: (data: BakabaseServiceModelsInputSavedSearchAddInputModel, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/saved-search`,
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
     * @name GetSavedSearches
     * @request GET:/resource/saved-search
     */
    getSavedSearches: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseServiceModelsViewSavedSearchViewModel, any>({
        path: `/resource/saved-search`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name PutSavedSearchName
     * @request PUT:/resource/saved-search/{idx}/name
     */
    putSavedSearchName: (idx: number, data: string, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/saved-search/${idx}/name`,
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
     * @name DeleteSavedSearch
     * @request DELETE:/resource/saved-search/{idx}
     */
    deleteSavedSearch: (idx: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/saved-search/${idx}`,
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
    searchResources: (
      data: BakabaseServiceModelsInputResourceSearchInputModel,
      query?: {
        saveSearch?: boolean;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsSearchResponse1BakabaseAbstractionsModelsDomainResource, any>({
        path: `/resource/search`,
        method: "POST",
        query: query,
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
        /** [0: None, 64: Alias, 128: Category, 160: Properties, 416: DisplayName, 512: HasChildren, 2048: MediaLibraryName, 4096: Cache, 7136: All] */
        additionalItems?: BakabaseInsideWorldModelsConstantsAdditionalItemsResourceAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseAbstractionsModelsDomainResource, any>({
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
     * @name DiscoverResourceCover
     * @request GET:/resource/{id}/cover
     */
    discoverResourceCover: (id: number, params: RequestParams = {}) =>
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
     * @request PUT:/resource/{id}/cover
     */
    saveCover: (id: number, data: BakabaseServiceModelsInputResourceCoverSaveInputModel, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/${id}/cover`,
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
     * @name PutResourcePropertyValue
     * @request PUT:/resource/{id}/property-value
     */
    putResourcePropertyValue: (
      id: number,
      data: BakabaseAbstractionsModelsInputResourcePropertyValuePutInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/${id}/property-value`,
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
     * @name PlayResourceFile
     * @request GET:/resource/{resourceId}/play
     */
    playResourceFile: (
      resourceId: number,
      query?: {
        file?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/${resourceId}/play`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name DeleteResourcesByKeys
     * @request DELETE:/resource/ids
     */
    deleteResourcesByKeys: (
      query?: {
        ids?: number[];
        deleteFiles?: boolean;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/ids`,
        method: "DELETE",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name GetUnknownResources
     * @request GET:/resource/unknown
     */
    getUnknownResources: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseAbstractionsModelsDomainResource, any>({
        path: `/resource/unknown`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name DeleteUnknownResources
     * @request DELETE:/resource/unknown
     */
    deleteUnknownResources: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/unknown`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name GetUnknownResourcesCount
     * @request GET:/resource/unknown/count
     */
    getUnknownResourcesCount: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1SystemInt32, any>({
        path: `/resource/unknown/count`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name PinResource
     * @request PUT:/resource/{id}/pin
     */
    pinResource: (
      id: number,
      query?: {
        pin?: boolean;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/${id}/pin`,
        method: "PUT",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name TransferResourceData
     * @request PUT:/resource/transfer
     */
    transferResourceData: (
      data: BakabaseAbstractionsModelsInputResourceTransferInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/transfer`,
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
     * @name SearchResourcePaths
     * @request GET:/resource/paths
     */
    searchResourcePaths: (
      query?: {
        keyword?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseServiceModelsViewResourcePathInfoViewModel, any>({
        path: `/resource/paths`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Resource
     * @name MarkResourceAsNotPlayed
     * @request DELETE:/resource/{id}/played-at
     */
    markResourceAsNotPlayed: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/resource/${id}/played-at`,
        method: "DELETE",
        format: "json",
        ...params,
      }),
  };
  mediaLibrary = {
    /**
     * No description
     *
     * @tags Enhancement
     * @name DeleteByEnhancementsMediaLibrary
     * @request DELETE:/media-library/{mediaLibraryId}/enhancement
     */
    deleteByEnhancementsMediaLibrary: (
      mediaLibraryId: number,
      query?: {
        deleteEmptyOnly?: boolean;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library/${mediaLibraryId}/enhancement`,
        method: "DELETE",
        query: query,
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
        /** [0: None, 1: Category, 2: FileSystemInfo, 4: PathConfigurationBoundProperties] */
        additionalItems?: BakabaseInsideWorldModelsConstantsAdditionalItemsMediaLibraryAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseAbstractionsModelsDomainMediaLibrary, any>({
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
    addMediaLibrary: (data: BakabaseAbstractionsModelsDtoMediaLibraryAddDto, params: RequestParams = {}) =>
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
     * @name GetMediaLibrary
     * @request GET:/media-library/{id}
     */
    getMediaLibrary: (
      id: number,
      query?: {
        /** [0: None, 1: Category, 2: FileSystemInfo, 4: PathConfigurationBoundProperties] */
        additionalItems?: BakabaseInsideWorldModelsConstantsAdditionalItemsMediaLibraryAdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsDomainMediaLibrary, any>({
        path: `/media-library/${id}`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibrary
     * @name DeleteMediaLibrary
     * @request DELETE:/media-library/{id}
     */
    deleteMediaLibrary: (id: number, params: RequestParams = {}) =>
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
      data: BakabaseAbstractionsModelsDtoMediaLibraryPatchDto,
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
     * @name StartSyncMediaLibrary
     * @request PUT:/media-library/sync
     */
    startSyncMediaLibrary: (params: RequestParams = {}) =>
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
     * @name ValidatePathConfiguration
     * @request POST:/media-library/path-configuration-validation
     */
    validatePathConfiguration: (data: BakabaseAbstractionsModelsDomainPathConfiguration, params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsDomainPathConfigurationTestResult,
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
      data: BakabaseAbstractionsModelsInputMediaLibraryPathConfigurationAddInputModel,
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
     * @name AddMediaLibrariesInBulk
     * @request POST:/media-library/bulk-add/{cId}
     */
    addMediaLibrariesInBulk: (
      cId: number,
      data: BakabaseAbstractionsModelsInputMediaLibraryAddInBulkInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
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
      data: BakabaseAbstractionsModelsInputMediaLibraryRootPathsAddInBulkInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
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
     * @name StartSyncingMediaLibraryResources
     * @request PUT:/media-library/{id}/synchronization
     */
    startSyncingMediaLibraryResources: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library/${id}/synchronization`,
        method: "PUT",
        format: "json",
        ...params,
      }),
  };
  enhancer = {
    /**
     * No description
     *
     * @tags Enhancement
     * @name DeleteEnhancementsByEnhancer
     * @request DELETE:/enhancer/{enhancerId}/enhancement
     */
    deleteEnhancementsByEnhancer: (
      enhancerId: number,
      query?: {
        deleteEmptyOnly?: boolean;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/enhancer/${enhancerId}/enhancement`,
        method: "DELETE",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Enhancer
     * @name GetAllEnhancerDescriptors
     * @request GET:/enhancer/descriptor
     */
    getAllEnhancerDescriptors: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsListResponse1BakabaseModulesEnhancerAbstractionsComponentsIEnhancerDescriptor,
        any
      >({
        path: `/enhancer/descriptor`,
        method: "GET",
        format: "json",
        ...params,
      }),
  };
  extensionGroup = {
    /**
     * No description
     *
     * @tags ExtensionGroup
     * @name GetAllExtensionGroups
     * @request GET:/extension-group
     */
    getAllExtensionGroups: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseAbstractionsModelsDomainExtensionGroup, any>({
        path: `/extension-group`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags ExtensionGroup
     * @name AddExtensionGroup
     * @request POST:/extension-group
     */
    addExtensionGroup: (data: BakabaseAbstractionsModelsInputExtensionGroupAddInputModel, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsDomainExtensionGroup, any>({
        path: `/extension-group`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags ExtensionGroup
     * @name GetExtensionGroup
     * @request GET:/extension-group/{id}
     */
    getExtensionGroup: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsDomainExtensionGroup, any>({
        path: `/extension-group/${id}`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags ExtensionGroup
     * @name PutExtensionGroup
     * @request PUT:/extension-group/{id}
     */
    putExtensionGroup: (
      id: number,
      data: BakabaseAbstractionsModelsInputExtensionGroupPutInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/extension-group/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags ExtensionGroup
     * @name DeleteExtensionGroup
     * @request DELETE:/extension-group/{id}
     */
    deleteExtensionGroup: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/extension-group/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),
  };
  file = {
    /**
     * No description
     *
     * @tags File
     * @name GetTopLevelFileSystemEntryNames
     * @request GET:/file/top-level-file-system-entries
     */
    getTopLevelFileSystemEntryNames: (
      query?: {
        root?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsListResponse1BakabaseServiceModelsViewFileSystemEntryNameViewModel,
        any
      >({
        path: `/file/top-level-file-system-entries`,
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
      this.request<
        BootstrapModelsResponseModelsListResponse1BakabaseServiceModelsViewFileSystemEntryNameViewModel,
        any
      >({
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
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
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
        /** [1: UnknownFile, 2: Directory, 3: Dynamic] */
        type?: BakabaseInfrastructuresComponentsGuiIconType;
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
     * @name PreviewFileSystemEntriesGroupResult
     * @request PUT:/file/group-preview
     */
    previewFileSystemEntriesGroupResult: (
      data: BakabaseServiceModelsInputFileSystemEntryGroupInputModel,
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsListResponse1BakabaseServiceModelsViewFileSystemEntryGroupResultViewModel,
        any
      >({
        path: `/file/group-preview`,
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
     * @name MergeFileSystemEntries
     * @request PUT:/file/group
     */
    mergeFileSystemEntries: (
      data: BakabaseServiceModelsInputFileSystemEntryGroupInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/file/group`,
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

    /**
     * No description
     *
     * @tags File
     * @name CheckPathIsFile
     * @request GET:/file/is-file
     */
    checkPathIsFile: (
      query?: {
        path?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1SystemBoolean, any>({
        path: `/file/is-file`,
        method: "GET",
        query: query,
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
  mediaLibraryTemplate = {
    /**
     * No description
     *
     * @tags MediaLibraryTemplate
     * @name GetAllMediaLibraryTemplates
     * @request GET:/media-library-template
     */
    getAllMediaLibraryTemplates: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseAbstractionsModelsDomainMediaLibraryTemplate, any>(
        {
          path: `/media-library-template`,
          method: "GET",
          format: "json",
          ...params,
        },
      ),

    /**
     * No description
     *
     * @tags MediaLibraryTemplate
     * @name AddMediaLibraryTemplate
     * @request POST:/media-library-template
     */
    addMediaLibraryTemplate: (
      data: BakabaseAbstractionsModelsInputMediaLibraryTemplateAddInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library-template`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibraryTemplate
     * @name GetMediaLibraryTemplate
     * @request GET:/media-library-template/{id}
     */
    getMediaLibraryTemplate: (id: number, params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsDomainMediaLibraryTemplate,
        any
      >({
        path: `/media-library-template/${id}`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibraryTemplate
     * @name PutMediaLibraryTemplate
     * @request PUT:/media-library-template/{id}
     */
    putMediaLibraryTemplate: (
      id: number,
      data: BakabaseAbstractionsModelsDomainMediaLibraryTemplate,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library-template/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibraryTemplate
     * @name DeleteMediaLibraryTemplate
     * @request DELETE:/media-library-template/{id}
     */
    deleteMediaLibraryTemplate: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library-template/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibraryTemplate
     * @name GetMediaLibraryTemplateShareCode
     * @request GET:/media-library-template/{id}/share-text
     */
    getMediaLibraryTemplateShareCode: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1SystemString, any>({
        path: `/media-library-template/${id}/share-text`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibraryTemplate
     * @name ValidateMediaLibraryTemplateShareCode
     * @request POST:/media-library-template/share-code/validate
     */
    validateMediaLibraryTemplateShareCode: (data: string, params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsViewMediaLibraryTemplateValidationViewModel,
        any
      >({
        path: `/media-library-template/share-code/validate`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibraryTemplate
     * @name ImportMediaLibraryTemplate
     * @request POST:/media-library-template/share-code/import
     */
    importMediaLibraryTemplate: (
      data: BakabaseAbstractionsModelsInputMediaLibraryTemplateImportInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library-template/share-code/import`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibraryTemplate
     * @name AddMediaLibraryTemplateByMediaLibraryV1
     * @request POST:/media-library-template/by-media-library-v1
     */
    addMediaLibraryTemplateByMediaLibraryV1: (
      data: BakabaseAbstractionsModelsInputMediaLibraryTemplateAddByMediaLibraryV1InputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library-template/by-media-library-v1`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibraryTemplate
     * @name DuplicateMediaLibraryTemplate
     * @request POST:/media-library-template/{id}/duplicate
     */
    duplicateMediaLibraryTemplate: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library-template/${id}/duplicate`,
        method: "POST",
        format: "json",
        ...params,
      }),
  };
  mediaLibraryV2 = {
    /**
     * No description
     *
     * @tags MediaLibraryV2
     * @name GetAllMediaLibraryV2
     * @request GET:/media-library-v2
     */
    getAllMediaLibraryV2: (
      query?: {
        /** [0: None, 1: Template] */
        additionalItems?: BakabaseAbstractionsModelsDomainConstantsMediaLibraryV2AdditionalItem;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseAbstractionsModelsDomainMediaLibraryV2, any>({
        path: `/media-library-v2`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibraryV2
     * @name AddMediaLibraryV2
     * @request POST:/media-library-v2
     */
    addMediaLibraryV2: (
      data: BakabaseAbstractionsModelsInputMediaLibraryV2AddOrPutInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library-v2`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibraryV2
     * @name SaveAllMediaLibrariesV2
     * @request PUT:/media-library-v2
     */
    saveAllMediaLibrariesV2: (data: BakabaseAbstractionsModelsDomainMediaLibraryV2[], params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library-v2`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibraryV2
     * @name GetMediaLibraryV2
     * @request GET:/media-library-v2/{id}
     */
    getMediaLibraryV2: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsDomainMediaLibraryV2, any>({
        path: `/media-library-v2/${id}`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibraryV2
     * @name PutMediaLibraryV2
     * @request PUT:/media-library-v2/{id}
     */
    putMediaLibraryV2: (
      id: number,
      data: BakabaseAbstractionsModelsInputMediaLibraryV2AddOrPutInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library-v2/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibraryV2
     * @name DeleteMediaLibraryV2
     * @request DELETE:/media-library-v2/{id}
     */
    deleteMediaLibraryV2: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library-v2/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibraryV2
     * @name SyncMediaLibraryV2
     * @request POST:/media-library-v2/{id}/sync
     */
    syncMediaLibraryV2: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library-v2/${id}/sync`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags MediaLibraryV2
     * @name SyncAllMediaLibrariesV2
     * @request POST:/media-library-v2/sync-all
     */
    syncAllMediaLibrariesV2: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/media-library-v2/sync-all`,
        method: "POST",
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
     * @name PutAppOptions
     * @request PUT:/options/app
     */
    putAppOptions: (data: BakabaseInfrastructuresComponentsConfigurationsAppAppOptions, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/options/app`,
        method: "PUT",
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
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptions,
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
      data: BakabaseServiceModelsInputResourceOptionsPatchInputModel,
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
    patchThirdPartyOptions: (
      data: BakabaseInsideWorldModelsRequestModelsThirdPartyOptionsPatchInput,
      params: RequestParams = {},
    ) =>
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
     * @name PutThirdPartyOptions
     * @request PUT:/options/thirdparty
     */
    putThirdPartyOptions: (data: BakabaseInsideWorldModelsConfigsThirdPartyOptions, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/options/thirdparty`,
        method: "PUT",
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
    patchNetworkOptions: (
      data: BakabaseInsideWorldModelsRequestModelsOptionsNetworkOptionsPatchInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/options/network`,
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
     * @name GetEnhancerOptions
     * @request GET:/options/enhancer
     */
    getEnhancerOptions: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsEnhancerOptions, any>(
        {
          path: `/options/enhancer`,
          method: "GET",
          format: "json",
          ...params,
        },
      ),

    /**
     * No description
     *
     * @tags Options
     * @name PatchEnhancerOptions
     * @request PATCH:/options/enhancer
     */
    patchEnhancerOptions: (data: BakabaseInsideWorldModelsConfigsEnhancerOptions, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/options/enhancer`,
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
     * @name GetTaskOptions
     * @request GET:/options/task
     */
    getTaskOptions: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsComponentsConfigurationTaskOptions,
        any
      >({
        path: `/options/task`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name PatchTaskOptions
     * @request PATCH:/options/task
     */
    patchTaskOptions: (data: BakabaseAbstractionsComponentsConfigurationTaskOptions, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/options/task`,
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
     * @name GetAiOptions
     * @request GET:/options/ai
     */
    getAiOptions: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldBusinessConfigurationsModelsDomainAiOptions,
        any
      >({
        path: `/options/ai`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name PatchAiOptions
     * @request PATCH:/options/ai
     */
    patchAiOptions: (
      data: BakabaseInsideWorldBusinessConfigurationsModelsDomainAiOptions,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/options/ai`,
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
     * @name PutAiOptions
     * @request PUT:/options/ai
     */
    putAiOptions: (data: BakabaseInsideWorldBusinessConfigurationsModelsDomainAiOptions, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/options/ai`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Options
     * @name GetSoulPlusOptions
     * @request GET:/options/soulplus
     */
    getSoulPlusOptions: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1BakabaseInsideWorldModelsConfigsSoulPlusOptions, any>(
        {
          path: `/options/soulplus`,
          method: "GET",
          format: "json",
          ...params,
        },
      ),

    /**
     * No description
     *
     * @tags Options
     * @name PatchSoulPlusOptions
     * @request PATCH:/options/soulplus
     */
    patchSoulPlusOptions: (
      data: BakabaseInsideWorldModelsRequestModelsSoulPlusOptionsPatchInputModel,
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/options/soulplus`,
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
     * @name PutSoulPlusOptions
     * @request PUT:/options/soulplus
     */
    putSoulPlusOptions: (data: BakabaseInsideWorldModelsConfigsSoulPlusOptions, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/options/soulplus`,
        method: "PUT",
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
  playHistory = {
    /**
     * No description
     *
     * @tags PlayHistory
     * @name SearchPlayHistories
     * @request GET:/play-history
     */
    searchPlayHistories: (
      query?: {
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
      this.request<BootstrapModelsResponseModelsSearchResponse1BakabaseAbstractionsModelsDbPlayHistoryDbModel, any>({
        path: `/play-history`,
        method: "GET",
        query: query,
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
  postParser = {
    /**
     * No description
     *
     * @tags PostParser
     * @name GetAllPostParserTasks
     * @request GET:/post-parser/task/all
     */
    getAllPostParserTasks: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsListResponse1BakabaseInsideWorldBusinessComponentsPostParserModelsDomainPostParserTask,
        any
      >({
        path: `/post-parser/task/all`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PostParser
     * @name DeleteAllPostParserTasks
     * @request DELETE:/post-parser/task/all
     */
    deleteAllPostParserTasks: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/post-parser/task/all`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PostParser
     * @name AddPostParserTasks
     * @request POST:/post-parser/task
     */
    addPostParserTasks: (data: Record<string, string[]>, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/post-parser/task`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PostParser
     * @name DeletePostParserTask
     * @request DELETE:/post-parser/task/{id}
     */
    deletePostParserTask: (id: number, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/post-parser/task/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PostParser
     * @name StartAllPostParserTasks
     * @request POST:/post-parser/start
     */
    startAllPostParserTasks: (params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/post-parser/start`,
        method: "POST",
        format: "json",
        ...params,
      }),
  };
  property = {
    /**
     * No description
     *
     * @tags Property
     * @name GetPropertiesByPool
     * @request GET:/property/pool/{pool}
     */
    getPropertiesByPool: (pool: BakabaseAbstractionsModelsDomainConstantsPropertyPool, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsListResponse1BakabaseModulesPropertyModelsViewPropertyViewModel, any>({
        path: `/property/pool/${pool}`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Property
     * @name GetAvailablePropertyTypesForManuallySettingValue
     * @request GET:/property/property-types-for-manually-setting-value
     */
    getAvailablePropertyTypesForManuallySettingValue: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsListResponse1BakabaseServiceModelsViewPropertyTypeForManuallySettingValueViewModel,
        any
      >({
        path: `/property/property-types-for-manually-setting-value`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Property
     * @name GetPropertyBizValue
     * @request GET:/property/pool/{pool}/id/{id}/biz-value
     */
    getPropertyBizValue: (
      pool: BakabaseAbstractionsModelsDomainConstantsPropertyPool,
      id: number,
      query?: {
        dbValue?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1SystemString, any>({
        path: `/property/pool/${pool}/id/${id}/biz-value`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Property
     * @name GetPropertyDbValue
     * @request GET:/property/pool/{pool}/id/{id}/db-value
     */
    getPropertyDbValue: (
      pool: BakabaseAbstractionsModelsDomainConstantsPropertyPool,
      id: number,
      query?: {
        bizValue?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1SystemString, any>({
        path: `/property/pool/${pool}/id/${id}/db-value`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
  };
  specialText = {
    /**
     * No description
     *
     * @tags SpecialText
     * @name GetAllSpecialTexts
     * @request GET:/special-text
     */
    getAllSpecialTexts: (params: RequestParams = {}) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2SystemInt32SystemCollectionsGenericList1BakabaseAbstractionsModelsDomainSpecialText,
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
     * @name AddSpecialText
     * @request POST:/special-text
     */
    addSpecialText: (data: BakabaseAbstractionsModelsInputSpecialTextAddInputModel, params: RequestParams = {}) =>
      this.request<BootstrapModelsResponseModelsSingletonResponse1BakabaseAbstractionsModelsDomainSpecialText, any>({
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
     * @name PatchSpecialText
     * @request PUT:/special-text/{id}
     */
    patchSpecialText: (
      id: number,
      data: BakabaseAbstractionsModelsInputSpecialTextPatchInputModel,
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
  tampermonkey = {
    /**
     * No description
     *
     * @tags Tampermonkey
     * @name InstallTampermonkeyScript
     * @request GET:/Tampermonkey/install
     */
    installTampermonkeyScript: (
      query?: {
        /** [1: SoulPlus, 2: ExHentai] */
        script?: BakabaseInsideWorldBusinessComponentsTampermonkeyModelsConstantsTampermonkeyScript;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/Tampermonkey/install`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Tampermonkey
     * @name GetTampermonkeyScript
     * @request GET:/Tampermonkey/script/{script}.user.js
     */
    getTampermonkeyScript: (
      script: BakabaseInsideWorldBusinessComponentsTampermonkeyModelsConstantsTampermonkeyScript,
      params: RequestParams = {},
    ) =>
      this.request<void, any>({
        path: `/Tampermonkey/script/${script}.user.js`,
        method: "GET",
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

    /**
     * No description
     *
     * @tags Tool
     * @name GetThumbnail
     * @request GET:/tool/thumbnail
     */
    getThumbnail: (
      query?: {
        path?: string;
        /** @format int32 */
        w?: number;
        /** @format int32 */
        h?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<void, any>({
        path: `/tool/thumbnail`,
        method: "GET",
        query: query,
        ...params,
      }),

    /**
     * No description
     *
     * @tags Tool
     * @name TestMatchAll
     * @request POST:/tool/match-all
     */
    testMatchAll: (
      query?: {
        regex?: string;
        text?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        BootstrapModelsResponseModelsSingletonResponse1SystemCollectionsGenericDictionary2SystemStringSystemCollectionsGenericList1SystemString,
        any
      >({
        path: `/tool/match-all`,
        method: "POST",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Tool
     * @name OpenFile
     * @request GET:/tool/open-file
     */
    openFile: (
      query?: {
        path?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapModelsResponseModelsBaseResponse, any>({
        path: `/tool/open-file`,
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
