// AUTO-GENERATED FROM server enums. Do not edit manually.
/* eslint-disable */

export enum CategoryResourceDisplayNameSegmentType {
  StaticText = 1,
  Property = 2,
  LeftWrapper = 3,
  RightWrapper = 4
}

export const categoryResourceDisplayNameSegmentTypes = [
  { label: 'StaticText', value: CategoryResourceDisplayNameSegmentType.StaticText },
  { label: 'Property', value: CategoryResourceDisplayNameSegmentType.Property },
  { label: 'LeftWrapper', value: CategoryResourceDisplayNameSegmentType.LeftWrapper },
  { label: 'RightWrapper', value: CategoryResourceDisplayNameSegmentType.RightWrapper }
] as const;

export const CategoryResourceDisplayNameSegmentTypeLabel: Record<CategoryResourceDisplayNameSegmentType, string> = {
  [CategoryResourceDisplayNameSegmentType.StaticText]: 'StaticText',
  [CategoryResourceDisplayNameSegmentType.Property]: 'Property',
  [CategoryResourceDisplayNameSegmentType.LeftWrapper]: 'LeftWrapper',
  [CategoryResourceDisplayNameSegmentType.RightWrapper]: 'RightWrapper'
};

export enum BTaskLevel {
  Default = 1,
  Critical = 2
}

export const bTaskLevels = [
  { label: 'Default', value: BTaskLevel.Default },
  { label: 'Critical', value: BTaskLevel.Critical }
] as const;

export const BTaskLevelLabel: Record<BTaskLevel, string> = {
  [BTaskLevel.Default]: 'Default',
  [BTaskLevel.Critical]: 'Critical'
};

export enum BTaskStatus {
  NotStarted = 1,
  Running = 2,
  Paused = 3,
  Error = 4,
  Completed = 5,
  Cancelled = 6
}

export const bTaskStatuses = [
  { label: 'NotStarted', value: BTaskStatus.NotStarted },
  { label: 'Running', value: BTaskStatus.Running },
  { label: 'Paused', value: BTaskStatus.Paused },
  { label: 'Error', value: BTaskStatus.Error },
  { label: 'Completed', value: BTaskStatus.Completed },
  { label: 'Cancelled', value: BTaskStatus.Cancelled }
] as const;

export const BTaskStatusLabel: Record<BTaskStatus, string> = {
  [BTaskStatus.NotStarted]: 'NotStarted',
  [BTaskStatus.Running]: 'Running',
  [BTaskStatus.Paused]: 'Paused',
  [BTaskStatus.Error]: 'Error',
  [BTaskStatus.Completed]: 'Completed',
  [BTaskStatus.Cancelled]: 'Cancelled'
};

export enum EnhancementRecordStatus {
  ContextCreated = 1,
  ContextApplied = 2
}

export const enhancementRecordStatuses = [
  { label: 'ContextCreated', value: EnhancementRecordStatus.ContextCreated },
  { label: 'ContextApplied', value: EnhancementRecordStatus.ContextApplied }
] as const;

export const EnhancementRecordStatusLabel: Record<EnhancementRecordStatus, string> = {
  [EnhancementRecordStatus.ContextCreated]: 'ContextCreated',
  [EnhancementRecordStatus.ContextApplied]: 'ContextApplied'
};

export enum FileExtensionGroup {
  Image = 1,
  Audio = 2,
  Video = 3,
  Document = 4,
  Application = 5,
  Archive = 6
}

export const fileExtensionGroups = [
  { label: 'Image', value: FileExtensionGroup.Image },
  { label: 'Audio', value: FileExtensionGroup.Audio },
  { label: 'Video', value: FileExtensionGroup.Video },
  { label: 'Document', value: FileExtensionGroup.Document },
  { label: 'Application', value: FileExtensionGroup.Application },
  { label: 'Archive', value: FileExtensionGroup.Archive }
] as const;

export const FileExtensionGroupLabel: Record<FileExtensionGroup, string> = {
  [FileExtensionGroup.Image]: 'Image',
  [FileExtensionGroup.Audio]: 'Audio',
  [FileExtensionGroup.Video]: 'Video',
  [FileExtensionGroup.Document]: 'Document',
  [FileExtensionGroup.Application]: 'Application',
  [FileExtensionGroup.Archive]: 'Archive'
};

export enum InitializationContentType {
  NotAcceptTerms = 1,
  NeedRestart = 2
}

export const initializationContentTypes = [
  { label: 'NotAcceptTerms', value: InitializationContentType.NotAcceptTerms },
  { label: 'NeedRestart', value: InitializationContentType.NeedRestart }
] as const;

export const InitializationContentTypeLabel: Record<InitializationContentType, string> = {
  [InitializationContentType.NotAcceptTerms]: 'NotAcceptTerms',
  [InitializationContentType.NeedRestart]: 'NeedRestart'
};

export enum InternalProperty {
  RootPath = 1,
  ParentResource = 2,
  Resource = 3,
  Filename = 15,
  DirectoryPath = 16,
  CreatedAt = 17,
  FileCreatedAt = 18,
  FileModifiedAt = 19,
  Category = 20,
  MediaLibrary = 21,
  MediaLibraryV2 = 24
}

export const internalProperties = [
  { label: 'RootPath', value: InternalProperty.RootPath },
  { label: 'ParentResource', value: InternalProperty.ParentResource },
  { label: 'Resource', value: InternalProperty.Resource },
  { label: 'Filename', value: InternalProperty.Filename },
  { label: 'DirectoryPath', value: InternalProperty.DirectoryPath },
  { label: 'CreatedAt', value: InternalProperty.CreatedAt },
  { label: 'FileCreatedAt', value: InternalProperty.FileCreatedAt },
  { label: 'FileModifiedAt', value: InternalProperty.FileModifiedAt },
  { label: 'Category', value: InternalProperty.Category },
  { label: 'MediaLibrary', value: InternalProperty.MediaLibrary },
  { label: 'MediaLibraryV2', value: InternalProperty.MediaLibraryV2 }
] as const;

export const InternalPropertyLabel: Record<InternalProperty, string> = {
  [InternalProperty.RootPath]: 'RootPath',
  [InternalProperty.ParentResource]: 'ParentResource',
  [InternalProperty.Resource]: 'Resource',
  [InternalProperty.Filename]: 'Filename',
  [InternalProperty.DirectoryPath]: 'DirectoryPath',
  [InternalProperty.CreatedAt]: 'CreatedAt',
  [InternalProperty.FileCreatedAt]: 'FileCreatedAt',
  [InternalProperty.FileModifiedAt]: 'FileModifiedAt',
  [InternalProperty.Category]: 'Category',
  [InternalProperty.MediaLibrary]: 'MediaLibrary',
  [InternalProperty.MediaLibraryV2]: 'MediaLibraryV2'
};

export enum MediaLibraryTemplateAdditionalItem {
  None = 0,
  ChildTemplate = 1
}

export const mediaLibraryTemplateAdditionalItems = [
  { label: 'None', value: MediaLibraryTemplateAdditionalItem.None },
  { label: 'ChildTemplate', value: MediaLibraryTemplateAdditionalItem.ChildTemplate }
] as const;

export const MediaLibraryTemplateAdditionalItemLabel: Record<MediaLibraryTemplateAdditionalItem, string> = {
  [MediaLibraryTemplateAdditionalItem.None]: 'None',
  [MediaLibraryTemplateAdditionalItem.ChildTemplate]: 'ChildTemplate'
};

export enum MediaLibraryV2AdditionalItem {
  None = 0,
  Template = 1
}

export const mediaLibraryV2AdditionalItems = [
  { label: 'None', value: MediaLibraryV2AdditionalItem.None },
  { label: 'Template', value: MediaLibraryV2AdditionalItem.Template }
] as const;

export const MediaLibraryV2AdditionalItemLabel: Record<MediaLibraryV2AdditionalItem, string> = {
  [MediaLibraryV2AdditionalItem.None]: 'None',
  [MediaLibraryV2AdditionalItem.Template]: 'Template'
};

export enum PathFilterFsType {
  File = 1,
  Directory = 2
}

export const pathFilterFsTypes = [
  { label: 'File', value: PathFilterFsType.File },
  { label: 'Directory', value: PathFilterFsType.Directory }
] as const;

export const PathFilterFsTypeLabel: Record<PathFilterFsType, string> = {
  [PathFilterFsType.File]: 'File',
  [PathFilterFsType.Directory]: 'Directory'
};

export enum PathPositioner {
  Layer = 1,
  Regex = 2
}

export const pathPositioners = [
  { label: 'Layer', value: PathPositioner.Layer },
  { label: 'Regex', value: PathPositioner.Regex }
] as const;

export const PathPositionerLabel: Record<PathPositioner, string> = {
  [PathPositioner.Layer]: 'Layer',
  [PathPositioner.Regex]: 'Regex'
};

export enum PathPropertyExtractorBasePathType {
  MediaLibrary = 1,
  Resource = 2
}

export const pathPropertyExtractorBasePathTypes = [
  { label: 'MediaLibrary', value: PathPropertyExtractorBasePathType.MediaLibrary },
  { label: 'Resource', value: PathPropertyExtractorBasePathType.Resource }
] as const;

export const PathPropertyExtractorBasePathTypeLabel: Record<PathPropertyExtractorBasePathType, string> = {
  [PathPropertyExtractorBasePathType.MediaLibrary]: 'MediaLibrary',
  [PathPropertyExtractorBasePathType.Resource]: 'Resource'
};

export enum PropertyPool {
  Internal = 1,
  Reserved = 2,
  Custom = 4,
  All = 7
}

export const propertyPools = [
  { label: 'Internal', value: PropertyPool.Internal },
  { label: 'Reserved', value: PropertyPool.Reserved },
  { label: 'Custom', value: PropertyPool.Custom },
  { label: 'All', value: PropertyPool.All }
] as const;

export const PropertyPoolLabel: Record<PropertyPool, string> = {
  [PropertyPool.Internal]: 'Internal',
  [PropertyPool.Reserved]: 'Reserved',
  [PropertyPool.Custom]: 'Custom',
  [PropertyPool.All]: 'All'
};

export enum PropertyType {
  SingleLineText = 1,
  MultilineText = 2,
  SingleChoice = 3,
  MultipleChoice = 4,
  Number = 5,
  Percentage = 6,
  Rating = 7,
  Boolean = 8,
  Link = 9,
  Attachment = 10,
  Date = 11,
  DateTime = 12,
  Time = 13,
  Formula = 14,
  Multilevel = 15,
  Tags = 16
}

export const propertyTypes = [
  { label: 'SingleLineText', value: PropertyType.SingleLineText },
  { label: 'MultilineText', value: PropertyType.MultilineText },
  { label: 'SingleChoice', value: PropertyType.SingleChoice },
  { label: 'MultipleChoice', value: PropertyType.MultipleChoice },
  { label: 'Number', value: PropertyType.Number },
  { label: 'Percentage', value: PropertyType.Percentage },
  { label: 'Rating', value: PropertyType.Rating },
  { label: 'Boolean', value: PropertyType.Boolean },
  { label: 'Link', value: PropertyType.Link },
  { label: 'Attachment', value: PropertyType.Attachment },
  { label: 'Date', value: PropertyType.Date },
  { label: 'DateTime', value: PropertyType.DateTime },
  { label: 'Time', value: PropertyType.Time },
  { label: 'Formula', value: PropertyType.Formula },
  { label: 'Multilevel', value: PropertyType.Multilevel },
  { label: 'Tags', value: PropertyType.Tags }
] as const;

export const PropertyTypeLabel: Record<PropertyType, string> = {
  [PropertyType.SingleLineText]: 'SingleLineText',
  [PropertyType.MultilineText]: 'MultilineText',
  [PropertyType.SingleChoice]: 'SingleChoice',
  [PropertyType.MultipleChoice]: 'MultipleChoice',
  [PropertyType.Number]: 'Number',
  [PropertyType.Percentage]: 'Percentage',
  [PropertyType.Rating]: 'Rating',
  [PropertyType.Boolean]: 'Boolean',
  [PropertyType.Link]: 'Link',
  [PropertyType.Attachment]: 'Attachment',
  [PropertyType.Date]: 'Date',
  [PropertyType.DateTime]: 'DateTime',
  [PropertyType.Time]: 'Time',
  [PropertyType.Formula]: 'Formula',
  [PropertyType.Multilevel]: 'Multilevel',
  [PropertyType.Tags]: 'Tags'
};

export enum PropertyValueScope {
  Manual = 0,
  Synchronization = 1,
  BakabaseEnhancer = 1000,
  ExHentaiEnhancer = 1001,
  BangumiEnhancer = 1002,
  DLsiteEnhancer = 1003,
  RegexEnhancer = 1004,
  KodiEnhancer = 1005,
  TmdbEnhancer = 1006,
  AvEnhancer = 1007
}

export const propertyValueScopes = [
  { label: 'Manual', value: PropertyValueScope.Manual },
  { label: 'Synchronization', value: PropertyValueScope.Synchronization },
  { label: 'BakabaseEnhancer', value: PropertyValueScope.BakabaseEnhancer },
  { label: 'ExHentaiEnhancer', value: PropertyValueScope.ExHentaiEnhancer },
  { label: 'BangumiEnhancer', value: PropertyValueScope.BangumiEnhancer },
  { label: 'DLsiteEnhancer', value: PropertyValueScope.DLsiteEnhancer },
  { label: 'RegexEnhancer', value: PropertyValueScope.RegexEnhancer },
  { label: 'KodiEnhancer', value: PropertyValueScope.KodiEnhancer },
  { label: 'TmdbEnhancer', value: PropertyValueScope.TmdbEnhancer },
  { label: 'AvEnhancer', value: PropertyValueScope.AvEnhancer }
] as const;

export const PropertyValueScopeLabel: Record<PropertyValueScope, string> = {
  [PropertyValueScope.Manual]: 'Manual',
  [PropertyValueScope.Synchronization]: 'Synchronization',
  [PropertyValueScope.BakabaseEnhancer]: 'BakabaseEnhancer',
  [PropertyValueScope.ExHentaiEnhancer]: 'ExHentaiEnhancer',
  [PropertyValueScope.BangumiEnhancer]: 'BangumiEnhancer',
  [PropertyValueScope.DLsiteEnhancer]: 'DLsiteEnhancer',
  [PropertyValueScope.RegexEnhancer]: 'RegexEnhancer',
  [PropertyValueScope.KodiEnhancer]: 'KodiEnhancer',
  [PropertyValueScope.TmdbEnhancer]: 'TmdbEnhancer',
  [PropertyValueScope.AvEnhancer]: 'AvEnhancer'
};

export enum ReservedProperty {
  Introduction = 12,
  Rating = 13,
  Cover = 22
}

export const reservedProperties = [
  { label: 'Introduction', value: ReservedProperty.Introduction },
  { label: 'Rating', value: ReservedProperty.Rating },
  { label: 'Cover', value: ReservedProperty.Cover }
] as const;

export const ReservedPropertyLabel: Record<ReservedProperty, string> = {
  [ReservedProperty.Introduction]: 'Introduction',
  [ReservedProperty.Rating]: 'Rating',
  [ReservedProperty.Cover]: 'Cover'
};

export enum ResourceCacheType {
  Covers = 1,
  PlayableFiles = 2
}

export const resourceCacheTypes = [
  { label: 'Covers', value: ResourceCacheType.Covers },
  { label: 'PlayableFiles', value: ResourceCacheType.PlayableFiles }
] as const;

export const ResourceCacheTypeLabel: Record<ResourceCacheType, string> = {
  [ResourceCacheType.Covers]: 'Covers',
  [ResourceCacheType.PlayableFiles]: 'PlayableFiles'
};

export enum ResourceTag {
  IsParent = 1,
  Pinned = 2,
  PathDoesNotExist = 4,
  UnknownMediaLibrary = 8
}

export const resourceTags = [
  { label: 'IsParent', value: ResourceTag.IsParent },
  { label: 'Pinned', value: ResourceTag.Pinned },
  { label: 'PathDoesNotExist', value: ResourceTag.PathDoesNotExist },
  { label: 'UnknownMediaLibrary', value: ResourceTag.UnknownMediaLibrary }
] as const;

export const ResourceTagLabel: Record<ResourceTag, string> = {
  [ResourceTag.IsParent]: 'IsParent',
  [ResourceTag.Pinned]: 'Pinned',
  [ResourceTag.PathDoesNotExist]: 'PathDoesNotExist',
  [ResourceTag.UnknownMediaLibrary]: 'UnknownMediaLibrary'
};

export enum SearchCombinator {
  And = 1,
  Or = 2
}

export const searchCombinators = [
  { label: 'And', value: SearchCombinator.And },
  { label: 'Or', value: SearchCombinator.Or }
] as const;

export const SearchCombinatorLabel: Record<SearchCombinator, string> = {
  [SearchCombinator.And]: 'And',
  [SearchCombinator.Or]: 'Or'
};

export enum SearchOperation {
  Equals = 1,
  NotEquals = 2,
  Contains = 3,
  NotContains = 4,
  StartsWith = 5,
  NotStartsWith = 6,
  EndsWith = 7,
  NotEndsWith = 8,
  GreaterThan = 9,
  LessThan = 10,
  GreaterThanOrEquals = 11,
  LessThanOrEquals = 12,
  IsNull = 13,
  IsNotNull = 14,
  In = 15,
  NotIn = 16,
  Matches = 17,
  NotMatches = 18
}

export const searchOperations = [
  { label: 'Equals', value: SearchOperation.Equals },
  { label: 'NotEquals', value: SearchOperation.NotEquals },
  { label: 'Contains', value: SearchOperation.Contains },
  { label: 'NotContains', value: SearchOperation.NotContains },
  { label: 'StartsWith', value: SearchOperation.StartsWith },
  { label: 'NotStartsWith', value: SearchOperation.NotStartsWith },
  { label: 'EndsWith', value: SearchOperation.EndsWith },
  { label: 'NotEndsWith', value: SearchOperation.NotEndsWith },
  { label: 'GreaterThan', value: SearchOperation.GreaterThan },
  { label: 'LessThan', value: SearchOperation.LessThan },
  { label: 'GreaterThanOrEquals', value: SearchOperation.GreaterThanOrEquals },
  { label: 'LessThanOrEquals', value: SearchOperation.LessThanOrEquals },
  { label: 'IsNull', value: SearchOperation.IsNull },
  { label: 'IsNotNull', value: SearchOperation.IsNotNull },
  { label: 'In', value: SearchOperation.In },
  { label: 'NotIn', value: SearchOperation.NotIn },
  { label: 'Matches', value: SearchOperation.Matches },
  { label: 'NotMatches', value: SearchOperation.NotMatches }
] as const;

export const SearchOperationLabel: Record<SearchOperation, string> = {
  [SearchOperation.Equals]: 'Equals',
  [SearchOperation.NotEquals]: 'NotEquals',
  [SearchOperation.Contains]: 'Contains',
  [SearchOperation.NotContains]: 'NotContains',
  [SearchOperation.StartsWith]: 'StartsWith',
  [SearchOperation.NotStartsWith]: 'NotStartsWith',
  [SearchOperation.EndsWith]: 'EndsWith',
  [SearchOperation.NotEndsWith]: 'NotEndsWith',
  [SearchOperation.GreaterThan]: 'GreaterThan',
  [SearchOperation.LessThan]: 'LessThan',
  [SearchOperation.GreaterThanOrEquals]: 'GreaterThanOrEquals',
  [SearchOperation.LessThanOrEquals]: 'LessThanOrEquals',
  [SearchOperation.IsNull]: 'IsNull',
  [SearchOperation.IsNotNull]: 'IsNotNull',
  [SearchOperation.In]: 'In',
  [SearchOperation.NotIn]: 'NotIn',
  [SearchOperation.Matches]: 'Matches',
  [SearchOperation.NotMatches]: 'NotMatches'
};

export enum SpecialTextType {
  Useless = 1,
  Wrapper = 3,
  Standardization = 4,
  Volume = 6,
  Trim = 7,
  DateTime = 8,
  Language = 9
}

export const specialTextTypes = [
  { label: 'Useless', value: SpecialTextType.Useless },
  { label: 'Wrapper', value: SpecialTextType.Wrapper },
  { label: 'Standardization', value: SpecialTextType.Standardization },
  { label: 'Volume', value: SpecialTextType.Volume },
  { label: 'Trim', value: SpecialTextType.Trim },
  { label: 'DateTime', value: SpecialTextType.DateTime },
  { label: 'Language', value: SpecialTextType.Language }
] as const;

export const SpecialTextTypeLabel: Record<SpecialTextType, string> = {
  [SpecialTextType.Useless]: 'Useless',
  [SpecialTextType.Wrapper]: 'Wrapper',
  [SpecialTextType.Standardization]: 'Standardization',
  [SpecialTextType.Volume]: 'Volume',
  [SpecialTextType.Trim]: 'Trim',
  [SpecialTextType.DateTime]: 'DateTime',
  [SpecialTextType.Language]: 'Language'
};

export enum StandardValueType {
  String = 1,
  ListString = 2,
  Decimal = 3,
  Link = 4,
  Boolean = 5,
  DateTime = 6,
  Time = 7,
  ListListString = 8,
  ListTag = 9
}

export const standardValueTypes = [
  { label: 'String', value: StandardValueType.String },
  { label: 'ListString', value: StandardValueType.ListString },
  { label: 'Decimal', value: StandardValueType.Decimal },
  { label: 'Link', value: StandardValueType.Link },
  { label: 'Boolean', value: StandardValueType.Boolean },
  { label: 'DateTime', value: StandardValueType.DateTime },
  { label: 'Time', value: StandardValueType.Time },
  { label: 'ListListString', value: StandardValueType.ListListString },
  { label: 'ListTag', value: StandardValueType.ListTag }
] as const;

export const StandardValueTypeLabel: Record<StandardValueType, string> = {
  [StandardValueType.String]: 'String',
  [StandardValueType.ListString]: 'ListString',
  [StandardValueType.Decimal]: 'Decimal',
  [StandardValueType.Link]: 'Link',
  [StandardValueType.Boolean]: 'Boolean',
  [StandardValueType.DateTime]: 'DateTime',
  [StandardValueType.Time]: 'Time',
  [StandardValueType.ListListString]: 'ListListString',
  [StandardValueType.ListTag]: 'ListTag'
};

export enum TextProcessingOperation {
  Delete = 1,
  SetWithFixedValue = 2,
  AddToStart = 3,
  AddToEnd = 4,
  AddToAnyPosition = 5,
  RemoveFromStart = 6,
  RemoveFromEnd = 7,
  RemoveFromAnyPosition = 8,
  ReplaceFromStart = 9,
  ReplaceFromEnd = 10,
  ReplaceFromAnyPosition = 11,
  ReplaceWithRegex = 12
}

export const textProcessingOperations = [
  { label: 'Delete', value: TextProcessingOperation.Delete },
  { label: 'SetWithFixedValue', value: TextProcessingOperation.SetWithFixedValue },
  { label: 'AddToStart', value: TextProcessingOperation.AddToStart },
  { label: 'AddToEnd', value: TextProcessingOperation.AddToEnd },
  { label: 'AddToAnyPosition', value: TextProcessingOperation.AddToAnyPosition },
  { label: 'RemoveFromStart', value: TextProcessingOperation.RemoveFromStart },
  { label: 'RemoveFromEnd', value: TextProcessingOperation.RemoveFromEnd },
  { label: 'RemoveFromAnyPosition', value: TextProcessingOperation.RemoveFromAnyPosition },
  { label: 'ReplaceFromStart', value: TextProcessingOperation.ReplaceFromStart },
  { label: 'ReplaceFromEnd', value: TextProcessingOperation.ReplaceFromEnd },
  { label: 'ReplaceFromAnyPosition', value: TextProcessingOperation.ReplaceFromAnyPosition },
  { label: 'ReplaceWithRegex', value: TextProcessingOperation.ReplaceWithRegex }
] as const;

export const TextProcessingOperationLabel: Record<TextProcessingOperation, string> = {
  [TextProcessingOperation.Delete]: 'Delete',
  [TextProcessingOperation.SetWithFixedValue]: 'SetWithFixedValue',
  [TextProcessingOperation.AddToStart]: 'AddToStart',
  [TextProcessingOperation.AddToEnd]: 'AddToEnd',
  [TextProcessingOperation.AddToAnyPosition]: 'AddToAnyPosition',
  [TextProcessingOperation.RemoveFromStart]: 'RemoveFromStart',
  [TextProcessingOperation.RemoveFromEnd]: 'RemoveFromEnd',
  [TextProcessingOperation.RemoveFromAnyPosition]: 'RemoveFromAnyPosition',
  [TextProcessingOperation.ReplaceFromStart]: 'ReplaceFromStart',
  [TextProcessingOperation.ReplaceFromEnd]: 'ReplaceFromEnd',
  [TextProcessingOperation.ReplaceFromAnyPosition]: 'ReplaceFromAnyPosition',
  [TextProcessingOperation.ReplaceWithRegex]: 'ReplaceWithRegex'
};

export enum BTaskDuplicateIdHandling {
  Reject = 1,
  Ignore = 2,
  Replace = 3
}

export const bTaskDuplicateIdHandlings = [
  { label: 'Reject', value: BTaskDuplicateIdHandling.Reject },
  { label: 'Ignore', value: BTaskDuplicateIdHandling.Ignore },
  { label: 'Replace', value: BTaskDuplicateIdHandling.Replace }
] as const;

export const BTaskDuplicateIdHandlingLabel: Record<BTaskDuplicateIdHandling, string> = {
  [BTaskDuplicateIdHandling.Reject]: 'Reject',
  [BTaskDuplicateIdHandling.Ignore]: 'Ignore',
  [BTaskDuplicateIdHandling.Replace]: 'Replace'
};

export enum BTaskResourceType {
  FileSystemEntry = 1,
  Resource = 2,
  Any = 1000
}

export const bTaskResourceTypes = [
  { label: 'FileSystemEntry', value: BTaskResourceType.FileSystemEntry },
  { label: 'Resource', value: BTaskResourceType.Resource },
  { label: 'Any', value: BTaskResourceType.Any }
] as const;

export const BTaskResourceTypeLabel: Record<BTaskResourceType, string> = {
  [BTaskResourceType.FileSystemEntry]: 'FileSystemEntry',
  [BTaskResourceType.Resource]: 'Resource',
  [BTaskResourceType.Any]: 'Any'
};

export enum BTaskType {
  Decompress = 1,
  MoveFiles = 2,
  MoveResources = 3,
  Any = 1000
}

export const bTaskTypes = [
  { label: 'Decompress', value: BTaskType.Decompress },
  { label: 'MoveFiles', value: BTaskType.MoveFiles },
  { label: 'MoveResources', value: BTaskType.MoveResources },
  { label: 'Any', value: BTaskType.Any }
] as const;

export const BTaskTypeLabel: Record<BTaskType, string> = {
  [BTaskType.Decompress]: 'Decompress',
  [BTaskType.MoveFiles]: 'MoveFiles',
  [BTaskType.MoveResources]: 'MoveResources',
  [BTaskType.Any]: 'Any'
};

export enum CoverDiscoverResultType {
  LocalFile = 1,
  FromAdditionalSource = 2,
  Icon = 3
}

export const coverDiscoverResultTypes = [
  { label: 'LocalFile', value: CoverDiscoverResultType.LocalFile },
  { label: 'FromAdditionalSource', value: CoverDiscoverResultType.FromAdditionalSource },
  { label: 'Icon', value: CoverDiscoverResultType.Icon }
] as const;

export const CoverDiscoverResultTypeLabel: Record<CoverDiscoverResultType, string> = {
  [CoverDiscoverResultType.LocalFile]: 'LocalFile',
  [CoverDiscoverResultType.FromAdditionalSource]: 'FromAdditionalSource',
  [CoverDiscoverResultType.Icon]: 'Icon'
};

export enum ResourceExistence {
  Exist = 1,
  Maybe = 2,
  New = 3
}

export const resourceExistences = [
  { label: 'Exist', value: ResourceExistence.Exist },
  { label: 'Maybe', value: ResourceExistence.Maybe },
  { label: 'New', value: ResourceExistence.New }
] as const;

export const ResourceExistenceLabel: Record<ResourceExistence, string> = {
  [ResourceExistence.Exist]: 'Exist',
  [ResourceExistence.Maybe]: 'Maybe',
  [ResourceExistence.New]: 'New'
};

export enum AdditionalCoverDiscoveringSource {
  CompressedFile = 1,
  Video = 2
}

export const additionalCoverDiscoveringSources = [
  { label: 'CompressedFile', value: AdditionalCoverDiscoveringSource.CompressedFile },
  { label: 'Video', value: AdditionalCoverDiscoveringSource.Video }
] as const;

export const AdditionalCoverDiscoveringSourceLabel: Record<AdditionalCoverDiscoveringSource, string> = {
  [AdditionalCoverDiscoveringSource.CompressedFile]: 'CompressedFile',
  [AdditionalCoverDiscoveringSource.Video]: 'Video'
};

export enum BackgroundTaskStatus {
  Running = 1,
  Complete = 2,
  Failed = 3
}

export const backgroundTaskStatuses = [
  { label: 'Running', value: BackgroundTaskStatus.Running },
  { label: 'Complete', value: BackgroundTaskStatus.Complete },
  { label: 'Failed', value: BackgroundTaskStatus.Failed }
] as const;

export const BackgroundTaskStatusLabel: Record<BackgroundTaskStatus, string> = {
  [BackgroundTaskStatus.Running]: 'Running',
  [BackgroundTaskStatus.Complete]: 'Complete',
  [BackgroundTaskStatus.Failed]: 'Failed'
};

export enum ComponentDescriptorType {
  Invalid = 0,
  Fixed = 1,
  Configurable = 2,
  Instance = 3
}

export const componentDescriptorTypes = [
  { label: 'Invalid', value: ComponentDescriptorType.Invalid },
  { label: 'Fixed', value: ComponentDescriptorType.Fixed },
  { label: 'Configurable', value: ComponentDescriptorType.Configurable },
  { label: 'Instance', value: ComponentDescriptorType.Instance }
] as const;

export const ComponentDescriptorTypeLabel: Record<ComponentDescriptorType, string> = {
  [ComponentDescriptorType.Invalid]: 'Invalid',
  [ComponentDescriptorType.Fixed]: 'Fixed',
  [ComponentDescriptorType.Configurable]: 'Configurable',
  [ComponentDescriptorType.Instance]: 'Instance'
};

export enum ComponentType {
  Enhancer = 1,
  PlayableFileSelector = 2,
  Player = 3
}

export const componentTypes = [
  { label: 'Enhancer', value: ComponentType.Enhancer },
  { label: 'PlayableFileSelector', value: ComponentType.PlayableFileSelector },
  { label: 'Player', value: ComponentType.Player }
] as const;

export const ComponentTypeLabel: Record<ComponentType, string> = {
  [ComponentType.Enhancer]: 'Enhancer',
  [ComponentType.PlayableFileSelector]: 'PlayableFileSelector',
  [ComponentType.Player]: 'Player'
};

export enum CookieValidatorTarget {
  BiliBili = 1,
  ExHentai = 2,
  Pixiv = 3
}

export const cookieValidatorTargets = [
  { label: 'BiliBili', value: CookieValidatorTarget.BiliBili },
  { label: 'ExHentai', value: CookieValidatorTarget.ExHentai },
  { label: 'Pixiv', value: CookieValidatorTarget.Pixiv }
] as const;

export const CookieValidatorTargetLabel: Record<CookieValidatorTarget, string> = {
  [CookieValidatorTarget.BiliBili]: 'BiliBili',
  [CookieValidatorTarget.ExHentai]: 'ExHentai',
  [CookieValidatorTarget.Pixiv]: 'Pixiv'
};

export enum CoverFit {
  Contain = 1,
  Cover = 2
}

export const coverFits = [
  { label: 'Contain', value: CoverFit.Contain },
  { label: 'Cover', value: CoverFit.Cover }
] as const;

export const CoverFitLabel: Record<CoverFit, string> = {
  [CoverFit.Contain]: 'Contain',
  [CoverFit.Cover]: 'Cover'
};

export enum CoverSaveMode {
  Replace = 1,
  Prepend = 2
}

export const coverSaveModes = [
  { label: 'Replace', value: CoverSaveMode.Replace },
  { label: 'Prepend', value: CoverSaveMode.Prepend }
] as const;

export const CoverSaveModeLabel: Record<CoverSaveMode, string> = {
  [CoverSaveMode.Replace]: 'Replace',
  [CoverSaveMode.Prepend]: 'Prepend'
};

export enum CoverSelectOrder {
  FilenameAscending = 1,
  FileModifyDtDescending = 2
}

export const coverSelectOrders = [
  { label: 'FilenameAscending', value: CoverSelectOrder.FilenameAscending },
  { label: 'FileModifyDtDescending', value: CoverSelectOrder.FileModifyDtDescending }
] as const;

export const CoverSelectOrderLabel: Record<CoverSelectOrder, string> = {
  [CoverSelectOrder.FilenameAscending]: 'FilenameAscending',
  [CoverSelectOrder.FileModifyDtDescending]: 'FileModifyDtDescending'
};

export enum CustomDataType {
  String = 1,
  DateTime = 2,
  Number = 3,
  Enum = 4
}

export const customDataTypes = [
  { label: 'String', value: CustomDataType.String },
  { label: 'DateTime', value: CustomDataType.DateTime },
  { label: 'Number', value: CustomDataType.Number },
  { label: 'Enum', value: CustomDataType.Enum }
] as const;

export const CustomDataTypeLabel: Record<CustomDataType, string> = {
  [CustomDataType.String]: 'String',
  [CustomDataType.DateTime]: 'DateTime',
  [CustomDataType.Number]: 'Number',
  [CustomDataType.Enum]: 'Enum'
};

export enum MatchResultType {
  Layer = 1,
  Regex = 2
}

export const matchResultTypes = [
  { label: 'Layer', value: MatchResultType.Layer },
  { label: 'Regex', value: MatchResultType.Regex }
] as const;

export const MatchResultTypeLabel: Record<MatchResultType, string> = {
  [MatchResultType.Layer]: 'Layer',
  [MatchResultType.Regex]: 'Regex'
};

export enum MediaLibraryFileSystemError {
  InvalidVolume = 1,
  FreeSpaceNotEnough = 2,
  Occupied = 3
}

export const mediaLibraryFileSystemErrors = [
  { label: 'InvalidVolume', value: MediaLibraryFileSystemError.InvalidVolume },
  { label: 'FreeSpaceNotEnough', value: MediaLibraryFileSystemError.FreeSpaceNotEnough },
  { label: 'Occupied', value: MediaLibraryFileSystemError.Occupied }
] as const;

export const MediaLibraryFileSystemErrorLabel: Record<MediaLibraryFileSystemError, string> = {
  [MediaLibraryFileSystemError.InvalidVolume]: 'InvalidVolume',
  [MediaLibraryFileSystemError.FreeSpaceNotEnough]: 'FreeSpaceNotEnough',
  [MediaLibraryFileSystemError.Occupied]: 'Occupied'
};

export enum MediaLibrarySyncStep {
  Filtering = 0,
  AcquireFileSystemInfo = 1,
  CleanResources = 2,
  CompareResources = 3,
  SaveResources = 4
}

export const mediaLibrarySyncSteps = [
  { label: 'Filtering', value: MediaLibrarySyncStep.Filtering },
  { label: 'AcquireFileSystemInfo', value: MediaLibrarySyncStep.AcquireFileSystemInfo },
  { label: 'CleanResources', value: MediaLibrarySyncStep.CleanResources },
  { label: 'CompareResources', value: MediaLibrarySyncStep.CompareResources },
  { label: 'SaveResources', value: MediaLibrarySyncStep.SaveResources }
] as const;

export const MediaLibrarySyncStepLabel: Record<MediaLibrarySyncStep, string> = {
  [MediaLibrarySyncStep.Filtering]: 'Filtering',
  [MediaLibrarySyncStep.AcquireFileSystemInfo]: 'AcquireFileSystemInfo',
  [MediaLibrarySyncStep.CleanResources]: 'CleanResources',
  [MediaLibrarySyncStep.CompareResources]: 'CompareResources',
  [MediaLibrarySyncStep.SaveResources]: 'SaveResources'
};

export enum MediaType {
  Image = 1,
  Audio = 2,
  Video = 3,
  Text = 4,
  Application = 5,
  Unknown = 1000
}

export const mediaTypes = [
  { label: 'Image', value: MediaType.Image },
  { label: 'Audio', value: MediaType.Audio },
  { label: 'Video', value: MediaType.Video },
  { label: 'Text', value: MediaType.Text },
  { label: 'Application', value: MediaType.Application },
  { label: 'Unknown', value: MediaType.Unknown }
] as const;

export const MediaTypeLabel: Record<MediaType, string> = {
  [MediaType.Image]: 'Image',
  [MediaType.Audio]: 'Audio',
  [MediaType.Video]: 'Video',
  [MediaType.Text]: 'Text',
  [MediaType.Application]: 'Application',
  [MediaType.Unknown]: 'Unknown'
};

export enum PlaylistItemType {
  Resource = 1,
  Video = 2,
  Image = 3,
  Audio = 4
}

export const playlistItemTypes = [
  { label: 'Resource', value: PlaylistItemType.Resource },
  { label: 'Video', value: PlaylistItemType.Video },
  { label: 'Image', value: PlaylistItemType.Image },
  { label: 'Audio', value: PlaylistItemType.Audio }
] as const;

export const PlaylistItemTypeLabel: Record<PlaylistItemType, string> = {
  [PlaylistItemType.Resource]: 'Resource',
  [PlaylistItemType.Video]: 'Video',
  [PlaylistItemType.Image]: 'Image',
  [PlaylistItemType.Audio]: 'Audio'
};

export enum ReservedResourceFileType {
  Cover = 1
}

export const reservedResourceFileTypes = [
  { label: 'Cover', value: ReservedResourceFileType.Cover }
] as const;

export const ReservedResourceFileTypeLabel: Record<ReservedResourceFileType, string> = {
  [ReservedResourceFileType.Cover]: 'Cover'
};

export enum ResourceDiffProperty {
  Category = 0,
  MediaLibrary = 1,
  ReleaseDt = 2,
  Publisher = 3,
  Name = 4,
  Language = 5,
  Volume = 6,
  Original = 7,
  Series = 8,
  Tag = 9,
  Introduction = 10,
  Rate = 11,
  CustomProperty = 12
}

export const resourceDiffProperties = [
  { label: 'Category', value: ResourceDiffProperty.Category },
  { label: 'MediaLibrary', value: ResourceDiffProperty.MediaLibrary },
  { label: 'ReleaseDt', value: ResourceDiffProperty.ReleaseDt },
  { label: 'Publisher', value: ResourceDiffProperty.Publisher },
  { label: 'Name', value: ResourceDiffProperty.Name },
  { label: 'Language', value: ResourceDiffProperty.Language },
  { label: 'Volume', value: ResourceDiffProperty.Volume },
  { label: 'Original', value: ResourceDiffProperty.Original },
  { label: 'Series', value: ResourceDiffProperty.Series },
  { label: 'Tag', value: ResourceDiffProperty.Tag },
  { label: 'Introduction', value: ResourceDiffProperty.Introduction },
  { label: 'Rate', value: ResourceDiffProperty.Rate },
  { label: 'CustomProperty', value: ResourceDiffProperty.CustomProperty }
] as const;

export const ResourceDiffPropertyLabel: Record<ResourceDiffProperty, string> = {
  [ResourceDiffProperty.Category]: 'Category',
  [ResourceDiffProperty.MediaLibrary]: 'MediaLibrary',
  [ResourceDiffProperty.ReleaseDt]: 'ReleaseDt',
  [ResourceDiffProperty.Publisher]: 'Publisher',
  [ResourceDiffProperty.Name]: 'Name',
  [ResourceDiffProperty.Language]: 'Language',
  [ResourceDiffProperty.Volume]: 'Volume',
  [ResourceDiffProperty.Original]: 'Original',
  [ResourceDiffProperty.Series]: 'Series',
  [ResourceDiffProperty.Tag]: 'Tag',
  [ResourceDiffProperty.Introduction]: 'Introduction',
  [ResourceDiffProperty.Rate]: 'Rate',
  [ResourceDiffProperty.CustomProperty]: 'CustomProperty'
};

export enum ResourceDiffType {
  Added = 1,
  Removed = 2,
  Modified = 3
}

export const resourceDiffTypes = [
  { label: 'Added', value: ResourceDiffType.Added },
  { label: 'Removed', value: ResourceDiffType.Removed },
  { label: 'Modified', value: ResourceDiffType.Modified }
] as const;

export const ResourceDiffTypeLabel: Record<ResourceDiffType, string> = {
  [ResourceDiffType.Added]: 'Added',
  [ResourceDiffType.Removed]: 'Removed',
  [ResourceDiffType.Modified]: 'Modified'
};

export enum ResourceDisplayContent {
  MediaLibrary = 1,
  Category = 2,
  Tags = 4,
  AddedDate = 8,
  UpdatedDate = 16,
  FileCreatedDate = 32,
  Default = 39,
  FileModifiedDate = 64
}

export const resourceDisplayContents = [
  { label: 'MediaLibrary', value: ResourceDisplayContent.MediaLibrary },
  { label: 'Category', value: ResourceDisplayContent.Category },
  { label: 'Tags', value: ResourceDisplayContent.Tags },
  { label: 'AddedDate', value: ResourceDisplayContent.AddedDate },
  { label: 'UpdatedDate', value: ResourceDisplayContent.UpdatedDate },
  { label: 'FileCreatedDate', value: ResourceDisplayContent.FileCreatedDate },
  { label: 'Default', value: ResourceDisplayContent.Default },
  { label: 'FileModifiedDate', value: ResourceDisplayContent.FileModifiedDate }
] as const;

export const ResourceDisplayContentLabel: Record<ResourceDisplayContent, string> = {
  [ResourceDisplayContent.MediaLibrary]: 'MediaLibrary',
  [ResourceDisplayContent.Category]: 'Category',
  [ResourceDisplayContent.Tags]: 'Tags',
  [ResourceDisplayContent.AddedDate]: 'AddedDate',
  [ResourceDisplayContent.UpdatedDate]: 'UpdatedDate',
  [ResourceDisplayContent.FileCreatedDate]: 'FileCreatedDate',
  [ResourceDisplayContent.Default]: 'Default',
  [ResourceDisplayContent.FileModifiedDate]: 'FileModifiedDate'
};

export enum ResourceLanguage {
  NotSet = 0,
  Chinese = 1,
  English = 2,
  Japanese = 3,
  Korean = 4,
  French = 5,
  German = 6,
  Spanish = 7,
  Russian = 8
}

export const resourceLanguages = [
  { label: 'NotSet', value: ResourceLanguage.NotSet },
  { label: 'Chinese', value: ResourceLanguage.Chinese },
  { label: 'English', value: ResourceLanguage.English },
  { label: 'Japanese', value: ResourceLanguage.Japanese },
  { label: 'Korean', value: ResourceLanguage.Korean },
  { label: 'French', value: ResourceLanguage.French },
  { label: 'German', value: ResourceLanguage.German },
  { label: 'Spanish', value: ResourceLanguage.Spanish },
  { label: 'Russian', value: ResourceLanguage.Russian }
] as const;

export const ResourceLanguageLabel: Record<ResourceLanguage, string> = {
  [ResourceLanguage.NotSet]: 'NotSet',
  [ResourceLanguage.Chinese]: 'Chinese',
  [ResourceLanguage.English]: 'English',
  [ResourceLanguage.Japanese]: 'Japanese',
  [ResourceLanguage.Korean]: 'Korean',
  [ResourceLanguage.French]: 'French',
  [ResourceLanguage.German]: 'German',
  [ResourceLanguage.Spanish]: 'Spanish',
  [ResourceLanguage.Russian]: 'Russian'
};

export enum ResourceMatcherValueType {
  Layer = 1,
  Regex = 2,
  FixedText = 3
}

export const resourceMatcherValueTypes = [
  { label: 'Layer', value: ResourceMatcherValueType.Layer },
  { label: 'Regex', value: ResourceMatcherValueType.Regex },
  { label: 'FixedText', value: ResourceMatcherValueType.FixedText }
] as const;

export const ResourceMatcherValueTypeLabel: Record<ResourceMatcherValueType, string> = {
  [ResourceMatcherValueType.Layer]: 'Layer',
  [ResourceMatcherValueType.Regex]: 'Regex',
  [ResourceMatcherValueType.FixedText]: 'FixedText'
};

export enum ResourceProperty {
  RootPath = 1,
  ParentResource = 2,
  Resource = 3,
  Introduction = 12,
  Rating = 13,
  CustomProperty = 14,
  Filename = 15,
  DirectoryPath = 16,
  CreatedAt = 17,
  FileCreatedAt = 18,
  FileModifiedAt = 19,
  Category = 20,
  MediaLibrary = 21,
  Cover = 22,
  PlayedAt = 23,
  MediaLibraryV2 = 24
}

export const resourceProperties = [
  { label: 'RootPath', value: ResourceProperty.RootPath },
  { label: 'ParentResource', value: ResourceProperty.ParentResource },
  { label: 'Resource', value: ResourceProperty.Resource },
  { label: 'Introduction', value: ResourceProperty.Introduction },
  { label: 'Rating', value: ResourceProperty.Rating },
  { label: 'CustomProperty', value: ResourceProperty.CustomProperty },
  { label: 'Filename', value: ResourceProperty.Filename },
  { label: 'DirectoryPath', value: ResourceProperty.DirectoryPath },
  { label: 'CreatedAt', value: ResourceProperty.CreatedAt },
  { label: 'FileCreatedAt', value: ResourceProperty.FileCreatedAt },
  { label: 'FileModifiedAt', value: ResourceProperty.FileModifiedAt },
  { label: 'Category', value: ResourceProperty.Category },
  { label: 'MediaLibrary', value: ResourceProperty.MediaLibrary },
  { label: 'Cover', value: ResourceProperty.Cover },
  { label: 'PlayedAt', value: ResourceProperty.PlayedAt },
  { label: 'MediaLibraryV2', value: ResourceProperty.MediaLibraryV2 }
] as const;

export const ResourcePropertyLabel: Record<ResourceProperty, string> = {
  [ResourceProperty.RootPath]: 'RootPath',
  [ResourceProperty.ParentResource]: 'ParentResource',
  [ResourceProperty.Resource]: 'Resource',
  [ResourceProperty.Introduction]: 'Introduction',
  [ResourceProperty.Rating]: 'Rating',
  [ResourceProperty.CustomProperty]: 'CustomProperty',
  [ResourceProperty.Filename]: 'Filename',
  [ResourceProperty.DirectoryPath]: 'DirectoryPath',
  [ResourceProperty.CreatedAt]: 'CreatedAt',
  [ResourceProperty.FileCreatedAt]: 'FileCreatedAt',
  [ResourceProperty.FileModifiedAt]: 'FileModifiedAt',
  [ResourceProperty.Category]: 'Category',
  [ResourceProperty.MediaLibrary]: 'MediaLibrary',
  [ResourceProperty.Cover]: 'Cover',
  [ResourceProperty.PlayedAt]: 'PlayedAt',
  [ResourceProperty.MediaLibraryV2]: 'MediaLibraryV2'
};

export enum ResourceTaskType {
  Moving = 1
}

export const resourceTaskTypes = [
  { label: 'Moving', value: ResourceTaskType.Moving }
] as const;

export const ResourceTaskTypeLabel: Record<ResourceTaskType, string> = {
  [ResourceTaskType.Moving]: 'Moving'
};

export enum SearchableReservedProperty {
  Introduction = 12,
  Rating = 13,
  FileName = 15,
  DirectoryPath = 16,
  CreatedAt = 17,
  FileCreatedAt = 18,
  FileModifiedAt = 19,
  Category = 20,
  MediaLibrary = 21,
  Cover = 22,
  MediaLibraryV2 = 24
}

export const searchableReservedProperties = [
  { label: 'Introduction', value: SearchableReservedProperty.Introduction },
  { label: 'Rating', value: SearchableReservedProperty.Rating },
  { label: 'FileName', value: SearchableReservedProperty.FileName },
  { label: 'DirectoryPath', value: SearchableReservedProperty.DirectoryPath },
  { label: 'CreatedAt', value: SearchableReservedProperty.CreatedAt },
  { label: 'FileCreatedAt', value: SearchableReservedProperty.FileCreatedAt },
  { label: 'FileModifiedAt', value: SearchableReservedProperty.FileModifiedAt },
  { label: 'Category', value: SearchableReservedProperty.Category },
  { label: 'MediaLibrary', value: SearchableReservedProperty.MediaLibrary },
  { label: 'Cover', value: SearchableReservedProperty.Cover },
  { label: 'MediaLibraryV2', value: SearchableReservedProperty.MediaLibraryV2 }
] as const;

export const SearchableReservedPropertyLabel: Record<SearchableReservedProperty, string> = {
  [SearchableReservedProperty.Introduction]: 'Introduction',
  [SearchableReservedProperty.Rating]: 'Rating',
  [SearchableReservedProperty.FileName]: 'FileName',
  [SearchableReservedProperty.DirectoryPath]: 'DirectoryPath',
  [SearchableReservedProperty.CreatedAt]: 'CreatedAt',
  [SearchableReservedProperty.FileCreatedAt]: 'FileCreatedAt',
  [SearchableReservedProperty.FileModifiedAt]: 'FileModifiedAt',
  [SearchableReservedProperty.Category]: 'Category',
  [SearchableReservedProperty.MediaLibrary]: 'MediaLibrary',
  [SearchableReservedProperty.Cover]: 'Cover',
  [SearchableReservedProperty.MediaLibraryV2]: 'MediaLibraryV2'
};

export enum StartupPage {
  Default = 0,
  Resource = 1
}

export const startupPages = [
  { label: 'Default', value: StartupPage.Default },
  { label: 'Resource', value: StartupPage.Resource }
] as const;

export const StartupPageLabel: Record<StartupPage, string> = {
  [StartupPage.Default]: 'Default',
  [StartupPage.Resource]: 'Resource'
};

export enum ThirdPartyId {
  Bilibili = 1,
  ExHentai = 2,
  Pixiv = 3,
  Bangumi = 4,
  SoulPlus = 5,
  DLsite = 6,
  Fanbox = 7,
  Fantia = 8,
  Cien = 9,
  Patreon = 10,
  Tmdb = 11
}

export const thirdPartyIds = [
  { label: 'Bilibili', value: ThirdPartyId.Bilibili },
  { label: 'ExHentai', value: ThirdPartyId.ExHentai },
  { label: 'Pixiv', value: ThirdPartyId.Pixiv },
  { label: 'Bangumi', value: ThirdPartyId.Bangumi },
  { label: 'SoulPlus', value: ThirdPartyId.SoulPlus },
  { label: 'DLsite', value: ThirdPartyId.DLsite },
  { label: 'Fanbox', value: ThirdPartyId.Fanbox },
  { label: 'Fantia', value: ThirdPartyId.Fantia },
  { label: 'Cien', value: ThirdPartyId.Cien },
  { label: 'Patreon', value: ThirdPartyId.Patreon },
  { label: 'Tmdb', value: ThirdPartyId.Tmdb }
] as const;

export const ThirdPartyIdLabel: Record<ThirdPartyId, string> = {
  [ThirdPartyId.Bilibili]: 'Bilibili',
  [ThirdPartyId.ExHentai]: 'ExHentai',
  [ThirdPartyId.Pixiv]: 'Pixiv',
  [ThirdPartyId.Bangumi]: 'Bangumi',
  [ThirdPartyId.SoulPlus]: 'SoulPlus',
  [ThirdPartyId.DLsite]: 'DLsite',
  [ThirdPartyId.Fanbox]: 'Fanbox',
  [ThirdPartyId.Fantia]: 'Fantia',
  [ThirdPartyId.Cien]: 'Cien',
  [ThirdPartyId.Patreon]: 'Patreon',
  [ThirdPartyId.Tmdb]: 'Tmdb'
};

export enum PasswordSearchOrder {
  Latest = 1,
  Frequency = 2
}

export const passwordSearchOrders = [
  { label: 'Latest', value: PasswordSearchOrder.Latest },
  { label: 'Frequency', value: PasswordSearchOrder.Frequency }
] as const;

export const PasswordSearchOrderLabel: Record<PasswordSearchOrder, string> = {
  [PasswordSearchOrder.Latest]: 'Latest',
  [PasswordSearchOrder.Frequency]: 'Frequency'
};

export enum ResourceSearchSortableProperty {
  FileCreateDt = 1,
  FileModifyDt = 2,
  Filename = 3,
  AddDt = 6,
  PlayedAt = 11
}

export const resourceSearchSortableProperties = [
  { label: 'FileCreateDt', value: ResourceSearchSortableProperty.FileCreateDt },
  { label: 'FileModifyDt', value: ResourceSearchSortableProperty.FileModifyDt },
  { label: 'Filename', value: ResourceSearchSortableProperty.Filename },
  { label: 'AddDt', value: ResourceSearchSortableProperty.AddDt },
  { label: 'PlayedAt', value: ResourceSearchSortableProperty.PlayedAt }
] as const;

export const ResourceSearchSortablePropertyLabel: Record<ResourceSearchSortableProperty, string> = {
  [ResourceSearchSortableProperty.FileCreateDt]: 'FileCreateDt',
  [ResourceSearchSortableProperty.FileModifyDt]: 'FileModifyDt',
  [ResourceSearchSortableProperty.Filename]: 'Filename',
  [ResourceSearchSortableProperty.AddDt]: 'AddDt',
  [ResourceSearchSortableProperty.PlayedAt]: 'PlayedAt'
};

export enum AliasAdditionalItem {
  Candidates = 1
}

export const aliasAdditionalItems = [
  { label: 'Candidates', value: AliasAdditionalItem.Candidates }
] as const;

export const AliasAdditionalItemLabel: Record<AliasAdditionalItem, string> = {
  [AliasAdditionalItem.Candidates]: 'Candidates'
};

export enum CategoryAdditionalItem {
  None = 0,
  Components = 1,
  Validation = 3,
  CustomProperties = 4,
  EnhancerOptions = 8
}

export const categoryAdditionalItems = [
  { label: 'None', value: CategoryAdditionalItem.None },
  { label: 'Components', value: CategoryAdditionalItem.Components },
  { label: 'Validation', value: CategoryAdditionalItem.Validation },
  { label: 'CustomProperties', value: CategoryAdditionalItem.CustomProperties },
  { label: 'EnhancerOptions', value: CategoryAdditionalItem.EnhancerOptions }
] as const;

export const CategoryAdditionalItemLabel: Record<CategoryAdditionalItem, string> = {
  [CategoryAdditionalItem.None]: 'None',
  [CategoryAdditionalItem.Components]: 'Components',
  [CategoryAdditionalItem.Validation]: 'Validation',
  [CategoryAdditionalItem.CustomProperties]: 'CustomProperties',
  [CategoryAdditionalItem.EnhancerOptions]: 'EnhancerOptions'
};

export enum ComponentDescriptorAdditionalItem {
  None = 0,
  AssociatedCategories = 1
}

export const componentDescriptorAdditionalItems = [
  { label: 'None', value: ComponentDescriptorAdditionalItem.None },
  { label: 'AssociatedCategories', value: ComponentDescriptorAdditionalItem.AssociatedCategories }
] as const;

export const ComponentDescriptorAdditionalItemLabel: Record<ComponentDescriptorAdditionalItem, string> = {
  [ComponentDescriptorAdditionalItem.None]: 'None',
  [ComponentDescriptorAdditionalItem.AssociatedCategories]: 'AssociatedCategories'
};

export enum CustomPropertyAdditionalItem {
  None = 0,
  Category = 1,
  ValueCount = 2
}

export const customPropertyAdditionalItems = [
  { label: 'None', value: CustomPropertyAdditionalItem.None },
  { label: 'Category', value: CustomPropertyAdditionalItem.Category },
  { label: 'ValueCount', value: CustomPropertyAdditionalItem.ValueCount }
] as const;

export const CustomPropertyAdditionalItemLabel: Record<CustomPropertyAdditionalItem, string> = {
  [CustomPropertyAdditionalItem.None]: 'None',
  [CustomPropertyAdditionalItem.Category]: 'Category',
  [CustomPropertyAdditionalItem.ValueCount]: 'ValueCount'
};

export enum CustomPropertyValueAdditionalItem {
  None = 0,
  BizValue = 1
}

export const customPropertyValueAdditionalItems = [
  { label: 'None', value: CustomPropertyValueAdditionalItem.None },
  { label: 'BizValue', value: CustomPropertyValueAdditionalItem.BizValue }
] as const;

export const CustomPropertyValueAdditionalItemLabel: Record<CustomPropertyValueAdditionalItem, string> = {
  [CustomPropertyValueAdditionalItem.None]: 'None',
  [CustomPropertyValueAdditionalItem.BizValue]: 'BizValue'
};

export enum MediaLibraryAdditionalItem {
  None = 0,
  Category = 1,
  FileSystemInfo = 2,
  PathConfigurationBoundProperties = 4
}

export const mediaLibraryAdditionalItems = [
  { label: 'None', value: MediaLibraryAdditionalItem.None },
  { label: 'Category', value: MediaLibraryAdditionalItem.Category },
  { label: 'FileSystemInfo', value: MediaLibraryAdditionalItem.FileSystemInfo },
  { label: 'PathConfigurationBoundProperties', value: MediaLibraryAdditionalItem.PathConfigurationBoundProperties }
] as const;

export const MediaLibraryAdditionalItemLabel: Record<MediaLibraryAdditionalItem, string> = {
  [MediaLibraryAdditionalItem.None]: 'None',
  [MediaLibraryAdditionalItem.Category]: 'Category',
  [MediaLibraryAdditionalItem.FileSystemInfo]: 'FileSystemInfo',
  [MediaLibraryAdditionalItem.PathConfigurationBoundProperties]: 'PathConfigurationBoundProperties'
};

export enum ResourceAdditionalItem {
  None = 0,
  Alias = 64,
  Category = 128,
  Properties = 160,
  DisplayName = 416,
  HasChildren = 512,
  MediaLibraryName = 2048,
  Cache = 4096,
  All = 7136
}

export const resourceAdditionalItems = [
  { label: 'None', value: ResourceAdditionalItem.None },
  { label: 'Alias', value: ResourceAdditionalItem.Alias },
  { label: 'Category', value: ResourceAdditionalItem.Category },
  { label: 'Properties', value: ResourceAdditionalItem.Properties },
  { label: 'DisplayName', value: ResourceAdditionalItem.DisplayName },
  { label: 'HasChildren', value: ResourceAdditionalItem.HasChildren },
  { label: 'MediaLibraryName', value: ResourceAdditionalItem.MediaLibraryName },
  { label: 'Cache', value: ResourceAdditionalItem.Cache },
  { label: 'All', value: ResourceAdditionalItem.All }
] as const;

export const ResourceAdditionalItemLabel: Record<ResourceAdditionalItem, string> = {
  [ResourceAdditionalItem.None]: 'None',
  [ResourceAdditionalItem.Alias]: 'Alias',
  [ResourceAdditionalItem.Category]: 'Category',
  [ResourceAdditionalItem.Properties]: 'Properties',
  [ResourceAdditionalItem.DisplayName]: 'DisplayName',
  [ResourceAdditionalItem.HasChildren]: 'HasChildren',
  [ResourceAdditionalItem.MediaLibraryName]: 'MediaLibraryName',
  [ResourceAdditionalItem.Cache]: 'Cache',
  [ResourceAdditionalItem.All]: 'All'
};

export enum TagAdditionalItem {
  None = 0,
  GroupName = 1,
  PreferredAlias = 2
}

export const tagAdditionalItems = [
  { label: 'None', value: TagAdditionalItem.None },
  { label: 'GroupName', value: TagAdditionalItem.GroupName },
  { label: 'PreferredAlias', value: TagAdditionalItem.PreferredAlias }
] as const;

export const TagAdditionalItemLabel: Record<TagAdditionalItem, string> = {
  [TagAdditionalItem.None]: 'None',
  [TagAdditionalItem.GroupName]: 'GroupName',
  [TagAdditionalItem.PreferredAlias]: 'PreferredAlias'
};

export enum TagGroupAdditionalItem {
  Tags = 1,
  PreferredAlias = 2,
  TagNamePreferredAlias = 4
}

export const tagGroupAdditionalItems = [
  { label: 'Tags', value: TagGroupAdditionalItem.Tags },
  { label: 'PreferredAlias', value: TagGroupAdditionalItem.PreferredAlias },
  { label: 'TagNamePreferredAlias', value: TagGroupAdditionalItem.TagNamePreferredAlias }
] as const;

export const TagGroupAdditionalItemLabel: Record<TagGroupAdditionalItem, string> = {
  [TagGroupAdditionalItem.Tags]: 'Tags',
  [TagGroupAdditionalItem.PreferredAlias]: 'PreferredAlias',
  [TagGroupAdditionalItem.TagNamePreferredAlias]: 'TagNamePreferredAlias'
};

export enum EnhancerId {
  Bakabase = 1,
  ExHentai = 2,
  Bangumi = 3,
  DLsite = 4,
  Regex = 5,
  Kodi = 6,
  Tmdb = 7,
  Av = 8
}

export const enhancerIds = [
  { label: 'Bakabase', value: EnhancerId.Bakabase },
  { label: 'ExHentai', value: EnhancerId.ExHentai },
  { label: 'Bangumi', value: EnhancerId.Bangumi },
  { label: 'DLsite', value: EnhancerId.DLsite },
  { label: 'Regex', value: EnhancerId.Regex },
  { label: 'Kodi', value: EnhancerId.Kodi },
  { label: 'Tmdb', value: EnhancerId.Tmdb },
  { label: 'Av', value: EnhancerId.Av }
] as const;

export const EnhancerIdLabel: Record<EnhancerId, string> = {
  [EnhancerId.Bakabase]: 'Bakabase',
  [EnhancerId.ExHentai]: 'ExHentai',
  [EnhancerId.Bangumi]: 'Bangumi',
  [EnhancerId.DLsite]: 'DLsite',
  [EnhancerId.Regex]: 'Regex',
  [EnhancerId.Kodi]: 'Kodi',
  [EnhancerId.Tmdb]: 'Tmdb',
  [EnhancerId.Av]: 'Av'
};

export enum TmdbEnhancerTarget {
  Title = 1,
  OriginalTitle = 2,
  Overview = 3,
  Rating = 4,
  VoteCount = 5,
  ReleaseDate = 6,
  Runtime = 7,
  Genres = 8,
  ProductionCountries = 9,
  SpokenLanguages = 10,
  Status = 11,
  Tagline = 12,
  Budget = 13,
  Revenue = 14,
  Cover = 15,
  Backdrop = 16
}

export const tmdbEnhancerTargets = [
  { label: 'Title', value: TmdbEnhancerTarget.Title },
  { label: 'OriginalTitle', value: TmdbEnhancerTarget.OriginalTitle },
  { label: 'Overview', value: TmdbEnhancerTarget.Overview },
  { label: 'Rating', value: TmdbEnhancerTarget.Rating },
  { label: 'VoteCount', value: TmdbEnhancerTarget.VoteCount },
  { label: 'ReleaseDate', value: TmdbEnhancerTarget.ReleaseDate },
  { label: 'Runtime', value: TmdbEnhancerTarget.Runtime },
  { label: 'Genres', value: TmdbEnhancerTarget.Genres },
  { label: 'ProductionCountries', value: TmdbEnhancerTarget.ProductionCountries },
  { label: 'SpokenLanguages', value: TmdbEnhancerTarget.SpokenLanguages },
  { label: 'Status', value: TmdbEnhancerTarget.Status },
  { label: 'Tagline', value: TmdbEnhancerTarget.Tagline },
  { label: 'Budget', value: TmdbEnhancerTarget.Budget },
  { label: 'Revenue', value: TmdbEnhancerTarget.Revenue },
  { label: 'Cover', value: TmdbEnhancerTarget.Cover },
  { label: 'Backdrop', value: TmdbEnhancerTarget.Backdrop }
] as const;

export const TmdbEnhancerTargetLabel: Record<TmdbEnhancerTarget, string> = {
  [TmdbEnhancerTarget.Title]: 'Title',
  [TmdbEnhancerTarget.OriginalTitle]: 'OriginalTitle',
  [TmdbEnhancerTarget.Overview]: 'Overview',
  [TmdbEnhancerTarget.Rating]: 'Rating',
  [TmdbEnhancerTarget.VoteCount]: 'VoteCount',
  [TmdbEnhancerTarget.ReleaseDate]: 'ReleaseDate',
  [TmdbEnhancerTarget.Runtime]: 'Runtime',
  [TmdbEnhancerTarget.Genres]: 'Genres',
  [TmdbEnhancerTarget.ProductionCountries]: 'ProductionCountries',
  [TmdbEnhancerTarget.SpokenLanguages]: 'SpokenLanguages',
  [TmdbEnhancerTarget.Status]: 'Status',
  [TmdbEnhancerTarget.Tagline]: 'Tagline',
  [TmdbEnhancerTarget.Budget]: 'Budget',
  [TmdbEnhancerTarget.Revenue]: 'Revenue',
  [TmdbEnhancerTarget.Cover]: 'Cover',
  [TmdbEnhancerTarget.Backdrop]: 'Backdrop'
};

export enum RegexEnhancerTarget {
  CaptureGroups = 0
}

export const regexEnhancerTargets = [
  { label: 'CaptureGroups', value: RegexEnhancerTarget.CaptureGroups }
] as const;

export const RegexEnhancerTargetLabel: Record<RegexEnhancerTarget, string> = {
  [RegexEnhancerTarget.CaptureGroups]: 'CaptureGroups'
};

export enum KodiEnhancerTarget {
  Title = 1,
  OriginalTitle = 2,
  SortTitle = 3,
  ShowTitle = 4,
  Outline = 5,
  Plot = 6,
  Tagline = 7,
  Runtime = 8,
  Mpaa = 9,
  PlayCount = 10,
  LastPlayed = 11,
  Id = 12,
  Genres = 13,
  Countries = 14,
  Tags = 15,
  VideoAssetTitle = 16,
  VideoAssetId = 17,
  VideoAssetType = 18,
  HasVideoVersions = 19,
  HasVideoExtras = 20,
  IsDefaultVideoVersion = 21,
  Credits = 22,
  Director = 23,
  Premiered = 24,
  Year = 25,
  Status = 26,
  Studio = 27,
  Trailer = 28,
  Season = 29,
  Episode = 30,
  DisplaySeason = 31,
  DisplayEpisode = 32,
  Genre = 33,
  Code = 34,
  Aired = 35,
  DateAdded = 36,
  Top250 = 37,
  UserRating = 38,
  Thumbs = 39,
  FanartThumbs = 40,
  UniqueIds = 41,
  Actors = 42,
  NamedSeasons = 43,
  Ratings = 44,
  Set = 45,
  Resume = 46,
  Artist = 47,
  Album = 48,
  MusicVideo = 49,
  Episodes = 50,
  Directors = 51,
  Track = 52,
  Style = 53,
  Mood = 54,
  Themes = 55,
  Compilation = 56,
  BoxSet = 57,
  Review = 58,
  Type = 59,
  ReleaseStatus = 60,
  ReleaseDate = 61,
  OriginalReleaseDate = 62,
  Label = 63,
  Duration = 64,
  Path = 65,
  Votes = 66,
  ReleaseType = 67,
  Rating = 68,
  SortName = 69,
  Gender = 70,
  Disambiguation = 71,
  Styles = 72,
  Moods = 73,
  YearsActive = 74,
  Born = 75,
  Formed = 76,
  Biography = 77,
  Died = 78,
  Disbanded = 79,
  AlbumArtistCredits = 80
}

export const kodiEnhancerTargets = [
  { label: 'Title', value: KodiEnhancerTarget.Title },
  { label: 'OriginalTitle', value: KodiEnhancerTarget.OriginalTitle },
  { label: 'SortTitle', value: KodiEnhancerTarget.SortTitle },
  { label: 'ShowTitle', value: KodiEnhancerTarget.ShowTitle },
  { label: 'Outline', value: KodiEnhancerTarget.Outline },
  { label: 'Plot', value: KodiEnhancerTarget.Plot },
  { label: 'Tagline', value: KodiEnhancerTarget.Tagline },
  { label: 'Runtime', value: KodiEnhancerTarget.Runtime },
  { label: 'Mpaa', value: KodiEnhancerTarget.Mpaa },
  { label: 'PlayCount', value: KodiEnhancerTarget.PlayCount },
  { label: 'LastPlayed', value: KodiEnhancerTarget.LastPlayed },
  { label: 'Id', value: KodiEnhancerTarget.Id },
  { label: 'Genres', value: KodiEnhancerTarget.Genres },
  { label: 'Countries', value: KodiEnhancerTarget.Countries },
  { label: 'Tags', value: KodiEnhancerTarget.Tags },
  { label: 'VideoAssetTitle', value: KodiEnhancerTarget.VideoAssetTitle },
  { label: 'VideoAssetId', value: KodiEnhancerTarget.VideoAssetId },
  { label: 'VideoAssetType', value: KodiEnhancerTarget.VideoAssetType },
  { label: 'HasVideoVersions', value: KodiEnhancerTarget.HasVideoVersions },
  { label: 'HasVideoExtras', value: KodiEnhancerTarget.HasVideoExtras },
  { label: 'IsDefaultVideoVersion', value: KodiEnhancerTarget.IsDefaultVideoVersion },
  { label: 'Credits', value: KodiEnhancerTarget.Credits },
  { label: 'Director', value: KodiEnhancerTarget.Director },
  { label: 'Premiered', value: KodiEnhancerTarget.Premiered },
  { label: 'Year', value: KodiEnhancerTarget.Year },
  { label: 'Status', value: KodiEnhancerTarget.Status },
  { label: 'Studio', value: KodiEnhancerTarget.Studio },
  { label: 'Trailer', value: KodiEnhancerTarget.Trailer },
  { label: 'Season', value: KodiEnhancerTarget.Season },
  { label: 'Episode', value: KodiEnhancerTarget.Episode },
  { label: 'DisplaySeason', value: KodiEnhancerTarget.DisplaySeason },
  { label: 'DisplayEpisode', value: KodiEnhancerTarget.DisplayEpisode },
  { label: 'Genre', value: KodiEnhancerTarget.Genre },
  { label: 'Code', value: KodiEnhancerTarget.Code },
  { label: 'Aired', value: KodiEnhancerTarget.Aired },
  { label: 'DateAdded', value: KodiEnhancerTarget.DateAdded },
  { label: 'Top250', value: KodiEnhancerTarget.Top250 },
  { label: 'UserRating', value: KodiEnhancerTarget.UserRating },
  { label: 'Thumbs', value: KodiEnhancerTarget.Thumbs },
  { label: 'FanartThumbs', value: KodiEnhancerTarget.FanartThumbs },
  { label: 'UniqueIds', value: KodiEnhancerTarget.UniqueIds },
  { label: 'Actors', value: KodiEnhancerTarget.Actors },
  { label: 'NamedSeasons', value: KodiEnhancerTarget.NamedSeasons },
  { label: 'Ratings', value: KodiEnhancerTarget.Ratings },
  { label: 'Set', value: KodiEnhancerTarget.Set },
  { label: 'Resume', value: KodiEnhancerTarget.Resume },
  { label: 'Artist', value: KodiEnhancerTarget.Artist },
  { label: 'Album', value: KodiEnhancerTarget.Album },
  { label: 'MusicVideo', value: KodiEnhancerTarget.MusicVideo },
  { label: 'Episodes', value: KodiEnhancerTarget.Episodes },
  { label: 'Directors', value: KodiEnhancerTarget.Directors },
  { label: 'Track', value: KodiEnhancerTarget.Track },
  { label: 'Style', value: KodiEnhancerTarget.Style },
  { label: 'Mood', value: KodiEnhancerTarget.Mood },
  { label: 'Themes', value: KodiEnhancerTarget.Themes },
  { label: 'Compilation', value: KodiEnhancerTarget.Compilation },
  { label: 'BoxSet', value: KodiEnhancerTarget.BoxSet },
  { label: 'Review', value: KodiEnhancerTarget.Review },
  { label: 'Type', value: KodiEnhancerTarget.Type },
  { label: 'ReleaseStatus', value: KodiEnhancerTarget.ReleaseStatus },
  { label: 'ReleaseDate', value: KodiEnhancerTarget.ReleaseDate },
  { label: 'OriginalReleaseDate', value: KodiEnhancerTarget.OriginalReleaseDate },
  { label: 'Label', value: KodiEnhancerTarget.Label },
  { label: 'Duration', value: KodiEnhancerTarget.Duration },
  { label: 'Path', value: KodiEnhancerTarget.Path },
  { label: 'Votes', value: KodiEnhancerTarget.Votes },
  { label: 'ReleaseType', value: KodiEnhancerTarget.ReleaseType },
  { label: 'Rating', value: KodiEnhancerTarget.Rating },
  { label: 'SortName', value: KodiEnhancerTarget.SortName },
  { label: 'Gender', value: KodiEnhancerTarget.Gender },
  { label: 'Disambiguation', value: KodiEnhancerTarget.Disambiguation },
  { label: 'Styles', value: KodiEnhancerTarget.Styles },
  { label: 'Moods', value: KodiEnhancerTarget.Moods },
  { label: 'YearsActive', value: KodiEnhancerTarget.YearsActive },
  { label: 'Born', value: KodiEnhancerTarget.Born },
  { label: 'Formed', value: KodiEnhancerTarget.Formed },
  { label: 'Biography', value: KodiEnhancerTarget.Biography },
  { label: 'Died', value: KodiEnhancerTarget.Died },
  { label: 'Disbanded', value: KodiEnhancerTarget.Disbanded },
  { label: 'AlbumArtistCredits', value: KodiEnhancerTarget.AlbumArtistCredits }
] as const;

export const KodiEnhancerTargetLabel: Record<KodiEnhancerTarget, string> = {
  [KodiEnhancerTarget.Title]: 'Title',
  [KodiEnhancerTarget.OriginalTitle]: 'OriginalTitle',
  [KodiEnhancerTarget.SortTitle]: 'SortTitle',
  [KodiEnhancerTarget.ShowTitle]: 'ShowTitle',
  [KodiEnhancerTarget.Outline]: 'Outline',
  [KodiEnhancerTarget.Plot]: 'Plot',
  [KodiEnhancerTarget.Tagline]: 'Tagline',
  [KodiEnhancerTarget.Runtime]: 'Runtime',
  [KodiEnhancerTarget.Mpaa]: 'Mpaa',
  [KodiEnhancerTarget.PlayCount]: 'PlayCount',
  [KodiEnhancerTarget.LastPlayed]: 'LastPlayed',
  [KodiEnhancerTarget.Id]: 'Id',
  [KodiEnhancerTarget.Genres]: 'Genres',
  [KodiEnhancerTarget.Countries]: 'Countries',
  [KodiEnhancerTarget.Tags]: 'Tags',
  [KodiEnhancerTarget.VideoAssetTitle]: 'VideoAssetTitle',
  [KodiEnhancerTarget.VideoAssetId]: 'VideoAssetId',
  [KodiEnhancerTarget.VideoAssetType]: 'VideoAssetType',
  [KodiEnhancerTarget.HasVideoVersions]: 'HasVideoVersions',
  [KodiEnhancerTarget.HasVideoExtras]: 'HasVideoExtras',
  [KodiEnhancerTarget.IsDefaultVideoVersion]: 'IsDefaultVideoVersion',
  [KodiEnhancerTarget.Credits]: 'Credits',
  [KodiEnhancerTarget.Director]: 'Director',
  [KodiEnhancerTarget.Premiered]: 'Premiered',
  [KodiEnhancerTarget.Year]: 'Year',
  [KodiEnhancerTarget.Status]: 'Status',
  [KodiEnhancerTarget.Studio]: 'Studio',
  [KodiEnhancerTarget.Trailer]: 'Trailer',
  [KodiEnhancerTarget.Season]: 'Season',
  [KodiEnhancerTarget.Episode]: 'Episode',
  [KodiEnhancerTarget.DisplaySeason]: 'DisplaySeason',
  [KodiEnhancerTarget.DisplayEpisode]: 'DisplayEpisode',
  [KodiEnhancerTarget.Genre]: 'Genre',
  [KodiEnhancerTarget.Code]: 'Code',
  [KodiEnhancerTarget.Aired]: 'Aired',
  [KodiEnhancerTarget.DateAdded]: 'DateAdded',
  [KodiEnhancerTarget.Top250]: 'Top250',
  [KodiEnhancerTarget.UserRating]: 'UserRating',
  [KodiEnhancerTarget.Thumbs]: 'Thumbs',
  [KodiEnhancerTarget.FanartThumbs]: 'FanartThumbs',
  [KodiEnhancerTarget.UniqueIds]: 'UniqueIds',
  [KodiEnhancerTarget.Actors]: 'Actors',
  [KodiEnhancerTarget.NamedSeasons]: 'NamedSeasons',
  [KodiEnhancerTarget.Ratings]: 'Ratings',
  [KodiEnhancerTarget.Set]: 'Set',
  [KodiEnhancerTarget.Resume]: 'Resume',
  [KodiEnhancerTarget.Artist]: 'Artist',
  [KodiEnhancerTarget.Album]: 'Album',
  [KodiEnhancerTarget.MusicVideo]: 'MusicVideo',
  [KodiEnhancerTarget.Episodes]: 'Episodes',
  [KodiEnhancerTarget.Directors]: 'Directors',
  [KodiEnhancerTarget.Track]: 'Track',
  [KodiEnhancerTarget.Style]: 'Style',
  [KodiEnhancerTarget.Mood]: 'Mood',
  [KodiEnhancerTarget.Themes]: 'Themes',
  [KodiEnhancerTarget.Compilation]: 'Compilation',
  [KodiEnhancerTarget.BoxSet]: 'BoxSet',
  [KodiEnhancerTarget.Review]: 'Review',
  [KodiEnhancerTarget.Type]: 'Type',
  [KodiEnhancerTarget.ReleaseStatus]: 'ReleaseStatus',
  [KodiEnhancerTarget.ReleaseDate]: 'ReleaseDate',
  [KodiEnhancerTarget.OriginalReleaseDate]: 'OriginalReleaseDate',
  [KodiEnhancerTarget.Label]: 'Label',
  [KodiEnhancerTarget.Duration]: 'Duration',
  [KodiEnhancerTarget.Path]: 'Path',
  [KodiEnhancerTarget.Votes]: 'Votes',
  [KodiEnhancerTarget.ReleaseType]: 'ReleaseType',
  [KodiEnhancerTarget.Rating]: 'Rating',
  [KodiEnhancerTarget.SortName]: 'SortName',
  [KodiEnhancerTarget.Gender]: 'Gender',
  [KodiEnhancerTarget.Disambiguation]: 'Disambiguation',
  [KodiEnhancerTarget.Styles]: 'Styles',
  [KodiEnhancerTarget.Moods]: 'Moods',
  [KodiEnhancerTarget.YearsActive]: 'YearsActive',
  [KodiEnhancerTarget.Born]: 'Born',
  [KodiEnhancerTarget.Formed]: 'Formed',
  [KodiEnhancerTarget.Biography]: 'Biography',
  [KodiEnhancerTarget.Died]: 'Died',
  [KodiEnhancerTarget.Disbanded]: 'Disbanded',
  [KodiEnhancerTarget.AlbumArtistCredits]: 'AlbumArtistCredits'
};

export enum ExHentaiEnhancerTarget {
  Name = 1,
  Introduction = 2,
  Rating = 3,
  Tags = 4,
  Cover = 5
}

export const exHentaiEnhancerTargets = [
  { label: 'Name', value: ExHentaiEnhancerTarget.Name },
  { label: 'Introduction', value: ExHentaiEnhancerTarget.Introduction },
  { label: 'Rating', value: ExHentaiEnhancerTarget.Rating },
  { label: 'Tags', value: ExHentaiEnhancerTarget.Tags },
  { label: 'Cover', value: ExHentaiEnhancerTarget.Cover }
] as const;

export const ExHentaiEnhancerTargetLabel: Record<ExHentaiEnhancerTarget, string> = {
  [ExHentaiEnhancerTarget.Name]: 'Name',
  [ExHentaiEnhancerTarget.Introduction]: 'Introduction',
  [ExHentaiEnhancerTarget.Rating]: 'Rating',
  [ExHentaiEnhancerTarget.Tags]: 'Tags',
  [ExHentaiEnhancerTarget.Cover]: 'Cover'
};

export enum DLsiteEnhancerTarget {
  Name = 0,
  Cover = 1,
  PropertiesOnTheRightSideOfCover = 2,
  Introduction = 3,
  Rating = 4
}

export const dLsiteEnhancerTargets = [
  { label: 'Name', value: DLsiteEnhancerTarget.Name },
  { label: 'Cover', value: DLsiteEnhancerTarget.Cover },
  { label: 'PropertiesOnTheRightSideOfCover', value: DLsiteEnhancerTarget.PropertiesOnTheRightSideOfCover },
  { label: 'Introduction', value: DLsiteEnhancerTarget.Introduction },
  { label: 'Rating', value: DLsiteEnhancerTarget.Rating }
] as const;

export const DLsiteEnhancerTargetLabel: Record<DLsiteEnhancerTarget, string> = {
  [DLsiteEnhancerTarget.Name]: 'Name',
  [DLsiteEnhancerTarget.Cover]: 'Cover',
  [DLsiteEnhancerTarget.PropertiesOnTheRightSideOfCover]: 'PropertiesOnTheRightSideOfCover',
  [DLsiteEnhancerTarget.Introduction]: 'Introduction',
  [DLsiteEnhancerTarget.Rating]: 'Rating'
};

export enum BangumiEnhancerTarget {
  Name = 1,
  Tags = 2,
  Introduction = 3,
  Rating = 4,
  OtherPropertiesInLeftPanel = 5,
  Cover = 6
}

export const bangumiEnhancerTargets = [
  { label: 'Name', value: BangumiEnhancerTarget.Name },
  { label: 'Tags', value: BangumiEnhancerTarget.Tags },
  { label: 'Introduction', value: BangumiEnhancerTarget.Introduction },
  { label: 'Rating', value: BangumiEnhancerTarget.Rating },
  { label: 'OtherPropertiesInLeftPanel', value: BangumiEnhancerTarget.OtherPropertiesInLeftPanel },
  { label: 'Cover', value: BangumiEnhancerTarget.Cover }
] as const;

export const BangumiEnhancerTargetLabel: Record<BangumiEnhancerTarget, string> = {
  [BangumiEnhancerTarget.Name]: 'Name',
  [BangumiEnhancerTarget.Tags]: 'Tags',
  [BangumiEnhancerTarget.Introduction]: 'Introduction',
  [BangumiEnhancerTarget.Rating]: 'Rating',
  [BangumiEnhancerTarget.OtherPropertiesInLeftPanel]: 'OtherPropertiesInLeftPanel',
  [BangumiEnhancerTarget.Cover]: 'Cover'
};

export enum BakabaseEnhancerTarget {
  Name = 1,
  Publisher = 2,
  ReleaseDt = 3,
  VolumeName = 4,
  VolumeTitle = 5,
  Originals = 6,
  Language = 7,
  Cover = 8
}

export const bakabaseEnhancerTargets = [
  { label: 'Name', value: BakabaseEnhancerTarget.Name },
  { label: 'Publisher', value: BakabaseEnhancerTarget.Publisher },
  { label: 'ReleaseDt', value: BakabaseEnhancerTarget.ReleaseDt },
  { label: 'VolumeName', value: BakabaseEnhancerTarget.VolumeName },
  { label: 'VolumeTitle', value: BakabaseEnhancerTarget.VolumeTitle },
  { label: 'Originals', value: BakabaseEnhancerTarget.Originals },
  { label: 'Language', value: BakabaseEnhancerTarget.Language },
  { label: 'Cover', value: BakabaseEnhancerTarget.Cover }
] as const;

export const BakabaseEnhancerTargetLabel: Record<BakabaseEnhancerTarget, string> = {
  [BakabaseEnhancerTarget.Name]: 'Name',
  [BakabaseEnhancerTarget.Publisher]: 'Publisher',
  [BakabaseEnhancerTarget.ReleaseDt]: 'ReleaseDt',
  [BakabaseEnhancerTarget.VolumeName]: 'VolumeName',
  [BakabaseEnhancerTarget.VolumeTitle]: 'VolumeTitle',
  [BakabaseEnhancerTarget.Originals]: 'Originals',
  [BakabaseEnhancerTarget.Language]: 'Language',
  [BakabaseEnhancerTarget.Cover]: 'Cover'
};

export enum AvEnhancerTarget {
  Number = 0,
  Title = 1,
  OriginalTitle = 2,
  Actor = 3,
  Tags = 4,
  Release = 5,
  Year = 6,
  Studio = 7,
  Publisher = 8,
  Series = 9,
  Runtime = 10,
  Director = 11,
  Source = 12,
  Cover = 13,
  Poster = 14,
  Website = 15,
  Mosaic = 16
}

export const avEnhancerTargets = [
  { label: 'Number', value: AvEnhancerTarget.Number },
  { label: 'Title', value: AvEnhancerTarget.Title },
  { label: 'OriginalTitle', value: AvEnhancerTarget.OriginalTitle },
  { label: 'Actor', value: AvEnhancerTarget.Actor },
  { label: 'Tags', value: AvEnhancerTarget.Tags },
  { label: 'Release', value: AvEnhancerTarget.Release },
  { label: 'Year', value: AvEnhancerTarget.Year },
  { label: 'Studio', value: AvEnhancerTarget.Studio },
  { label: 'Publisher', value: AvEnhancerTarget.Publisher },
  { label: 'Series', value: AvEnhancerTarget.Series },
  { label: 'Runtime', value: AvEnhancerTarget.Runtime },
  { label: 'Director', value: AvEnhancerTarget.Director },
  { label: 'Source', value: AvEnhancerTarget.Source },
  { label: 'Cover', value: AvEnhancerTarget.Cover },
  { label: 'Poster', value: AvEnhancerTarget.Poster },
  { label: 'Website', value: AvEnhancerTarget.Website },
  { label: 'Mosaic', value: AvEnhancerTarget.Mosaic }
] as const;

export const AvEnhancerTargetLabel: Record<AvEnhancerTarget, string> = {
  [AvEnhancerTarget.Number]: 'Number',
  [AvEnhancerTarget.Title]: 'Title',
  [AvEnhancerTarget.OriginalTitle]: 'OriginalTitle',
  [AvEnhancerTarget.Actor]: 'Actor',
  [AvEnhancerTarget.Tags]: 'Tags',
  [AvEnhancerTarget.Release]: 'Release',
  [AvEnhancerTarget.Year]: 'Year',
  [AvEnhancerTarget.Studio]: 'Studio',
  [AvEnhancerTarget.Publisher]: 'Publisher',
  [AvEnhancerTarget.Series]: 'Series',
  [AvEnhancerTarget.Runtime]: 'Runtime',
  [AvEnhancerTarget.Director]: 'Director',
  [AvEnhancerTarget.Source]: 'Source',
  [AvEnhancerTarget.Cover]: 'Cover',
  [AvEnhancerTarget.Poster]: 'Poster',
  [AvEnhancerTarget.Website]: 'Website',
  [AvEnhancerTarget.Mosaic]: 'Mosaic'
};

export enum EnhancementAdditionalItem {
  None = 0,
  GeneratedPropertyValue = 1
}

export const enhancementAdditionalItems = [
  { label: 'None', value: EnhancementAdditionalItem.None },
  { label: 'GeneratedPropertyValue', value: EnhancementAdditionalItem.GeneratedPropertyValue }
] as const;

export const EnhancementAdditionalItemLabel: Record<EnhancementAdditionalItem, string> = {
  [EnhancementAdditionalItem.None]: 'None',
  [EnhancementAdditionalItem.GeneratedPropertyValue]: 'GeneratedPropertyValue'
};

export enum EnhancerTargetOptionsItem {
  AutoBindProperty = 1,
  AutoMatchMultilevelString = 2,
  CoverSelectOrder = 3
}

export const enhancerTargetOptionsItems = [
  { label: 'AutoBindProperty', value: EnhancerTargetOptionsItem.AutoBindProperty },
  { label: 'AutoMatchMultilevelString', value: EnhancerTargetOptionsItem.AutoMatchMultilevelString },
  { label: 'CoverSelectOrder', value: EnhancerTargetOptionsItem.CoverSelectOrder }
] as const;

export const EnhancerTargetOptionsItemLabel: Record<EnhancerTargetOptionsItem, string> = {
  [EnhancerTargetOptionsItem.AutoBindProperty]: 'AutoBindProperty',
  [EnhancerTargetOptionsItem.AutoMatchMultilevelString]: 'AutoMatchMultilevelString',
  [EnhancerTargetOptionsItem.CoverSelectOrder]: 'CoverSelectOrder'
};

export enum BackgroundTaskName {
  SyncMediaLibrary = 1,
  PrepareCache = 2,
  MoveFiles = 3,
  Enhance = 4
}

export const backgroundTaskNames = [
  { label: 'SyncMediaLibrary', value: BackgroundTaskName.SyncMediaLibrary },
  { label: 'PrepareCache', value: BackgroundTaskName.PrepareCache },
  { label: 'MoveFiles', value: BackgroundTaskName.MoveFiles },
  { label: 'Enhance', value: BackgroundTaskName.Enhance }
] as const;

export const BackgroundTaskNameLabel: Record<BackgroundTaskName, string> = {
  [BackgroundTaskName.SyncMediaLibrary]: 'SyncMediaLibrary',
  [BackgroundTaskName.PrepareCache]: 'PrepareCache',
  [BackgroundTaskName.MoveFiles]: 'MoveFiles',
  [BackgroundTaskName.Enhance]: 'Enhance'
};

export enum BuiltinPropertyForDisplayName {
  Filename = 15
}

export const builtinPropertyForDisplayNames = [
  { label: 'Filename', value: BuiltinPropertyForDisplayName.Filename }
] as const;

export const BuiltinPropertyForDisplayNameLabel: Record<BuiltinPropertyForDisplayName, string> = {
  [BuiltinPropertyForDisplayName.Filename]: 'Filename'
};

export enum TampermonkeyScript {
  SoulPlus = 1,
  ExHentai = 2
}

export const tampermonkeyScripts = [
  { label: 'SoulPlus', value: TampermonkeyScript.SoulPlus },
  { label: 'ExHentai', value: TampermonkeyScript.ExHentai }
] as const;

export const TampermonkeyScriptLabel: Record<TampermonkeyScript, string> = {
  [TampermonkeyScript.SoulPlus]: 'SoulPlus',
  [TampermonkeyScript.ExHentai]: 'ExHentai'
};

export enum PostParserSource {
  SoulPlus = 5
}

export const postParserSources = [
  { label: 'SoulPlus', value: PostParserSource.SoulPlus }
] as const;

export const PostParserSourceLabel: Record<PostParserSource, string> = {
  [PostParserSource.SoulPlus]: 'SoulPlus'
};

export enum FileNameModifierCaseType {
  TitleCase = 1,
  UpperCase = 2,
  LowerCase = 3,
  CamelCase = 4,
  PascalCase = 5
}

export const fileNameModifierCaseTypes = [
  { label: 'TitleCase', value: FileNameModifierCaseType.TitleCase },
  { label: 'UpperCase', value: FileNameModifierCaseType.UpperCase },
  { label: 'LowerCase', value: FileNameModifierCaseType.LowerCase },
  { label: 'CamelCase', value: FileNameModifierCaseType.CamelCase },
  { label: 'PascalCase', value: FileNameModifierCaseType.PascalCase }
] as const;

export const FileNameModifierCaseTypeLabel: Record<FileNameModifierCaseType, string> = {
  [FileNameModifierCaseType.TitleCase]: 'TitleCase',
  [FileNameModifierCaseType.UpperCase]: 'UpperCase',
  [FileNameModifierCaseType.LowerCase]: 'LowerCase',
  [FileNameModifierCaseType.CamelCase]: 'CamelCase',
  [FileNameModifierCaseType.PascalCase]: 'PascalCase'
};

export enum FileNameModifierFileNameTarget {
  FileName = 1,
  FileNameWithoutExtension = 2,
  Extension = 3,
  ExtensionWithoutDot = 4
}

export const fileNameModifierFileNameTargets = [
  { label: 'FileName', value: FileNameModifierFileNameTarget.FileName },
  { label: 'FileNameWithoutExtension', value: FileNameModifierFileNameTarget.FileNameWithoutExtension },
  { label: 'Extension', value: FileNameModifierFileNameTarget.Extension },
  { label: 'ExtensionWithoutDot', value: FileNameModifierFileNameTarget.ExtensionWithoutDot }
] as const;

export const FileNameModifierFileNameTargetLabel: Record<FileNameModifierFileNameTarget, string> = {
  [FileNameModifierFileNameTarget.FileName]: 'FileName',
  [FileNameModifierFileNameTarget.FileNameWithoutExtension]: 'FileNameWithoutExtension',
  [FileNameModifierFileNameTarget.Extension]: 'Extension',
  [FileNameModifierFileNameTarget.ExtensionWithoutDot]: 'ExtensionWithoutDot'
};

export enum FileNameModifierOperationType {
  Insert = 1,
  AddDateTime = 2,
  Delete = 3,
  Replace = 4,
  ChangeCase = 5,
  AddAlphabetSequence = 6,
  Reverse = 7
}

export const fileNameModifierOperationTypes = [
  { label: 'Insert', value: FileNameModifierOperationType.Insert },
  { label: 'AddDateTime', value: FileNameModifierOperationType.AddDateTime },
  { label: 'Delete', value: FileNameModifierOperationType.Delete },
  { label: 'Replace', value: FileNameModifierOperationType.Replace },
  { label: 'ChangeCase', value: FileNameModifierOperationType.ChangeCase },
  { label: 'AddAlphabetSequence', value: FileNameModifierOperationType.AddAlphabetSequence },
  { label: 'Reverse', value: FileNameModifierOperationType.Reverse }
] as const;

export const FileNameModifierOperationTypeLabel: Record<FileNameModifierOperationType, string> = {
  [FileNameModifierOperationType.Insert]: 'Insert',
  [FileNameModifierOperationType.AddDateTime]: 'AddDateTime',
  [FileNameModifierOperationType.Delete]: 'Delete',
  [FileNameModifierOperationType.Replace]: 'Replace',
  [FileNameModifierOperationType.ChangeCase]: 'ChangeCase',
  [FileNameModifierOperationType.AddAlphabetSequence]: 'AddAlphabetSequence',
  [FileNameModifierOperationType.Reverse]: 'Reverse'
};

export enum FileNameModifierPosition {
  Start = 1,
  End = 2,
  AtPosition = 3,
  AfterText = 4,
  BeforeText = 5
}

export const fileNameModifierPositions = [
  { label: 'Start', value: FileNameModifierPosition.Start },
  { label: 'End', value: FileNameModifierPosition.End },
  { label: 'AtPosition', value: FileNameModifierPosition.AtPosition },
  { label: 'AfterText', value: FileNameModifierPosition.AfterText },
  { label: 'BeforeText', value: FileNameModifierPosition.BeforeText }
] as const;

export const FileNameModifierPositionLabel: Record<FileNameModifierPosition, string> = {
  [FileNameModifierPosition.Start]: 'Start',
  [FileNameModifierPosition.End]: 'End',
  [FileNameModifierPosition.AtPosition]: 'AtPosition',
  [FileNameModifierPosition.AfterText]: 'AfterText',
  [FileNameModifierPosition.BeforeText]: 'BeforeText'
};

export enum IwFsAttribute {
  Hidden = 1
}

export const iwFsAttributes = [
  { label: 'Hidden', value: IwFsAttribute.Hidden }
] as const;

export const IwFsAttributeLabel: Record<IwFsAttribute, string> = {
  [IwFsAttribute.Hidden]: 'Hidden'
};

export enum IwFsEntryChangeType {
  Created = 1,
  Renamed = 2,
  Changed = 3,
  Deleted = 4
}

export const iwFsEntryChangeTypes = [
  { label: 'Created', value: IwFsEntryChangeType.Created },
  { label: 'Renamed', value: IwFsEntryChangeType.Renamed },
  { label: 'Changed', value: IwFsEntryChangeType.Changed },
  { label: 'Deleted', value: IwFsEntryChangeType.Deleted }
] as const;

export const IwFsEntryChangeTypeLabel: Record<IwFsEntryChangeType, string> = {
  [IwFsEntryChangeType.Created]: 'Created',
  [IwFsEntryChangeType.Renamed]: 'Renamed',
  [IwFsEntryChangeType.Changed]: 'Changed',
  [IwFsEntryChangeType.Deleted]: 'Deleted'
};

export enum IwFsType {
  Unknown = 0,
  Directory = 100,
  Image = 200,
  CompressedFileEntry = 300,
  CompressedFilePart = 400,
  Symlink = 500,
  Video = 600,
  Audio = 700,
  Drive = 1000,
  Invalid = 10000
}

export const iwFsTypes = [
  { label: 'Unknown', value: IwFsType.Unknown },
  { label: 'Directory', value: IwFsType.Directory },
  { label: 'Image', value: IwFsType.Image },
  { label: 'CompressedFileEntry', value: IwFsType.CompressedFileEntry },
  { label: 'CompressedFilePart', value: IwFsType.CompressedFilePart },
  { label: 'Symlink', value: IwFsType.Symlink },
  { label: 'Video', value: IwFsType.Video },
  { label: 'Audio', value: IwFsType.Audio },
  { label: 'Drive', value: IwFsType.Drive },
  { label: 'Invalid', value: IwFsType.Invalid }
] as const;

export const IwFsTypeLabel: Record<IwFsType, string> = {
  [IwFsType.Unknown]: 'Unknown',
  [IwFsType.Directory]: 'Directory',
  [IwFsType.Image]: 'Image',
  [IwFsType.CompressedFileEntry]: 'CompressedFileEntry',
  [IwFsType.CompressedFilePart]: 'CompressedFilePart',
  [IwFsType.Symlink]: 'Symlink',
  [IwFsType.Video]: 'Video',
  [IwFsType.Audio]: 'Audio',
  [IwFsType.Drive]: 'Drive',
  [IwFsType.Invalid]: 'Invalid'
};

export enum PixivDownloadTaskType {
  Search = 1,
  Ranking = 2,
  Following = 3
}

export const pixivDownloadTaskTypes = [
  { label: 'Search', value: PixivDownloadTaskType.Search },
  { label: 'Ranking', value: PixivDownloadTaskType.Ranking },
  { label: 'Following', value: PixivDownloadTaskType.Following }
] as const;

export const PixivDownloadTaskTypeLabel: Record<PixivDownloadTaskType, string> = {
  [PixivDownloadTaskType.Search]: 'Search',
  [PixivDownloadTaskType.Ranking]: 'Ranking',
  [PixivDownloadTaskType.Following]: 'Following'
};

export enum PixivNamingFields {
  IllustrationId = 0,
  IllustrationTitle = 1,
  UploadDate = 2,
  Tags = 3,
  UserId = 4,
  UserName = 5,
  PageNo = 6,
  Extension = 7
}

export const pixivNamingFields = [
  { label: 'IllustrationId', value: PixivNamingFields.IllustrationId },
  { label: 'IllustrationTitle', value: PixivNamingFields.IllustrationTitle },
  { label: 'UploadDate', value: PixivNamingFields.UploadDate },
  { label: 'Tags', value: PixivNamingFields.Tags },
  { label: 'UserId', value: PixivNamingFields.UserId },
  { label: 'UserName', value: PixivNamingFields.UserName },
  { label: 'PageNo', value: PixivNamingFields.PageNo },
  { label: 'Extension', value: PixivNamingFields.Extension }
] as const;

export const PixivNamingFieldsLabel: Record<PixivNamingFields, string> = {
  [PixivNamingFields.IllustrationId]: 'IllustrationId',
  [PixivNamingFields.IllustrationTitle]: 'IllustrationTitle',
  [PixivNamingFields.UploadDate]: 'UploadDate',
  [PixivNamingFields.Tags]: 'Tags',
  [PixivNamingFields.UserId]: 'UserId',
  [PixivNamingFields.UserName]: 'UserName',
  [PixivNamingFields.PageNo]: 'PageNo',
  [PixivNamingFields.Extension]: 'Extension'
};

export enum PatreonDownloadTaskType {
  Creator = 1,
  Following = 2,
  SinglePost = 3
}

export const patreonDownloadTaskTypes = [
  { label: 'Creator', value: PatreonDownloadTaskType.Creator },
  { label: 'Following', value: PatreonDownloadTaskType.Following },
  { label: 'SinglePost', value: PatreonDownloadTaskType.SinglePost }
] as const;

export const PatreonDownloadTaskTypeLabel: Record<PatreonDownloadTaskType, string> = {
  [PatreonDownloadTaskType.Creator]: 'Creator',
  [PatreonDownloadTaskType.Following]: 'Following',
  [PatreonDownloadTaskType.SinglePost]: 'SinglePost'
};

export enum PatreonNamingFields {
  PostId = 0,
  PostTitle = 1,
  PublishDate = 2,
  CreatorId = 3,
  CreatorName = 4,
  TierLevel = 5,
  FileNo = 6,
  Extension = 7
}

export const patreonNamingFields = [
  { label: 'PostId', value: PatreonNamingFields.PostId },
  { label: 'PostTitle', value: PatreonNamingFields.PostTitle },
  { label: 'PublishDate', value: PatreonNamingFields.PublishDate },
  { label: 'CreatorId', value: PatreonNamingFields.CreatorId },
  { label: 'CreatorName', value: PatreonNamingFields.CreatorName },
  { label: 'TierLevel', value: PatreonNamingFields.TierLevel },
  { label: 'FileNo', value: PatreonNamingFields.FileNo },
  { label: 'Extension', value: PatreonNamingFields.Extension }
] as const;

export const PatreonNamingFieldsLabel: Record<PatreonNamingFields, string> = {
  [PatreonNamingFields.PostId]: 'PostId',
  [PatreonNamingFields.PostTitle]: 'PostTitle',
  [PatreonNamingFields.PublishDate]: 'PublishDate',
  [PatreonNamingFields.CreatorId]: 'CreatorId',
  [PatreonNamingFields.CreatorName]: 'CreatorName',
  [PatreonNamingFields.TierLevel]: 'TierLevel',
  [PatreonNamingFields.FileNo]: 'FileNo',
  [PatreonNamingFields.Extension]: 'Extension'
};

export enum FantiaDownloadTaskType {
  Creator = 1,
  Following = 2,
  SinglePost = 3
}

export const fantiaDownloadTaskTypes = [
  { label: 'Creator', value: FantiaDownloadTaskType.Creator },
  { label: 'Following', value: FantiaDownloadTaskType.Following },
  { label: 'SinglePost', value: FantiaDownloadTaskType.SinglePost }
] as const;

export const FantiaDownloadTaskTypeLabel: Record<FantiaDownloadTaskType, string> = {
  [FantiaDownloadTaskType.Creator]: 'Creator',
  [FantiaDownloadTaskType.Following]: 'Following',
  [FantiaDownloadTaskType.SinglePost]: 'SinglePost'
};

export enum FantiaNamingFields {
  PostId = 0,
  PostTitle = 1,
  PublishDate = 2,
  FanclubId = 3,
  FanclubName = 4,
  FileNo = 5,
  Extension = 6
}

export const fantiaNamingFields = [
  { label: 'PostId', value: FantiaNamingFields.PostId },
  { label: 'PostTitle', value: FantiaNamingFields.PostTitle },
  { label: 'PublishDate', value: FantiaNamingFields.PublishDate },
  { label: 'FanclubId', value: FantiaNamingFields.FanclubId },
  { label: 'FanclubName', value: FantiaNamingFields.FanclubName },
  { label: 'FileNo', value: FantiaNamingFields.FileNo },
  { label: 'Extension', value: FantiaNamingFields.Extension }
] as const;

export const FantiaNamingFieldsLabel: Record<FantiaNamingFields, string> = {
  [FantiaNamingFields.PostId]: 'PostId',
  [FantiaNamingFields.PostTitle]: 'PostTitle',
  [FantiaNamingFields.PublishDate]: 'PublishDate',
  [FantiaNamingFields.FanclubId]: 'FanclubId',
  [FantiaNamingFields.FanclubName]: 'FanclubName',
  [FantiaNamingFields.FileNo]: 'FileNo',
  [FantiaNamingFields.Extension]: 'Extension'
};

export enum FanboxDownloadTaskType {
  Creator = 1,
  Following = 2,
  SinglePost = 3
}

export const fanboxDownloadTaskTypes = [
  { label: 'Creator', value: FanboxDownloadTaskType.Creator },
  { label: 'Following', value: FanboxDownloadTaskType.Following },
  { label: 'SinglePost', value: FanboxDownloadTaskType.SinglePost }
] as const;

export const FanboxDownloadTaskTypeLabel: Record<FanboxDownloadTaskType, string> = {
  [FanboxDownloadTaskType.Creator]: 'Creator',
  [FanboxDownloadTaskType.Following]: 'Following',
  [FanboxDownloadTaskType.SinglePost]: 'SinglePost'
};

export enum FanboxNamingFields {
  PostId = 0,
  PostTitle = 1,
  PublishDate = 2,
  CreatorId = 3,
  CreatorName = 4,
  FileNo = 5,
  Extension = 6
}

export const fanboxNamingFields = [
  { label: 'PostId', value: FanboxNamingFields.PostId },
  { label: 'PostTitle', value: FanboxNamingFields.PostTitle },
  { label: 'PublishDate', value: FanboxNamingFields.PublishDate },
  { label: 'CreatorId', value: FanboxNamingFields.CreatorId },
  { label: 'CreatorName', value: FanboxNamingFields.CreatorName },
  { label: 'FileNo', value: FanboxNamingFields.FileNo },
  { label: 'Extension', value: FanboxNamingFields.Extension }
] as const;

export const FanboxNamingFieldsLabel: Record<FanboxNamingFields, string> = {
  [FanboxNamingFields.PostId]: 'PostId',
  [FanboxNamingFields.PostTitle]: 'PostTitle',
  [FanboxNamingFields.PublishDate]: 'PublishDate',
  [FanboxNamingFields.CreatorId]: 'CreatorId',
  [FanboxNamingFields.CreatorName]: 'CreatorName',
  [FanboxNamingFields.FileNo]: 'FileNo',
  [FanboxNamingFields.Extension]: 'Extension'
};

export enum ExHentaiDownloadTaskType {
  SingleWork = 1,
  Watched = 2,
  List = 3,
  Torrent = 4
}

export const exHentaiDownloadTaskTypes = [
  { label: 'SingleWork', value: ExHentaiDownloadTaskType.SingleWork },
  { label: 'Watched', value: ExHentaiDownloadTaskType.Watched },
  { label: 'List', value: ExHentaiDownloadTaskType.List },
  { label: 'Torrent', value: ExHentaiDownloadTaskType.Torrent }
] as const;

export const ExHentaiDownloadTaskTypeLabel: Record<ExHentaiDownloadTaskType, string> = {
  [ExHentaiDownloadTaskType.SingleWork]: 'SingleWork',
  [ExHentaiDownloadTaskType.Watched]: 'Watched',
  [ExHentaiDownloadTaskType.List]: 'List',
  [ExHentaiDownloadTaskType.Torrent]: 'Torrent'
};

export enum ExHentaiNamingFields {
  GalleryId = 0,
  GalleryToken = 1,
  RawName = 2,
  Name = 3,
  Category = 4,
  PageTitle = 5,
  Extension = 6
}

export const exHentaiNamingFields = [
  { label: 'GalleryId', value: ExHentaiNamingFields.GalleryId },
  { label: 'GalleryToken', value: ExHentaiNamingFields.GalleryToken },
  { label: 'RawName', value: ExHentaiNamingFields.RawName },
  { label: 'Name', value: ExHentaiNamingFields.Name },
  { label: 'Category', value: ExHentaiNamingFields.Category },
  { label: 'PageTitle', value: ExHentaiNamingFields.PageTitle },
  { label: 'Extension', value: ExHentaiNamingFields.Extension }
] as const;

export const ExHentaiNamingFieldsLabel: Record<ExHentaiNamingFields, string> = {
  [ExHentaiNamingFields.GalleryId]: 'GalleryId',
  [ExHentaiNamingFields.GalleryToken]: 'GalleryToken',
  [ExHentaiNamingFields.RawName]: 'RawName',
  [ExHentaiNamingFields.Name]: 'Name',
  [ExHentaiNamingFields.Category]: 'Category',
  [ExHentaiNamingFields.PageTitle]: 'PageTitle',
  [ExHentaiNamingFields.Extension]: 'Extension'
};

export enum CienDownloadTaskType {
  Creator = 1,
  Following = 2,
  SinglePost = 3
}

export const cienDownloadTaskTypes = [
  { label: 'Creator', value: CienDownloadTaskType.Creator },
  { label: 'Following', value: CienDownloadTaskType.Following },
  { label: 'SinglePost', value: CienDownloadTaskType.SinglePost }
] as const;

export const CienDownloadTaskTypeLabel: Record<CienDownloadTaskType, string> = {
  [CienDownloadTaskType.Creator]: 'Creator',
  [CienDownloadTaskType.Following]: 'Following',
  [CienDownloadTaskType.SinglePost]: 'SinglePost'
};

export enum CienNamingFields {
  ArticleId = 0,
  ArticleTitle = 1,
  PublishDate = 2,
  AuthorId = 3,
  AuthorName = 4,
  FileNo = 5,
  Extension = 6
}

export const cienNamingFields = [
  { label: 'ArticleId', value: CienNamingFields.ArticleId },
  { label: 'ArticleTitle', value: CienNamingFields.ArticleTitle },
  { label: 'PublishDate', value: CienNamingFields.PublishDate },
  { label: 'AuthorId', value: CienNamingFields.AuthorId },
  { label: 'AuthorName', value: CienNamingFields.AuthorName },
  { label: 'FileNo', value: CienNamingFields.FileNo },
  { label: 'Extension', value: CienNamingFields.Extension }
] as const;

export const CienNamingFieldsLabel: Record<CienNamingFields, string> = {
  [CienNamingFields.ArticleId]: 'ArticleId',
  [CienNamingFields.ArticleTitle]: 'ArticleTitle',
  [CienNamingFields.PublishDate]: 'PublishDate',
  [CienNamingFields.AuthorId]: 'AuthorId',
  [CienNamingFields.AuthorName]: 'AuthorName',
  [CienNamingFields.FileNo]: 'FileNo',
  [CienNamingFields.Extension]: 'Extension'
};

export enum BilibiliDownloadTaskType {
  Favorites = 1
}

export const bilibiliDownloadTaskTypes = [
  { label: 'Favorites', value: BilibiliDownloadTaskType.Favorites }
] as const;

export const BilibiliDownloadTaskTypeLabel: Record<BilibiliDownloadTaskType, string> = {
  [BilibiliDownloadTaskType.Favorites]: 'Favorites'
};

export enum BilibiliNamingFields {
  UploaderId = 0,
  UploaderName = 1,
  AId = 2,
  BvId = 3,
  PostTitle = 4,
  CId = 5,
  PartNo = 6,
  PartName = 7,
  QualityName = 8,
  Extension = 9
}

export const bilibiliNamingFields = [
  { label: 'UploaderId', value: BilibiliNamingFields.UploaderId },
  { label: 'UploaderName', value: BilibiliNamingFields.UploaderName },
  { label: 'AId', value: BilibiliNamingFields.AId },
  { label: 'BvId', value: BilibiliNamingFields.BvId },
  { label: 'PostTitle', value: BilibiliNamingFields.PostTitle },
  { label: 'CId', value: BilibiliNamingFields.CId },
  { label: 'PartNo', value: BilibiliNamingFields.PartNo },
  { label: 'PartName', value: BilibiliNamingFields.PartName },
  { label: 'QualityName', value: BilibiliNamingFields.QualityName },
  { label: 'Extension', value: BilibiliNamingFields.Extension }
] as const;

export const BilibiliNamingFieldsLabel: Record<BilibiliNamingFields, string> = {
  [BilibiliNamingFields.UploaderId]: 'UploaderId',
  [BilibiliNamingFields.UploaderName]: 'UploaderName',
  [BilibiliNamingFields.AId]: 'AId',
  [BilibiliNamingFields.BvId]: 'BvId',
  [BilibiliNamingFields.PostTitle]: 'PostTitle',
  [BilibiliNamingFields.CId]: 'CId',
  [BilibiliNamingFields.PartNo]: 'PartNo',
  [BilibiliNamingFields.PartName]: 'PartName',
  [BilibiliNamingFields.QualityName]: 'QualityName',
  [BilibiliNamingFields.Extension]: 'Extension'
};

export enum DownloaderStatus {
  JustCreated = 0,
  Starting = 100,
  Downloading = 200,
  Complete = 300,
  Failed = 400,
  Stopping = 500,
  Stopped = 600
}

export const downloaderStatuses = [
  { label: 'JustCreated', value: DownloaderStatus.JustCreated },
  { label: 'Starting', value: DownloaderStatus.Starting },
  { label: 'Downloading', value: DownloaderStatus.Downloading },
  { label: 'Complete', value: DownloaderStatus.Complete },
  { label: 'Failed', value: DownloaderStatus.Failed },
  { label: 'Stopping', value: DownloaderStatus.Stopping },
  { label: 'Stopped', value: DownloaderStatus.Stopped }
] as const;

export const DownloaderStatusLabel: Record<DownloaderStatus, string> = {
  [DownloaderStatus.JustCreated]: 'JustCreated',
  [DownloaderStatus.Starting]: 'Starting',
  [DownloaderStatus.Downloading]: 'Downloading',
  [DownloaderStatus.Complete]: 'Complete',
  [DownloaderStatus.Failed]: 'Failed',
  [DownloaderStatus.Stopping]: 'Stopping',
  [DownloaderStatus.Stopped]: 'Stopped'
};

export enum DownloaderStopBy {
  ManuallyStop = 1,
  AppendToTheQueue = 2
}

export const downloaderStopBies = [
  { label: 'ManuallyStop', value: DownloaderStopBy.ManuallyStop },
  { label: 'AppendToTheQueue', value: DownloaderStopBy.AppendToTheQueue }
] as const;

export const DownloaderStopByLabel: Record<DownloaderStopBy, string> = {
  [DownloaderStopBy.ManuallyStop]: 'ManuallyStop',
  [DownloaderStopBy.AppendToTheQueue]: 'AppendToTheQueue'
};

export enum DownloadTaskAction {
  StartManually = 1,
  Restart = 2,
  Disable = 3,
  StartAutomatically = 4
}

export const downloadTaskActions = [
  { label: 'StartManually', value: DownloadTaskAction.StartManually },
  { label: 'Restart', value: DownloadTaskAction.Restart },
  { label: 'Disable', value: DownloadTaskAction.Disable },
  { label: 'StartAutomatically', value: DownloadTaskAction.StartAutomatically }
] as const;

export const DownloadTaskActionLabel: Record<DownloadTaskAction, string> = {
  [DownloadTaskAction.StartManually]: 'StartManually',
  [DownloadTaskAction.Restart]: 'Restart',
  [DownloadTaskAction.Disable]: 'Disable',
  [DownloadTaskAction.StartAutomatically]: 'StartAutomatically'
};

export enum DownloadTaskActionOnConflict {
  NotSet = 0,
  StopOthers = 1,
  Ignore = 2
}

export const downloadTaskActionOnConflicts = [
  { label: 'NotSet', value: DownloadTaskActionOnConflict.NotSet },
  { label: 'StopOthers', value: DownloadTaskActionOnConflict.StopOthers },
  { label: 'Ignore', value: DownloadTaskActionOnConflict.Ignore }
] as const;

export const DownloadTaskActionOnConflictLabel: Record<DownloadTaskActionOnConflict, string> = {
  [DownloadTaskActionOnConflict.NotSet]: 'NotSet',
  [DownloadTaskActionOnConflict.StopOthers]: 'StopOthers',
  [DownloadTaskActionOnConflict.Ignore]: 'Ignore'
};

export enum DownloadTaskDbModelStatus {
  InProgress = 100,
  Disabled = 200,
  Complete = 300,
  Failed = 400
}

export const downloadTaskDbModelStatuses = [
  { label: 'InProgress', value: DownloadTaskDbModelStatus.InProgress },
  { label: 'Disabled', value: DownloadTaskDbModelStatus.Disabled },
  { label: 'Complete', value: DownloadTaskDbModelStatus.Complete },
  { label: 'Failed', value: DownloadTaskDbModelStatus.Failed }
] as const;

export const DownloadTaskDbModelStatusLabel: Record<DownloadTaskDbModelStatus, string> = {
  [DownloadTaskDbModelStatus.InProgress]: 'InProgress',
  [DownloadTaskDbModelStatus.Disabled]: 'Disabled',
  [DownloadTaskDbModelStatus.Complete]: 'Complete',
  [DownloadTaskDbModelStatus.Failed]: 'Failed'
};

export enum DownloadTaskStartMode {
  AutoStart = 1,
  ManualStart = 2
}

export const downloadTaskStartModes = [
  { label: 'AutoStart', value: DownloadTaskStartMode.AutoStart },
  { label: 'ManualStart', value: DownloadTaskStartMode.ManualStart }
] as const;

export const DownloadTaskStartModeLabel: Record<DownloadTaskStartMode, string> = {
  [DownloadTaskStartMode.AutoStart]: 'AutoStart',
  [DownloadTaskStartMode.ManualStart]: 'ManualStart'
};

export enum DownloadTaskStatus {
  Idle = 100,
  InQueue = 200,
  Starting = 300,
  Downloading = 400,
  Stopping = 500,
  Complete = 600,
  Failed = 700,
  Disabled = 800
}

export const downloadTaskStatuses = [
  { label: 'Idle', value: DownloadTaskStatus.Idle },
  { label: 'InQueue', value: DownloadTaskStatus.InQueue },
  { label: 'Starting', value: DownloadTaskStatus.Starting },
  { label: 'Downloading', value: DownloadTaskStatus.Downloading },
  { label: 'Stopping', value: DownloadTaskStatus.Stopping },
  { label: 'Complete', value: DownloadTaskStatus.Complete },
  { label: 'Failed', value: DownloadTaskStatus.Failed },
  { label: 'Disabled', value: DownloadTaskStatus.Disabled }
] as const;

export const DownloadTaskStatusLabel: Record<DownloadTaskStatus, string> = {
  [DownloadTaskStatus.Idle]: 'Idle',
  [DownloadTaskStatus.InQueue]: 'InQueue',
  [DownloadTaskStatus.Starting]: 'Starting',
  [DownloadTaskStatus.Downloading]: 'Downloading',
  [DownloadTaskStatus.Stopping]: 'Stopping',
  [DownloadTaskStatus.Complete]: 'Complete',
  [DownloadTaskStatus.Failed]: 'Failed',
  [DownloadTaskStatus.Disabled]: 'Disabled'
};

export enum DependentComponentStatus {
  NotInstalled = 1,
  Installed = 2,
  Installing = 3
}

export const dependentComponentStatuses = [
  { label: 'NotInstalled', value: DependentComponentStatus.NotInstalled },
  { label: 'Installed', value: DependentComponentStatus.Installed },
  { label: 'Installing', value: DependentComponentStatus.Installing }
] as const;

export const DependentComponentStatusLabel: Record<DependentComponentStatus, string> = {
  [DependentComponentStatus.NotInstalled]: 'NotInstalled',
  [DependentComponentStatus.Installed]: 'Installed',
  [DependentComponentStatus.Installing]: 'Installing'
};

export enum CaptchaType {
  Image = 1,
  SmsMessage = 2
}

export const captchaTypes = [
  { label: 'Image', value: CaptchaType.Image },
  { label: 'SmsMessage', value: CaptchaType.SmsMessage }
] as const;

export const CaptchaTypeLabel: Record<CaptchaType, string> = {
  [CaptchaType.Image]: 'Image',
  [CaptchaType.SmsMessage]: 'SmsMessage'
};

export enum DingSysLevel {
  Other = 0,
  MainAdministrator = 1,
  SubAdministrator = 2,
  Boss = 100
}

export const dingSysLevels = [
  { label: 'Other', value: DingSysLevel.Other },
  { label: 'MainAdministrator', value: DingSysLevel.MainAdministrator },
  { label: 'SubAdministrator', value: DingSysLevel.SubAdministrator },
  { label: 'Boss', value: DingSysLevel.Boss }
] as const;

export const DingSysLevelLabel: Record<DingSysLevel, string> = {
  [DingSysLevel.Other]: 'Other',
  [DingSysLevel.MainAdministrator]: 'MainAdministrator',
  [DingSysLevel.SubAdministrator]: 'SubAdministrator',
  [DingSysLevel.Boss]: 'Boss'
};

export enum ResponseCode {
  Success = 0,
  NotModified = 304,
  InvalidPayloadOrOperation = 400,
  Unauthenticated = 401,
  Unauthorized = 403,
  NotFound = 404,
  Conflict = 409,
  SystemError = 500,
  Timeout = 504,
  InvalidCaptcha = 100400
}

export const responseCodes = [
  { label: 'Success', value: ResponseCode.Success },
  { label: 'NotModified', value: ResponseCode.NotModified },
  { label: 'InvalidPayloadOrOperation', value: ResponseCode.InvalidPayloadOrOperation },
  { label: 'Unauthenticated', value: ResponseCode.Unauthenticated },
  { label: 'Unauthorized', value: ResponseCode.Unauthorized },
  { label: 'NotFound', value: ResponseCode.NotFound },
  { label: 'Conflict', value: ResponseCode.Conflict },
  { label: 'SystemError', value: ResponseCode.SystemError },
  { label: 'Timeout', value: ResponseCode.Timeout },
  { label: 'InvalidCaptcha', value: ResponseCode.InvalidCaptcha }
] as const;

export const ResponseCodeLabel: Record<ResponseCode, string> = {
  [ResponseCode.Success]: 'Success',
  [ResponseCode.NotModified]: 'NotModified',
  [ResponseCode.InvalidPayloadOrOperation]: 'InvalidPayloadOrOperation',
  [ResponseCode.Unauthenticated]: 'Unauthenticated',
  [ResponseCode.Unauthorized]: 'Unauthorized',
  [ResponseCode.NotFound]: 'NotFound',
  [ResponseCode.Conflict]: 'Conflict',
  [ResponseCode.SystemError]: 'SystemError',
  [ResponseCode.Timeout]: 'Timeout',
  [ResponseCode.InvalidCaptcha]: 'InvalidCaptcha'
};

export enum RuntimeMode {
  Dev = 0,
  WinForms = 1,
  Docker = 2
}

export const runtimeModes = [
  { label: 'Dev', value: RuntimeMode.Dev },
  { label: 'WinForms', value: RuntimeMode.WinForms },
  { label: 'Docker', value: RuntimeMode.Docker }
] as const;

export const RuntimeModeLabel: Record<RuntimeMode, string> = {
  [RuntimeMode.Dev]: 'Dev',
  [RuntimeMode.WinForms]: 'WinForms',
  [RuntimeMode.Docker]: 'Docker'
};

export enum Operation {
  DELETE = 0,
  INSERT = 1,
  EQUAL = 2
}

export const operations = [
  { label: 'DELETE', value: Operation.DELETE },
  { label: 'INSERT', value: Operation.INSERT },
  { label: 'EQUAL', value: Operation.EQUAL }
] as const;

export const OperationLabel: Record<Operation, string> = {
  [Operation.DELETE]: 'DELETE',
  [Operation.INSERT]: 'INSERT',
  [Operation.EQUAL]: 'EQUAL'
};

export enum ProgressorClientAction {
  Start = 1,
  Stop = 2,
  Initialize = 3
}

export const progressorClientActions = [
  { label: 'Start', value: ProgressorClientAction.Start },
  { label: 'Stop', value: ProgressorClientAction.Stop },
  { label: 'Initialize', value: ProgressorClientAction.Initialize }
] as const;

export const ProgressorClientActionLabel: Record<ProgressorClientAction, string> = {
  [ProgressorClientAction.Start]: 'Start',
  [ProgressorClientAction.Stop]: 'Stop',
  [ProgressorClientAction.Initialize]: 'Initialize'
};

export enum ProgressorEvent {
  StateChanged = 1,
  ProgressChanged = 2,
  ErrorOccurred = 3
}

export const progressorEvents = [
  { label: 'StateChanged', value: ProgressorEvent.StateChanged },
  { label: 'ProgressChanged', value: ProgressorEvent.ProgressChanged },
  { label: 'ErrorOccurred', value: ProgressorEvent.ErrorOccurred }
] as const;

export const ProgressorEventLabel: Record<ProgressorEvent, string> = {
  [ProgressorEvent.StateChanged]: 'StateChanged',
  [ProgressorEvent.ProgressChanged]: 'ProgressChanged',
  [ProgressorEvent.ErrorOccurred]: 'ErrorOccurred'
};

export enum ProgressorStatus {
  Idle = 1,
  Running = 2,
  Complete = 3,
  Suspended = 4
}

export const progressorStatuses = [
  { label: 'Idle', value: ProgressorStatus.Idle },
  { label: 'Running', value: ProgressorStatus.Running },
  { label: 'Complete', value: ProgressorStatus.Complete },
  { label: 'Suspended', value: ProgressorStatus.Suspended }
] as const;

export const ProgressorStatusLabel: Record<ProgressorStatus, string> = {
  [ProgressorStatus.Idle]: 'Idle',
  [ProgressorStatus.Running]: 'Running',
  [ProgressorStatus.Complete]: 'Complete',
  [ProgressorStatus.Suspended]: 'Suspended'
};

export enum FileStorageUploadResponseCode {
  Success = 0,
  Error = 500
}

export const fileStorageUploadResponseCodes = [
  { label: 'Success', value: FileStorageUploadResponseCode.Success },
  { label: 'Error', value: FileStorageUploadResponseCode.Error }
] as const;

export const FileStorageUploadResponseCodeLabel: Record<FileStorageUploadResponseCode, string> = {
  [FileStorageUploadResponseCode.Success]: 'Success',
  [FileStorageUploadResponseCode.Error]: 'Error'
};

export enum MessageStatus {
  ToBeSent = 0,
  Succeed = 1,
  Failed = 2
}

export const messageStatuses = [
  { label: 'ToBeSent', value: MessageStatus.ToBeSent },
  { label: 'Succeed', value: MessageStatus.Succeed },
  { label: 'Failed', value: MessageStatus.Failed }
] as const;

export const MessageStatusLabel: Record<MessageStatus, string> = {
  [MessageStatus.ToBeSent]: 'ToBeSent',
  [MessageStatus.Succeed]: 'Succeed',
  [MessageStatus.Failed]: 'Failed'
};

export enum NotificationType {
  Os = 1,
  Email = 2,
  OsAndEmail = 3,
  WeChat = 4,
  Sms = 8
}

export const notificationTypes = [
  { label: 'Os', value: NotificationType.Os },
  { label: 'Email', value: NotificationType.Email },
  { label: 'OsAndEmail', value: NotificationType.OsAndEmail },
  { label: 'WeChat', value: NotificationType.WeChat },
  { label: 'Sms', value: NotificationType.Sms }
] as const;

export const NotificationTypeLabel: Record<NotificationType, string> = {
  [NotificationType.Os]: 'Os',
  [NotificationType.Email]: 'Email',
  [NotificationType.OsAndEmail]: 'OsAndEmail',
  [NotificationType.WeChat]: 'WeChat',
  [NotificationType.Sms]: 'Sms'
};

export enum AdbDeviceState {
  Device = 1,
  Offline = 2,
  NoDevice = 3
}

export const adbDeviceStates = [
  { label: 'Device', value: AdbDeviceState.Device },
  { label: 'Offline', value: AdbDeviceState.Offline },
  { label: 'NoDevice', value: AdbDeviceState.NoDevice }
] as const;

export const AdbDeviceStateLabel: Record<AdbDeviceState, string> = {
  [AdbDeviceState.Device]: 'Device',
  [AdbDeviceState.Offline]: 'Offline',
  [AdbDeviceState.NoDevice]: 'NoDevice'
};

export enum AdbExceptionCode {
  Error = 1,
  InvalidExitCode = 2
}

export const adbExceptionCodes = [
  { label: 'Error', value: AdbExceptionCode.Error },
  { label: 'InvalidExitCode', value: AdbExceptionCode.InvalidExitCode }
] as const;

export const AdbExceptionCodeLabel: Record<AdbExceptionCode, string> = {
  [AdbExceptionCode.Error]: 'Error',
  [AdbExceptionCode.InvalidExitCode]: 'InvalidExitCode'
};

export enum AdbInternalError {
  Error = 1,
  INSTALL_FAILED_ALREADY_EXISTS = 100,
  DELETE_FAILED_INTERNAL_ERROR = 101,
  FailedToConnectDevice = 200
}

export const adbInternalErrors = [
  { label: 'Error', value: AdbInternalError.Error },
  { label: 'INSTALL_FAILED_ALREADY_EXISTS', value: AdbInternalError.INSTALL_FAILED_ALREADY_EXISTS },
  { label: 'DELETE_FAILED_INTERNAL_ERROR', value: AdbInternalError.DELETE_FAILED_INTERNAL_ERROR },
  { label: 'FailedToConnectDevice', value: AdbInternalError.FailedToConnectDevice }
] as const;

export const AdbInternalErrorLabel: Record<AdbInternalError, string> = {
  [AdbInternalError.Error]: 'Error',
  [AdbInternalError.INSTALL_FAILED_ALREADY_EXISTS]: 'INSTALL_FAILED_ALREADY_EXISTS',
  [AdbInternalError.DELETE_FAILED_INTERNAL_ERROR]: 'DELETE_FAILED_INTERNAL_ERROR',
  [AdbInternalError.FailedToConnectDevice]: 'FailedToConnectDevice'
};

export enum BulkModificationProcessorOptionsItemsFilterBy {
  All = 1,
  Containing = 2,
  Matching = 3
}

export const bulkModificationProcessorOptionsItemsFilterBies = [
  { label: 'All', value: BulkModificationProcessorOptionsItemsFilterBy.All },
  { label: 'Containing', value: BulkModificationProcessorOptionsItemsFilterBy.Containing },
  { label: 'Matching', value: BulkModificationProcessorOptionsItemsFilterBy.Matching }
] as const;

export const BulkModificationProcessorOptionsItemsFilterByLabel: Record<BulkModificationProcessorOptionsItemsFilterBy, string> = {
  [BulkModificationProcessorOptionsItemsFilterBy.All]: 'All',
  [BulkModificationProcessorOptionsItemsFilterBy.Containing]: 'Containing',
  [BulkModificationProcessorOptionsItemsFilterBy.Matching]: 'Matching'
};

export enum BulkModificationStringProcessOperation {
  Delete = 1,
  SetWithFixedValue = 2,
  AddToStart = 3,
  AddToEnd = 4,
  AddToAnyPosition = 5,
  RemoveFromStart = 6,
  RemoveFromEnd = 7,
  RemoveFromAnyPosition = 8,
  ReplaceFromStart = 9,
  ReplaceFromEnd = 10,
  ReplaceFromAnyPosition = 11,
  ReplaceWithRegex = 12
}

export const bulkModificationStringProcessOperations = [
  { label: 'Delete', value: BulkModificationStringProcessOperation.Delete },
  { label: 'SetWithFixedValue', value: BulkModificationStringProcessOperation.SetWithFixedValue },
  { label: 'AddToStart', value: BulkModificationStringProcessOperation.AddToStart },
  { label: 'AddToEnd', value: BulkModificationStringProcessOperation.AddToEnd },
  { label: 'AddToAnyPosition', value: BulkModificationStringProcessOperation.AddToAnyPosition },
  { label: 'RemoveFromStart', value: BulkModificationStringProcessOperation.RemoveFromStart },
  { label: 'RemoveFromEnd', value: BulkModificationStringProcessOperation.RemoveFromEnd },
  { label: 'RemoveFromAnyPosition', value: BulkModificationStringProcessOperation.RemoveFromAnyPosition },
  { label: 'ReplaceFromStart', value: BulkModificationStringProcessOperation.ReplaceFromStart },
  { label: 'ReplaceFromEnd', value: BulkModificationStringProcessOperation.ReplaceFromEnd },
  { label: 'ReplaceFromAnyPosition', value: BulkModificationStringProcessOperation.ReplaceFromAnyPosition },
  { label: 'ReplaceWithRegex', value: BulkModificationStringProcessOperation.ReplaceWithRegex }
] as const;

export const BulkModificationStringProcessOperationLabel: Record<BulkModificationStringProcessOperation, string> = {
  [BulkModificationStringProcessOperation.Delete]: 'Delete',
  [BulkModificationStringProcessOperation.SetWithFixedValue]: 'SetWithFixedValue',
  [BulkModificationStringProcessOperation.AddToStart]: 'AddToStart',
  [BulkModificationStringProcessOperation.AddToEnd]: 'AddToEnd',
  [BulkModificationStringProcessOperation.AddToAnyPosition]: 'AddToAnyPosition',
  [BulkModificationStringProcessOperation.RemoveFromStart]: 'RemoveFromStart',
  [BulkModificationStringProcessOperation.RemoveFromEnd]: 'RemoveFromEnd',
  [BulkModificationStringProcessOperation.RemoveFromAnyPosition]: 'RemoveFromAnyPosition',
  [BulkModificationStringProcessOperation.ReplaceFromStart]: 'ReplaceFromStart',
  [BulkModificationStringProcessOperation.ReplaceFromEnd]: 'ReplaceFromEnd',
  [BulkModificationStringProcessOperation.ReplaceFromAnyPosition]: 'ReplaceFromAnyPosition',
  [BulkModificationStringProcessOperation.ReplaceWithRegex]: 'ReplaceWithRegex'
};

export enum BulkModificationListStringProcessOperation {
  SetWithFixedValue = 1,
  Append = 2,
  Prepend = 3,
  Modify = 4,
  Delete = 5
}

export const bulkModificationListStringProcessOperations = [
  { label: 'SetWithFixedValue', value: BulkModificationListStringProcessOperation.SetWithFixedValue },
  { label: 'Append', value: BulkModificationListStringProcessOperation.Append },
  { label: 'Prepend', value: BulkModificationListStringProcessOperation.Prepend },
  { label: 'Modify', value: BulkModificationListStringProcessOperation.Modify },
  { label: 'Delete', value: BulkModificationListStringProcessOperation.Delete }
] as const;

export const BulkModificationListStringProcessOperationLabel: Record<BulkModificationListStringProcessOperation, string> = {
  [BulkModificationListStringProcessOperation.SetWithFixedValue]: 'SetWithFixedValue',
  [BulkModificationListStringProcessOperation.Append]: 'Append',
  [BulkModificationListStringProcessOperation.Prepend]: 'Prepend',
  [BulkModificationListStringProcessOperation.Modify]: 'Modify',
  [BulkModificationListStringProcessOperation.Delete]: 'Delete'
};

export enum BulkModificationDiffType {
  Added = 1,
  Removed = 2,
  Modified = 3
}

export const bulkModificationDiffTypes = [
  { label: 'Added', value: BulkModificationDiffType.Added },
  { label: 'Removed', value: BulkModificationDiffType.Removed },
  { label: 'Modified', value: BulkModificationDiffType.Modified }
] as const;

export const BulkModificationDiffTypeLabel: Record<BulkModificationDiffType, string> = {
  [BulkModificationDiffType.Added]: 'Added',
  [BulkModificationDiffType.Removed]: 'Removed',
  [BulkModificationDiffType.Modified]: 'Modified'
};

export enum BulkModificationProcessorValueType {
  ManuallyInput = 1,
  Variable = 2
}

export const bulkModificationProcessorValueTypes = [
  { label: 'ManuallyInput', value: BulkModificationProcessorValueType.ManuallyInput },
  { label: 'Variable', value: BulkModificationProcessorValueType.Variable }
] as const;

export const BulkModificationProcessorValueTypeLabel: Record<BulkModificationProcessorValueType, string> = {
  [BulkModificationProcessorValueType.ManuallyInput]: 'ManuallyInput',
  [BulkModificationProcessorValueType.Variable]: 'Variable'
};

export enum AliasExceptionType {
  ConflictAliasGroup = 1
}

export const aliasExceptionTypes = [
  { label: 'ConflictAliasGroup', value: AliasExceptionType.ConflictAliasGroup }
] as const;

export const AliasExceptionTypeLabel: Record<AliasExceptionType, string> = {
  [AliasExceptionType.ConflictAliasGroup]: 'ConflictAliasGroup'
};

export enum CloseBehavior {
  Prompt = 0,
  Exit = 1,
  Minimize = 2,
  Cancel = 1000
}

export const closeBehaviors = [
  { label: 'Prompt', value: CloseBehavior.Prompt },
  { label: 'Exit', value: CloseBehavior.Exit },
  { label: 'Minimize', value: CloseBehavior.Minimize },
  { label: 'Cancel', value: CloseBehavior.Cancel }
] as const;

export const CloseBehaviorLabel: Record<CloseBehavior, string> = {
  [CloseBehavior.Prompt]: 'Prompt',
  [CloseBehavior.Exit]: 'Exit',
  [CloseBehavior.Minimize]: 'Minimize',
  [CloseBehavior.Cancel]: 'Cancel'
};

export enum IconType {
  UnknownFile = 1,
  Directory = 2,
  Dynamic = 3
}

export const iconTypes = [
  { label: 'UnknownFile', value: IconType.UnknownFile },
  { label: 'Directory', value: IconType.Directory },
  { label: 'Dynamic', value: IconType.Dynamic }
] as const;

export const IconTypeLabel: Record<IconType, string> = {
  [IconType.UnknownFile]: 'UnknownFile',
  [IconType.Directory]: 'Directory',
  [IconType.Dynamic]: 'Dynamic'
};

export enum UiTheme {
  FollowSystem = 0,
  Light = 1,
  Dark = 2
}

export const uiThemes = [
  { label: 'FollowSystem', value: UiTheme.FollowSystem },
  { label: 'Light', value: UiTheme.Light },
  { label: 'Dark', value: UiTheme.Dark }
] as const;

export const UiThemeLabel: Record<UiTheme, string> = {
  [UiTheme.FollowSystem]: 'FollowSystem',
  [UiTheme.Light]: 'Light',
  [UiTheme.Dark]: 'Dark'
};

export enum UpdaterStatus {
  Idle = 1,
  Running = 2,
  PendingRestart = 3,
  UpToDate = 4,
  Failed = 5
}

export const updaterStatuses = [
  { label: 'Idle', value: UpdaterStatus.Idle },
  { label: 'Running', value: UpdaterStatus.Running },
  { label: 'PendingRestart', value: UpdaterStatus.PendingRestart },
  { label: 'UpToDate', value: UpdaterStatus.UpToDate },
  { label: 'Failed', value: UpdaterStatus.Failed }
] as const;

export const UpdaterStatusLabel: Record<UpdaterStatus, string> = {
  [UpdaterStatus.Idle]: 'Idle',
  [UpdaterStatus.Running]: 'Running',
  [UpdaterStatus.PendingRestart]: 'PendingRestart',
  [UpdaterStatus.UpToDate]: 'UpToDate',
  [UpdaterStatus.Failed]: 'Failed'
};

export enum AppDistributionType {
  WindowsApp = 0,
  MacOsApp = 1,
  LinuxApp = 2,
  Android = 3,
  Ios = 4,
  WindowsServer = 5,
  LinuxServer = 6
}

export const appDistributionTypes = [
  { label: 'WindowsApp', value: AppDistributionType.WindowsApp },
  { label: 'MacOsApp', value: AppDistributionType.MacOsApp },
  { label: 'LinuxApp', value: AppDistributionType.LinuxApp },
  { label: 'Android', value: AppDistributionType.Android },
  { label: 'Ios', value: AppDistributionType.Ios },
  { label: 'WindowsServer', value: AppDistributionType.WindowsServer },
  { label: 'LinuxServer', value: AppDistributionType.LinuxServer }
] as const;

export const AppDistributionTypeLabel: Record<AppDistributionType, string> = {
  [AppDistributionType.WindowsApp]: 'WindowsApp',
  [AppDistributionType.MacOsApp]: 'MacOsApp',
  [AppDistributionType.LinuxApp]: 'LinuxApp',
  [AppDistributionType.Android]: 'Android',
  [AppDistributionType.Ios]: 'Ios',
  [AppDistributionType.WindowsServer]: 'WindowsServer',
  [AppDistributionType.LinuxServer]: 'LinuxServer'
};

export enum MigrationTiming {
  BeforeDbMigration = 1,
  AfterDbMigration = 2
}

export const migrationTimings = [
  { label: 'BeforeDbMigration', value: MigrationTiming.BeforeDbMigration },
  { label: 'AfterDbMigration', value: MigrationTiming.AfterDbMigration }
] as const;

export const MigrationTimingLabel: Record<MigrationTiming, string> = {
  [MigrationTiming.BeforeDbMigration]: 'BeforeDbMigration',
  [MigrationTiming.AfterDbMigration]: 'AfterDbMigration'
};

export enum OsPlatform {
  Unknown = 0,
  Windows = 1,
  Osx = 2,
  Linux = 3,
  FreeBsd = 4
}

export const osPlatforms = [
  { label: 'Unknown', value: OsPlatform.Unknown },
  { label: 'Windows', value: OsPlatform.Windows },
  { label: 'Osx', value: OsPlatform.Osx },
  { label: 'Linux', value: OsPlatform.Linux },
  { label: 'FreeBsd', value: OsPlatform.FreeBsd }
] as const;

export const OsPlatformLabel: Record<OsPlatform, string> = {
  [OsPlatform.Unknown]: 'Unknown',
  [OsPlatform.Windows]: 'Windows',
  [OsPlatform.Osx]: 'Osx',
  [OsPlatform.Linux]: 'Linux',
  [OsPlatform.FreeBsd]: 'FreeBsd'
};

export enum ExHentaiCategory {
  Unknown = 0,
  Misc = 1,
  Doushijin = 2,
  Manga = 4,
  ArtistCG = 8,
  GameCG = 16,
  ImageSet = 32,
  Cosplay = 64,
  AsianPorn = 128,
  NonH = 256,
  Western = 512
}

export const exHentaiCategories = [
  { label: 'Unknown', value: ExHentaiCategory.Unknown },
  { label: 'Misc', value: ExHentaiCategory.Misc },
  { label: 'Doushijin', value: ExHentaiCategory.Doushijin },
  { label: 'Manga', value: ExHentaiCategory.Manga },
  { label: 'ArtistCG', value: ExHentaiCategory.ArtistCG },
  { label: 'GameCG', value: ExHentaiCategory.GameCG },
  { label: 'ImageSet', value: ExHentaiCategory.ImageSet },
  { label: 'Cosplay', value: ExHentaiCategory.Cosplay },
  { label: 'AsianPorn', value: ExHentaiCategory.AsianPorn },
  { label: 'NonH', value: ExHentaiCategory.NonH },
  { label: 'Western', value: ExHentaiCategory.Western }
] as const;

export const ExHentaiCategoryLabel: Record<ExHentaiCategory, string> = {
  [ExHentaiCategory.Unknown]: 'Unknown',
  [ExHentaiCategory.Misc]: 'Misc',
  [ExHentaiCategory.Doushijin]: 'Doushijin',
  [ExHentaiCategory.Manga]: 'Manga',
  [ExHentaiCategory.ArtistCG]: 'ArtistCG',
  [ExHentaiCategory.GameCG]: 'GameCG',
  [ExHentaiCategory.ImageSet]: 'ImageSet',
  [ExHentaiCategory.Cosplay]: 'Cosplay',
  [ExHentaiCategory.AsianPorn]: 'AsianPorn',
  [ExHentaiCategory.NonH]: 'NonH',
  [ExHentaiCategory.Western]: 'Western'
};

export enum ExHentaiConnectionStatus {
  Ok = 1,
  InvalidCookie = 2,
  IpBanned = 3,
  UnknownError = 4
}

export const exHentaiConnectionStatuses = [
  { label: 'Ok', value: ExHentaiConnectionStatus.Ok },
  { label: 'InvalidCookie', value: ExHentaiConnectionStatus.InvalidCookie },
  { label: 'IpBanned', value: ExHentaiConnectionStatus.IpBanned },
  { label: 'UnknownError', value: ExHentaiConnectionStatus.UnknownError }
] as const;

export const ExHentaiConnectionStatusLabel: Record<ExHentaiConnectionStatus, string> = {
  [ExHentaiConnectionStatus.Ok]: 'Ok',
  [ExHentaiConnectionStatus.InvalidCookie]: 'InvalidCookie',
  [ExHentaiConnectionStatus.IpBanned]: 'IpBanned',
  [ExHentaiConnectionStatus.UnknownError]: 'UnknownError'
};

export enum ThirdPartyRequestResultType {
  Succeed = 1,
  TimedOut = 2,
  Banned = 3,
  Canceled = 4,
  Failed = 1000
}

export const thirdPartyRequestResultTypes = [
  { label: 'Succeed', value: ThirdPartyRequestResultType.Succeed },
  { label: 'TimedOut', value: ThirdPartyRequestResultType.TimedOut },
  { label: 'Banned', value: ThirdPartyRequestResultType.Banned },
  { label: 'Canceled', value: ThirdPartyRequestResultType.Canceled },
  { label: 'Failed', value: ThirdPartyRequestResultType.Failed }
] as const;

export const ThirdPartyRequestResultTypeLabel: Record<ThirdPartyRequestResultType, string> = {
  [ThirdPartyRequestResultType.Succeed]: 'Succeed',
  [ThirdPartyRequestResultType.TimedOut]: 'TimedOut',
  [ThirdPartyRequestResultType.Banned]: 'Banned',
  [ThirdPartyRequestResultType.Canceled]: 'Canceled',
  [ThirdPartyRequestResultType.Failed]: 'Failed'
};

export enum StandardValueConversionRule {
  Directly = 1,
  Incompatible = 2,
  ValuesWillBeMerged = 4,
  DateWillBeLost = 8,
  StringToTag = 16,
  OnlyFirstValidRemains = 64,
  StringToDateTime = 128,
  StringToTime = 256,
  UrlWillBeLost = 1024,
  StringToNumber = 2048,
  Trim = 8192,
  StringToLink = 16384,
  ValueWillBeSplit = 32768,
  BooleanToNumber = 65536,
  TimeToDateTime = 131072,
  TagGroupWillBeLost = 262144,
  ValueToBoolean = 524288
}

export const standardValueConversionRules = [
  { label: 'Directly', value: StandardValueConversionRule.Directly },
  { label: 'Incompatible', value: StandardValueConversionRule.Incompatible },
  { label: 'ValuesWillBeMerged', value: StandardValueConversionRule.ValuesWillBeMerged },
  { label: 'DateWillBeLost', value: StandardValueConversionRule.DateWillBeLost },
  { label: 'StringToTag', value: StandardValueConversionRule.StringToTag },
  { label: 'OnlyFirstValidRemains', value: StandardValueConversionRule.OnlyFirstValidRemains },
  { label: 'StringToDateTime', value: StandardValueConversionRule.StringToDateTime },
  { label: 'StringToTime', value: StandardValueConversionRule.StringToTime },
  { label: 'UrlWillBeLost', value: StandardValueConversionRule.UrlWillBeLost },
  { label: 'StringToNumber', value: StandardValueConversionRule.StringToNumber },
  { label: 'Trim', value: StandardValueConversionRule.Trim },
  { label: 'StringToLink', value: StandardValueConversionRule.StringToLink },
  { label: 'ValueWillBeSplit', value: StandardValueConversionRule.ValueWillBeSplit },
  { label: 'BooleanToNumber', value: StandardValueConversionRule.BooleanToNumber },
  { label: 'TimeToDateTime', value: StandardValueConversionRule.TimeToDateTime },
  { label: 'TagGroupWillBeLost', value: StandardValueConversionRule.TagGroupWillBeLost },
  { label: 'ValueToBoolean', value: StandardValueConversionRule.ValueToBoolean }
] as const;

export const StandardValueConversionRuleLabel: Record<StandardValueConversionRule, string> = {
  [StandardValueConversionRule.Directly]: 'Directly',
  [StandardValueConversionRule.Incompatible]: 'Incompatible',
  [StandardValueConversionRule.ValuesWillBeMerged]: 'ValuesWillBeMerged',
  [StandardValueConversionRule.DateWillBeLost]: 'DateWillBeLost',
  [StandardValueConversionRule.StringToTag]: 'StringToTag',
  [StandardValueConversionRule.OnlyFirstValidRemains]: 'OnlyFirstValidRemains',
  [StandardValueConversionRule.StringToDateTime]: 'StringToDateTime',
  [StandardValueConversionRule.StringToTime]: 'StringToTime',
  [StandardValueConversionRule.UrlWillBeLost]: 'UrlWillBeLost',
  [StandardValueConversionRule.StringToNumber]: 'StringToNumber',
  [StandardValueConversionRule.Trim]: 'Trim',
  [StandardValueConversionRule.StringToLink]: 'StringToLink',
  [StandardValueConversionRule.ValueWillBeSplit]: 'ValueWillBeSplit',
  [StandardValueConversionRule.BooleanToNumber]: 'BooleanToNumber',
  [StandardValueConversionRule.TimeToDateTime]: 'TimeToDateTime',
  [StandardValueConversionRule.TagGroupWillBeLost]: 'TagGroupWillBeLost',
  [StandardValueConversionRule.ValueToBoolean]: 'ValueToBoolean'
};

export enum PresetProperty {
  Name = 1,
  ReleaseDate = 2,
  Author = 3,
  Publisher = 4,
  Series = 5,
  Tag = 6,
  Language = 7,
  Original = 8,
  Actor = 9,
  VoiceActor = 10,
  Duration = 11,
  Director = 12,
  Singer = 13,
  EpisodeCount = 14,
  Resolution = 15,
  AspectRatio = 16,
  SubtitleLanguage = 17,
  VideoCodec = 18,
  IsCensored = 19,
  Is3D = 20,
  ImageCount = 21,
  IsAi = 22,
  Developer = 23,
  Character = 24,
  AudioFormat = 25,
  Bitrate = 26,
  Platform = 27,
  SubscriptionPlatform = 28,
  Type = 29
}

export const presetProperties = [
  { label: 'Name', value: PresetProperty.Name },
  { label: 'ReleaseDate', value: PresetProperty.ReleaseDate },
  { label: 'Author', value: PresetProperty.Author },
  { label: 'Publisher', value: PresetProperty.Publisher },
  { label: 'Series', value: PresetProperty.Series },
  { label: 'Tag', value: PresetProperty.Tag },
  { label: 'Language', value: PresetProperty.Language },
  { label: 'Original', value: PresetProperty.Original },
  { label: 'Actor', value: PresetProperty.Actor },
  { label: 'VoiceActor', value: PresetProperty.VoiceActor },
  { label: 'Duration', value: PresetProperty.Duration },
  { label: 'Director', value: PresetProperty.Director },
  { label: 'Singer', value: PresetProperty.Singer },
  { label: 'EpisodeCount', value: PresetProperty.EpisodeCount },
  { label: 'Resolution', value: PresetProperty.Resolution },
  { label: 'AspectRatio', value: PresetProperty.AspectRatio },
  { label: 'SubtitleLanguage', value: PresetProperty.SubtitleLanguage },
  { label: 'VideoCodec', value: PresetProperty.VideoCodec },
  { label: 'IsCensored', value: PresetProperty.IsCensored },
  { label: 'Is3D', value: PresetProperty.Is3D },
  { label: 'ImageCount', value: PresetProperty.ImageCount },
  { label: 'IsAi', value: PresetProperty.IsAi },
  { label: 'Developer', value: PresetProperty.Developer },
  { label: 'Character', value: PresetProperty.Character },
  { label: 'AudioFormat', value: PresetProperty.AudioFormat },
  { label: 'Bitrate', value: PresetProperty.Bitrate },
  { label: 'Platform', value: PresetProperty.Platform },
  { label: 'SubscriptionPlatform', value: PresetProperty.SubscriptionPlatform },
  { label: 'Type', value: PresetProperty.Type }
] as const;

export const PresetPropertyLabel: Record<PresetProperty, string> = {
  [PresetProperty.Name]: 'Name',
  [PresetProperty.ReleaseDate]: 'ReleaseDate',
  [PresetProperty.Author]: 'Author',
  [PresetProperty.Publisher]: 'Publisher',
  [PresetProperty.Series]: 'Series',
  [PresetProperty.Tag]: 'Tag',
  [PresetProperty.Language]: 'Language',
  [PresetProperty.Original]: 'Original',
  [PresetProperty.Actor]: 'Actor',
  [PresetProperty.VoiceActor]: 'VoiceActor',
  [PresetProperty.Duration]: 'Duration',
  [PresetProperty.Director]: 'Director',
  [PresetProperty.Singer]: 'Singer',
  [PresetProperty.EpisodeCount]: 'EpisodeCount',
  [PresetProperty.Resolution]: 'Resolution',
  [PresetProperty.AspectRatio]: 'AspectRatio',
  [PresetProperty.SubtitleLanguage]: 'SubtitleLanguage',
  [PresetProperty.VideoCodec]: 'VideoCodec',
  [PresetProperty.IsCensored]: 'IsCensored',
  [PresetProperty.Is3D]: 'Is3D',
  [PresetProperty.ImageCount]: 'ImageCount',
  [PresetProperty.IsAi]: 'IsAi',
  [PresetProperty.Developer]: 'Developer',
  [PresetProperty.Character]: 'Character',
  [PresetProperty.AudioFormat]: 'AudioFormat',
  [PresetProperty.Bitrate]: 'Bitrate',
  [PresetProperty.Platform]: 'Platform',
  [PresetProperty.SubscriptionPlatform]: 'SubscriptionPlatform',
  [PresetProperty.Type]: 'Type'
};

export enum PresetResourceType {
  Video = 1000,
  Movie = 1001,
  Anime = 1002,
  Ova = 1003,
  TvSeries = 1004,
  TvShow = 1005,
  Documentary = 1006,
  Clip = 1007,
  LiveStream = 1008,
  VideoSubscription = 1009,
  Av = 1010,
  AvClip = 1011,
  AvSubscription = 1012,
  Mmd = 1013,
  AdultMmd = 1014,
  Vr = 1015,
  VrAv = 1016,
  VrAnime = 1017,
  AiVideo = 1018,
  AsmrVideo = 1019,
  Image = 2000,
  Manga = 2001,
  Comic = 2002,
  Doushijin = 2003,
  Artbook = 2004,
  Illustration = 2005,
  ArtistCg = 2006,
  GameCg = 2007,
  ImageSubscription = 2008,
  IllustrationSubscription = 2009,
  MangaSubscription = 2010,
  Manga3D = 2011,
  Photograph = 2012,
  Cosplay = 2013,
  AiImage = 2014,
  Audio = 3000,
  AsmrAudio = 3001,
  Music = 3002,
  Podcast = 3003,
  Application = 4000,
  Game = 4001,
  Galgame = 4002,
  VrGame = 4003,
  Text = 5000,
  Novel = 5001,
  MotionManga = 10000,
  Mod = 10001,
  Tool = 10002
}

export const presetResourceTypes = [
  { label: 'Video', value: PresetResourceType.Video },
  { label: 'Movie', value: PresetResourceType.Movie },
  { label: 'Anime', value: PresetResourceType.Anime },
  { label: 'Ova', value: PresetResourceType.Ova },
  { label: 'TvSeries', value: PresetResourceType.TvSeries },
  { label: 'TvShow', value: PresetResourceType.TvShow },
  { label: 'Documentary', value: PresetResourceType.Documentary },
  { label: 'Clip', value: PresetResourceType.Clip },
  { label: 'LiveStream', value: PresetResourceType.LiveStream },
  { label: 'VideoSubscription', value: PresetResourceType.VideoSubscription },
  { label: 'Av', value: PresetResourceType.Av },
  { label: 'AvClip', value: PresetResourceType.AvClip },
  { label: 'AvSubscription', value: PresetResourceType.AvSubscription },
  { label: 'Mmd', value: PresetResourceType.Mmd },
  { label: 'AdultMmd', value: PresetResourceType.AdultMmd },
  { label: 'Vr', value: PresetResourceType.Vr },
  { label: 'VrAv', value: PresetResourceType.VrAv },
  { label: 'VrAnime', value: PresetResourceType.VrAnime },
  { label: 'AiVideo', value: PresetResourceType.AiVideo },
  { label: 'AsmrVideo', value: PresetResourceType.AsmrVideo },
  { label: 'Image', value: PresetResourceType.Image },
  { label: 'Manga', value: PresetResourceType.Manga },
  { label: 'Comic', value: PresetResourceType.Comic },
  { label: 'Doushijin', value: PresetResourceType.Doushijin },
  { label: 'Artbook', value: PresetResourceType.Artbook },
  { label: 'Illustration', value: PresetResourceType.Illustration },
  { label: 'ArtistCg', value: PresetResourceType.ArtistCg },
  { label: 'GameCg', value: PresetResourceType.GameCg },
  { label: 'ImageSubscription', value: PresetResourceType.ImageSubscription },
  { label: 'IllustrationSubscription', value: PresetResourceType.IllustrationSubscription },
  { label: 'MangaSubscription', value: PresetResourceType.MangaSubscription },
  { label: 'Manga3D', value: PresetResourceType.Manga3D },
  { label: 'Photograph', value: PresetResourceType.Photograph },
  { label: 'Cosplay', value: PresetResourceType.Cosplay },
  { label: 'AiImage', value: PresetResourceType.AiImage },
  { label: 'Audio', value: PresetResourceType.Audio },
  { label: 'AsmrAudio', value: PresetResourceType.AsmrAudio },
  { label: 'Music', value: PresetResourceType.Music },
  { label: 'Podcast', value: PresetResourceType.Podcast },
  { label: 'Application', value: PresetResourceType.Application },
  { label: 'Game', value: PresetResourceType.Game },
  { label: 'Galgame', value: PresetResourceType.Galgame },
  { label: 'VrGame', value: PresetResourceType.VrGame },
  { label: 'Text', value: PresetResourceType.Text },
  { label: 'Novel', value: PresetResourceType.Novel },
  { label: 'MotionManga', value: PresetResourceType.MotionManga },
  { label: 'Mod', value: PresetResourceType.Mod },
  { label: 'Tool', value: PresetResourceType.Tool }
] as const;

export const PresetResourceTypeLabel: Record<PresetResourceType, string> = {
  [PresetResourceType.Video]: 'Video',
  [PresetResourceType.Movie]: 'Movie',
  [PresetResourceType.Anime]: 'Anime',
  [PresetResourceType.Ova]: 'Ova',
  [PresetResourceType.TvSeries]: 'TvSeries',
  [PresetResourceType.TvShow]: 'TvShow',
  [PresetResourceType.Documentary]: 'Documentary',
  [PresetResourceType.Clip]: 'Clip',
  [PresetResourceType.LiveStream]: 'LiveStream',
  [PresetResourceType.VideoSubscription]: 'VideoSubscription',
  [PresetResourceType.Av]: 'Av',
  [PresetResourceType.AvClip]: 'AvClip',
  [PresetResourceType.AvSubscription]: 'AvSubscription',
  [PresetResourceType.Mmd]: 'Mmd',
  [PresetResourceType.AdultMmd]: 'AdultMmd',
  [PresetResourceType.Vr]: 'Vr',
  [PresetResourceType.VrAv]: 'VrAv',
  [PresetResourceType.VrAnime]: 'VrAnime',
  [PresetResourceType.AiVideo]: 'AiVideo',
  [PresetResourceType.AsmrVideo]: 'AsmrVideo',
  [PresetResourceType.Image]: 'Image',
  [PresetResourceType.Manga]: 'Manga',
  [PresetResourceType.Comic]: 'Comic',
  [PresetResourceType.Doushijin]: 'Doushijin',
  [PresetResourceType.Artbook]: 'Artbook',
  [PresetResourceType.Illustration]: 'Illustration',
  [PresetResourceType.ArtistCg]: 'ArtistCg',
  [PresetResourceType.GameCg]: 'GameCg',
  [PresetResourceType.ImageSubscription]: 'ImageSubscription',
  [PresetResourceType.IllustrationSubscription]: 'IllustrationSubscription',
  [PresetResourceType.MangaSubscription]: 'MangaSubscription',
  [PresetResourceType.Manga3D]: 'Manga3D',
  [PresetResourceType.Photograph]: 'Photograph',
  [PresetResourceType.Cosplay]: 'Cosplay',
  [PresetResourceType.AiImage]: 'AiImage',
  [PresetResourceType.Audio]: 'Audio',
  [PresetResourceType.AsmrAudio]: 'AsmrAudio',
  [PresetResourceType.Music]: 'Music',
  [PresetResourceType.Podcast]: 'Podcast',
  [PresetResourceType.Application]: 'Application',
  [PresetResourceType.Game]: 'Game',
  [PresetResourceType.Galgame]: 'Galgame',
  [PresetResourceType.VrGame]: 'VrGame',
  [PresetResourceType.Text]: 'Text',
  [PresetResourceType.Novel]: 'Novel',
  [PresetResourceType.MotionManga]: 'MotionManga',
  [PresetResourceType.Mod]: 'Mod',
  [PresetResourceType.Tool]: 'Tool'
};

export enum LegacyResourceProperty {
  ReleaseDt = 4,
  Publisher = 5,
  Name = 6,
  Language = 7,
  Volume = 8,
  Original = 9,
  Series = 10,
  Tag = 11,
  CustomProperty = 14,
  Favorites = 22,
  Cover = 23
}

export const legacyResourceProperties = [
  { label: 'ReleaseDt', value: LegacyResourceProperty.ReleaseDt },
  { label: 'Publisher', value: LegacyResourceProperty.Publisher },
  { label: 'Name', value: LegacyResourceProperty.Name },
  { label: 'Language', value: LegacyResourceProperty.Language },
  { label: 'Volume', value: LegacyResourceProperty.Volume },
  { label: 'Original', value: LegacyResourceProperty.Original },
  { label: 'Series', value: LegacyResourceProperty.Series },
  { label: 'Tag', value: LegacyResourceProperty.Tag },
  { label: 'CustomProperty', value: LegacyResourceProperty.CustomProperty },
  { label: 'Favorites', value: LegacyResourceProperty.Favorites },
  { label: 'Cover', value: LegacyResourceProperty.Cover }
] as const;

export const LegacyResourcePropertyLabel: Record<LegacyResourceProperty, string> = {
  [LegacyResourceProperty.ReleaseDt]: 'ReleaseDt',
  [LegacyResourceProperty.Publisher]: 'Publisher',
  [LegacyResourceProperty.Name]: 'Name',
  [LegacyResourceProperty.Language]: 'Language',
  [LegacyResourceProperty.Volume]: 'Volume',
  [LegacyResourceProperty.Original]: 'Original',
  [LegacyResourceProperty.Series]: 'Series',
  [LegacyResourceProperty.Tag]: 'Tag',
  [LegacyResourceProperty.CustomProperty]: 'CustomProperty',
  [LegacyResourceProperty.Favorites]: 'Favorites',
  [LegacyResourceProperty.Cover]: 'Cover'
};

export enum LogLevel {
  Trace = 0,
  Debug = 1,
  Information = 2,
  Warning = 3,
  Error = 4,
  Critical = 5,
  None = 6
}

export const logLevels = [
  { label: 'Trace', value: LogLevel.Trace },
  { label: 'Debug', value: LogLevel.Debug },
  { label: 'Information', value: LogLevel.Information },
  { label: 'Warning', value: LogLevel.Warning },
  { label: 'Error', value: LogLevel.Error },
  { label: 'Critical', value: LogLevel.Critical },
  { label: 'None', value: LogLevel.None }
] as const;

export const LogLevelLabel: Record<LogLevel, string> = {
  [LogLevel.Trace]: 'Trace',
  [LogLevel.Debug]: 'Debug',
  [LogLevel.Information]: 'Information',
  [LogLevel.Warning]: 'Warning',
  [LogLevel.Error]: 'Error',
  [LogLevel.Critical]: 'Critical',
  [LogLevel.None]: 'None'
};