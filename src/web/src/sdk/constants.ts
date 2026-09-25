// AUTO-GENERATED FROM server enums. Do not edit manually.
/* eslint-disable */

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
  Filename = 15,
  Name = 27
}

export const builtinPropertyForDisplayNames = [
  { label: 'Filename', value: BuiltinPropertyForDisplayName.Filename },
  { label: 'Name', value: BuiltinPropertyForDisplayName.Name }
] as const;

export const BuiltinPropertyForDisplayNameLabel: Record<BuiltinPropertyForDisplayName, string> = {
  [BuiltinPropertyForDisplayName.Filename]: 'Filename',
  [BuiltinPropertyForDisplayName.Name]: 'Name'
};

export enum IndexOperationType {
  Update = 0,
  Remove = 1
}

export const indexOperationTypes = [
  { label: 'Update', value: IndexOperationType.Update },
  { label: 'Remove', value: IndexOperationType.Remove }
] as const;

export const IndexOperationTypeLabel: Record<IndexOperationType, string> = {
  [IndexOperationType.Update]: 'Update',
  [IndexOperationType.Remove]: 'Remove'
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

export enum PostParserTaskStatus {
  None = 0,
  Pending = 1,
  Complete = 2,
  Failed = 3,
  Deleted = 4
}

export const postParserTaskStatuses = [
  { label: 'None', value: PostParserTaskStatus.None },
  { label: 'Pending', value: PostParserTaskStatus.Pending },
  { label: 'Complete', value: PostParserTaskStatus.Complete },
  { label: 'Failed', value: PostParserTaskStatus.Failed },
  { label: 'Deleted', value: PostParserTaskStatus.Deleted }
] as const;

export const PostParserTaskStatusLabel: Record<PostParserTaskStatus, string> = {
  [PostParserTaskStatus.None]: 'None',
  [PostParserTaskStatus.Pending]: 'Pending',
  [PostParserTaskStatus.Complete]: 'Complete',
  [PostParserTaskStatus.Failed]: 'Failed',
  [PostParserTaskStatus.Deleted]: 'Deleted'
};

export enum PostParseTarget {
  DownloadInfo = 1
}

export const postParseTargets = [
  { label: 'DownloadInfo', value: PostParseTarget.DownloadInfo }
] as const;

export const PostParseTargetLabel: Record<PostParseTarget, string> = {
  [PostParseTarget.DownloadInfo]: 'DownloadInfo'
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
  List = 3
}

export const exHentaiDownloadTaskTypes = [
  { label: 'SingleWork', value: ExHentaiDownloadTaskType.SingleWork },
  { label: 'Watched', value: ExHentaiDownloadTaskType.Watched },
  { label: 'List', value: ExHentaiDownloadTaskType.List }
] as const;

export const ExHentaiDownloadTaskTypeLabel: Record<ExHentaiDownloadTaskType, string> = {
  [ExHentaiDownloadTaskType.SingleWork]: 'SingleWork',
  [ExHentaiDownloadTaskType.Watched]: 'Watched',
  [ExHentaiDownloadTaskType.List]: 'List'
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

export enum DownloadResultKind {
  TorrentMetadata = 1,
  LocalFiles = 2
}

export const downloadResultKinds = [
  { label: 'TorrentMetadata', value: DownloadResultKind.TorrentMetadata },
  { label: 'LocalFiles', value: DownloadResultKind.LocalFiles }
] as const;

export const DownloadResultKindLabel: Record<DownloadResultKind, string> = {
  [DownloadResultKind.TorrentMetadata]: 'TorrentMetadata',
  [DownloadResultKind.LocalFiles]: 'LocalFiles'
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
  AppendToTheQueue = 2,
  Defer = 3
}

export const downloaderStopBies = [
  { label: 'ManuallyStop', value: DownloaderStopBy.ManuallyStop },
  { label: 'AppendToTheQueue', value: DownloaderStopBy.AppendToTheQueue },
  { label: 'Defer', value: DownloaderStopBy.Defer }
] as const;

export const DownloaderStopByLabel: Record<DownloaderStopBy, string> = {
  [DownloaderStopBy.ManuallyStop]: 'ManuallyStop',
  [DownloaderStopBy.AppendToTheQueue]: 'AppendToTheQueue',
  [DownloaderStopBy.Defer]: 'Defer'
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

export enum DownloadTaskPrecheckOutcome {
  Run = 0,
  Defer = 1,
  AlreadySatisfied = 2
}

export const downloadTaskPrecheckOutcomes = [
  { label: 'Run', value: DownloadTaskPrecheckOutcome.Run },
  { label: 'Defer', value: DownloadTaskPrecheckOutcome.Defer },
  { label: 'AlreadySatisfied', value: DownloadTaskPrecheckOutcome.AlreadySatisfied }
] as const;

export const DownloadTaskPrecheckOutcomeLabel: Record<DownloadTaskPrecheckOutcome, string> = {
  [DownloadTaskPrecheckOutcome.Run]: 'Run',
  [DownloadTaskPrecheckOutcome.Defer]: 'Defer',
  [DownloadTaskPrecheckOutcome.AlreadySatisfied]: 'AlreadySatisfied'
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
  Failed = 5,
  Unavailable = 6
}

export const updaterStatuses = [
  { label: 'Idle', value: UpdaterStatus.Idle },
  { label: 'Running', value: UpdaterStatus.Running },
  { label: 'PendingRestart', value: UpdaterStatus.PendingRestart },
  { label: 'UpToDate', value: UpdaterStatus.UpToDate },
  { label: 'Failed', value: UpdaterStatus.Failed },
  { label: 'Unavailable', value: UpdaterStatus.Unavailable }
] as const;

export const UpdaterStatusLabel: Record<UpdaterStatus, string> = {
  [UpdaterStatus.Idle]: 'Idle',
  [UpdaterStatus.Running]: 'Running',
  [UpdaterStatus.PendingRestart]: 'PendingRestart',
  [UpdaterStatus.UpToDate]: 'UpToDate',
  [UpdaterStatus.Failed]: 'Failed',
  [UpdaterStatus.Unavailable]: 'Unavailable'
};

export enum DataDirectoryLockStatus {
  Acquired = 1,
  HeldByAnotherProcess = 2,
  Unavailable = 3
}

export const dataDirectoryLockStatuses = [
  { label: 'Acquired', value: DataDirectoryLockStatus.Acquired },
  { label: 'HeldByAnotherProcess', value: DataDirectoryLockStatus.HeldByAnotherProcess },
  { label: 'Unavailable', value: DataDirectoryLockStatus.Unavailable }
] as const;

export const DataDirectoryLockStatusLabel: Record<DataDirectoryLockStatus, string> = {
  [DataDirectoryLockStatus.Acquired]: 'Acquired',
  [DataDirectoryLockStatus.HeldByAnotherProcess]: 'HeldByAnotherProcess',
  [DataDirectoryLockStatus.Unavailable]: 'Unavailable'
};

export enum SingleInstanceEntry {
  Entered = 1,
  Refused = 2,
  Unguarded = 3,
  NotApplicable = 4
}

export const singleInstanceEntries = [
  { label: 'Entered', value: SingleInstanceEntry.Entered },
  { label: 'Refused', value: SingleInstanceEntry.Refused },
  { label: 'Unguarded', value: SingleInstanceEntry.Unguarded },
  { label: 'NotApplicable', value: SingleInstanceEntry.NotApplicable }
] as const;

export const SingleInstanceEntryLabel: Record<SingleInstanceEntry, string> = {
  [SingleInstanceEntry.Entered]: 'Entered',
  [SingleInstanceEntry.Refused]: 'Refused',
  [SingleInstanceEntry.Unguarded]: 'Unguarded',
  [SingleInstanceEntry.NotApplicable]: 'NotApplicable'
};

export enum RelocationMode {
  UseTarget = 1,
  MergeOverwrite = 3
}

export const relocationModes = [
  { label: 'UseTarget', value: RelocationMode.UseTarget },
  { label: 'MergeOverwrite', value: RelocationMode.MergeOverwrite }
] as const;

export const RelocationModeLabel: Record<RelocationMode, string> = {
  [RelocationMode.UseTarget]: 'UseTarget',
  [RelocationMode.MergeOverwrite]: 'MergeOverwrite'
};

export enum RelocationOutcomeKind {
  NoOp = 0,
  Success = 1,
  Error = 2,
  UnknownSchemaVersion = 3
}

export const relocationOutcomeKinds = [
  { label: 'NoOp', value: RelocationOutcomeKind.NoOp },
  { label: 'Success', value: RelocationOutcomeKind.Success },
  { label: 'Error', value: RelocationOutcomeKind.Error },
  { label: 'UnknownSchemaVersion', value: RelocationOutcomeKind.UnknownSchemaVersion }
] as const;

export const RelocationOutcomeKindLabel: Record<RelocationOutcomeKind, string> = {
  [RelocationOutcomeKind.NoOp]: 'NoOp',
  [RelocationOutcomeKind.Success]: 'Success',
  [RelocationOutcomeKind.Error]: 'Error',
  [RelocationOutcomeKind.UnknownSchemaVersion]: 'UnknownSchemaVersion'
};

export enum RelocationPhase {
  Starting = 0,
  Copying = 1,
  Validating = 2,
  Replacing = 3,
  Finalizing = 4,
  Done = 5
}

export const relocationPhases = [
  { label: 'Starting', value: RelocationPhase.Starting },
  { label: 'Copying', value: RelocationPhase.Copying },
  { label: 'Validating', value: RelocationPhase.Validating },
  { label: 'Replacing', value: RelocationPhase.Replacing },
  { label: 'Finalizing', value: RelocationPhase.Finalizing },
  { label: 'Done', value: RelocationPhase.Done }
] as const;

export const RelocationPhaseLabel: Record<RelocationPhase, string> = {
  [RelocationPhase.Starting]: 'Starting',
  [RelocationPhase.Copying]: 'Copying',
  [RelocationPhase.Validating]: 'Validating',
  [RelocationPhase.Replacing]: 'Replacing',
  [RelocationPhase.Finalizing]: 'Finalizing',
  [RelocationPhase.Done]: 'Done'
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

export enum DataPathSource {
  Default = 0,
  UserConfigured = 1,
  Environment = 2
}

export const dataPathSources = [
  { label: 'Default', value: DataPathSource.Default },
  { label: 'UserConfigured', value: DataPathSource.UserConfigured },
  { label: 'Environment', value: DataPathSource.Environment }
] as const;

export const DataPathSourceLabel: Record<DataPathSource, string> = {
  [DataPathSource.Default]: 'Default',
  [DataPathSource.UserConfigured]: 'UserConfigured',
  [DataPathSource.Environment]: 'Environment'
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

export enum SteamMetadataField {
  Name = 1,
  Type = 2,
  ShortDescription = 3,
  DetailedDescription = 4,
  HeaderImage = 5,
  CapsuleImage = 6,
  Developers = 7,
  Publishers = 8,
  Genres = 9,
  Categories = 10,
  MetacriticScore = 11,
  ReleaseDate = 12
}

export const steamMetadataFields = [
  { label: 'Name', value: SteamMetadataField.Name },
  { label: 'Type', value: SteamMetadataField.Type },
  { label: 'ShortDescription', value: SteamMetadataField.ShortDescription },
  { label: 'DetailedDescription', value: SteamMetadataField.DetailedDescription },
  { label: 'HeaderImage', value: SteamMetadataField.HeaderImage },
  { label: 'CapsuleImage', value: SteamMetadataField.CapsuleImage },
  { label: 'Developers', value: SteamMetadataField.Developers },
  { label: 'Publishers', value: SteamMetadataField.Publishers },
  { label: 'Genres', value: SteamMetadataField.Genres },
  { label: 'Categories', value: SteamMetadataField.Categories },
  { label: 'MetacriticScore', value: SteamMetadataField.MetacriticScore },
  { label: 'ReleaseDate', value: SteamMetadataField.ReleaseDate }
] as const;

export const SteamMetadataFieldLabel: Record<SteamMetadataField, string> = {
  [SteamMetadataField.Name]: 'Name',
  [SteamMetadataField.Type]: 'Type',
  [SteamMetadataField.ShortDescription]: 'ShortDescription',
  [SteamMetadataField.DetailedDescription]: 'DetailedDescription',
  [SteamMetadataField.HeaderImage]: 'HeaderImage',
  [SteamMetadataField.CapsuleImage]: 'CapsuleImage',
  [SteamMetadataField.Developers]: 'Developers',
  [SteamMetadataField.Publishers]: 'Publishers',
  [SteamMetadataField.Genres]: 'Genres',
  [SteamMetadataField.Categories]: 'Categories',
  [SteamMetadataField.MetacriticScore]: 'MetacriticScore',
  [SteamMetadataField.ReleaseDate]: 'ReleaseDate'
};

export enum ExHentaiMetadataField {
  Name = 1,
  RawName = 2,
  Introduction = 3,
  Rate = 4,
  Category = 5,
  CoverUrl = 6,
  FileCount = 7,
  PageCount = 8
}

export const exHentaiMetadataFields = [
  { label: 'Name', value: ExHentaiMetadataField.Name },
  { label: 'RawName', value: ExHentaiMetadataField.RawName },
  { label: 'Introduction', value: ExHentaiMetadataField.Introduction },
  { label: 'Rate', value: ExHentaiMetadataField.Rate },
  { label: 'Category', value: ExHentaiMetadataField.Category },
  { label: 'CoverUrl', value: ExHentaiMetadataField.CoverUrl },
  { label: 'FileCount', value: ExHentaiMetadataField.FileCount },
  { label: 'PageCount', value: ExHentaiMetadataField.PageCount }
] as const;

export const ExHentaiMetadataFieldLabel: Record<ExHentaiMetadataField, string> = {
  [ExHentaiMetadataField.Name]: 'Name',
  [ExHentaiMetadataField.RawName]: 'RawName',
  [ExHentaiMetadataField.Introduction]: 'Introduction',
  [ExHentaiMetadataField.Rate]: 'Rate',
  [ExHentaiMetadataField.Category]: 'Category',
  [ExHentaiMetadataField.CoverUrl]: 'CoverUrl',
  [ExHentaiMetadataField.FileCount]: 'FileCount',
  [ExHentaiMetadataField.PageCount]: 'PageCount'
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

export enum DLsiteMetadataField {
  Name = 1,
  Introduction = 2,
  Rating = 3,
  CoverUrls = 4
}

export const dLsiteMetadataFields = [
  { label: 'Name', value: DLsiteMetadataField.Name },
  { label: 'Introduction', value: DLsiteMetadataField.Introduction },
  { label: 'Rating', value: DLsiteMetadataField.Rating },
  { label: 'CoverUrls', value: DLsiteMetadataField.CoverUrls }
] as const;

export const DLsiteMetadataFieldLabel: Record<DLsiteMetadataField, string> = {
  [DLsiteMetadataField.Name]: 'Name',
  [DLsiteMetadataField.Introduction]: 'Introduction',
  [DLsiteMetadataField.Rating]: 'Rating',
  [DLsiteMetadataField.CoverUrls]: 'CoverUrls'
};

export enum BilibiliApiCodeClass {
  Ok = 0,
  RiskControl = 1,
  ServiceBusy = 2,
  NotLoggedIn = 3,
  ContentState = 4,
  Unknown = 5
}

export const bilibiliApiCodeClasses = [
  { label: 'Ok', value: BilibiliApiCodeClass.Ok },
  { label: 'RiskControl', value: BilibiliApiCodeClass.RiskControl },
  { label: 'ServiceBusy', value: BilibiliApiCodeClass.ServiceBusy },
  { label: 'NotLoggedIn', value: BilibiliApiCodeClass.NotLoggedIn },
  { label: 'ContentState', value: BilibiliApiCodeClass.ContentState },
  { label: 'Unknown', value: BilibiliApiCodeClass.Unknown }
] as const;

export const BilibiliApiCodeClassLabel: Record<BilibiliApiCodeClass, string> = {
  [BilibiliApiCodeClass.Ok]: 'Ok',
  [BilibiliApiCodeClass.RiskControl]: 'RiskControl',
  [BilibiliApiCodeClass.ServiceBusy]: 'ServiceBusy',
  [BilibiliApiCodeClass.NotLoggedIn]: 'NotLoggedIn',
  [BilibiliApiCodeClass.ContentState]: 'ContentState',
  [BilibiliApiCodeClass.Unknown]: 'Unknown'
};

export enum BilibiliViewOutcomeKind {
  Proceed = 1,
  FollowForward = 2,
  CheckExistence = 3,
  PageListFallback = 4,
  Skip = 5
}

export const bilibiliViewOutcomeKinds = [
  { label: 'Proceed', value: BilibiliViewOutcomeKind.Proceed },
  { label: 'FollowForward', value: BilibiliViewOutcomeKind.FollowForward },
  { label: 'CheckExistence', value: BilibiliViewOutcomeKind.CheckExistence },
  { label: 'PageListFallback', value: BilibiliViewOutcomeKind.PageListFallback },
  { label: 'Skip', value: BilibiliViewOutcomeKind.Skip }
] as const;

export const BilibiliViewOutcomeKindLabel: Record<BilibiliViewOutcomeKind, string> = {
  [BilibiliViewOutcomeKind.Proceed]: 'Proceed',
  [BilibiliViewOutcomeKind.FollowForward]: 'FollowForward',
  [BilibiliViewOutcomeKind.CheckExistence]: 'CheckExistence',
  [BilibiliViewOutcomeKind.PageListFallback]: 'PageListFallback',
  [BilibiliViewOutcomeKind.Skip]: 'Skip'
};

export enum BilibiliTemporaryFailureKind {
  RiskControl = 1,
  ServiceBusy = 2,
  CdnUnavailable = 3
}

export const bilibiliTemporaryFailureKinds = [
  { label: 'RiskControl', value: BilibiliTemporaryFailureKind.RiskControl },
  { label: 'ServiceBusy', value: BilibiliTemporaryFailureKind.ServiceBusy },
  { label: 'CdnUnavailable', value: BilibiliTemporaryFailureKind.CdnUnavailable }
] as const;

export const BilibiliTemporaryFailureKindLabel: Record<BilibiliTemporaryFailureKind, string> = {
  [BilibiliTemporaryFailureKind.RiskControl]: 'RiskControl',
  [BilibiliTemporaryFailureKind.ServiceBusy]: 'ServiceBusy',
  [BilibiliTemporaryFailureKind.CdnUnavailable]: 'CdnUnavailable'
};

export enum BilibiliFavoriteItemKind {
  Video = 1,
  OgvEpisode = 2,
  Audio = 3,
  UgcSeason = 4,
  Unknown = 5
}

export const bilibiliFavoriteItemKinds = [
  { label: 'Video', value: BilibiliFavoriteItemKind.Video },
  { label: 'OgvEpisode', value: BilibiliFavoriteItemKind.OgvEpisode },
  { label: 'Audio', value: BilibiliFavoriteItemKind.Audio },
  { label: 'UgcSeason', value: BilibiliFavoriteItemKind.UgcSeason },
  { label: 'Unknown', value: BilibiliFavoriteItemKind.Unknown }
] as const;

export const BilibiliFavoriteItemKindLabel: Record<BilibiliFavoriteItemKind, string> = {
  [BilibiliFavoriteItemKind.Video]: 'Video',
  [BilibiliFavoriteItemKind.OgvEpisode]: 'OgvEpisode',
  [BilibiliFavoriteItemKind.Audio]: 'Audio',
  [BilibiliFavoriteItemKind.UgcSeason]: 'UgcSeason',
  [BilibiliFavoriteItemKind.Unknown]: 'Unknown'
};

export enum BilibiliPlayUrlOutcomeKind {
  Dash = 1,
  Durl = 2,
  Skip = 3,
  NoStreams = 4
}

export const bilibiliPlayUrlOutcomeKinds = [
  { label: 'Dash', value: BilibiliPlayUrlOutcomeKind.Dash },
  { label: 'Durl', value: BilibiliPlayUrlOutcomeKind.Durl },
  { label: 'Skip', value: BilibiliPlayUrlOutcomeKind.Skip },
  { label: 'NoStreams', value: BilibiliPlayUrlOutcomeKind.NoStreams }
] as const;

export const BilibiliPlayUrlOutcomeKindLabel: Record<BilibiliPlayUrlOutcomeKind, string> = {
  [BilibiliPlayUrlOutcomeKind.Dash]: 'Dash',
  [BilibiliPlayUrlOutcomeKind.Durl]: 'Durl',
  [BilibiliPlayUrlOutcomeKind.Skip]: 'Skip',
  [BilibiliPlayUrlOutcomeKind.NoStreams]: 'NoStreams'
};

export enum BilibiliSkipReason {
  InvalidItem = 1,
  UnsupportedOgvEpisode = 2,
  UnsupportedAudio = 3,
  UnsupportedCollection = 4,
  UnsupportedItemType = 5,
  InteractiveVideo = 6,
  Deleted = 7,
  PrivateToUploader = 8,
  UnderReview = 9,
  RegionRestrictedOrHidden = 10,
  AccessDenied = 11,
  SupporterOnly = 12,
  SupporterOnlyPreview = 13,
  PgcMemberOrPaid = 14,
  PreviewOnly = 15,
  Unavailable = 16,
  PgcEpisodeNotSupported = 17,
  MergeFailed = 18,
  CdnUnavailable = 19
}

export const bilibiliSkipReasons = [
  { label: 'InvalidItem', value: BilibiliSkipReason.InvalidItem },
  { label: 'UnsupportedOgvEpisode', value: BilibiliSkipReason.UnsupportedOgvEpisode },
  { label: 'UnsupportedAudio', value: BilibiliSkipReason.UnsupportedAudio },
  { label: 'UnsupportedCollection', value: BilibiliSkipReason.UnsupportedCollection },
  { label: 'UnsupportedItemType', value: BilibiliSkipReason.UnsupportedItemType },
  { label: 'InteractiveVideo', value: BilibiliSkipReason.InteractiveVideo },
  { label: 'Deleted', value: BilibiliSkipReason.Deleted },
  { label: 'PrivateToUploader', value: BilibiliSkipReason.PrivateToUploader },
  { label: 'UnderReview', value: BilibiliSkipReason.UnderReview },
  { label: 'RegionRestrictedOrHidden', value: BilibiliSkipReason.RegionRestrictedOrHidden },
  { label: 'AccessDenied', value: BilibiliSkipReason.AccessDenied },
  { label: 'SupporterOnly', value: BilibiliSkipReason.SupporterOnly },
  { label: 'SupporterOnlyPreview', value: BilibiliSkipReason.SupporterOnlyPreview },
  { label: 'PgcMemberOrPaid', value: BilibiliSkipReason.PgcMemberOrPaid },
  { label: 'PreviewOnly', value: BilibiliSkipReason.PreviewOnly },
  { label: 'Unavailable', value: BilibiliSkipReason.Unavailable },
  { label: 'PgcEpisodeNotSupported', value: BilibiliSkipReason.PgcEpisodeNotSupported },
  { label: 'MergeFailed', value: BilibiliSkipReason.MergeFailed },
  { label: 'CdnUnavailable', value: BilibiliSkipReason.CdnUnavailable }
] as const;

export const BilibiliSkipReasonLabel: Record<BilibiliSkipReason, string> = {
  [BilibiliSkipReason.InvalidItem]: 'InvalidItem',
  [BilibiliSkipReason.UnsupportedOgvEpisode]: 'UnsupportedOgvEpisode',
  [BilibiliSkipReason.UnsupportedAudio]: 'UnsupportedAudio',
  [BilibiliSkipReason.UnsupportedCollection]: 'UnsupportedCollection',
  [BilibiliSkipReason.UnsupportedItemType]: 'UnsupportedItemType',
  [BilibiliSkipReason.InteractiveVideo]: 'InteractiveVideo',
  [BilibiliSkipReason.Deleted]: 'Deleted',
  [BilibiliSkipReason.PrivateToUploader]: 'PrivateToUploader',
  [BilibiliSkipReason.UnderReview]: 'UnderReview',
  [BilibiliSkipReason.RegionRestrictedOrHidden]: 'RegionRestrictedOrHidden',
  [BilibiliSkipReason.AccessDenied]: 'AccessDenied',
  [BilibiliSkipReason.SupporterOnly]: 'SupporterOnly',
  [BilibiliSkipReason.SupporterOnlyPreview]: 'SupporterOnlyPreview',
  [BilibiliSkipReason.PgcMemberOrPaid]: 'PgcMemberOrPaid',
  [BilibiliSkipReason.PreviewOnly]: 'PreviewOnly',
  [BilibiliSkipReason.Unavailable]: 'Unavailable',
  [BilibiliSkipReason.PgcEpisodeNotSupported]: 'PgcEpisodeNotSupported',
  [BilibiliSkipReason.MergeFailed]: 'MergeFailed',
  [BilibiliSkipReason.CdnUnavailable]: 'CdnUnavailable'
};

export enum BilibiliAudioKind {
  None = 0,
  Aac = 1,
  Flac = 2,
  DolbyEac3 = 3
}

export const bilibiliAudioKinds = [
  { label: 'None', value: BilibiliAudioKind.None },
  { label: 'Aac', value: BilibiliAudioKind.Aac },
  { label: 'Flac', value: BilibiliAudioKind.Flac },
  { label: 'DolbyEac3', value: BilibiliAudioKind.DolbyEac3 }
] as const;

export const BilibiliAudioKindLabel: Record<BilibiliAudioKind, string> = {
  [BilibiliAudioKind.None]: 'None',
  [BilibiliAudioKind.Aac]: 'Aac',
  [BilibiliAudioKind.Flac]: 'Flac',
  [BilibiliAudioKind.DolbyEac3]: 'DolbyEac3'
};

export enum BilibiliPageStatus {
  Downloaded = 1,
  AlreadyExists = 2,
  Skipped = 3
}

export const bilibiliPageStatuses = [
  { label: 'Downloaded', value: BilibiliPageStatus.Downloaded },
  { label: 'AlreadyExists', value: BilibiliPageStatus.AlreadyExists },
  { label: 'Skipped', value: BilibiliPageStatus.Skipped }
] as const;

export const BilibiliPageStatusLabel: Record<BilibiliPageStatus, string> = {
  [BilibiliPageStatus.Downloaded]: 'Downloaded',
  [BilibiliPageStatus.AlreadyExists]: 'AlreadyExists',
  [BilibiliPageStatus.Skipped]: 'Skipped'
};

export enum BangumiSubjectType {
  All = 0,
  Anime = 1,
  Book = 2,
  Music = 3,
  Game = 4,
  Real = 5
}

export const bangumiSubjectTypes = [
  { label: 'All', value: BangumiSubjectType.All },
  { label: 'Anime', value: BangumiSubjectType.Anime },
  { label: 'Book', value: BangumiSubjectType.Book },
  { label: 'Music', value: BangumiSubjectType.Music },
  { label: 'Game', value: BangumiSubjectType.Game },
  { label: 'Real', value: BangumiSubjectType.Real }
] as const;

export const BangumiSubjectTypeLabel: Record<BangumiSubjectType, string> = {
  [BangumiSubjectType.All]: 'All',
  [BangumiSubjectType.Anime]: 'Anime',
  [BangumiSubjectType.Book]: 'Book',
  [BangumiSubjectType.Music]: 'Music',
  [BangumiSubjectType.Game]: 'Game',
  [BangumiSubjectType.Real]: 'Real'
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

export enum BatchPlaySkipReason {
  NoPlayableFiles = 1,
  AllFilesMissing = 2,
  ResourceNotFound = 3,
  NoFilesMatchingPlayer = 4
}

export const batchPlaySkipReasons = [
  { label: 'NoPlayableFiles', value: BatchPlaySkipReason.NoPlayableFiles },
  { label: 'AllFilesMissing', value: BatchPlaySkipReason.AllFilesMissing },
  { label: 'ResourceNotFound', value: BatchPlaySkipReason.ResourceNotFound },
  { label: 'NoFilesMatchingPlayer', value: BatchPlaySkipReason.NoFilesMatchingPlayer }
] as const;

export const BatchPlaySkipReasonLabel: Record<BatchPlaySkipReason, string> = {
  [BatchPlaySkipReason.NoPlayableFiles]: 'NoPlayableFiles',
  [BatchPlaySkipReason.AllFilesMissing]: 'AllFilesMissing',
  [BatchPlaySkipReason.ResourceNotFound]: 'ResourceNotFound',
  [BatchPlaySkipReason.NoFilesMatchingPlayer]: 'NoFilesMatchingPlayer'
};

export enum BatchPlayCandidateType {
  ProfilePlayer = 1,
  KnownPlayer = 2
}

export const batchPlayCandidateTypes = [
  { label: 'ProfilePlayer', value: BatchPlayCandidateType.ProfilePlayer },
  { label: 'KnownPlayer', value: BatchPlayCandidateType.KnownPlayer }
] as const;

export const BatchPlayCandidateTypeLabel: Record<BatchPlayCandidateType, string> = {
  [BatchPlayCandidateType.ProfilePlayer]: 'ProfilePlayer',
  [BatchPlayCandidateType.KnownPlayer]: 'KnownPlayer'
};

export enum BatchPlayCapability {
  None = 0,
  PlaylistFile = 1,
  MultiFileArguments = 2
}

export const batchPlayCapabilities = [
  { label: 'None', value: BatchPlayCapability.None },
  { label: 'PlaylistFile', value: BatchPlayCapability.PlaylistFile },
  { label: 'MultiFileArguments', value: BatchPlayCapability.MultiFileArguments }
] as const;

export const BatchPlayCapabilityLabel: Record<BatchPlayCapability, string> = {
  [BatchPlayCapability.None]: 'None',
  [BatchPlayCapability.PlaylistFile]: 'PlaylistFile',
  [BatchPlayCapability.MultiFileArguments]: 'MultiFileArguments'
};

export enum BatchPlayFileSelectionMode {
  FirstFilePerResource = 1,
  AllFiles = 2
}

export const batchPlayFileSelectionModes = [
  { label: 'FirstFilePerResource', value: BatchPlayFileSelectionMode.FirstFilePerResource },
  { label: 'AllFiles', value: BatchPlayFileSelectionMode.AllFiles }
] as const;

export const BatchPlayFileSelectionModeLabel: Record<BatchPlayFileSelectionMode, string> = {
  [BatchPlayFileSelectionMode.FirstFilePerResource]: 'FirstFilePerResource',
  [BatchPlayFileSelectionMode.AllFiles]: 'AllFiles'
};

export enum BatchPlayLaunchMethod {
  PlaylistFile = 1,
  MultiFileArguments = 2
}

export const batchPlayLaunchMethods = [
  { label: 'PlaylistFile', value: BatchPlayLaunchMethod.PlaylistFile },
  { label: 'MultiFileArguments', value: BatchPlayLaunchMethod.MultiFileArguments }
] as const;

export const BatchPlayLaunchMethodLabel: Record<BatchPlayLaunchMethod, string> = {
  [BatchPlayLaunchMethod.PlaylistFile]: 'PlaylistFile',
  [BatchPlayLaunchMethod.MultiFileArguments]: 'MultiFileArguments'
};

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

export enum ResolverConfigFieldType {
  String = 1,
  Password = 2,
  StringList = 3,
  DirectoryList = 4
}

export const resolverConfigFieldTypes = [
  { label: 'String', value: ResolverConfigFieldType.String },
  { label: 'Password', value: ResolverConfigFieldType.Password },
  { label: 'StringList', value: ResolverConfigFieldType.StringList },
  { label: 'DirectoryList', value: ResolverConfigFieldType.DirectoryList }
] as const;

export const ResolverConfigFieldTypeLabel: Record<ResolverConfigFieldType, string> = {
  [ResolverConfigFieldType.String]: 'String',
  [ResolverConfigFieldType.Password]: 'Password',
  [ResolverConfigFieldType.StringList]: 'StringList',
  [ResolverConfigFieldType.DirectoryList]: 'DirectoryList'
};

export enum AppNotificationBehavior {
  AutoDismiss = 0,
  Persistent = 1
}

export const appNotificationBehaviors = [
  { label: 'AutoDismiss', value: AppNotificationBehavior.AutoDismiss },
  { label: 'Persistent', value: AppNotificationBehavior.Persistent }
] as const;

export const AppNotificationBehaviorLabel: Record<AppNotificationBehavior, string> = {
  [AppNotificationBehavior.AutoDismiss]: 'AutoDismiss',
  [AppNotificationBehavior.Persistent]: 'Persistent'
};

export enum AppNotificationSeverity {
  Info = 0,
  Success = 1,
  Warning = 2,
  Error = 3
}

export const appNotificationSeverities = [
  { label: 'Info', value: AppNotificationSeverity.Info },
  { label: 'Success', value: AppNotificationSeverity.Success },
  { label: 'Warning', value: AppNotificationSeverity.Warning },
  { label: 'Error', value: AppNotificationSeverity.Error }
] as const;

export const AppNotificationSeverityLabel: Record<AppNotificationSeverity, string> = {
  [AppNotificationSeverity.Info]: 'Info',
  [AppNotificationSeverity.Success]: 'Success',
  [AppNotificationSeverity.Warning]: 'Warning',
  [AppNotificationSeverity.Error]: 'Error'
};

export enum BTaskDependencyFailurePolicy {
  Wait = 1,
  Skip = 2,
  Fail = 3
}

export const bTaskDependencyFailurePolicies = [
  { label: 'Wait', value: BTaskDependencyFailurePolicy.Wait },
  { label: 'Skip', value: BTaskDependencyFailurePolicy.Skip },
  { label: 'Fail', value: BTaskDependencyFailurePolicy.Fail }
] as const;

export const BTaskDependencyFailurePolicyLabel: Record<BTaskDependencyFailurePolicy, string> = {
  [BTaskDependencyFailurePolicy.Wait]: 'Wait',
  [BTaskDependencyFailurePolicy.Skip]: 'Skip',
  [BTaskDependencyFailurePolicy.Fail]: 'Fail'
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
  Cancelled = 6,
  Cancelling = 7,
  Pausing = 8,
  Resuming = 9
}

export const bTaskStatuses = [
  { label: 'NotStarted', value: BTaskStatus.NotStarted },
  { label: 'Running', value: BTaskStatus.Running },
  { label: 'Paused', value: BTaskStatus.Paused },
  { label: 'Error', value: BTaskStatus.Error },
  { label: 'Completed', value: BTaskStatus.Completed },
  { label: 'Cancelled', value: BTaskStatus.Cancelled },
  { label: 'Cancelling', value: BTaskStatus.Cancelling },
  { label: 'Pausing', value: BTaskStatus.Pausing },
  { label: 'Resuming', value: BTaskStatus.Resuming }
] as const;

export const BTaskStatusLabel: Record<BTaskStatus, string> = {
  [BTaskStatus.NotStarted]: 'NotStarted',
  [BTaskStatus.Running]: 'Running',
  [BTaskStatus.Paused]: 'Paused',
  [BTaskStatus.Error]: 'Error',
  [BTaskStatus.Completed]: 'Completed',
  [BTaskStatus.Cancelled]: 'Cancelled',
  [BTaskStatus.Cancelling]: 'Cancelling',
  [BTaskStatus.Pausing]: 'Pausing',
  [BTaskStatus.Resuming]: 'Resuming'
};

export enum ClientMode {
  AllInOne = 0,
  RemoteBrowser = 1,
  PureClient = 2
}

export const clientModes = [
  { label: 'AllInOne', value: ClientMode.AllInOne },
  { label: 'RemoteBrowser', value: ClientMode.RemoteBrowser },
  { label: 'PureClient', value: ClientMode.PureClient }
] as const;

export const ClientModeLabel: Record<ClientMode, string> = {
  [ClientMode.AllInOne]: 'AllInOne',
  [ClientMode.RemoteBrowser]: 'RemoteBrowser',
  [ClientMode.PureClient]: 'PureClient'
};

export enum DataOrigin {
  Manual = 1,
  FileSystem = 2,
  Steam = 3,
  DLsite = 4,
  ExHentai = 5,
  ExternalIdentity = 6
}

export const dataOrigins = [
  { label: 'Manual', value: DataOrigin.Manual },
  { label: 'FileSystem', value: DataOrigin.FileSystem },
  { label: 'Steam', value: DataOrigin.Steam },
  { label: 'DLsite', value: DataOrigin.DLsite },
  { label: 'ExHentai', value: DataOrigin.ExHentai },
  { label: 'ExternalIdentity', value: DataOrigin.ExternalIdentity }
] as const;

export const DataOriginLabel: Record<DataOrigin, string> = {
  [DataOrigin.Manual]: 'Manual',
  [DataOrigin.FileSystem]: 'FileSystem',
  [DataOrigin.Steam]: 'Steam',
  [DataOrigin.DLsite]: 'DLsite',
  [DataOrigin.ExHentai]: 'ExHentai',
  [DataOrigin.ExternalIdentity]: 'ExternalIdentity'
};

export enum DataStatus {
  NotStarted = 1,
  Ready = 2,
  Failed = 3
}

export const dataStatuses = [
  { label: 'NotStarted', value: DataStatus.NotStarted },
  { label: 'Ready', value: DataStatus.Ready },
  { label: 'Failed', value: DataStatus.Failed }
] as const;

export const DataStatusLabel: Record<DataStatus, string> = {
  [DataStatus.NotStarted]: 'NotStarted',
  [DataStatus.Ready]: 'Ready',
  [DataStatus.Failed]: 'Failed'
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

export enum FileRenameStatus {
  Pending = 1,
  Conflict = 2,
  Excluded = 3,
  Applied = 4,
  Failed = 5,
  Undone = 6
}

export const fileRenameStatuses = [
  { label: 'Pending', value: FileRenameStatus.Pending },
  { label: 'Conflict', value: FileRenameStatus.Conflict },
  { label: 'Excluded', value: FileRenameStatus.Excluded },
  { label: 'Applied', value: FileRenameStatus.Applied },
  { label: 'Failed', value: FileRenameStatus.Failed },
  { label: 'Undone', value: FileRenameStatus.Undone }
] as const;

export const FileRenameStatusLabel: Record<FileRenameStatus, string> = {
  [FileRenameStatus.Pending]: 'Pending',
  [FileRenameStatus.Conflict]: 'Conflict',
  [FileRenameStatus.Excluded]: 'Excluded',
  [FileRenameStatus.Applied]: 'Applied',
  [FileRenameStatus.Failed]: 'Failed',
  [FileRenameStatus.Undone]: 'Undone'
};

export enum FilterDisplayMode {
  Simple = 1,
  Advanced = 2
}

export const filterDisplayModes = [
  { label: 'Simple', value: FilterDisplayMode.Simple },
  { label: 'Advanced', value: FilterDisplayMode.Advanced }
] as const;

export const FilterDisplayModeLabel: Record<FilterDisplayMode, string> = {
  [FilterDisplayMode.Simple]: 'Simple',
  [FilterDisplayMode.Advanced]: 'Advanced'
};

export enum FsScanTarget {
  Files = 1,
  Directories = 2,
  Both = 3
}

export const fsScanTargets = [
  { label: 'Files', value: FsScanTarget.Files },
  { label: 'Directories', value: FsScanTarget.Directories },
  { label: 'Both', value: FsScanTarget.Both }
] as const;

export const FsScanTargetLabel: Record<FsScanTarget, string> = {
  [FsScanTarget.Files]: 'Files',
  [FsScanTarget.Directories]: 'Directories',
  [FsScanTarget.Both]: 'Both'
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
  PlayedAt = 23,
  MediaLibraryV2 = 24,
  MediaLibraryV2Multi = 25,
  Source = 26,
  HealthScore = 28,
  CollectionMulti = 29,
  HasLocalPath = 30
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
  { label: 'PlayedAt', value: InternalProperty.PlayedAt },
  { label: 'MediaLibraryV2', value: InternalProperty.MediaLibraryV2 },
  { label: 'MediaLibraryV2Multi', value: InternalProperty.MediaLibraryV2Multi },
  { label: 'Source', value: InternalProperty.Source },
  { label: 'HealthScore', value: InternalProperty.HealthScore },
  { label: 'CollectionMulti', value: InternalProperty.CollectionMulti },
  { label: 'HasLocalPath', value: InternalProperty.HasLocalPath }
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
  [InternalProperty.PlayedAt]: 'PlayedAt',
  [InternalProperty.MediaLibraryV2]: 'MediaLibraryV2',
  [InternalProperty.MediaLibraryV2Multi]: 'MediaLibraryV2Multi',
  [InternalProperty.Source]: 'Source',
  [InternalProperty.HealthScore]: 'HealthScore',
  [InternalProperty.CollectionMulti]: 'CollectionMulti',
  [InternalProperty.HasLocalPath]: 'HasLocalPath'
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

export enum PathMarkAdditionalItem {
  None = 0,
  Property = 1,
  MediaLibrary = 2
}

export const pathMarkAdditionalItems = [
  { label: 'None', value: PathMarkAdditionalItem.None },
  { label: 'Property', value: PathMarkAdditionalItem.Property },
  { label: 'MediaLibrary', value: PathMarkAdditionalItem.MediaLibrary }
] as const;

export const PathMarkAdditionalItemLabel: Record<PathMarkAdditionalItem, string> = {
  [PathMarkAdditionalItem.None]: 'None',
  [PathMarkAdditionalItem.Property]: 'Property',
  [PathMarkAdditionalItem.MediaLibrary]: 'MediaLibrary'
};

export enum PathMarkApplyScope {
  MatchedOnly = 1,
  MatchedAndSubdirectories = 2
}

export const pathMarkApplyScopes = [
  { label: 'MatchedOnly', value: PathMarkApplyScope.MatchedOnly },
  { label: 'MatchedAndSubdirectories', value: PathMarkApplyScope.MatchedAndSubdirectories }
] as const;

export const PathMarkApplyScopeLabel: Record<PathMarkApplyScope, string> = {
  [PathMarkApplyScope.MatchedOnly]: 'MatchedOnly',
  [PathMarkApplyScope.MatchedAndSubdirectories]: 'MatchedAndSubdirectories'
};

export enum PathMarkSyncStatus {
  Pending = 0,
  Syncing = 1,
  Synced = 2,
  Failed = 3,
  PendingDelete = 4
}

export const pathMarkSyncStatuses = [
  { label: 'Pending', value: PathMarkSyncStatus.Pending },
  { label: 'Syncing', value: PathMarkSyncStatus.Syncing },
  { label: 'Synced', value: PathMarkSyncStatus.Synced },
  { label: 'Failed', value: PathMarkSyncStatus.Failed },
  { label: 'PendingDelete', value: PathMarkSyncStatus.PendingDelete }
] as const;

export const PathMarkSyncStatusLabel: Record<PathMarkSyncStatus, string> = {
  [PathMarkSyncStatus.Pending]: 'Pending',
  [PathMarkSyncStatus.Syncing]: 'Syncing',
  [PathMarkSyncStatus.Synced]: 'Synced',
  [PathMarkSyncStatus.Failed]: 'Failed',
  [PathMarkSyncStatus.PendingDelete]: 'PendingDelete'
};

export enum PathMarkType {
  Resource = 1,
  Property = 2,
  MediaLibrary = 3
}

export const pathMarkTypes = [
  { label: 'Resource', value: PathMarkType.Resource },
  { label: 'Property', value: PathMarkType.Property },
  { label: 'MediaLibrary', value: PathMarkType.MediaLibrary }
] as const;

export const PathMarkTypeLabel: Record<PathMarkType, string> = {
  [PathMarkType.Resource]: 'Resource',
  [PathMarkType.Property]: 'Property',
  [PathMarkType.MediaLibrary]: 'MediaLibrary'
};

export enum PathMatchMode {
  Layer = 1,
  Regex = 2
}

export const pathMatchModes = [
  { label: 'Layer', value: PathMatchMode.Layer },
  { label: 'Regex', value: PathMatchMode.Regex }
] as const;

export const PathMatchModeLabel: Record<PathMatchMode, string> = {
  [PathMatchMode.Layer]: 'Layer',
  [PathMatchMode.Regex]: 'Regex'
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
  Bakabase = 1000,
  ExHentai = 1001,
  Bangumi = 1002,
  DLsite = 1003,
  Regex = 1004,
  Kodi = 1005,
  Tmdb = 1006,
  Av = 1007,
  Ai = 1008,
  Steam = 1009
}

export const propertyValueScopes = [
  { label: 'Manual', value: PropertyValueScope.Manual },
  { label: 'Synchronization', value: PropertyValueScope.Synchronization },
  { label: 'Bakabase', value: PropertyValueScope.Bakabase },
  { label: 'ExHentai', value: PropertyValueScope.ExHentai },
  { label: 'Bangumi', value: PropertyValueScope.Bangumi },
  { label: 'DLsite', value: PropertyValueScope.DLsite },
  { label: 'Regex', value: PropertyValueScope.Regex },
  { label: 'Kodi', value: PropertyValueScope.Kodi },
  { label: 'Tmdb', value: PropertyValueScope.Tmdb },
  { label: 'Av', value: PropertyValueScope.Av },
  { label: 'Ai', value: PropertyValueScope.Ai },
  { label: 'Steam', value: PropertyValueScope.Steam }
] as const;

export const PropertyValueScopeLabel: Record<PropertyValueScope, string> = {
  [PropertyValueScope.Manual]: 'Manual',
  [PropertyValueScope.Synchronization]: 'Synchronization',
  [PropertyValueScope.Bakabase]: 'Bakabase',
  [PropertyValueScope.ExHentai]: 'ExHentai',
  [PropertyValueScope.Bangumi]: 'Bangumi',
  [PropertyValueScope.DLsite]: 'DLsite',
  [PropertyValueScope.Regex]: 'Regex',
  [PropertyValueScope.Kodi]: 'Kodi',
  [PropertyValueScope.Tmdb]: 'Tmdb',
  [PropertyValueScope.Av]: 'Av',
  [PropertyValueScope.Ai]: 'Ai',
  [PropertyValueScope.Steam]: 'Steam'
};

export enum PropertyValueType {
  Fixed = 1,
  Dynamic = 2
}

export const propertyValueTypes = [
  { label: 'Fixed', value: PropertyValueType.Fixed },
  { label: 'Dynamic', value: PropertyValueType.Dynamic }
] as const;

export const PropertyValueTypeLabel: Record<PropertyValueType, string> = {
  [PropertyValueType.Fixed]: 'Fixed',
  [PropertyValueType.Dynamic]: 'Dynamic'
};

export enum RemoteAccessMode {
  Disabled = 0,
  Enabled = 1,
  Unrestricted = 2
}

export const remoteAccessModes = [
  { label: 'Disabled', value: RemoteAccessMode.Disabled },
  { label: 'Enabled', value: RemoteAccessMode.Enabled },
  { label: 'Unrestricted', value: RemoteAccessMode.Unrestricted }
] as const;

export const RemoteAccessModeLabel: Record<RemoteAccessMode, string> = {
  [RemoteAccessMode.Disabled]: 'Disabled',
  [RemoteAccessMode.Enabled]: 'Enabled',
  [RemoteAccessMode.Unrestricted]: 'Unrestricted'
};

export enum ReservedProperty {
  Introduction = 12,
  Rating = 13,
  Cover = 22,
  Name = 27
}

export const reservedProperties = [
  { label: 'Introduction', value: ReservedProperty.Introduction },
  { label: 'Rating', value: ReservedProperty.Rating },
  { label: 'Cover', value: ReservedProperty.Cover },
  { label: 'Name', value: ReservedProperty.Name }
] as const;

export const ReservedPropertyLabel: Record<ReservedProperty, string> = {
  [ReservedProperty.Introduction]: 'Introduction',
  [ReservedProperty.Rating]: 'Rating',
  [ReservedProperty.Cover]: 'Cover',
  [ReservedProperty.Name]: 'Name'
};

export enum ResourceCacheType {
  Covers = 1,
  PlayableFiles = 2,
  ResourceMarkers = 4
}

export const resourceCacheTypes = [
  { label: 'Covers', value: ResourceCacheType.Covers },
  { label: 'PlayableFiles', value: ResourceCacheType.PlayableFiles },
  { label: 'ResourceMarkers', value: ResourceCacheType.ResourceMarkers }
] as const;

export const ResourceCacheTypeLabel: Record<ResourceCacheType, string> = {
  [ResourceCacheType.Covers]: 'Covers',
  [ResourceCacheType.PlayableFiles]: 'PlayableFiles',
  [ResourceCacheType.ResourceMarkers]: 'ResourceMarkers'
};

export enum ResourceDataType {
  Cover = 1,
  PlayableItem = 2,
  Metadata = 3
}

export const resourceDataTypes = [
  { label: 'Cover', value: ResourceDataType.Cover },
  { label: 'PlayableItem', value: ResourceDataType.PlayableItem },
  { label: 'Metadata', value: ResourceDataType.Metadata }
] as const;

export const ResourceDataTypeLabel: Record<ResourceDataType, string> = {
  [ResourceDataType.Cover]: 'Cover',
  [ResourceDataType.PlayableItem]: 'PlayableItem',
  [ResourceDataType.Metadata]: 'Metadata'
};

export enum ResourceMatchSuggestionStatus {
  Pending = 1,
  Dismissed = 2
}

export const resourceMatchSuggestionStatuses = [
  { label: 'Pending', value: ResourceMatchSuggestionStatus.Pending },
  { label: 'Dismissed', value: ResourceMatchSuggestionStatus.Dismissed }
] as const;

export const ResourceMatchSuggestionStatusLabel: Record<ResourceMatchSuggestionStatus, string> = {
  [ResourceMatchSuggestionStatus.Pending]: 'Pending',
  [ResourceMatchSuggestionStatus.Dismissed]: 'Dismissed'
};

export enum ResourceMoveRecordStatus {
  Pending = 1,
  Moving = 2,
  Succeeded = 3,
  Failed = 4,
  Cancelled = 5,
  Interrupted = 6
}

export const resourceMoveRecordStatuses = [
  { label: 'Pending', value: ResourceMoveRecordStatus.Pending },
  { label: 'Moving', value: ResourceMoveRecordStatus.Moving },
  { label: 'Succeeded', value: ResourceMoveRecordStatus.Succeeded },
  { label: 'Failed', value: ResourceMoveRecordStatus.Failed },
  { label: 'Cancelled', value: ResourceMoveRecordStatus.Cancelled },
  { label: 'Interrupted', value: ResourceMoveRecordStatus.Interrupted }
] as const;

export const ResourceMoveRecordStatusLabel: Record<ResourceMoveRecordStatus, string> = {
  [ResourceMoveRecordStatus.Pending]: 'Pending',
  [ResourceMoveRecordStatus.Moving]: 'Moving',
  [ResourceMoveRecordStatus.Succeeded]: 'Succeeded',
  [ResourceMoveRecordStatus.Failed]: 'Failed',
  [ResourceMoveRecordStatus.Cancelled]: 'Cancelled',
  [ResourceMoveRecordStatus.Interrupted]: 'Interrupted'
};

export enum ResourceSource {
  PathMark = 1,
  Steam = 2,
  DLsite = 3,
  ExHentai = 4,
  Aigc = 5,
  Pixiv = 7
}

export const resourceSources = [
  { label: 'PathMark', value: ResourceSource.PathMark },
  { label: 'Steam', value: ResourceSource.Steam },
  { label: 'DLsite', value: ResourceSource.DLsite },
  { label: 'ExHentai', value: ResourceSource.ExHentai },
  { label: 'Aigc', value: ResourceSource.Aigc },
  { label: 'Pixiv', value: ResourceSource.Pixiv }
] as const;

export const ResourceSourceLabel: Record<ResourceSource, string> = {
  [ResourceSource.PathMark]: 'PathMark',
  [ResourceSource.Steam]: 'Steam',
  [ResourceSource.DLsite]: 'DLsite',
  [ResourceSource.ExHentai]: 'ExHentai',
  [ResourceSource.Aigc]: 'Aigc',
  [ResourceSource.Pixiv]: 'Pixiv'
};

export enum ResourceStatus {
  Active = 1,
  Absent = 2,
  Unavailable = 3
}

export const resourceStatuses = [
  { label: 'Active', value: ResourceStatus.Active },
  { label: 'Absent', value: ResourceStatus.Absent },
  { label: 'Unavailable', value: ResourceStatus.Unavailable }
] as const;

export const ResourceStatusLabel: Record<ResourceStatus, string> = {
  [ResourceStatus.Active]: 'Active',
  [ResourceStatus.Absent]: 'Absent',
  [ResourceStatus.Unavailable]: 'Unavailable'
};

export enum ResourceTag {
  IsParent = 1,
  Pinned = 2
}

export const resourceTags = [
  { label: 'IsParent', value: ResourceTag.IsParent },
  { label: 'Pinned', value: ResourceTag.Pinned }
] as const;

export const ResourceTagLabel: Record<ResourceTag, string> = {
  [ResourceTag.IsParent]: 'IsParent',
  [ResourceTag.Pinned]: 'Pinned'
};

export enum RuleQueueAction {
  Apply = 1,
  Reevaluate = 2,
  FileSystemChange = 3
}

export const ruleQueueActions = [
  { label: 'Apply', value: RuleQueueAction.Apply },
  { label: 'Reevaluate', value: RuleQueueAction.Reevaluate },
  { label: 'FileSystemChange', value: RuleQueueAction.FileSystemChange }
] as const;

export const RuleQueueActionLabel: Record<RuleQueueAction, string> = {
  [RuleQueueAction.Apply]: 'Apply',
  [RuleQueueAction.Reevaluate]: 'Reevaluate',
  [RuleQueueAction.FileSystemChange]: 'FileSystemChange'
};

export enum RuleQueueStatus {
  Pending = 0,
  Processing = 1,
  Completed = 2,
  Failed = 3
}

export const ruleQueueStatuses = [
  { label: 'Pending', value: RuleQueueStatus.Pending },
  { label: 'Processing', value: RuleQueueStatus.Processing },
  { label: 'Completed', value: RuleQueueStatus.Completed },
  { label: 'Failed', value: RuleQueueStatus.Failed }
] as const;

export const RuleQueueStatusLabel: Record<RuleQueueStatus, string> = {
  [RuleQueueStatus.Pending]: 'Pending',
  [RuleQueueStatus.Processing]: 'Processing',
  [RuleQueueStatus.Completed]: 'Completed',
  [RuleQueueStatus.Failed]: 'Failed'
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

export enum TextTypeShape {
  Values = 1,
  DelimiterPair = 2,
  MappingPair = 3
}

export const textTypeShapes = [
  { label: 'Values', value: TextTypeShape.Values },
  { label: 'DelimiterPair', value: TextTypeShape.DelimiterPair },
  { label: 'MappingPair', value: TextTypeShape.MappingPair }
] as const;

export const TextTypeShapeLabel: Record<TextTypeShape, string> = {
  [TextTypeShape.Values]: 'Values',
  [TextTypeShape.DelimiterPair]: 'DelimiterPair',
  [TextTypeShape.MappingPair]: 'MappingPair'
};

export enum WellKnownTextType {
  Useless = 1,
  Wrapper = 3,
  Standardization = 4,
  Volume = 6,
  Trim = 7,
  DateTime = 8,
  Language = 9
}

export const wellKnownTextTypes = [
  { label: 'Useless', value: WellKnownTextType.Useless },
  { label: 'Wrapper', value: WellKnownTextType.Wrapper },
  { label: 'Standardization', value: WellKnownTextType.Standardization },
  { label: 'Volume', value: WellKnownTextType.Volume },
  { label: 'Trim', value: WellKnownTextType.Trim },
  { label: 'DateTime', value: WellKnownTextType.DateTime },
  { label: 'Language', value: WellKnownTextType.Language }
] as const;

export const WellKnownTextTypeLabel: Record<WellKnownTextType, string> = {
  [WellKnownTextType.Useless]: 'Useless',
  [WellKnownTextType.Wrapper]: 'Wrapper',
  [WellKnownTextType.Standardization]: 'Standardization',
  [WellKnownTextType.Volume]: 'Volume',
  [WellKnownTextType.Trim]: 'Trim',
  [WellKnownTextType.DateTime]: 'DateTime',
  [WellKnownTextType.Language]: 'Language'
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

export enum TextMatchMode {
  EqualsAny = 1,
  ContainsAny = 2,
  RegexAny = 3
}

export const textMatchModes = [
  { label: 'EqualsAny', value: TextMatchMode.EqualsAny },
  { label: 'ContainsAny', value: TextMatchMode.ContainsAny },
  { label: 'RegexAny', value: TextMatchMode.RegexAny }
] as const;

export const TextMatchModeLabel: Record<TextMatchMode, string> = {
  [TextMatchMode.EqualsAny]: 'EqualsAny',
  [TextMatchMode.ContainsAny]: 'ContainsAny',
  [TextMatchMode.RegexAny]: 'RegexAny'
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
  CopyFiles = 4,
  Download = 5,
  Acquisition = 6,
  Any = 1000
}

export const bTaskTypes = [
  { label: 'Decompress', value: BTaskType.Decompress },
  { label: 'MoveFiles', value: BTaskType.MoveFiles },
  { label: 'MoveResources', value: BTaskType.MoveResources },
  { label: 'CopyFiles', value: BTaskType.CopyFiles },
  { label: 'Download', value: BTaskType.Download },
  { label: 'Acquisition', value: BTaskType.Acquisition },
  { label: 'Any', value: BTaskType.Any }
] as const;

export const BTaskTypeLabel: Record<BTaskType, string> = {
  [BTaskType.Decompress]: 'Decompress',
  [BTaskType.MoveFiles]: 'MoveFiles',
  [BTaskType.MoveResources]: 'MoveResources',
  [BTaskType.CopyFiles]: 'CopyFiles',
  [BTaskType.Download]: 'Download',
  [BTaskType.Acquisition]: 'Acquisition',
  [BTaskType.Any]: 'Any'
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

export enum CookieValidatorTarget {
  BiliBili = 1,
  ExHentai = 2,
  Pixiv = 3,
  Bangumi = 4,
  SoulPlus = 5,
  DLsite = 6,
  Fanbox = 7,
  Fantia = 8,
  Cien = 9,
  Patreon = 10
}

export const cookieValidatorTargets = [
  { label: 'BiliBili', value: CookieValidatorTarget.BiliBili },
  { label: 'ExHentai', value: CookieValidatorTarget.ExHentai },
  { label: 'Pixiv', value: CookieValidatorTarget.Pixiv },
  { label: 'Bangumi', value: CookieValidatorTarget.Bangumi },
  { label: 'SoulPlus', value: CookieValidatorTarget.SoulPlus },
  { label: 'DLsite', value: CookieValidatorTarget.DLsite },
  { label: 'Fanbox', value: CookieValidatorTarget.Fanbox },
  { label: 'Fantia', value: CookieValidatorTarget.Fantia },
  { label: 'Cien', value: CookieValidatorTarget.Cien },
  { label: 'Patreon', value: CookieValidatorTarget.Patreon }
] as const;

export const CookieValidatorTargetLabel: Record<CookieValidatorTarget, string> = {
  [CookieValidatorTarget.BiliBili]: 'BiliBili',
  [CookieValidatorTarget.ExHentai]: 'ExHentai',
  [CookieValidatorTarget.Pixiv]: 'Pixiv',
  [CookieValidatorTarget.Bangumi]: 'Bangumi',
  [CookieValidatorTarget.SoulPlus]: 'SoulPlus',
  [CookieValidatorTarget.DLsite]: 'DLsite',
  [CookieValidatorTarget.Fanbox]: 'Fanbox',
  [CookieValidatorTarget.Fantia]: 'Fantia',
  [CookieValidatorTarget.Cien]: 'Cien',
  [CookieValidatorTarget.Patreon]: 'Patreon'
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
  Cover = 22,
  PlayedAt = 23,
  MediaLibraryV2 = 24,
  MediaLibraryV2Multi = 25,
  Source = 26,
  Name = 27,
  HealthScore = 28,
  CollectionMulti = 29,
  HasLocalPath = 30
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
  { label: 'Cover', value: ResourceProperty.Cover },
  { label: 'PlayedAt', value: ResourceProperty.PlayedAt },
  { label: 'MediaLibraryV2', value: ResourceProperty.MediaLibraryV2 },
  { label: 'MediaLibraryV2Multi', value: ResourceProperty.MediaLibraryV2Multi },
  { label: 'Source', value: ResourceProperty.Source },
  { label: 'Name', value: ResourceProperty.Name },
  { label: 'HealthScore', value: ResourceProperty.HealthScore },
  { label: 'CollectionMulti', value: ResourceProperty.CollectionMulti },
  { label: 'HasLocalPath', value: ResourceProperty.HasLocalPath }
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
  [ResourceProperty.Cover]: 'Cover',
  [ResourceProperty.PlayedAt]: 'PlayedAt',
  [ResourceProperty.MediaLibraryV2]: 'MediaLibraryV2',
  [ResourceProperty.MediaLibraryV2Multi]: 'MediaLibraryV2Multi',
  [ResourceProperty.Source]: 'Source',
  [ResourceProperty.Name]: 'Name',
  [ResourceProperty.HealthScore]: 'HealthScore',
  [ResourceProperty.CollectionMulti]: 'CollectionMulti',
  [ResourceProperty.HasLocalPath]: 'HasLocalPath'
};

export enum SearchableReservedProperty {
  Introduction = 12,
  Rating = 13,
  FileName = 15,
  DirectoryPath = 16,
  CreatedAt = 17,
  FileCreatedAt = 18,
  FileModifiedAt = 19,
  Cover = 22,
  MediaLibraryV2 = 24,
  MediaLibraryV2Multi = 25,
  Source = 26,
  Name = 27,
  CollectionMulti = 29,
  HasLocalPath = 30
}

export const searchableReservedProperties = [
  { label: 'Introduction', value: SearchableReservedProperty.Introduction },
  { label: 'Rating', value: SearchableReservedProperty.Rating },
  { label: 'FileName', value: SearchableReservedProperty.FileName },
  { label: 'DirectoryPath', value: SearchableReservedProperty.DirectoryPath },
  { label: 'CreatedAt', value: SearchableReservedProperty.CreatedAt },
  { label: 'FileCreatedAt', value: SearchableReservedProperty.FileCreatedAt },
  { label: 'FileModifiedAt', value: SearchableReservedProperty.FileModifiedAt },
  { label: 'Cover', value: SearchableReservedProperty.Cover },
  { label: 'MediaLibraryV2', value: SearchableReservedProperty.MediaLibraryV2 },
  { label: 'MediaLibraryV2Multi', value: SearchableReservedProperty.MediaLibraryV2Multi },
  { label: 'Source', value: SearchableReservedProperty.Source },
  { label: 'Name', value: SearchableReservedProperty.Name },
  { label: 'CollectionMulti', value: SearchableReservedProperty.CollectionMulti },
  { label: 'HasLocalPath', value: SearchableReservedProperty.HasLocalPath }
] as const;

export const SearchableReservedPropertyLabel: Record<SearchableReservedProperty, string> = {
  [SearchableReservedProperty.Introduction]: 'Introduction',
  [SearchableReservedProperty.Rating]: 'Rating',
  [SearchableReservedProperty.FileName]: 'FileName',
  [SearchableReservedProperty.DirectoryPath]: 'DirectoryPath',
  [SearchableReservedProperty.CreatedAt]: 'CreatedAt',
  [SearchableReservedProperty.FileCreatedAt]: 'FileCreatedAt',
  [SearchableReservedProperty.FileModifiedAt]: 'FileModifiedAt',
  [SearchableReservedProperty.Cover]: 'Cover',
  [SearchableReservedProperty.MediaLibraryV2]: 'MediaLibraryV2',
  [SearchableReservedProperty.MediaLibraryV2Multi]: 'MediaLibraryV2Multi',
  [SearchableReservedProperty.Source]: 'Source',
  [SearchableReservedProperty.Name]: 'Name',
  [SearchableReservedProperty.CollectionMulti]: 'CollectionMulti',
  [SearchableReservedProperty.HasLocalPath]: 'HasLocalPath'
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
  Tmdb = 11,
  Steam = 12,
  Vndb = 13
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
  { label: 'Tmdb', value: ThirdPartyId.Tmdb },
  { label: 'Steam', value: ThirdPartyId.Steam },
  { label: 'Vndb', value: ThirdPartyId.Vndb }
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
  [ThirdPartyId.Tmdb]: 'Tmdb',
  [ThirdPartyId.Steam]: 'Steam',
  [ThirdPartyId.Vndb]: 'Vndb'
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
  PlayedAt = 11,
  HealthScore = 12
}

export const resourceSearchSortableProperties = [
  { label: 'FileCreateDt', value: ResourceSearchSortableProperty.FileCreateDt },
  { label: 'FileModifyDt', value: ResourceSearchSortableProperty.FileModifyDt },
  { label: 'Filename', value: ResourceSearchSortableProperty.Filename },
  { label: 'AddDt', value: ResourceSearchSortableProperty.AddDt },
  { label: 'PlayedAt', value: ResourceSearchSortableProperty.PlayedAt },
  { label: 'HealthScore', value: ResourceSearchSortableProperty.HealthScore }
] as const;

export const ResourceSearchSortablePropertyLabel: Record<ResourceSearchSortableProperty, string> = {
  [ResourceSearchSortableProperty.FileCreateDt]: 'FileCreateDt',
  [ResourceSearchSortableProperty.FileModifyDt]: 'FileModifyDt',
  [ResourceSearchSortableProperty.Filename]: 'Filename',
  [ResourceSearchSortableProperty.AddDt]: 'AddDt',
  [ResourceSearchSortableProperty.PlayedAt]: 'PlayedAt',
  [ResourceSearchSortableProperty.HealthScore]: 'HealthScore'
};

export enum CustomPropertyAdditionalItem {
  None = 0,
  ValueCount = 2
}

export const customPropertyAdditionalItems = [
  { label: 'None', value: CustomPropertyAdditionalItem.None },
  { label: 'ValueCount', value: CustomPropertyAdditionalItem.ValueCount }
] as const;

export const CustomPropertyAdditionalItemLabel: Record<CustomPropertyAdditionalItem, string> = {
  [CustomPropertyAdditionalItem.None]: 'None',
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

export enum ResourceAdditionalItem {
  None = 0,
  Properties = 32,
  Alias = 64,
  DisplayName = 288,
  HasChildren = 512,
  MediaLibraryName = 2048,
  Cover = 16416,
  PlayableItem = 32768,
  CollectionName = 65536,
  All = 117600
}

export const resourceAdditionalItems = [
  { label: 'None', value: ResourceAdditionalItem.None },
  { label: 'Properties', value: ResourceAdditionalItem.Properties },
  { label: 'Alias', value: ResourceAdditionalItem.Alias },
  { label: 'DisplayName', value: ResourceAdditionalItem.DisplayName },
  { label: 'HasChildren', value: ResourceAdditionalItem.HasChildren },
  { label: 'MediaLibraryName', value: ResourceAdditionalItem.MediaLibraryName },
  { label: 'Cover', value: ResourceAdditionalItem.Cover },
  { label: 'PlayableItem', value: ResourceAdditionalItem.PlayableItem },
  { label: 'CollectionName', value: ResourceAdditionalItem.CollectionName },
  { label: 'All', value: ResourceAdditionalItem.All }
] as const;

export const ResourceAdditionalItemLabel: Record<ResourceAdditionalItem, string> = {
  [ResourceAdditionalItem.None]: 'None',
  [ResourceAdditionalItem.Properties]: 'Properties',
  [ResourceAdditionalItem.Alias]: 'Alias',
  [ResourceAdditionalItem.DisplayName]: 'DisplayName',
  [ResourceAdditionalItem.HasChildren]: 'HasChildren',
  [ResourceAdditionalItem.MediaLibraryName]: 'MediaLibraryName',
  [ResourceAdditionalItem.Cover]: 'Cover',
  [ResourceAdditionalItem.PlayableItem]: 'PlayableItem',
  [ResourceAdditionalItem.CollectionName]: 'CollectionName',
  [ResourceAdditionalItem.All]: 'All'
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
  Docker = 2,
  MacOS = 3
}

export const runtimeModes = [
  { label: 'Dev', value: RuntimeMode.Dev },
  { label: 'WinForms', value: RuntimeMode.WinForms },
  { label: 'Docker', value: RuntimeMode.Docker },
  { label: 'MacOS', value: RuntimeMode.MacOS }
] as const;

export const RuntimeModeLabel: Record<RuntimeMode, string> = {
  [RuntimeMode.Dev]: 'Dev',
  [RuntimeMode.WinForms]: 'WinForms',
  [RuntimeMode.Docker]: 'Docker',
  [RuntimeMode.MacOS]: 'MacOS'
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

export enum EnhancerId {
  Bakabase = 1,
  ExHentai = 2,
  Bangumi = 3,
  DLsite = 4,
  Regex = 5,
  Kodi = 6,
  Tmdb = 7,
  Av = 8,
  AI = 9
}

export const enhancerIds = [
  { label: 'Bakabase', value: EnhancerId.Bakabase },
  { label: 'ExHentai', value: EnhancerId.ExHentai },
  { label: 'Bangumi', value: EnhancerId.Bangumi },
  { label: 'DLsite', value: EnhancerId.DLsite },
  { label: 'Regex', value: EnhancerId.Regex },
  { label: 'Kodi', value: EnhancerId.Kodi },
  { label: 'Tmdb', value: EnhancerId.Tmdb },
  { label: 'Av', value: EnhancerId.Av },
  { label: 'AI', value: EnhancerId.AI }
] as const;

export const EnhancerIdLabel: Record<EnhancerId, string> = {
  [EnhancerId.Bakabase]: 'Bakabase',
  [EnhancerId.ExHentai]: 'ExHentai',
  [EnhancerId.Bangumi]: 'Bangumi',
  [EnhancerId.DLsite]: 'DLsite',
  [EnhancerId.Regex]: 'Regex',
  [EnhancerId.Kodi]: 'Kodi',
  [EnhancerId.Tmdb]: 'Tmdb',
  [EnhancerId.Av]: 'Av',
  [EnhancerId.AI]: 'AI'
};

export enum EnhancerTag {
  UseRegex = 1,
  UseKeyword = 2
}

export const enhancerTags = [
  { label: 'UseRegex', value: EnhancerTag.UseRegex },
  { label: 'UseKeyword', value: EnhancerTag.UseKeyword }
] as const;

export const EnhancerTagLabel: Record<EnhancerTag, string> = {
  [EnhancerTag.UseRegex]: 'UseRegex',
  [EnhancerTag.UseKeyword]: 'UseKeyword'
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
  Mosaic = 16,
  Introduction = 17
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
  { label: 'Mosaic', value: AvEnhancerTarget.Mosaic },
  { label: 'Introduction', value: AvEnhancerTarget.Introduction }
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
  [AvEnhancerTarget.Mosaic]: 'Mosaic',
  [AvEnhancerTarget.Introduction]: 'Introduction'
};

export enum AiEnhancerTarget {
  Properties = 1
}

export const aiEnhancerTargets = [
  { label: 'Properties', value: AiEnhancerTarget.Properties }
] as const;

export const AiEnhancerTargetLabel: Record<AiEnhancerTarget, string> = {
  [AiEnhancerTarget.Properties]: 'Properties'
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

export enum AcquisitionRunState {
  Completed = 1,
  Waiting = 2,
  Failed = 3
}

export const acquisitionRunStates = [
  { label: 'Completed', value: AcquisitionRunState.Completed },
  { label: 'Waiting', value: AcquisitionRunState.Waiting },
  { label: 'Failed', value: AcquisitionRunState.Failed }
] as const;

export const AcquisitionRunStateLabel: Record<AcquisitionRunState, string> = {
  [AcquisitionRunState.Completed]: 'Completed',
  [AcquisitionRunState.Waiting]: 'Waiting',
  [AcquisitionRunState.Failed]: 'Failed'
};

export enum PlacementConflictPolicy {
  Rename = 1,
  Merge = 2,
  Ask = 3
}

export const placementConflictPolicies = [
  { label: 'Rename', value: PlacementConflictPolicy.Rename },
  { label: 'Merge', value: PlacementConflictPolicy.Merge },
  { label: 'Ask', value: PlacementConflictPolicy.Ask }
] as const;

export const PlacementConflictPolicyLabel: Record<PlacementConflictPolicy, string> = {
  [PlacementConflictPolicy.Rename]: 'Rename',
  [PlacementConflictPolicy.Merge]: 'Merge',
  [PlacementConflictPolicy.Ask]: 'Ask'
};

export enum AcquisitionDriveKind {
  Unknown = 0,
  DirectUrl = 1,
  Baidu = 2,
  Xunlei = 3,
  Feimao = 4,
  Cloudflare = 5,
  Mega = 6,
  PikPak = 7,
  GoogleDrive = 8,
  OneDrive = 9,
  Magnet = 10,
  OneOneFive = 11
}

export const acquisitionDriveKinds = [
  { label: 'Unknown', value: AcquisitionDriveKind.Unknown },
  { label: 'DirectUrl', value: AcquisitionDriveKind.DirectUrl },
  { label: 'Baidu', value: AcquisitionDriveKind.Baidu },
  { label: 'Xunlei', value: AcquisitionDriveKind.Xunlei },
  { label: 'Feimao', value: AcquisitionDriveKind.Feimao },
  { label: 'Cloudflare', value: AcquisitionDriveKind.Cloudflare },
  { label: 'Mega', value: AcquisitionDriveKind.Mega },
  { label: 'PikPak', value: AcquisitionDriveKind.PikPak },
  { label: 'GoogleDrive', value: AcquisitionDriveKind.GoogleDrive },
  { label: 'OneDrive', value: AcquisitionDriveKind.OneDrive },
  { label: 'Magnet', value: AcquisitionDriveKind.Magnet },
  { label: 'OneOneFive', value: AcquisitionDriveKind.OneOneFive }
] as const;

export const AcquisitionDriveKindLabel: Record<AcquisitionDriveKind, string> = {
  [AcquisitionDriveKind.Unknown]: 'Unknown',
  [AcquisitionDriveKind.DirectUrl]: 'DirectUrl',
  [AcquisitionDriveKind.Baidu]: 'Baidu',
  [AcquisitionDriveKind.Xunlei]: 'Xunlei',
  [AcquisitionDriveKind.Feimao]: 'Feimao',
  [AcquisitionDriveKind.Cloudflare]: 'Cloudflare',
  [AcquisitionDriveKind.Mega]: 'Mega',
  [AcquisitionDriveKind.PikPak]: 'PikPak',
  [AcquisitionDriveKind.GoogleDrive]: 'GoogleDrive',
  [AcquisitionDriveKind.OneDrive]: 'OneDrive',
  [AcquisitionDriveKind.Magnet]: 'Magnet',
  [AcquisitionDriveKind.OneOneFive]: 'OneOneFive'
};

export enum AcquisitionLeadKind {
  PlatformHolding = 1,
  SharedPage = 2,
  SharedDocument = 3,
  DirectUrl = 4,
  Magnet = 5,
  Manual = 6,
  Torrent = 7
}

export const acquisitionLeadKinds = [
  { label: 'PlatformHolding', value: AcquisitionLeadKind.PlatformHolding },
  { label: 'SharedPage', value: AcquisitionLeadKind.SharedPage },
  { label: 'SharedDocument', value: AcquisitionLeadKind.SharedDocument },
  { label: 'DirectUrl', value: AcquisitionLeadKind.DirectUrl },
  { label: 'Magnet', value: AcquisitionLeadKind.Magnet },
  { label: 'Manual', value: AcquisitionLeadKind.Manual },
  { label: 'Torrent', value: AcquisitionLeadKind.Torrent }
] as const;

export const AcquisitionLeadKindLabel: Record<AcquisitionLeadKind, string> = {
  [AcquisitionLeadKind.PlatformHolding]: 'PlatformHolding',
  [AcquisitionLeadKind.SharedPage]: 'SharedPage',
  [AcquisitionLeadKind.SharedDocument]: 'SharedDocument',
  [AcquisitionLeadKind.DirectUrl]: 'DirectUrl',
  [AcquisitionLeadKind.Magnet]: 'Magnet',
  [AcquisitionLeadKind.Manual]: 'Manual',
  [AcquisitionLeadKind.Torrent]: 'Torrent'
};

export enum AcquisitionLeadOrigin {
  User = 1,
  Subscription = 2,
  SharedListImport = 3,
  PostParser = 4
}

export const acquisitionLeadOrigins = [
  { label: 'User', value: AcquisitionLeadOrigin.User },
  { label: 'Subscription', value: AcquisitionLeadOrigin.Subscription },
  { label: 'SharedListImport', value: AcquisitionLeadOrigin.SharedListImport },
  { label: 'PostParser', value: AcquisitionLeadOrigin.PostParser }
] as const;

export const AcquisitionLeadOriginLabel: Record<AcquisitionLeadOrigin, string> = {
  [AcquisitionLeadOrigin.User]: 'User',
  [AcquisitionLeadOrigin.Subscription]: 'Subscription',
  [AcquisitionLeadOrigin.SharedListImport]: 'SharedListImport',
  [AcquisitionLeadOrigin.PostParser]: 'PostParser'
};

export enum AcquisitionLeadResult {
  Succeeded = 1,
  Failed = 2
}

export const acquisitionLeadResults = [
  { label: 'Succeeded', value: AcquisitionLeadResult.Succeeded },
  { label: 'Failed', value: AcquisitionLeadResult.Failed }
] as const;

export const AcquisitionLeadResultLabel: Record<AcquisitionLeadResult, string> = {
  [AcquisitionLeadResult.Succeeded]: 'Succeeded',
  [AcquisitionLeadResult.Failed]: 'Failed'
};

export enum AcquisitionStatus {
  Pending = 1,
  Running = 2,
  Waiting = 3,
  Completed = 4,
  Failed = 5,
  Cancelled = 6
}

export const acquisitionStatuses = [
  { label: 'Pending', value: AcquisitionStatus.Pending },
  { label: 'Running', value: AcquisitionStatus.Running },
  { label: 'Waiting', value: AcquisitionStatus.Waiting },
  { label: 'Completed', value: AcquisitionStatus.Completed },
  { label: 'Failed', value: AcquisitionStatus.Failed },
  { label: 'Cancelled', value: AcquisitionStatus.Cancelled }
] as const;

export const AcquisitionStatusLabel: Record<AcquisitionStatus, string> = {
  [AcquisitionStatus.Pending]: 'Pending',
  [AcquisitionStatus.Running]: 'Running',
  [AcquisitionStatus.Waiting]: 'Waiting',
  [AcquisitionStatus.Completed]: 'Completed',
  [AcquisitionStatus.Failed]: 'Failed',
  [AcquisitionStatus.Cancelled]: 'Cancelled'
};

export enum AcquisitionWaitReason {
  WaitingForFile = 1,
  AmbiguousInboxFile = 2,
  PaidContent = 3,
  NoLinks = 4,
  ChooseLink = 5,
  PasswordUnknown = 6,
  TargetExists = 7,
  PickDirectory = 8,
  PlatformFetch = 9
}

export const acquisitionWaitReasons = [
  { label: 'WaitingForFile', value: AcquisitionWaitReason.WaitingForFile },
  { label: 'AmbiguousInboxFile', value: AcquisitionWaitReason.AmbiguousInboxFile },
  { label: 'PaidContent', value: AcquisitionWaitReason.PaidContent },
  { label: 'NoLinks', value: AcquisitionWaitReason.NoLinks },
  { label: 'ChooseLink', value: AcquisitionWaitReason.ChooseLink },
  { label: 'PasswordUnknown', value: AcquisitionWaitReason.PasswordUnknown },
  { label: 'TargetExists', value: AcquisitionWaitReason.TargetExists },
  { label: 'PickDirectory', value: AcquisitionWaitReason.PickDirectory },
  { label: 'PlatformFetch', value: AcquisitionWaitReason.PlatformFetch }
] as const;

export const AcquisitionWaitReasonLabel: Record<AcquisitionWaitReason, string> = {
  [AcquisitionWaitReason.WaitingForFile]: 'WaitingForFile',
  [AcquisitionWaitReason.AmbiguousInboxFile]: 'AmbiguousInboxFile',
  [AcquisitionWaitReason.PaidContent]: 'PaidContent',
  [AcquisitionWaitReason.NoLinks]: 'NoLinks',
  [AcquisitionWaitReason.ChooseLink]: 'ChooseLink',
  [AcquisitionWaitReason.PasswordUnknown]: 'PasswordUnknown',
  [AcquisitionWaitReason.TargetExists]: 'TargetExists',
  [AcquisitionWaitReason.PickDirectory]: 'PickDirectory',
  [AcquisitionWaitReason.PlatformFetch]: 'PlatformFetch'
};

export enum WorkflowActivationMode {
  Unknown = 0,
  Manual = 1,
  Module = 2,
  SystemEvent = 3,
  Schedule = 4,
  Watch = 5
}

export const workflowActivationModes = [
  { label: 'Unknown', value: WorkflowActivationMode.Unknown },
  { label: 'Manual', value: WorkflowActivationMode.Manual },
  { label: 'Module', value: WorkflowActivationMode.Module },
  { label: 'SystemEvent', value: WorkflowActivationMode.SystemEvent },
  { label: 'Schedule', value: WorkflowActivationMode.Schedule },
  { label: 'Watch', value: WorkflowActivationMode.Watch }
] as const;

export const WorkflowActivationModeLabel: Record<WorkflowActivationMode, string> = {
  [WorkflowActivationMode.Unknown]: 'Unknown',
  [WorkflowActivationMode.Manual]: 'Manual',
  [WorkflowActivationMode.Module]: 'Module',
  [WorkflowActivationMode.SystemEvent]: 'SystemEvent',
  [WorkflowActivationMode.Schedule]: 'Schedule',
  [WorkflowActivationMode.Watch]: 'Watch'
};

export enum WorkflowActivityCardinality {
  OneToOne = 1,
  OneToMany = 2
}

export const workflowActivityCardinalities = [
  { label: 'OneToOne', value: WorkflowActivityCardinality.OneToOne },
  { label: 'OneToMany', value: WorkflowActivityCardinality.OneToMany }
] as const;

export const WorkflowActivityCardinalityLabel: Record<WorkflowActivityCardinality, string> = {
  [WorkflowActivityCardinality.OneToOne]: 'OneToOne',
  [WorkflowActivityCardinality.OneToMany]: 'OneToMany'
};

export enum WorkflowActivityCategory {
  Filter = 1,
  Action = 2,
  Transform = 3
}

export const workflowActivityCategories = [
  { label: 'Filter', value: WorkflowActivityCategory.Filter },
  { label: 'Action', value: WorkflowActivityCategory.Action },
  { label: 'Transform', value: WorkflowActivityCategory.Transform }
] as const;

export const WorkflowActivityCategoryLabel: Record<WorkflowActivityCategory, string> = {
  [WorkflowActivityCategory.Filter]: 'Filter',
  [WorkflowActivityCategory.Action]: 'Action',
  [WorkflowActivityCategory.Transform]: 'Transform'
};

export enum WorkflowActivityErrorBehavior {
  Fail = 1,
  Skip = 2
}

export const workflowActivityErrorBehaviors = [
  { label: 'Fail', value: WorkflowActivityErrorBehavior.Fail },
  { label: 'Skip', value: WorkflowActivityErrorBehavior.Skip }
] as const;

export const WorkflowActivityErrorBehaviorLabel: Record<WorkflowActivityErrorBehavior, string> = {
  [WorkflowActivityErrorBehavior.Fail]: 'Fail',
  [WorkflowActivityErrorBehavior.Skip]: 'Skip'
};

export enum WorkflowItemTypeBehavior {
  Passthrough = 1,
  Fixed = 2,
  AdaptToNext = 3
}

export const workflowItemTypeBehaviors = [
  { label: 'Passthrough', value: WorkflowItemTypeBehavior.Passthrough },
  { label: 'Fixed', value: WorkflowItemTypeBehavior.Fixed },
  { label: 'AdaptToNext', value: WorkflowItemTypeBehavior.AdaptToNext }
] as const;

export const WorkflowItemTypeBehaviorLabel: Record<WorkflowItemTypeBehavior, string> = {
  [WorkflowItemTypeBehavior.Passthrough]: 'Passthrough',
  [WorkflowItemTypeBehavior.Fixed]: 'Fixed',
  [WorkflowItemTypeBehavior.AdaptToNext]: 'AdaptToNext'
};

export enum WorkflowRunStatus {
  Pending = 1,
  Running = 2,
  Success = 3,
  Failed = 4,
  Cancelled = 5,
  Interrupted = 6,
  Waiting = 7
}

export const workflowRunStatuses = [
  { label: 'Pending', value: WorkflowRunStatus.Pending },
  { label: 'Running', value: WorkflowRunStatus.Running },
  { label: 'Success', value: WorkflowRunStatus.Success },
  { label: 'Failed', value: WorkflowRunStatus.Failed },
  { label: 'Cancelled', value: WorkflowRunStatus.Cancelled },
  { label: 'Interrupted', value: WorkflowRunStatus.Interrupted },
  { label: 'Waiting', value: WorkflowRunStatus.Waiting }
] as const;

export const WorkflowRunStatusLabel: Record<WorkflowRunStatus, string> = {
  [WorkflowRunStatus.Pending]: 'Pending',
  [WorkflowRunStatus.Running]: 'Running',
  [WorkflowRunStatus.Success]: 'Success',
  [WorkflowRunStatus.Failed]: 'Failed',
  [WorkflowRunStatus.Cancelled]: 'Cancelled',
  [WorkflowRunStatus.Interrupted]: 'Interrupted',
  [WorkflowRunStatus.Waiting]: 'Waiting'
};

export enum AttachmentLayout {
  Tile = 0,
  Carousel = 1
}

export const attachmentLayouts = [
  { label: 'Tile', value: AttachmentLayout.Tile },
  { label: 'Carousel', value: AttachmentLayout.Carousel }
] as const;

export const AttachmentLayoutLabel: Record<AttachmentLayout, string> = {
  [AttachmentLayout.Tile]: 'Tile',
  [AttachmentLayout.Carousel]: 'Carousel'
};

export enum PropertyValueMatchPolicy {
  MatchOnly = 1,
  AutoCreateOptions = 2
}

export const propertyValueMatchPolicies = [
  { label: 'MatchOnly', value: PropertyValueMatchPolicy.MatchOnly },
  { label: 'AutoCreateOptions', value: PropertyValueMatchPolicy.AutoCreateOptions }
] as const;

export const PropertyValueMatchPolicyLabel: Record<PropertyValueMatchPolicy, string> = {
  [PropertyValueMatchPolicy.MatchOnly]: 'MatchOnly',
  [PropertyValueMatchPolicy.AutoCreateOptions]: 'AutoCreateOptions'
};

export enum ComparisonMode {
  StrictEqual = 0,
  TextSimilarity = 2,
  RegexExtractNumber = 3,
  FixedTolerance = 4,
  RelativeTolerance = 5,
  SetIntersection = 6,
  Subset = 7,
  TimeWindow = 8,
  SameDay = 9,
  ExtensionMap = 10
}

export const comparisonModes = [
  { label: 'StrictEqual', value: ComparisonMode.StrictEqual },
  { label: 'TextSimilarity', value: ComparisonMode.TextSimilarity },
  { label: 'RegexExtractNumber', value: ComparisonMode.RegexExtractNumber },
  { label: 'FixedTolerance', value: ComparisonMode.FixedTolerance },
  { label: 'RelativeTolerance', value: ComparisonMode.RelativeTolerance },
  { label: 'SetIntersection', value: ComparisonMode.SetIntersection },
  { label: 'Subset', value: ComparisonMode.Subset },
  { label: 'TimeWindow', value: ComparisonMode.TimeWindow },
  { label: 'SameDay', value: ComparisonMode.SameDay },
  { label: 'ExtensionMap', value: ComparisonMode.ExtensionMap }
] as const;

export const ComparisonModeLabel: Record<ComparisonMode, string> = {
  [ComparisonMode.StrictEqual]: 'StrictEqual',
  [ComparisonMode.TextSimilarity]: 'TextSimilarity',
  [ComparisonMode.RegexExtractNumber]: 'RegexExtractNumber',
  [ComparisonMode.FixedTolerance]: 'FixedTolerance',
  [ComparisonMode.RelativeTolerance]: 'RelativeTolerance',
  [ComparisonMode.SetIntersection]: 'SetIntersection',
  [ComparisonMode.Subset]: 'Subset',
  [ComparisonMode.TimeWindow]: 'TimeWindow',
  [ComparisonMode.SameDay]: 'SameDay',
  [ComparisonMode.ExtensionMap]: 'ExtensionMap'
};

export enum NullValueBehavior {
  Skip = 0,
  Fail = 1,
  Pass = 2
}

export const nullValueBehaviors = [
  { label: 'Skip', value: NullValueBehavior.Skip },
  { label: 'Fail', value: NullValueBehavior.Fail },
  { label: 'Pass', value: NullValueBehavior.Pass }
] as const;

export const NullValueBehaviorLabel: Record<NullValueBehavior, string> = {
  [NullValueBehavior.Skip]: 'Skip',
  [NullValueBehavior.Fail]: 'Fail',
  [NullValueBehavior.Pass]: 'Pass'
};

export enum DeviceAuthOutcome {
  Anonymous = 0,
  Authenticated = 1,
  UnknownDevice = 2,
  BadSignature = 3,
  Expired = 4,
  Replayed = 5
}

export const deviceAuthOutcomes = [
  { label: 'Anonymous', value: DeviceAuthOutcome.Anonymous },
  { label: 'Authenticated', value: DeviceAuthOutcome.Authenticated },
  { label: 'UnknownDevice', value: DeviceAuthOutcome.UnknownDevice },
  { label: 'BadSignature', value: DeviceAuthOutcome.BadSignature },
  { label: 'Expired', value: DeviceAuthOutcome.Expired },
  { label: 'Replayed', value: DeviceAuthOutcome.Replayed }
] as const;

export const DeviceAuthOutcomeLabel: Record<DeviceAuthOutcome, string> = {
  [DeviceAuthOutcome.Anonymous]: 'Anonymous',
  [DeviceAuthOutcome.Authenticated]: 'Authenticated',
  [DeviceAuthOutcome.UnknownDevice]: 'UnknownDevice',
  [DeviceAuthOutcome.BadSignature]: 'BadSignature',
  [DeviceAuthOutcome.Expired]: 'Expired',
  [DeviceAuthOutcome.Replayed]: 'Replayed'
};

export enum ManagedServerOutcome {
  Ok = 0,
  AwaitingApproval = 1,
  Unreachable = 2,
  NotBakabase = 3,
  ThisAppTooOld = 4,
  ServerTooOld = 5,
  RemoteAccessDisabled = 6,
  ThisDevice = 7,
  CodeRejected = 8,
  RequestRejected = 9,
  TooManyAttempts = 10,
  PairingUnsupported = 11
}

export const managedServerOutcomes = [
  { label: 'Ok', value: ManagedServerOutcome.Ok },
  { label: 'AwaitingApproval', value: ManagedServerOutcome.AwaitingApproval },
  { label: 'Unreachable', value: ManagedServerOutcome.Unreachable },
  { label: 'NotBakabase', value: ManagedServerOutcome.NotBakabase },
  { label: 'ThisAppTooOld', value: ManagedServerOutcome.ThisAppTooOld },
  { label: 'ServerTooOld', value: ManagedServerOutcome.ServerTooOld },
  { label: 'RemoteAccessDisabled', value: ManagedServerOutcome.RemoteAccessDisabled },
  { label: 'ThisDevice', value: ManagedServerOutcome.ThisDevice },
  { label: 'CodeRejected', value: ManagedServerOutcome.CodeRejected },
  { label: 'RequestRejected', value: ManagedServerOutcome.RequestRejected },
  { label: 'TooManyAttempts', value: ManagedServerOutcome.TooManyAttempts },
  { label: 'PairingUnsupported', value: ManagedServerOutcome.PairingUnsupported }
] as const;

export const ManagedServerOutcomeLabel: Record<ManagedServerOutcome, string> = {
  [ManagedServerOutcome.Ok]: 'Ok',
  [ManagedServerOutcome.AwaitingApproval]: 'AwaitingApproval',
  [ManagedServerOutcome.Unreachable]: 'Unreachable',
  [ManagedServerOutcome.NotBakabase]: 'NotBakabase',
  [ManagedServerOutcome.ThisAppTooOld]: 'ThisAppTooOld',
  [ManagedServerOutcome.ServerTooOld]: 'ServerTooOld',
  [ManagedServerOutcome.RemoteAccessDisabled]: 'RemoteAccessDisabled',
  [ManagedServerOutcome.ThisDevice]: 'ThisDevice',
  [ManagedServerOutcome.CodeRejected]: 'CodeRejected',
  [ManagedServerOutcome.RequestRejected]: 'RequestRejected',
  [ManagedServerOutcome.TooManyAttempts]: 'TooManyAttempts',
  [ManagedServerOutcome.PairingUnsupported]: 'PairingUnsupported'
};

export enum ManagedServerState {
  Unknown = 0,
  Online = 1,
  Offline = 2,
  Revoked = 3,
  WrongServer = 4
}

export const managedServerStates = [
  { label: 'Unknown', value: ManagedServerState.Unknown },
  { label: 'Online', value: ManagedServerState.Online },
  { label: 'Offline', value: ManagedServerState.Offline },
  { label: 'Revoked', value: ManagedServerState.Revoked },
  { label: 'WrongServer', value: ManagedServerState.WrongServer }
] as const;

export const ManagedServerStateLabel: Record<ManagedServerState, string> = {
  [ManagedServerState.Unknown]: 'Unknown',
  [ManagedServerState.Online]: 'Online',
  [ManagedServerState.Offline]: 'Offline',
  [ManagedServerState.Revoked]: 'Revoked',
  [ManagedServerState.WrongServer]: 'WrongServer'
};

export enum PairingFailure {
  None = 0,
  CodeRejected = 1,
  RequestRejected = 2,
  NotYetApproved = 3,
  TooManyAttempts = 4
}

export const pairingFailures = [
  { label: 'None', value: PairingFailure.None },
  { label: 'CodeRejected', value: PairingFailure.CodeRejected },
  { label: 'RequestRejected', value: PairingFailure.RequestRejected },
  { label: 'NotYetApproved', value: PairingFailure.NotYetApproved },
  { label: 'TooManyAttempts', value: PairingFailure.TooManyAttempts }
] as const;

export const PairingFailureLabel: Record<PairingFailure, string> = {
  [PairingFailure.None]: 'None',
  [PairingFailure.CodeRejected]: 'CodeRejected',
  [PairingFailure.RequestRejected]: 'RequestRejected',
  [PairingFailure.NotYetApproved]: 'NotYetApproved',
  [PairingFailure.TooManyAttempts]: 'TooManyAttempts'
};

export enum RemoteAccessDenialReason {
  None = 0,
  Disabled = 1,
  HostOnly = 2,
  PathNotServable = 3,
  TranscodeDisabled = 4,
  RunsOnUserMachine = 5,
  Unauthenticated = 6,
  SignatureExpired = 7,
  DeviceRevoked = 8
}

export const remoteAccessDenialReasons = [
  { label: 'None', value: RemoteAccessDenialReason.None },
  { label: 'Disabled', value: RemoteAccessDenialReason.Disabled },
  { label: 'HostOnly', value: RemoteAccessDenialReason.HostOnly },
  { label: 'PathNotServable', value: RemoteAccessDenialReason.PathNotServable },
  { label: 'TranscodeDisabled', value: RemoteAccessDenialReason.TranscodeDisabled },
  { label: 'RunsOnUserMachine', value: RemoteAccessDenialReason.RunsOnUserMachine },
  { label: 'Unauthenticated', value: RemoteAccessDenialReason.Unauthenticated },
  { label: 'SignatureExpired', value: RemoteAccessDenialReason.SignatureExpired },
  { label: 'DeviceRevoked', value: RemoteAccessDenialReason.DeviceRevoked }
] as const;

export const RemoteAccessDenialReasonLabel: Record<RemoteAccessDenialReason, string> = {
  [RemoteAccessDenialReason.None]: 'None',
  [RemoteAccessDenialReason.Disabled]: 'Disabled',
  [RemoteAccessDenialReason.HostOnly]: 'HostOnly',
  [RemoteAccessDenialReason.PathNotServable]: 'PathNotServable',
  [RemoteAccessDenialReason.TranscodeDisabled]: 'TranscodeDisabled',
  [RemoteAccessDenialReason.RunsOnUserMachine]: 'RunsOnUserMachine',
  [RemoteAccessDenialReason.Unauthenticated]: 'Unauthenticated',
  [RemoteAccessDenialReason.SignatureExpired]: 'SignatureExpired',
  [RemoteAccessDenialReason.DeviceRevoked]: 'DeviceRevoked'
};

export enum RemoteDevicePlatform {
  Unknown = 0,
  Windows = 1,
  MacOS = 2,
  Linux = 3,
  Android = 4,
  IOS = 5
}

export const remoteDevicePlatforms = [
  { label: 'Unknown', value: RemoteDevicePlatform.Unknown },
  { label: 'Windows', value: RemoteDevicePlatform.Windows },
  { label: 'MacOS', value: RemoteDevicePlatform.MacOS },
  { label: 'Linux', value: RemoteDevicePlatform.Linux },
  { label: 'Android', value: RemoteDevicePlatform.Android },
  { label: 'IOS', value: RemoteDevicePlatform.IOS }
] as const;

export const RemoteDevicePlatformLabel: Record<RemoteDevicePlatform, string> = {
  [RemoteDevicePlatform.Unknown]: 'Unknown',
  [RemoteDevicePlatform.Windows]: 'Windows',
  [RemoteDevicePlatform.MacOS]: 'MacOS',
  [RemoteDevicePlatform.Linux]: 'Linux',
  [RemoteDevicePlatform.Android]: 'Android',
  [RemoteDevicePlatform.IOS]: 'IOS'
};

export enum ServerKind {
  Unknown = 0,
  Desktop = 1,
  Headless = 2
}

export const serverKinds = [
  { label: 'Unknown', value: ServerKind.Unknown },
  { label: 'Desktop', value: ServerKind.Desktop },
  { label: 'Headless', value: ServerKind.Headless }
] as const;

export const ServerKindLabel: Record<ServerKind, string> = {
  [ServerKind.Unknown]: 'Unknown',
  [ServerKind.Desktop]: 'Desktop',
  [ServerKind.Headless]: 'Headless'
};

export enum DataSyncVvRelation {
  Equal = 1,
  Dominates = 2,
  DominatedBy = 3,
  Concurrent = 4
}

export const dataSyncVvRelations = [
  { label: 'Equal', value: DataSyncVvRelation.Equal },
  { label: 'Dominates', value: DataSyncVvRelation.Dominates },
  { label: 'DominatedBy', value: DataSyncVvRelation.DominatedBy },
  { label: 'Concurrent', value: DataSyncVvRelation.Concurrent }
] as const;

export const DataSyncVvRelationLabel: Record<DataSyncVvRelation, string> = {
  [DataSyncVvRelation.Equal]: 'Equal',
  [DataSyncVvRelation.Dominates]: 'Dominates',
  [DataSyncVvRelation.DominatedBy]: 'DominatedBy',
  [DataSyncVvRelation.Concurrent]: 'Concurrent'
};

export enum DataSyncInboxItemType {
  FieldConflict = 1,
  ChildRenameConflict = 2,
  TypeChange = 3,
  DeletedThere = 4,
  ChildDeletedInUse = 5,
  DeletedHereEditedThere = 6,
  LinkSuggestion = 7,
  IdentityConflict = 8,
  MassChildDeletion = 9,
  SuspectedLostUpdate = 10,
  LargeChange = 11
}

export const dataSyncInboxItemTypes = [
  { label: 'FieldConflict', value: DataSyncInboxItemType.FieldConflict },
  { label: 'ChildRenameConflict', value: DataSyncInboxItemType.ChildRenameConflict },
  { label: 'TypeChange', value: DataSyncInboxItemType.TypeChange },
  { label: 'DeletedThere', value: DataSyncInboxItemType.DeletedThere },
  { label: 'ChildDeletedInUse', value: DataSyncInboxItemType.ChildDeletedInUse },
  { label: 'DeletedHereEditedThere', value: DataSyncInboxItemType.DeletedHereEditedThere },
  { label: 'LinkSuggestion', value: DataSyncInboxItemType.LinkSuggestion },
  { label: 'IdentityConflict', value: DataSyncInboxItemType.IdentityConflict },
  { label: 'MassChildDeletion', value: DataSyncInboxItemType.MassChildDeletion },
  { label: 'SuspectedLostUpdate', value: DataSyncInboxItemType.SuspectedLostUpdate },
  { label: 'LargeChange', value: DataSyncInboxItemType.LargeChange }
] as const;

export const DataSyncInboxItemTypeLabel: Record<DataSyncInboxItemType, string> = {
  [DataSyncInboxItemType.FieldConflict]: 'FieldConflict',
  [DataSyncInboxItemType.ChildRenameConflict]: 'ChildRenameConflict',
  [DataSyncInboxItemType.TypeChange]: 'TypeChange',
  [DataSyncInboxItemType.DeletedThere]: 'DeletedThere',
  [DataSyncInboxItemType.ChildDeletedInUse]: 'ChildDeletedInUse',
  [DataSyncInboxItemType.DeletedHereEditedThere]: 'DeletedHereEditedThere',
  [DataSyncInboxItemType.LinkSuggestion]: 'LinkSuggestion',
  [DataSyncInboxItemType.IdentityConflict]: 'IdentityConflict',
  [DataSyncInboxItemType.MassChildDeletion]: 'MassChildDeletion',
  [DataSyncInboxItemType.SuspectedLostUpdate]: 'SuspectedLostUpdate',
  [DataSyncInboxItemType.LargeChange]: 'LargeChange'
};

export enum DataSyncInboxItemOrigin {
  Merger = 1,
  State = 2
}

export const dataSyncInboxItemOrigins = [
  { label: 'Merger', value: DataSyncInboxItemOrigin.Merger },
  { label: 'State', value: DataSyncInboxItemOrigin.State }
] as const;

export const DataSyncInboxItemOriginLabel: Record<DataSyncInboxItemOrigin, string> = {
  [DataSyncInboxItemOrigin.Merger]: 'Merger',
  [DataSyncInboxItemOrigin.State]: 'State'
};

export enum DataSyncInboxAction {
  KeepLocal = 1,
  UseRemote = 2,
  UseCustom = 3,
  Detach = 4,
  DeleteHere = 5,
  KeepHereOnly = 6,
  RestoreEverywhere = 7,
  RestoreHere = 8,
  KeepDeleted = 9,
  Link = 10,
  KeepBoth = 11,
  Convert = 12,
  ApplyAll = 13,
  ReviewEach = 14,
  KeepWithEntity = 15,
  Publish = 16,
  Reapply = 17,
  Skip = 18,
  KeepRecordLinked = 19
}

export const dataSyncInboxActions = [
  { label: 'KeepLocal', value: DataSyncInboxAction.KeepLocal },
  { label: 'UseRemote', value: DataSyncInboxAction.UseRemote },
  { label: 'UseCustom', value: DataSyncInboxAction.UseCustom },
  { label: 'Detach', value: DataSyncInboxAction.Detach },
  { label: 'DeleteHere', value: DataSyncInboxAction.DeleteHere },
  { label: 'KeepHereOnly', value: DataSyncInboxAction.KeepHereOnly },
  { label: 'RestoreEverywhere', value: DataSyncInboxAction.RestoreEverywhere },
  { label: 'RestoreHere', value: DataSyncInboxAction.RestoreHere },
  { label: 'KeepDeleted', value: DataSyncInboxAction.KeepDeleted },
  { label: 'Link', value: DataSyncInboxAction.Link },
  { label: 'KeepBoth', value: DataSyncInboxAction.KeepBoth },
  { label: 'Convert', value: DataSyncInboxAction.Convert },
  { label: 'ApplyAll', value: DataSyncInboxAction.ApplyAll },
  { label: 'ReviewEach', value: DataSyncInboxAction.ReviewEach },
  { label: 'KeepWithEntity', value: DataSyncInboxAction.KeepWithEntity },
  { label: 'Publish', value: DataSyncInboxAction.Publish },
  { label: 'Reapply', value: DataSyncInboxAction.Reapply },
  { label: 'Skip', value: DataSyncInboxAction.Skip },
  { label: 'KeepRecordLinked', value: DataSyncInboxAction.KeepRecordLinked }
] as const;

export const DataSyncInboxActionLabel: Record<DataSyncInboxAction, string> = {
  [DataSyncInboxAction.KeepLocal]: 'KeepLocal',
  [DataSyncInboxAction.UseRemote]: 'UseRemote',
  [DataSyncInboxAction.UseCustom]: 'UseCustom',
  [DataSyncInboxAction.Detach]: 'Detach',
  [DataSyncInboxAction.DeleteHere]: 'DeleteHere',
  [DataSyncInboxAction.KeepHereOnly]: 'KeepHereOnly',
  [DataSyncInboxAction.RestoreEverywhere]: 'RestoreEverywhere',
  [DataSyncInboxAction.RestoreHere]: 'RestoreHere',
  [DataSyncInboxAction.KeepDeleted]: 'KeepDeleted',
  [DataSyncInboxAction.Link]: 'Link',
  [DataSyncInboxAction.KeepBoth]: 'KeepBoth',
  [DataSyncInboxAction.Convert]: 'Convert',
  [DataSyncInboxAction.ApplyAll]: 'ApplyAll',
  [DataSyncInboxAction.ReviewEach]: 'ReviewEach',
  [DataSyncInboxAction.KeepWithEntity]: 'KeepWithEntity',
  [DataSyncInboxAction.Publish]: 'Publish',
  [DataSyncInboxAction.Reapply]: 'Reapply',
  [DataSyncInboxAction.Skip]: 'Skip',
  [DataSyncInboxAction.KeepRecordLinked]: 'KeepRecordLinked'
};

export enum DataSyncInboxClosure {
  ResolvedHere = 1,
  ResolvedElsewhere = 2,
  Superseded = 3,
  LinkRemoved = 4,
  LinkStopped = 5
}

export const dataSyncInboxClosures = [
  { label: 'ResolvedHere', value: DataSyncInboxClosure.ResolvedHere },
  { label: 'ResolvedElsewhere', value: DataSyncInboxClosure.ResolvedElsewhere },
  { label: 'Superseded', value: DataSyncInboxClosure.Superseded },
  { label: 'LinkRemoved', value: DataSyncInboxClosure.LinkRemoved },
  { label: 'LinkStopped', value: DataSyncInboxClosure.LinkStopped }
] as const;

export const DataSyncInboxClosureLabel: Record<DataSyncInboxClosure, string> = {
  [DataSyncInboxClosure.ResolvedHere]: 'ResolvedHere',
  [DataSyncInboxClosure.ResolvedElsewhere]: 'ResolvedElsewhere',
  [DataSyncInboxClosure.Superseded]: 'Superseded',
  [DataSyncInboxClosure.LinkRemoved]: 'LinkRemoved',
  [DataSyncInboxClosure.LinkStopped]: 'LinkStopped'
};

export enum DataSyncHistoryKind {
  FirstLink = 1,
  CopyOnce = 2,
  AutoSync = 3,
  Resolution = 4,
  Undo = 5,
  Restore = 6,
  EntitySetting = 7
}

export const dataSyncHistoryKinds = [
  { label: 'FirstLink', value: DataSyncHistoryKind.FirstLink },
  { label: 'CopyOnce', value: DataSyncHistoryKind.CopyOnce },
  { label: 'AutoSync', value: DataSyncHistoryKind.AutoSync },
  { label: 'Resolution', value: DataSyncHistoryKind.Resolution },
  { label: 'Undo', value: DataSyncHistoryKind.Undo },
  { label: 'Restore', value: DataSyncHistoryKind.Restore },
  { label: 'EntitySetting', value: DataSyncHistoryKind.EntitySetting }
] as const;

export const DataSyncHistoryKindLabel: Record<DataSyncHistoryKind, string> = {
  [DataSyncHistoryKind.FirstLink]: 'FirstLink',
  [DataSyncHistoryKind.CopyOnce]: 'CopyOnce',
  [DataSyncHistoryKind.AutoSync]: 'AutoSync',
  [DataSyncHistoryKind.Resolution]: 'Resolution',
  [DataSyncHistoryKind.Undo]: 'Undo',
  [DataSyncHistoryKind.Restore]: 'Restore',
  [DataSyncHistoryKind.EntitySetting]: 'EntitySetting'
};

export enum DataSyncEntitySyncState {
  Synced = 1,
  LocalOnly = 2,
  Detached = 3
}

export const dataSyncEntitySyncStates = [
  { label: 'Synced', value: DataSyncEntitySyncState.Synced },
  { label: 'LocalOnly', value: DataSyncEntitySyncState.LocalOnly },
  { label: 'Detached', value: DataSyncEntitySyncState.Detached }
] as const;

export const DataSyncEntitySyncStateLabel: Record<DataSyncEntitySyncState, string> = {
  [DataSyncEntitySyncState.Synced]: 'Synced',
  [DataSyncEntitySyncState.LocalOnly]: 'LocalOnly',
  [DataSyncEntitySyncState.Detached]: 'Detached'
};

export enum DataSyncTombstoneKind {
  Deleted = 1,
  UndoneCreate = 2
}

export const dataSyncTombstoneKinds = [
  { label: 'Deleted', value: DataSyncTombstoneKind.Deleted },
  { label: 'UndoneCreate', value: DataSyncTombstoneKind.UndoneCreate }
] as const;

export const DataSyncTombstoneKindLabel: Record<DataSyncTombstoneKind, string> = {
  [DataSyncTombstoneKind.Deleted]: 'Deleted',
  [DataSyncTombstoneKind.UndoneCreate]: 'UndoneCreate'
};

export enum DataSyncBaseState {
  Normal = 1,
  Excluded = 2,
  MissingAtPeer = 3,
  Held = 4,
  Unbound = 5
}

export const dataSyncBaseStates = [
  { label: 'Normal', value: DataSyncBaseState.Normal },
  { label: 'Excluded', value: DataSyncBaseState.Excluded },
  { label: 'MissingAtPeer', value: DataSyncBaseState.MissingAtPeer },
  { label: 'Held', value: DataSyncBaseState.Held },
  { label: 'Unbound', value: DataSyncBaseState.Unbound }
] as const;

export const DataSyncBaseStateLabel: Record<DataSyncBaseState, string> = {
  [DataSyncBaseState.Normal]: 'Normal',
  [DataSyncBaseState.Excluded]: 'Excluded',
  [DataSyncBaseState.MissingAtPeer]: 'MissingAtPeer',
  [DataSyncBaseState.Held]: 'Held',
  [DataSyncBaseState.Unbound]: 'Unbound'
};

export enum DataSyncExclusionReason {
  Skipped = 1,
  Undone = 2,
  NotSyncedHere = 3,
  DroppedIdentity = 4
}

export const dataSyncExclusionReasons = [
  { label: 'Skipped', value: DataSyncExclusionReason.Skipped },
  { label: 'Undone', value: DataSyncExclusionReason.Undone },
  { label: 'NotSyncedHere', value: DataSyncExclusionReason.NotSyncedHere },
  { label: 'DroppedIdentity', value: DataSyncExclusionReason.DroppedIdentity }
] as const;

export const DataSyncExclusionReasonLabel: Record<DataSyncExclusionReason, string> = {
  [DataSyncExclusionReason.Skipped]: 'Skipped',
  [DataSyncExclusionReason.Undone]: 'Undone',
  [DataSyncExclusionReason.NotSyncedHere]: 'NotSyncedHere',
  [DataSyncExclusionReason.DroppedIdentity]: 'DroppedIdentity'
};

export enum DataSyncPendingReason {
  Conflict = 1,
  TypeChange = 2,
  MassChildDeletion = 3,
  LargeChange = 4,
  Retry = 5,
  Held = 6,
  AwaitingDecision = 7,
  OverBudget = 8,
  PublishHeld = 9,
  IdentityConflict = 10
}

export const dataSyncPendingReasons = [
  { label: 'Conflict', value: DataSyncPendingReason.Conflict },
  { label: 'TypeChange', value: DataSyncPendingReason.TypeChange },
  { label: 'MassChildDeletion', value: DataSyncPendingReason.MassChildDeletion },
  { label: 'LargeChange', value: DataSyncPendingReason.LargeChange },
  { label: 'Retry', value: DataSyncPendingReason.Retry },
  { label: 'Held', value: DataSyncPendingReason.Held },
  { label: 'AwaitingDecision', value: DataSyncPendingReason.AwaitingDecision },
  { label: 'OverBudget', value: DataSyncPendingReason.OverBudget },
  { label: 'PublishHeld', value: DataSyncPendingReason.PublishHeld },
  { label: 'IdentityConflict', value: DataSyncPendingReason.IdentityConflict }
] as const;

export const DataSyncPendingReasonLabel: Record<DataSyncPendingReason, string> = {
  [DataSyncPendingReason.Conflict]: 'Conflict',
  [DataSyncPendingReason.TypeChange]: 'TypeChange',
  [DataSyncPendingReason.MassChildDeletion]: 'MassChildDeletion',
  [DataSyncPendingReason.LargeChange]: 'LargeChange',
  [DataSyncPendingReason.Retry]: 'Retry',
  [DataSyncPendingReason.Held]: 'Held',
  [DataSyncPendingReason.AwaitingDecision]: 'AwaitingDecision',
  [DataSyncPendingReason.OverBudget]: 'OverBudget',
  [DataSyncPendingReason.PublishHeld]: 'PublishHeld',
  [DataSyncPendingReason.IdentityConflict]: 'IdentityConflict'
};

export enum DataSyncRevisionKind {
  LocalEdit = 1,
  Create = 2,
  FastForward = 3,
  MergedNoConflict = 4,
  MergedWithConflicts = 5,
  Resolution = 6,
  LocalDelete = 7,
  AcceptRemoteDelete = 8,
  KeepDeleted = 9,
  Undo = 10,
  RestoreWins = 11,
  FollowMerged = 12,
  Revive = 13,
  Retire = 14
}

export const dataSyncRevisionKinds = [
  { label: 'LocalEdit', value: DataSyncRevisionKind.LocalEdit },
  { label: 'Create', value: DataSyncRevisionKind.Create },
  { label: 'FastForward', value: DataSyncRevisionKind.FastForward },
  { label: 'MergedNoConflict', value: DataSyncRevisionKind.MergedNoConflict },
  { label: 'MergedWithConflicts', value: DataSyncRevisionKind.MergedWithConflicts },
  { label: 'Resolution', value: DataSyncRevisionKind.Resolution },
  { label: 'LocalDelete', value: DataSyncRevisionKind.LocalDelete },
  { label: 'AcceptRemoteDelete', value: DataSyncRevisionKind.AcceptRemoteDelete },
  { label: 'KeepDeleted', value: DataSyncRevisionKind.KeepDeleted },
  { label: 'Undo', value: DataSyncRevisionKind.Undo },
  { label: 'RestoreWins', value: DataSyncRevisionKind.RestoreWins },
  { label: 'FollowMerged', value: DataSyncRevisionKind.FollowMerged },
  { label: 'Revive', value: DataSyncRevisionKind.Revive },
  { label: 'Retire', value: DataSyncRevisionKind.Retire }
] as const;

export const DataSyncRevisionKindLabel: Record<DataSyncRevisionKind, string> = {
  [DataSyncRevisionKind.LocalEdit]: 'LocalEdit',
  [DataSyncRevisionKind.Create]: 'Create',
  [DataSyncRevisionKind.FastForward]: 'FastForward',
  [DataSyncRevisionKind.MergedNoConflict]: 'MergedNoConflict',
  [DataSyncRevisionKind.MergedWithConflicts]: 'MergedWithConflicts',
  [DataSyncRevisionKind.Resolution]: 'Resolution',
  [DataSyncRevisionKind.LocalDelete]: 'LocalDelete',
  [DataSyncRevisionKind.AcceptRemoteDelete]: 'AcceptRemoteDelete',
  [DataSyncRevisionKind.KeepDeleted]: 'KeepDeleted',
  [DataSyncRevisionKind.Undo]: 'Undo',
  [DataSyncRevisionKind.RestoreWins]: 'RestoreWins',
  [DataSyncRevisionKind.FollowMerged]: 'FollowMerged',
  [DataSyncRevisionKind.Revive]: 'Revive',
  [DataSyncRevisionKind.Retire]: 'Retire'
};

export enum DataSyncFieldResolution {
  Unchanged = 1,
  TookRemote = 2,
  KeptLocal = 3,
  Combined = 4,
  Conflict = 5,
  AppearanceTookRemote = 6,
  AppearanceKeptLocal = 7,
  FollowTookRemote = 8,
  DeletionHeldInUse = 9,
  EditWinsRestored = 10,
  TypeChangeHeld = 11
}

export const dataSyncFieldResolutions = [
  { label: 'Unchanged', value: DataSyncFieldResolution.Unchanged },
  { label: 'TookRemote', value: DataSyncFieldResolution.TookRemote },
  { label: 'KeptLocal', value: DataSyncFieldResolution.KeptLocal },
  { label: 'Combined', value: DataSyncFieldResolution.Combined },
  { label: 'Conflict', value: DataSyncFieldResolution.Conflict },
  { label: 'AppearanceTookRemote', value: DataSyncFieldResolution.AppearanceTookRemote },
  { label: 'AppearanceKeptLocal', value: DataSyncFieldResolution.AppearanceKeptLocal },
  { label: 'FollowTookRemote', value: DataSyncFieldResolution.FollowTookRemote },
  { label: 'DeletionHeldInUse', value: DataSyncFieldResolution.DeletionHeldInUse },
  { label: 'EditWinsRestored', value: DataSyncFieldResolution.EditWinsRestored },
  { label: 'TypeChangeHeld', value: DataSyncFieldResolution.TypeChangeHeld }
] as const;

export const DataSyncFieldResolutionLabel: Record<DataSyncFieldResolution, string> = {
  [DataSyncFieldResolution.Unchanged]: 'Unchanged',
  [DataSyncFieldResolution.TookRemote]: 'TookRemote',
  [DataSyncFieldResolution.KeptLocal]: 'KeptLocal',
  [DataSyncFieldResolution.Combined]: 'Combined',
  [DataSyncFieldResolution.Conflict]: 'Conflict',
  [DataSyncFieldResolution.AppearanceTookRemote]: 'AppearanceTookRemote',
  [DataSyncFieldResolution.AppearanceKeptLocal]: 'AppearanceKeptLocal',
  [DataSyncFieldResolution.FollowTookRemote]: 'FollowTookRemote',
  [DataSyncFieldResolution.DeletionHeldInUse]: 'DeletionHeldInUse',
  [DataSyncFieldResolution.EditWinsRestored]: 'EditWinsRestored',
  [DataSyncFieldResolution.TypeChangeHeld]: 'TypeChangeHeld'
};

export enum DataSyncMergeSide {
  Local = 1,
  Remote = 2
}

export const dataSyncMergeSides = [
  { label: 'Local', value: DataSyncMergeSide.Local },
  { label: 'Remote', value: DataSyncMergeSide.Remote }
] as const;

export const DataSyncMergeSideLabel: Record<DataSyncMergeSide, string> = {
  [DataSyncMergeSide.Local]: 'Local',
  [DataSyncMergeSide.Remote]: 'Remote'
};

export enum DataSyncMerge3Mode {
  ThreeWay = 1,
  FastForward = 2,
  NoBase = 3,
  Convert = 4
}

export const dataSyncMerge3Modes = [
  { label: 'ThreeWay', value: DataSyncMerge3Mode.ThreeWay },
  { label: 'FastForward', value: DataSyncMerge3Mode.FastForward },
  { label: 'NoBase', value: DataSyncMerge3Mode.NoBase },
  { label: 'Convert', value: DataSyncMerge3Mode.Convert }
] as const;

export const DataSyncMerge3ModeLabel: Record<DataSyncMerge3Mode, string> = {
  [DataSyncMerge3Mode.ThreeWay]: 'ThreeWay',
  [DataSyncMerge3Mode.FastForward]: 'FastForward',
  [DataSyncMerge3Mode.NoBase]: 'NoBase',
  [DataSyncMerge3Mode.Convert]: 'Convert'
};

export enum DataSyncLinkMode {
  Off = 0,
  Follow = 1,
  TwoWay = 2
}

export const dataSyncLinkModes = [
  { label: 'Off', value: DataSyncLinkMode.Off },
  { label: 'Follow', value: DataSyncLinkMode.Follow },
  { label: 'TwoWay', value: DataSyncLinkMode.TwoWay }
] as const;

export const DataSyncLinkModeLabel: Record<DataSyncLinkMode, string> = {
  [DataSyncLinkMode.Off]: 'Off',
  [DataSyncLinkMode.Follow]: 'Follow',
  [DataSyncLinkMode.TwoWay]: 'TwoWay'
};

export enum DataSyncLinkState {
  Active = 1,
  AwaitingAccess = 2,
  AwaitingReview = 3,
  WaitingForPeerReview = 4,
  Paused = 5,
  Stopped = 6,
  PeerTooOld = 7,
  ThisTooOld = 8,
  AccessRevoked = 9,
  PeerSharingOff = 10,
  PeerRemoteAccessOff = 11
}

export const dataSyncLinkStates = [
  { label: 'Active', value: DataSyncLinkState.Active },
  { label: 'AwaitingAccess', value: DataSyncLinkState.AwaitingAccess },
  { label: 'AwaitingReview', value: DataSyncLinkState.AwaitingReview },
  { label: 'WaitingForPeerReview', value: DataSyncLinkState.WaitingForPeerReview },
  { label: 'Paused', value: DataSyncLinkState.Paused },
  { label: 'Stopped', value: DataSyncLinkState.Stopped },
  { label: 'PeerTooOld', value: DataSyncLinkState.PeerTooOld },
  { label: 'ThisTooOld', value: DataSyncLinkState.ThisTooOld },
  { label: 'AccessRevoked', value: DataSyncLinkState.AccessRevoked },
  { label: 'PeerSharingOff', value: DataSyncLinkState.PeerSharingOff },
  { label: 'PeerRemoteAccessOff', value: DataSyncLinkState.PeerRemoteAccessOff }
] as const;

export const DataSyncLinkStateLabel: Record<DataSyncLinkState, string> = {
  [DataSyncLinkState.Active]: 'Active',
  [DataSyncLinkState.AwaitingAccess]: 'AwaitingAccess',
  [DataSyncLinkState.AwaitingReview]: 'AwaitingReview',
  [DataSyncLinkState.WaitingForPeerReview]: 'WaitingForPeerReview',
  [DataSyncLinkState.Paused]: 'Paused',
  [DataSyncLinkState.Stopped]: 'Stopped',
  [DataSyncLinkState.PeerTooOld]: 'PeerTooOld',
  [DataSyncLinkState.ThisTooOld]: 'ThisTooOld',
  [DataSyncLinkState.AccessRevoked]: 'AccessRevoked',
  [DataSyncLinkState.PeerSharingOff]: 'PeerSharingOff',
  [DataSyncLinkState.PeerRemoteAccessOff]: 'PeerRemoteAccessOff'
};

export enum DataSyncPauseReason {
  ByUser = 1,
  AllPaused = 2,
  PeerReset = 3,
  PeerIdentityDuplicated = 4,
  MassDeletion = 5,
  KindEmptied = 6,
  LocalRestoreDetected = 7,
  TooManyDecisions = 8,
  LocalRestoreSuspected = 9
}

export const dataSyncPauseReasons = [
  { label: 'ByUser', value: DataSyncPauseReason.ByUser },
  { label: 'AllPaused', value: DataSyncPauseReason.AllPaused },
  { label: 'PeerReset', value: DataSyncPauseReason.PeerReset },
  { label: 'PeerIdentityDuplicated', value: DataSyncPauseReason.PeerIdentityDuplicated },
  { label: 'MassDeletion', value: DataSyncPauseReason.MassDeletion },
  { label: 'KindEmptied', value: DataSyncPauseReason.KindEmptied },
  { label: 'LocalRestoreDetected', value: DataSyncPauseReason.LocalRestoreDetected },
  { label: 'TooManyDecisions', value: DataSyncPauseReason.TooManyDecisions },
  { label: 'LocalRestoreSuspected', value: DataSyncPauseReason.LocalRestoreSuspected }
] as const;

export const DataSyncPauseReasonLabel: Record<DataSyncPauseReason, string> = {
  [DataSyncPauseReason.ByUser]: 'ByUser',
  [DataSyncPauseReason.AllPaused]: 'AllPaused',
  [DataSyncPauseReason.PeerReset]: 'PeerReset',
  [DataSyncPauseReason.PeerIdentityDuplicated]: 'PeerIdentityDuplicated',
  [DataSyncPauseReason.MassDeletion]: 'MassDeletion',
  [DataSyncPauseReason.KindEmptied]: 'KindEmptied',
  [DataSyncPauseReason.LocalRestoreDetected]: 'LocalRestoreDetected',
  [DataSyncPauseReason.TooManyDecisions]: 'TooManyDecisions',
  [DataSyncPauseReason.LocalRestoreSuspected]: 'LocalRestoreSuspected'
};

export enum DataSyncLinkInitiator {
  ThisDevice = 1,
  Peer = 2
}

export const dataSyncLinkInitiators = [
  { label: 'ThisDevice', value: DataSyncLinkInitiator.ThisDevice },
  { label: 'Peer', value: DataSyncLinkInitiator.Peer }
] as const;

export const DataSyncLinkInitiatorLabel: Record<DataSyncLinkInitiator, string> = {
  [DataSyncLinkInitiator.ThisDevice]: 'ThisDevice',
  [DataSyncLinkInitiator.Peer]: 'Peer'
};

export enum DataSyncRequestIntent {
  Follow = 1,
  TwoWay = 2
}

export const dataSyncRequestIntents = [
  { label: 'Follow', value: DataSyncRequestIntent.Follow },
  { label: 'TwoWay', value: DataSyncRequestIntent.TwoWay }
] as const;

export const DataSyncRequestIntentLabel: Record<DataSyncRequestIntent, string> = {
  [DataSyncRequestIntent.Follow]: 'Follow',
  [DataSyncRequestIntent.TwoWay]: 'TwoWay'
};

export enum DataSyncRequestDirection {
  Incoming = 1,
  Outgoing = 2
}

export const dataSyncRequestDirections = [
  { label: 'Incoming', value: DataSyncRequestDirection.Incoming },
  { label: 'Outgoing', value: DataSyncRequestDirection.Outgoing }
] as const;

export const DataSyncRequestDirectionLabel: Record<DataSyncRequestDirection, string> = {
  [DataSyncRequestDirection.Incoming]: 'Incoming',
  [DataSyncRequestDirection.Outgoing]: 'Outgoing'
};

export enum DataSyncPeerErrorCode {
  Unreachable = 1,
  AccessMissing = 2,
  AccessRevoked = 3,
  PeerSharingOff = 4,
  PeerReset = 5,
  PeerTooOld = 6,
  ThisTooOld = 7,
  CursorSuperseded = 8,
  SnapshotExpired = 9,
  Busy = 10,
  InvalidResponse = 11,
  TooLarge = 12,
  IdentityConflict = 13,
  PeerRemoteAccessOff = 14,
  PeerRestorePending = 15
}

export const dataSyncPeerErrorCodes = [
  { label: 'Unreachable', value: DataSyncPeerErrorCode.Unreachable },
  { label: 'AccessMissing', value: DataSyncPeerErrorCode.AccessMissing },
  { label: 'AccessRevoked', value: DataSyncPeerErrorCode.AccessRevoked },
  { label: 'PeerSharingOff', value: DataSyncPeerErrorCode.PeerSharingOff },
  { label: 'PeerReset', value: DataSyncPeerErrorCode.PeerReset },
  { label: 'PeerTooOld', value: DataSyncPeerErrorCode.PeerTooOld },
  { label: 'ThisTooOld', value: DataSyncPeerErrorCode.ThisTooOld },
  { label: 'CursorSuperseded', value: DataSyncPeerErrorCode.CursorSuperseded },
  { label: 'SnapshotExpired', value: DataSyncPeerErrorCode.SnapshotExpired },
  { label: 'Busy', value: DataSyncPeerErrorCode.Busy },
  { label: 'InvalidResponse', value: DataSyncPeerErrorCode.InvalidResponse },
  { label: 'TooLarge', value: DataSyncPeerErrorCode.TooLarge },
  { label: 'IdentityConflict', value: DataSyncPeerErrorCode.IdentityConflict },
  { label: 'PeerRemoteAccessOff', value: DataSyncPeerErrorCode.PeerRemoteAccessOff },
  { label: 'PeerRestorePending', value: DataSyncPeerErrorCode.PeerRestorePending }
] as const;

export const DataSyncPeerErrorCodeLabel: Record<DataSyncPeerErrorCode, string> = {
  [DataSyncPeerErrorCode.Unreachable]: 'Unreachable',
  [DataSyncPeerErrorCode.AccessMissing]: 'AccessMissing',
  [DataSyncPeerErrorCode.AccessRevoked]: 'AccessRevoked',
  [DataSyncPeerErrorCode.PeerSharingOff]: 'PeerSharingOff',
  [DataSyncPeerErrorCode.PeerReset]: 'PeerReset',
  [DataSyncPeerErrorCode.PeerTooOld]: 'PeerTooOld',
  [DataSyncPeerErrorCode.ThisTooOld]: 'ThisTooOld',
  [DataSyncPeerErrorCode.CursorSuperseded]: 'CursorSuperseded',
  [DataSyncPeerErrorCode.SnapshotExpired]: 'SnapshotExpired',
  [DataSyncPeerErrorCode.Busy]: 'Busy',
  [DataSyncPeerErrorCode.InvalidResponse]: 'InvalidResponse',
  [DataSyncPeerErrorCode.TooLarge]: 'TooLarge',
  [DataSyncPeerErrorCode.IdentityConflict]: 'IdentityConflict',
  [DataSyncPeerErrorCode.PeerRemoteAccessOff]: 'PeerRemoteAccessOff',
  [DataSyncPeerErrorCode.PeerRestorePending]: 'PeerRestorePending'
};

export enum DataSyncStatusLevel {
  Off = 0,
  InStep = 1,
  Syncing = 2,
  NeedsYou = 3,
  Paused = 4,
  Offline = 5,
  Failed = 6,
  UpdateNeeded = 7
}

export const dataSyncStatusLevels = [
  { label: 'Off', value: DataSyncStatusLevel.Off },
  { label: 'InStep', value: DataSyncStatusLevel.InStep },
  { label: 'Syncing', value: DataSyncStatusLevel.Syncing },
  { label: 'NeedsYou', value: DataSyncStatusLevel.NeedsYou },
  { label: 'Paused', value: DataSyncStatusLevel.Paused },
  { label: 'Offline', value: DataSyncStatusLevel.Offline },
  { label: 'Failed', value: DataSyncStatusLevel.Failed },
  { label: 'UpdateNeeded', value: DataSyncStatusLevel.UpdateNeeded }
] as const;

export const DataSyncStatusLevelLabel: Record<DataSyncStatusLevel, string> = {
  [DataSyncStatusLevel.Off]: 'Off',
  [DataSyncStatusLevel.InStep]: 'InStep',
  [DataSyncStatusLevel.Syncing]: 'Syncing',
  [DataSyncStatusLevel.NeedsYou]: 'NeedsYou',
  [DataSyncStatusLevel.Paused]: 'Paused',
  [DataSyncStatusLevel.Offline]: 'Offline',
  [DataSyncStatusLevel.Failed]: 'Failed',
  [DataSyncStatusLevel.UpdateNeeded]: 'UpdateNeeded'
};

export enum DataSyncReviewState {
  Staged = 1,
  Applying = 2,
  Applied = 3,
  Failed = 4
}

export const dataSyncReviewStates = [
  { label: 'Staged', value: DataSyncReviewState.Staged },
  { label: 'Applying', value: DataSyncReviewState.Applying },
  { label: 'Applied', value: DataSyncReviewState.Applied },
  { label: 'Failed', value: DataSyncReviewState.Failed }
] as const;

export const DataSyncReviewStateLabel: Record<DataSyncReviewState, string> = {
  [DataSyncReviewState.Staged]: 'Staged',
  [DataSyncReviewState.Applying]: 'Applying',
  [DataSyncReviewState.Applied]: 'Applied',
  [DataSyncReviewState.Failed]: 'Failed'
};

export enum DataSyncRestoreChoice {
  ThisDeviceWins = 1,
  OthersWin = 2
}

export const dataSyncRestoreChoices = [
  { label: 'ThisDeviceWins', value: DataSyncRestoreChoice.ThisDeviceWins },
  { label: 'OthersWin', value: DataSyncRestoreChoice.OthersWin }
] as const;

export const DataSyncRestoreChoiceLabel: Record<DataSyncRestoreChoice, string> = {
  [DataSyncRestoreChoice.ThisDeviceWins]: 'ThisDeviceWins',
  [DataSyncRestoreChoice.OthersWin]: 'OthersWin'
};

export enum DataSyncUndoState {
  Available = 1,
  Undone = 2,
  Expired = 3
}

export const dataSyncUndoStates = [
  { label: 'Available', value: DataSyncUndoState.Available },
  { label: 'Undone', value: DataSyncUndoState.Undone },
  { label: 'Expired', value: DataSyncUndoState.Expired }
] as const;

export const DataSyncUndoStateLabel: Record<DataSyncUndoState, string> = {
  [DataSyncUndoState.Available]: 'Available',
  [DataSyncUndoState.Undone]: 'Undone',
  [DataSyncUndoState.Expired]: 'Expired'
};

export enum DataSyncUndoAction {
  Remove = 1,
  Revert = 2,
  RemoveAliases = 3,
  Recreate = 4,
  Exclude = 5
}

export const dataSyncUndoActions = [
  { label: 'Remove', value: DataSyncUndoAction.Remove },
  { label: 'Revert', value: DataSyncUndoAction.Revert },
  { label: 'RemoveAliases', value: DataSyncUndoAction.RemoveAliases },
  { label: 'Recreate', value: DataSyncUndoAction.Recreate },
  { label: 'Exclude', value: DataSyncUndoAction.Exclude }
] as const;

export const DataSyncUndoActionLabel: Record<DataSyncUndoAction, string> = {
  [DataSyncUndoAction.Remove]: 'Remove',
  [DataSyncUndoAction.Revert]: 'Revert',
  [DataSyncUndoAction.RemoveAliases]: 'RemoveAliases',
  [DataSyncUndoAction.Recreate]: 'Recreate',
  [DataSyncUndoAction.Exclude]: 'Exclude'
};

export enum DataSyncUndoBlock {
  ChangedSinceImport = 1,
  InUse = 2,
  AddedOptionsInUse = 3,
  Missing = 4
}

export const dataSyncUndoBlocks = [
  { label: 'ChangedSinceImport', value: DataSyncUndoBlock.ChangedSinceImport },
  { label: 'InUse', value: DataSyncUndoBlock.InUse },
  { label: 'AddedOptionsInUse', value: DataSyncUndoBlock.AddedOptionsInUse },
  { label: 'Missing', value: DataSyncUndoBlock.Missing }
] as const;

export const DataSyncUndoBlockLabel: Record<DataSyncUndoBlock, string> = {
  [DataSyncUndoBlock.ChangedSinceImport]: 'ChangedSinceImport',
  [DataSyncUndoBlock.InUse]: 'InUse',
  [DataSyncUndoBlock.AddedOptionsInUse]: 'AddedOptionsInUse',
  [DataSyncUndoBlock.Missing]: 'Missing'
};

export enum DataSyncProblemCode {
  Busy = 1,
  ApplyInProgress = 2,
  DecisionsInvalid = 3,
  PlanChanged = 4,
  UnknownItem = 5,
  UnknownKind = 6,
  ReviewExpired = 7,
  NothingToReview = 8,
  UndoNotAvailable = 9,
  PeerUnreachable = 10,
  AccessMissing = 11,
  AccessRevoked = 12,
  PeerSharingOff = 13,
  PeerTooOld = 14,
  ThisTooOld = 15,
  PeerReset = 16,
  LinkNotFound = 17,
  LinkExists = 18,
  SharingOff = 19,
  RequestNotFound = 20,
  InvitationInvalid = 21,
  InboxItemChanged = 22,
  InboxItemClosed = 23,
  BackupFailed = 24,
  NothingSelected = 25,
  NotAllowedOnThisDevice = 26,
  ResolveTogether = 27,
  RemoteAccessOff = 28
}

export const dataSyncProblemCodes = [
  { label: 'Busy', value: DataSyncProblemCode.Busy },
  { label: 'ApplyInProgress', value: DataSyncProblemCode.ApplyInProgress },
  { label: 'DecisionsInvalid', value: DataSyncProblemCode.DecisionsInvalid },
  { label: 'PlanChanged', value: DataSyncProblemCode.PlanChanged },
  { label: 'UnknownItem', value: DataSyncProblemCode.UnknownItem },
  { label: 'UnknownKind', value: DataSyncProblemCode.UnknownKind },
  { label: 'ReviewExpired', value: DataSyncProblemCode.ReviewExpired },
  { label: 'NothingToReview', value: DataSyncProblemCode.NothingToReview },
  { label: 'UndoNotAvailable', value: DataSyncProblemCode.UndoNotAvailable },
  { label: 'PeerUnreachable', value: DataSyncProblemCode.PeerUnreachable },
  { label: 'AccessMissing', value: DataSyncProblemCode.AccessMissing },
  { label: 'AccessRevoked', value: DataSyncProblemCode.AccessRevoked },
  { label: 'PeerSharingOff', value: DataSyncProblemCode.PeerSharingOff },
  { label: 'PeerTooOld', value: DataSyncProblemCode.PeerTooOld },
  { label: 'ThisTooOld', value: DataSyncProblemCode.ThisTooOld },
  { label: 'PeerReset', value: DataSyncProblemCode.PeerReset },
  { label: 'LinkNotFound', value: DataSyncProblemCode.LinkNotFound },
  { label: 'LinkExists', value: DataSyncProblemCode.LinkExists },
  { label: 'SharingOff', value: DataSyncProblemCode.SharingOff },
  { label: 'RequestNotFound', value: DataSyncProblemCode.RequestNotFound },
  { label: 'InvitationInvalid', value: DataSyncProblemCode.InvitationInvalid },
  { label: 'InboxItemChanged', value: DataSyncProblemCode.InboxItemChanged },
  { label: 'InboxItemClosed', value: DataSyncProblemCode.InboxItemClosed },
  { label: 'BackupFailed', value: DataSyncProblemCode.BackupFailed },
  { label: 'NothingSelected', value: DataSyncProblemCode.NothingSelected },
  { label: 'NotAllowedOnThisDevice', value: DataSyncProblemCode.NotAllowedOnThisDevice },
  { label: 'ResolveTogether', value: DataSyncProblemCode.ResolveTogether },
  { label: 'RemoteAccessOff', value: DataSyncProblemCode.RemoteAccessOff }
] as const;

export const DataSyncProblemCodeLabel: Record<DataSyncProblemCode, string> = {
  [DataSyncProblemCode.Busy]: 'Busy',
  [DataSyncProblemCode.ApplyInProgress]: 'ApplyInProgress',
  [DataSyncProblemCode.DecisionsInvalid]: 'DecisionsInvalid',
  [DataSyncProblemCode.PlanChanged]: 'PlanChanged',
  [DataSyncProblemCode.UnknownItem]: 'UnknownItem',
  [DataSyncProblemCode.UnknownKind]: 'UnknownKind',
  [DataSyncProblemCode.ReviewExpired]: 'ReviewExpired',
  [DataSyncProblemCode.NothingToReview]: 'NothingToReview',
  [DataSyncProblemCode.UndoNotAvailable]: 'UndoNotAvailable',
  [DataSyncProblemCode.PeerUnreachable]: 'PeerUnreachable',
  [DataSyncProblemCode.AccessMissing]: 'AccessMissing',
  [DataSyncProblemCode.AccessRevoked]: 'AccessRevoked',
  [DataSyncProblemCode.PeerSharingOff]: 'PeerSharingOff',
  [DataSyncProblemCode.PeerTooOld]: 'PeerTooOld',
  [DataSyncProblemCode.ThisTooOld]: 'ThisTooOld',
  [DataSyncProblemCode.PeerReset]: 'PeerReset',
  [DataSyncProblemCode.LinkNotFound]: 'LinkNotFound',
  [DataSyncProblemCode.LinkExists]: 'LinkExists',
  [DataSyncProblemCode.SharingOff]: 'SharingOff',
  [DataSyncProblemCode.RequestNotFound]: 'RequestNotFound',
  [DataSyncProblemCode.InvitationInvalid]: 'InvitationInvalid',
  [DataSyncProblemCode.InboxItemChanged]: 'InboxItemChanged',
  [DataSyncProblemCode.InboxItemClosed]: 'InboxItemClosed',
  [DataSyncProblemCode.BackupFailed]: 'BackupFailed',
  [DataSyncProblemCode.NothingSelected]: 'NothingSelected',
  [DataSyncProblemCode.NotAllowedOnThisDevice]: 'NotAllowedOnThisDevice',
  [DataSyncProblemCode.ResolveTogether]: 'ResolveTogether',
  [DataSyncProblemCode.RemoteAccessOff]: 'RemoteAccessOff'
};

export enum DataSyncResumeAction {
  Resume = 1,
  ReviewDeletions = 2,
  ApplyAsUsual = 3,
  AskAccessAgain = 4,
  ThisDeviceWins = 5,
  TakeTheirs = 6,
  StartAnyway = 7
}

export const dataSyncResumeActions = [
  { label: 'Resume', value: DataSyncResumeAction.Resume },
  { label: 'ReviewDeletions', value: DataSyncResumeAction.ReviewDeletions },
  { label: 'ApplyAsUsual', value: DataSyncResumeAction.ApplyAsUsual },
  { label: 'AskAccessAgain', value: DataSyncResumeAction.AskAccessAgain },
  { label: 'ThisDeviceWins', value: DataSyncResumeAction.ThisDeviceWins },
  { label: 'TakeTheirs', value: DataSyncResumeAction.TakeTheirs },
  { label: 'StartAnyway', value: DataSyncResumeAction.StartAnyway }
] as const;

export const DataSyncResumeActionLabel: Record<DataSyncResumeAction, string> = {
  [DataSyncResumeAction.Resume]: 'Resume',
  [DataSyncResumeAction.ReviewDeletions]: 'ReviewDeletions',
  [DataSyncResumeAction.ApplyAsUsual]: 'ApplyAsUsual',
  [DataSyncResumeAction.AskAccessAgain]: 'AskAccessAgain',
  [DataSyncResumeAction.ThisDeviceWins]: 'ThisDeviceWins',
  [DataSyncResumeAction.TakeTheirs]: 'TakeTheirs',
  [DataSyncResumeAction.StartAnyway]: 'StartAnyway'
};

export enum DataSyncPlanItemType {
  Create = 1,
  Update = 2,
  Unchanged = 3,
  Link = 4,
  NeedsDecision = 5,
  Held = 6
}

export const dataSyncPlanItemTypes = [
  { label: 'Create', value: DataSyncPlanItemType.Create },
  { label: 'Update', value: DataSyncPlanItemType.Update },
  { label: 'Unchanged', value: DataSyncPlanItemType.Unchanged },
  { label: 'Link', value: DataSyncPlanItemType.Link },
  { label: 'NeedsDecision', value: DataSyncPlanItemType.NeedsDecision },
  { label: 'Held', value: DataSyncPlanItemType.Held }
] as const;

export const DataSyncPlanItemTypeLabel: Record<DataSyncPlanItemType, string> = {
  [DataSyncPlanItemType.Create]: 'Create',
  [DataSyncPlanItemType.Update]: 'Update',
  [DataSyncPlanItemType.Unchanged]: 'Unchanged',
  [DataSyncPlanItemType.Link]: 'Link',
  [DataSyncPlanItemType.NeedsDecision]: 'NeedsDecision',
  [DataSyncPlanItemType.Held]: 'Held'
};

export enum DataSyncPlanItemReason {
  AmbiguousNameMatch = 1,
  TypeMismatch = 2,
  NameClashDifferentType = 3,
  IdentityConflict = 4,
  DuplicateInPackage = 5,
  LocalIsNewer = 6
}

export const dataSyncPlanItemReasons = [
  { label: 'AmbiguousNameMatch', value: DataSyncPlanItemReason.AmbiguousNameMatch },
  { label: 'TypeMismatch', value: DataSyncPlanItemReason.TypeMismatch },
  { label: 'NameClashDifferentType', value: DataSyncPlanItemReason.NameClashDifferentType },
  { label: 'IdentityConflict', value: DataSyncPlanItemReason.IdentityConflict },
  { label: 'DuplicateInPackage', value: DataSyncPlanItemReason.DuplicateInPackage },
  { label: 'LocalIsNewer', value: DataSyncPlanItemReason.LocalIsNewer }
] as const;

export const DataSyncPlanItemReasonLabel: Record<DataSyncPlanItemReason, string> = {
  [DataSyncPlanItemReason.AmbiguousNameMatch]: 'AmbiguousNameMatch',
  [DataSyncPlanItemReason.TypeMismatch]: 'TypeMismatch',
  [DataSyncPlanItemReason.NameClashDifferentType]: 'NameClashDifferentType',
  [DataSyncPlanItemReason.IdentityConflict]: 'IdentityConflict',
  [DataSyncPlanItemReason.DuplicateInPackage]: 'DuplicateInPackage',
  [DataSyncPlanItemReason.LocalIsNewer]: 'LocalIsNewer'
};

export enum DataSyncHeldReason {
  NewerSchema = 1,
  UnknownKind = 2,
  UnknownEnumValue = 3,
  Invalid = 4,
  AtSource = 5,
  LocalUnreadable = 6,
  PendingDecision = 7,
  TooLarge = 8
}

export const dataSyncHeldReasons = [
  { label: 'NewerSchema', value: DataSyncHeldReason.NewerSchema },
  { label: 'UnknownKind', value: DataSyncHeldReason.UnknownKind },
  { label: 'UnknownEnumValue', value: DataSyncHeldReason.UnknownEnumValue },
  { label: 'Invalid', value: DataSyncHeldReason.Invalid },
  { label: 'AtSource', value: DataSyncHeldReason.AtSource },
  { label: 'LocalUnreadable', value: DataSyncHeldReason.LocalUnreadable },
  { label: 'PendingDecision', value: DataSyncHeldReason.PendingDecision },
  { label: 'TooLarge', value: DataSyncHeldReason.TooLarge }
] as const;

export const DataSyncHeldReasonLabel: Record<DataSyncHeldReason, string> = {
  [DataSyncHeldReason.NewerSchema]: 'NewerSchema',
  [DataSyncHeldReason.UnknownKind]: 'UnknownKind',
  [DataSyncHeldReason.UnknownEnumValue]: 'UnknownEnumValue',
  [DataSyncHeldReason.Invalid]: 'Invalid',
  [DataSyncHeldReason.AtSource]: 'AtSource',
  [DataSyncHeldReason.LocalUnreadable]: 'LocalUnreadable',
  [DataSyncHeldReason.PendingDecision]: 'PendingDecision',
  [DataSyncHeldReason.TooLarge]: 'TooLarge'
};

export enum DataSyncPlanResolution {
  Create = 1,
  Update = 2,
  Link = 3,
  CreateSeparate = 4,
  Skip = 5
}

export const dataSyncPlanResolutions = [
  { label: 'Create', value: DataSyncPlanResolution.Create },
  { label: 'Update', value: DataSyncPlanResolution.Update },
  { label: 'Link', value: DataSyncPlanResolution.Link },
  { label: 'CreateSeparate', value: DataSyncPlanResolution.CreateSeparate },
  { label: 'Skip', value: DataSyncPlanResolution.Skip }
] as const;

export const DataSyncPlanResolutionLabel: Record<DataSyncPlanResolution, string> = {
  [DataSyncPlanResolution.Create]: 'Create',
  [DataSyncPlanResolution.Update]: 'Update',
  [DataSyncPlanResolution.Link]: 'Link',
  [DataSyncPlanResolution.CreateSeparate]: 'CreateSeparate',
  [DataSyncPlanResolution.Skip]: 'Skip'
};

export enum DataSyncFieldChangeKind {
  Set = 1,
  AddChild = 2,
  RenameChild = 3,
  RecolorChild = 4,
  AddMember = 5,
  RemoveChild = 6,
  SetType = 7,
  MoveChild = 8
}

export const dataSyncFieldChangeKinds = [
  { label: 'Set', value: DataSyncFieldChangeKind.Set },
  { label: 'AddChild', value: DataSyncFieldChangeKind.AddChild },
  { label: 'RenameChild', value: DataSyncFieldChangeKind.RenameChild },
  { label: 'RecolorChild', value: DataSyncFieldChangeKind.RecolorChild },
  { label: 'AddMember', value: DataSyncFieldChangeKind.AddMember },
  { label: 'RemoveChild', value: DataSyncFieldChangeKind.RemoveChild },
  { label: 'SetType', value: DataSyncFieldChangeKind.SetType },
  { label: 'MoveChild', value: DataSyncFieldChangeKind.MoveChild }
] as const;

export const DataSyncFieldChangeKindLabel: Record<DataSyncFieldChangeKind, string> = {
  [DataSyncFieldChangeKind.Set]: 'Set',
  [DataSyncFieldChangeKind.AddChild]: 'AddChild',
  [DataSyncFieldChangeKind.RenameChild]: 'RenameChild',
  [DataSyncFieldChangeKind.RecolorChild]: 'RecolorChild',
  [DataSyncFieldChangeKind.AddMember]: 'AddMember',
  [DataSyncFieldChangeKind.RemoveChild]: 'RemoveChild',
  [DataSyncFieldChangeKind.SetType]: 'SetType',
  [DataSyncFieldChangeKind.MoveChild]: 'MoveChild'
};

export enum DataSyncWarningCode {
  UnknownFieldsIgnored = 1,
  OptionLabelConflict = 2,
  DefaultValueRefDropped = 3,
  PreviouslyDeletedHere = 4,
  SettingsIgnoredForType = 5,
  NodeMoveIgnored = 6,
  OptionUuidRemapped = 7,
  FromThisDevice = 8,
  OptionDropped = 9,
  ChildRestored = 10,
  ChildrenOmittedByPeer = 11,
  NameUsedEverywhere = 12,
  NormalizationChanged = 13,
  ChildrenLocalTurnedOff = 14
}

export const dataSyncWarningCodes = [
  { label: 'UnknownFieldsIgnored', value: DataSyncWarningCode.UnknownFieldsIgnored },
  { label: 'OptionLabelConflict', value: DataSyncWarningCode.OptionLabelConflict },
  { label: 'DefaultValueRefDropped', value: DataSyncWarningCode.DefaultValueRefDropped },
  { label: 'PreviouslyDeletedHere', value: DataSyncWarningCode.PreviouslyDeletedHere },
  { label: 'SettingsIgnoredForType', value: DataSyncWarningCode.SettingsIgnoredForType },
  { label: 'NodeMoveIgnored', value: DataSyncWarningCode.NodeMoveIgnored },
  { label: 'OptionUuidRemapped', value: DataSyncWarningCode.OptionUuidRemapped },
  { label: 'FromThisDevice', value: DataSyncWarningCode.FromThisDevice },
  { label: 'OptionDropped', value: DataSyncWarningCode.OptionDropped },
  { label: 'ChildRestored', value: DataSyncWarningCode.ChildRestored },
  { label: 'ChildrenOmittedByPeer', value: DataSyncWarningCode.ChildrenOmittedByPeer },
  { label: 'NameUsedEverywhere', value: DataSyncWarningCode.NameUsedEverywhere },
  { label: 'NormalizationChanged', value: DataSyncWarningCode.NormalizationChanged },
  { label: 'ChildrenLocalTurnedOff', value: DataSyncWarningCode.ChildrenLocalTurnedOff }
] as const;

export const DataSyncWarningCodeLabel: Record<DataSyncWarningCode, string> = {
  [DataSyncWarningCode.UnknownFieldsIgnored]: 'UnknownFieldsIgnored',
  [DataSyncWarningCode.OptionLabelConflict]: 'OptionLabelConflict',
  [DataSyncWarningCode.DefaultValueRefDropped]: 'DefaultValueRefDropped',
  [DataSyncWarningCode.PreviouslyDeletedHere]: 'PreviouslyDeletedHere',
  [DataSyncWarningCode.SettingsIgnoredForType]: 'SettingsIgnoredForType',
  [DataSyncWarningCode.NodeMoveIgnored]: 'NodeMoveIgnored',
  [DataSyncWarningCode.OptionUuidRemapped]: 'OptionUuidRemapped',
  [DataSyncWarningCode.FromThisDevice]: 'FromThisDevice',
  [DataSyncWarningCode.OptionDropped]: 'OptionDropped',
  [DataSyncWarningCode.ChildRestored]: 'ChildRestored',
  [DataSyncWarningCode.ChildrenOmittedByPeer]: 'ChildrenOmittedByPeer',
  [DataSyncWarningCode.NameUsedEverywhere]: 'NameUsedEverywhere',
  [DataSyncWarningCode.NormalizationChanged]: 'NormalizationChanged',
  [DataSyncWarningCode.ChildrenLocalTurnedOff]: 'ChildrenLocalTurnedOff'
};

export enum DataSyncDecisionErrorCode {
  DecisionMissing = 1,
  ResolutionNotAllowed = 2,
  TargetNotAllowed = 3,
  TargetUsedTwice = 4,
  ChangedSinceReview = 5,
  UnknownChange = 6,
  InvalidName = 7,
  UnknownItem = 8,
  DuplicateDecision = 9
}

export const dataSyncDecisionErrorCodes = [
  { label: 'DecisionMissing', value: DataSyncDecisionErrorCode.DecisionMissing },
  { label: 'ResolutionNotAllowed', value: DataSyncDecisionErrorCode.ResolutionNotAllowed },
  { label: 'TargetNotAllowed', value: DataSyncDecisionErrorCode.TargetNotAllowed },
  { label: 'TargetUsedTwice', value: DataSyncDecisionErrorCode.TargetUsedTwice },
  { label: 'ChangedSinceReview', value: DataSyncDecisionErrorCode.ChangedSinceReview },
  { label: 'UnknownChange', value: DataSyncDecisionErrorCode.UnknownChange },
  { label: 'InvalidName', value: DataSyncDecisionErrorCode.InvalidName },
  { label: 'UnknownItem', value: DataSyncDecisionErrorCode.UnknownItem },
  { label: 'DuplicateDecision', value: DataSyncDecisionErrorCode.DuplicateDecision }
] as const;

export const DataSyncDecisionErrorCodeLabel: Record<DataSyncDecisionErrorCode, string> = {
  [DataSyncDecisionErrorCode.DecisionMissing]: 'DecisionMissing',
  [DataSyncDecisionErrorCode.ResolutionNotAllowed]: 'ResolutionNotAllowed',
  [DataSyncDecisionErrorCode.TargetNotAllowed]: 'TargetNotAllowed',
  [DataSyncDecisionErrorCode.TargetUsedTwice]: 'TargetUsedTwice',
  [DataSyncDecisionErrorCode.ChangedSinceReview]: 'ChangedSinceReview',
  [DataSyncDecisionErrorCode.UnknownChange]: 'UnknownChange',
  [DataSyncDecisionErrorCode.InvalidName]: 'InvalidName',
  [DataSyncDecisionErrorCode.UnknownItem]: 'UnknownItem',
  [DataSyncDecisionErrorCode.DuplicateDecision]: 'DuplicateDecision'
};

export enum DataSyncItemOutcome {
  Applied = 1,
  SkippedByUser = 2,
  ChangedSinceReview = 3,
  Held = 4,
  ChangedDuringApply = 5,
  NoChange = 6
}

export const dataSyncItemOutcomes = [
  { label: 'Applied', value: DataSyncItemOutcome.Applied },
  { label: 'SkippedByUser', value: DataSyncItemOutcome.SkippedByUser },
  { label: 'ChangedSinceReview', value: DataSyncItemOutcome.ChangedSinceReview },
  { label: 'Held', value: DataSyncItemOutcome.Held },
  { label: 'ChangedDuringApply', value: DataSyncItemOutcome.ChangedDuringApply },
  { label: 'NoChange', value: DataSyncItemOutcome.NoChange }
] as const;

export const DataSyncItemOutcomeLabel: Record<DataSyncItemOutcome, string> = {
  [DataSyncItemOutcome.Applied]: 'Applied',
  [DataSyncItemOutcome.SkippedByUser]: 'SkippedByUser',
  [DataSyncItemOutcome.ChangedSinceReview]: 'ChangedSinceReview',
  [DataSyncItemOutcome.Held]: 'Held',
  [DataSyncItemOutcome.ChangedDuringApply]: 'ChangedDuringApply',
  [DataSyncItemOutcome.NoChange]: 'NoChange'
};

export enum DataSyncItemAction {
  None = 0,
  Created = 1,
  Updated = 2,
  Linked = 3,
  KeysRecorded = 4,
  Deleted = 5,
  TypeChanged = 6,
  Reordered = 7
}

export const dataSyncItemActions = [
  { label: 'None', value: DataSyncItemAction.None },
  { label: 'Created', value: DataSyncItemAction.Created },
  { label: 'Updated', value: DataSyncItemAction.Updated },
  { label: 'Linked', value: DataSyncItemAction.Linked },
  { label: 'KeysRecorded', value: DataSyncItemAction.KeysRecorded },
  { label: 'Deleted', value: DataSyncItemAction.Deleted },
  { label: 'TypeChanged', value: DataSyncItemAction.TypeChanged },
  { label: 'Reordered', value: DataSyncItemAction.Reordered }
] as const;

export const DataSyncItemActionLabel: Record<DataSyncItemAction, string> = {
  [DataSyncItemAction.None]: 'None',
  [DataSyncItemAction.Created]: 'Created',
  [DataSyncItemAction.Updated]: 'Updated',
  [DataSyncItemAction.Linked]: 'Linked',
  [DataSyncItemAction.KeysRecorded]: 'KeysRecorded',
  [DataSyncItemAction.Deleted]: 'Deleted',
  [DataSyncItemAction.TypeChanged]: 'TypeChanged',
  [DataSyncItemAction.Reordered]: 'Reordered'
};

export enum DataSyncRefreshAction {
  HashesOnly = 1,
  LocalEdit = 2,
  HoldForLostUpdate = 3,
  RefreshHeldItem = 4
}

export const dataSyncRefreshActions = [
  { label: 'HashesOnly', value: DataSyncRefreshAction.HashesOnly },
  { label: 'LocalEdit', value: DataSyncRefreshAction.LocalEdit },
  { label: 'HoldForLostUpdate', value: DataSyncRefreshAction.HoldForLostUpdate },
  { label: 'RefreshHeldItem', value: DataSyncRefreshAction.RefreshHeldItem }
] as const;

export const DataSyncRefreshActionLabel: Record<DataSyncRefreshAction, string> = {
  [DataSyncRefreshAction.HashesOnly]: 'HashesOnly',
  [DataSyncRefreshAction.LocalEdit]: 'LocalEdit',
  [DataSyncRefreshAction.HoldForLostUpdate]: 'HoldForLostUpdate',
  [DataSyncRefreshAction.RefreshHeldItem]: 'RefreshHeldItem'
};

export enum DataSyncNaturalMatch {
  None = 0,
  Clash = 1,
  Similar = 2,
  Exact = 3,
  Identical = 4
}

export const dataSyncNaturalMatches = [
  { label: 'None', value: DataSyncNaturalMatch.None },
  { label: 'Clash', value: DataSyncNaturalMatch.Clash },
  { label: 'Similar', value: DataSyncNaturalMatch.Similar },
  { label: 'Exact', value: DataSyncNaturalMatch.Exact },
  { label: 'Identical', value: DataSyncNaturalMatch.Identical }
] as const;

export const DataSyncNaturalMatchLabel: Record<DataSyncNaturalMatch, string> = {
  [DataSyncNaturalMatch.None]: 'None',
  [DataSyncNaturalMatch.Clash]: 'Clash',
  [DataSyncNaturalMatch.Similar]: 'Similar',
  [DataSyncNaturalMatch.Exact]: 'Exact',
  [DataSyncNaturalMatch.Identical]: 'Identical'
};

export enum DataSyncTableClass {
  Synced = 1,
  NeverSync = 2,
  LibraryData = 3
}

export const dataSyncTableClasses = [
  { label: 'Synced', value: DataSyncTableClass.Synced },
  { label: 'NeverSync', value: DataSyncTableClass.NeverSync },
  { label: 'LibraryData', value: DataSyncTableClass.LibraryData }
] as const;

export const DataSyncTableClassLabel: Record<DataSyncTableClass, string> = {
  [DataSyncTableClass.Synced]: 'Synced',
  [DataSyncTableClass.NeverSync]: 'NeverSync',
  [DataSyncTableClass.LibraryData]: 'LibraryData'
};

export enum DataSyncChildDeletionMode {
  Normal = 1,
  Apply = 2,
  ReviewEach = 3,
  Restore = 4
}

export const dataSyncChildDeletionModes = [
  { label: 'Normal', value: DataSyncChildDeletionMode.Normal },
  { label: 'Apply', value: DataSyncChildDeletionMode.Apply },
  { label: 'ReviewEach', value: DataSyncChildDeletionMode.ReviewEach },
  { label: 'Restore', value: DataSyncChildDeletionMode.Restore }
] as const;

export const DataSyncChildDeletionModeLabel: Record<DataSyncChildDeletionMode, string> = {
  [DataSyncChildDeletionMode.Normal]: 'Normal',
  [DataSyncChildDeletionMode.Apply]: 'Apply',
  [DataSyncChildDeletionMode.ReviewEach]: 'ReviewEach',
  [DataSyncChildDeletionMode.Restore]: 'Restore'
};

export enum FileOperationType {
  Rename = 1,
  Move = 2,
  CreateDirectory = 3
}

export const fileOperationTypes = [
  { label: 'Rename', value: FileOperationType.Rename },
  { label: 'Move', value: FileOperationType.Move },
  { label: 'CreateDirectory', value: FileOperationType.CreateDirectory }
] as const;

export const FileOperationTypeLabel: Record<FileOperationType, string> = {
  [FileOperationType.Rename]: 'Rename',
  [FileOperationType.Move]: 'Move',
  [FileOperationType.CreateDirectory]: 'CreateDirectory'
};

export enum ChatStreamEventType {
  TextDelta = 1,
  ToolCallStart = 2,
  ToolCallResult = 3,
  Done = 4,
  Error = 5
}

export const chatStreamEventTypes = [
  { label: 'TextDelta', value: ChatStreamEventType.TextDelta },
  { label: 'ToolCallStart', value: ChatStreamEventType.ToolCallStart },
  { label: 'ToolCallResult', value: ChatStreamEventType.ToolCallResult },
  { label: 'Done', value: ChatStreamEventType.Done },
  { label: 'Error', value: ChatStreamEventType.Error }
] as const;

export const ChatStreamEventTypeLabel: Record<ChatStreamEventType, string> = {
  [ChatStreamEventType.TextDelta]: 'TextDelta',
  [ChatStreamEventType.ToolCallStart]: 'ToolCallStart',
  [ChatStreamEventType.ToolCallResult]: 'ToolCallResult',
  [ChatStreamEventType.Done]: 'Done',
  [ChatStreamEventType.Error]: 'Error'
};

export enum AigcGeneratorComfyUIImportStatus {
  Imported = 1,
  SkippedDuplicate = 2,
  SkippedInvalidJson = 3,
  SkippedNotComfyUIWorkflow = 4,
  Failed = 5
}

export const aigcGeneratorComfyUIImportStatuses = [
  { label: 'Imported', value: AigcGeneratorComfyUIImportStatus.Imported },
  { label: 'SkippedDuplicate', value: AigcGeneratorComfyUIImportStatus.SkippedDuplicate },
  { label: 'SkippedInvalidJson', value: AigcGeneratorComfyUIImportStatus.SkippedInvalidJson },
  { label: 'SkippedNotComfyUIWorkflow', value: AigcGeneratorComfyUIImportStatus.SkippedNotComfyUIWorkflow },
  { label: 'Failed', value: AigcGeneratorComfyUIImportStatus.Failed }
] as const;

export const AigcGeneratorComfyUIImportStatusLabel: Record<AigcGeneratorComfyUIImportStatus, string> = {
  [AigcGeneratorComfyUIImportStatus.Imported]: 'Imported',
  [AigcGeneratorComfyUIImportStatus.SkippedDuplicate]: 'SkippedDuplicate',
  [AigcGeneratorComfyUIImportStatus.SkippedInvalidJson]: 'SkippedInvalidJson',
  [AigcGeneratorComfyUIImportStatus.SkippedNotComfyUIWorkflow]: 'SkippedNotComfyUIWorkflow',
  [AigcGeneratorComfyUIImportStatus.Failed]: 'Failed'
};

export enum AiFeature {
  Default = 0,
  Enhancer = 1,
  Translation = 2,
  FileProcessor = 3,
  PostParser = 4,
  Chat = 5
}

export const aiFeatures = [
  { label: 'Default', value: AiFeature.Default },
  { label: 'Enhancer', value: AiFeature.Enhancer },
  { label: 'Translation', value: AiFeature.Translation },
  { label: 'FileProcessor', value: AiFeature.FileProcessor },
  { label: 'PostParser', value: AiFeature.PostParser },
  { label: 'Chat', value: AiFeature.Chat }
] as const;

export const AiFeatureLabel: Record<AiFeature, string> = {
  [AiFeature.Default]: 'Default',
  [AiFeature.Enhancer]: 'Enhancer',
  [AiFeature.Translation]: 'Translation',
  [AiFeature.FileProcessor]: 'FileProcessor',
  [AiFeature.PostParser]: 'PostParser',
  [AiFeature.Chat]: 'Chat'
};

export enum AigcArtifactResourceMode {
  PerArtifact = 1,
  PerRun = 2
}

export const aigcArtifactResourceModes = [
  { label: 'PerArtifact', value: AigcArtifactResourceMode.PerArtifact },
  { label: 'PerRun', value: AigcArtifactResourceMode.PerRun }
] as const;

export const AigcArtifactResourceModeLabel: Record<AigcArtifactResourceMode, string> = {
  [AigcArtifactResourceMode.PerArtifact]: 'PerArtifact',
  [AigcArtifactResourceMode.PerRun]: 'PerRun'
};

export enum AigcGenerationStatus {
  Pending = 1,
  Running = 2,
  Succeeded = 3,
  Failed = 4,
  Imported = 5,
  Cancelled = 6
}

export const aigcGenerationStatuses = [
  { label: 'Pending', value: AigcGenerationStatus.Pending },
  { label: 'Running', value: AigcGenerationStatus.Running },
  { label: 'Succeeded', value: AigcGenerationStatus.Succeeded },
  { label: 'Failed', value: AigcGenerationStatus.Failed },
  { label: 'Imported', value: AigcGenerationStatus.Imported },
  { label: 'Cancelled', value: AigcGenerationStatus.Cancelled }
] as const;

export const AigcGenerationStatusLabel: Record<AigcGenerationStatus, string> = {
  [AigcGenerationStatus.Pending]: 'Pending',
  [AigcGenerationStatus.Running]: 'Running',
  [AigcGenerationStatus.Succeeded]: 'Succeeded',
  [AigcGenerationStatus.Failed]: 'Failed',
  [AigcGenerationStatus.Imported]: 'Imported',
  [AigcGenerationStatus.Cancelled]: 'Cancelled'
};

export enum AigcMediaType {
  Image = 1,
  Text = 2,
  Audio = 3,
  Video = 4,
  Other = 99
}

export const aigcMediaTypes = [
  { label: 'Image', value: AigcMediaType.Image },
  { label: 'Text', value: AigcMediaType.Text },
  { label: 'Audio', value: AigcMediaType.Audio },
  { label: 'Video', value: AigcMediaType.Video },
  { label: 'Other', value: AigcMediaType.Other }
] as const;

export const AigcMediaTypeLabel: Record<AigcMediaType, string> = {
  [AigcMediaType.Image]: 'Image',
  [AigcMediaType.Text]: 'Text',
  [AigcMediaType.Audio]: 'Audio',
  [AigcMediaType.Video]: 'Video',
  [AigcMediaType.Other]: 'Other'
};

export enum AiProviderCapability {
  None = 0,
  Llm = 1,
  Aigc = 2
}

export const aiProviderCapabilities = [
  { label: 'None', value: AiProviderCapability.None },
  { label: 'Llm', value: AiProviderCapability.Llm },
  { label: 'Aigc', value: AiProviderCapability.Aigc }
] as const;

export const AiProviderCapabilityLabel: Record<AiProviderCapability, string> = {
  [AiProviderCapability.None]: 'None',
  [AiProviderCapability.Llm]: 'Llm',
  [AiProviderCapability.Aigc]: 'Aigc'
};

export enum AiProviderKind {
  OpenAI = 1,
  Claude = 2,
  Ollama = 3,
  DashScope = 4,
  Gemini = 5,
  StableDiffusionWebUI = 100,
  ComfyUI = 101,
  HttpCustom = 199
}

export const aiProviderKinds = [
  { label: 'OpenAI', value: AiProviderKind.OpenAI },
  { label: 'Claude', value: AiProviderKind.Claude },
  { label: 'Ollama', value: AiProviderKind.Ollama },
  { label: 'DashScope', value: AiProviderKind.DashScope },
  { label: 'Gemini', value: AiProviderKind.Gemini },
  { label: 'StableDiffusionWebUI', value: AiProviderKind.StableDiffusionWebUI },
  { label: 'ComfyUI', value: AiProviderKind.ComfyUI },
  { label: 'HttpCustom', value: AiProviderKind.HttpCustom }
] as const;

export const AiProviderKindLabel: Record<AiProviderKind, string> = {
  [AiProviderKind.OpenAI]: 'OpenAI',
  [AiProviderKind.Claude]: 'Claude',
  [AiProviderKind.Ollama]: 'Ollama',
  [AiProviderKind.DashScope]: 'DashScope',
  [AiProviderKind.Gemini]: 'Gemini',
  [AiProviderKind.StableDiffusionWebUI]: 'StableDiffusionWebUI',
  [AiProviderKind.ComfyUI]: 'ComfyUI',
  [AiProviderKind.HttpCustom]: 'HttpCustom'
};

export enum LlmCallStatus {
  Success = 1,
  Error = 2,
  Timeout = 3,
  Cancelled = 4
}

export const llmCallStatuses = [
  { label: 'Success', value: LlmCallStatus.Success },
  { label: 'Error', value: LlmCallStatus.Error },
  { label: 'Timeout', value: LlmCallStatus.Timeout },
  { label: 'Cancelled', value: LlmCallStatus.Cancelled }
] as const;

export const LlmCallStatusLabel: Record<LlmCallStatus, string> = {
  [LlmCallStatus.Success]: 'Success',
  [LlmCallStatus.Error]: 'Error',
  [LlmCallStatus.Timeout]: 'Timeout',
  [LlmCallStatus.Cancelled]: 'Cancelled'
};

export enum LlmCapabilities {
  None = 0,
  Chat = 1,
  ToolCalling = 2,
  Vision = 4,
  Streaming = 8,
  Embedding = 16,
  JsonMode = 32
}

export const llmCapabilities = [
  { label: 'None', value: LlmCapabilities.None },
  { label: 'Chat', value: LlmCapabilities.Chat },
  { label: 'ToolCalling', value: LlmCapabilities.ToolCalling },
  { label: 'Vision', value: LlmCapabilities.Vision },
  { label: 'Streaming', value: LlmCapabilities.Streaming },
  { label: 'Embedding', value: LlmCapabilities.Embedding },
  { label: 'JsonMode', value: LlmCapabilities.JsonMode }
] as const;

export const LlmCapabilitiesLabel: Record<LlmCapabilities, string> = {
  [LlmCapabilities.None]: 'None',
  [LlmCapabilities.Chat]: 'Chat',
  [LlmCapabilities.ToolCalling]: 'ToolCalling',
  [LlmCapabilities.Vision]: 'Vision',
  [LlmCapabilities.Streaming]: 'Streaming',
  [LlmCapabilities.Embedding]: 'Embedding',
  [LlmCapabilities.JsonMode]: 'JsonMode'
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

export enum BulkModificationTimeProcessOperation {
  Delete = 1,
  SetWithFixedValue = 2,
  AddHours = 3,
  SubtractHours = 4,
  AddMinutes = 5,
  SubtractMinutes = 6,
  AddSeconds = 7,
  SubtractSeconds = 8
}

export const bulkModificationTimeProcessOperations = [
  { label: 'Delete', value: BulkModificationTimeProcessOperation.Delete },
  { label: 'SetWithFixedValue', value: BulkModificationTimeProcessOperation.SetWithFixedValue },
  { label: 'AddHours', value: BulkModificationTimeProcessOperation.AddHours },
  { label: 'SubtractHours', value: BulkModificationTimeProcessOperation.SubtractHours },
  { label: 'AddMinutes', value: BulkModificationTimeProcessOperation.AddMinutes },
  { label: 'SubtractMinutes', value: BulkModificationTimeProcessOperation.SubtractMinutes },
  { label: 'AddSeconds', value: BulkModificationTimeProcessOperation.AddSeconds },
  { label: 'SubtractSeconds', value: BulkModificationTimeProcessOperation.SubtractSeconds }
] as const;

export const BulkModificationTimeProcessOperationLabel: Record<BulkModificationTimeProcessOperation, string> = {
  [BulkModificationTimeProcessOperation.Delete]: 'Delete',
  [BulkModificationTimeProcessOperation.SetWithFixedValue]: 'SetWithFixedValue',
  [BulkModificationTimeProcessOperation.AddHours]: 'AddHours',
  [BulkModificationTimeProcessOperation.SubtractHours]: 'SubtractHours',
  [BulkModificationTimeProcessOperation.AddMinutes]: 'AddMinutes',
  [BulkModificationTimeProcessOperation.SubtractMinutes]: 'SubtractMinutes',
  [BulkModificationTimeProcessOperation.AddSeconds]: 'AddSeconds',
  [BulkModificationTimeProcessOperation.SubtractSeconds]: 'SubtractSeconds'
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

export enum BulkModificationListTagProcessOperation {
  Delete = 1,
  SetWithFixedValue = 2,
  Append = 3,
  Prepend = 4,
  Remove = 5
}

export const bulkModificationListTagProcessOperations = [
  { label: 'Delete', value: BulkModificationListTagProcessOperation.Delete },
  { label: 'SetWithFixedValue', value: BulkModificationListTagProcessOperation.SetWithFixedValue },
  { label: 'Append', value: BulkModificationListTagProcessOperation.Append },
  { label: 'Prepend', value: BulkModificationListTagProcessOperation.Prepend },
  { label: 'Remove', value: BulkModificationListTagProcessOperation.Remove }
] as const;

export const BulkModificationListTagProcessOperationLabel: Record<BulkModificationListTagProcessOperation, string> = {
  [BulkModificationListTagProcessOperation.Delete]: 'Delete',
  [BulkModificationListTagProcessOperation.SetWithFixedValue]: 'SetWithFixedValue',
  [BulkModificationListTagProcessOperation.Append]: 'Append',
  [BulkModificationListTagProcessOperation.Prepend]: 'Prepend',
  [BulkModificationListTagProcessOperation.Remove]: 'Remove'
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

export enum BulkModificationListListStringProcessOperation {
  Delete = 1,
  SetWithFixedValue = 2,
  Append = 3,
  Prepend = 4,
  Remove = 5
}

export const bulkModificationListListStringProcessOperations = [
  { label: 'Delete', value: BulkModificationListListStringProcessOperation.Delete },
  { label: 'SetWithFixedValue', value: BulkModificationListListStringProcessOperation.SetWithFixedValue },
  { label: 'Append', value: BulkModificationListListStringProcessOperation.Append },
  { label: 'Prepend', value: BulkModificationListListStringProcessOperation.Prepend },
  { label: 'Remove', value: BulkModificationListListStringProcessOperation.Remove }
] as const;

export const BulkModificationListListStringProcessOperationLabel: Record<BulkModificationListListStringProcessOperation, string> = {
  [BulkModificationListListStringProcessOperation.Delete]: 'Delete',
  [BulkModificationListListStringProcessOperation.SetWithFixedValue]: 'SetWithFixedValue',
  [BulkModificationListListStringProcessOperation.Append]: 'Append',
  [BulkModificationListListStringProcessOperation.Prepend]: 'Prepend',
  [BulkModificationListListStringProcessOperation.Remove]: 'Remove'
};

export enum BulkModificationLinkProcessOperation {
  Delete = 1,
  SetWithFixedValue = 2,
  SetText = 3,
  SetUrl = 4,
  ModifyText = 5,
  ModifyUrl = 6
}

export const bulkModificationLinkProcessOperations = [
  { label: 'Delete', value: BulkModificationLinkProcessOperation.Delete },
  { label: 'SetWithFixedValue', value: BulkModificationLinkProcessOperation.SetWithFixedValue },
  { label: 'SetText', value: BulkModificationLinkProcessOperation.SetText },
  { label: 'SetUrl', value: BulkModificationLinkProcessOperation.SetUrl },
  { label: 'ModifyText', value: BulkModificationLinkProcessOperation.ModifyText },
  { label: 'ModifyUrl', value: BulkModificationLinkProcessOperation.ModifyUrl }
] as const;

export const BulkModificationLinkProcessOperationLabel: Record<BulkModificationLinkProcessOperation, string> = {
  [BulkModificationLinkProcessOperation.Delete]: 'Delete',
  [BulkModificationLinkProcessOperation.SetWithFixedValue]: 'SetWithFixedValue',
  [BulkModificationLinkProcessOperation.SetText]: 'SetText',
  [BulkModificationLinkProcessOperation.SetUrl]: 'SetUrl',
  [BulkModificationLinkProcessOperation.ModifyText]: 'ModifyText',
  [BulkModificationLinkProcessOperation.ModifyUrl]: 'ModifyUrl'
};

export enum BulkModificationDecimalProcessOperation {
  Delete = 1,
  SetWithFixedValue = 2,
  Add = 3,
  Subtract = 4,
  Multiply = 5,
  Divide = 6,
  Round = 7,
  Ceil = 8,
  Floor = 9
}

export const bulkModificationDecimalProcessOperations = [
  { label: 'Delete', value: BulkModificationDecimalProcessOperation.Delete },
  { label: 'SetWithFixedValue', value: BulkModificationDecimalProcessOperation.SetWithFixedValue },
  { label: 'Add', value: BulkModificationDecimalProcessOperation.Add },
  { label: 'Subtract', value: BulkModificationDecimalProcessOperation.Subtract },
  { label: 'Multiply', value: BulkModificationDecimalProcessOperation.Multiply },
  { label: 'Divide', value: BulkModificationDecimalProcessOperation.Divide },
  { label: 'Round', value: BulkModificationDecimalProcessOperation.Round },
  { label: 'Ceil', value: BulkModificationDecimalProcessOperation.Ceil },
  { label: 'Floor', value: BulkModificationDecimalProcessOperation.Floor }
] as const;

export const BulkModificationDecimalProcessOperationLabel: Record<BulkModificationDecimalProcessOperation, string> = {
  [BulkModificationDecimalProcessOperation.Delete]: 'Delete',
  [BulkModificationDecimalProcessOperation.SetWithFixedValue]: 'SetWithFixedValue',
  [BulkModificationDecimalProcessOperation.Add]: 'Add',
  [BulkModificationDecimalProcessOperation.Subtract]: 'Subtract',
  [BulkModificationDecimalProcessOperation.Multiply]: 'Multiply',
  [BulkModificationDecimalProcessOperation.Divide]: 'Divide',
  [BulkModificationDecimalProcessOperation.Round]: 'Round',
  [BulkModificationDecimalProcessOperation.Ceil]: 'Ceil',
  [BulkModificationDecimalProcessOperation.Floor]: 'Floor'
};

export enum BulkModificationDateTimeProcessOperation {
  Delete = 1,
  SetWithFixedValue = 2,
  AddDays = 3,
  SubtractDays = 4,
  AddMonths = 5,
  SubtractMonths = 6,
  AddYears = 7,
  SubtractYears = 8,
  SetToNow = 9
}

export const bulkModificationDateTimeProcessOperations = [
  { label: 'Delete', value: BulkModificationDateTimeProcessOperation.Delete },
  { label: 'SetWithFixedValue', value: BulkModificationDateTimeProcessOperation.SetWithFixedValue },
  { label: 'AddDays', value: BulkModificationDateTimeProcessOperation.AddDays },
  { label: 'SubtractDays', value: BulkModificationDateTimeProcessOperation.SubtractDays },
  { label: 'AddMonths', value: BulkModificationDateTimeProcessOperation.AddMonths },
  { label: 'SubtractMonths', value: BulkModificationDateTimeProcessOperation.SubtractMonths },
  { label: 'AddYears', value: BulkModificationDateTimeProcessOperation.AddYears },
  { label: 'SubtractYears', value: BulkModificationDateTimeProcessOperation.SubtractYears },
  { label: 'SetToNow', value: BulkModificationDateTimeProcessOperation.SetToNow }
] as const;

export const BulkModificationDateTimeProcessOperationLabel: Record<BulkModificationDateTimeProcessOperation, string> = {
  [BulkModificationDateTimeProcessOperation.Delete]: 'Delete',
  [BulkModificationDateTimeProcessOperation.SetWithFixedValue]: 'SetWithFixedValue',
  [BulkModificationDateTimeProcessOperation.AddDays]: 'AddDays',
  [BulkModificationDateTimeProcessOperation.SubtractDays]: 'SubtractDays',
  [BulkModificationDateTimeProcessOperation.AddMonths]: 'AddMonths',
  [BulkModificationDateTimeProcessOperation.SubtractMonths]: 'SubtractMonths',
  [BulkModificationDateTimeProcessOperation.AddYears]: 'AddYears',
  [BulkModificationDateTimeProcessOperation.SubtractYears]: 'SubtractYears',
  [BulkModificationDateTimeProcessOperation.SetToNow]: 'SetToNow'
};

export enum BulkModificationBooleanProcessOperation {
  Delete = 1,
  SetWithFixedValue = 2,
  Toggle = 3
}

export const bulkModificationBooleanProcessOperations = [
  { label: 'Delete', value: BulkModificationBooleanProcessOperation.Delete },
  { label: 'SetWithFixedValue', value: BulkModificationBooleanProcessOperation.SetWithFixedValue },
  { label: 'Toggle', value: BulkModificationBooleanProcessOperation.Toggle }
] as const;

export const BulkModificationBooleanProcessOperationLabel: Record<BulkModificationBooleanProcessOperation, string> = {
  [BulkModificationBooleanProcessOperation.Delete]: 'Delete',
  [BulkModificationBooleanProcessOperation.SetWithFixedValue]: 'SetWithFixedValue',
  [BulkModificationBooleanProcessOperation.Toggle]: 'Toggle'
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

export enum CollectionMemberFilter {
  All = 0,
  Owned = 1,
  Missing = 2,
  Acquiring = 3,
  Ignored = 4,
  GoneFromSource = 5
}

export const collectionMemberFilters = [
  { label: 'All', value: CollectionMemberFilter.All },
  { label: 'Owned', value: CollectionMemberFilter.Owned },
  { label: 'Missing', value: CollectionMemberFilter.Missing },
  { label: 'Acquiring', value: CollectionMemberFilter.Acquiring },
  { label: 'Ignored', value: CollectionMemberFilter.Ignored },
  { label: 'GoneFromSource', value: CollectionMemberFilter.GoneFromSource }
] as const;

export const CollectionMemberFilterLabel: Record<CollectionMemberFilter, string> = {
  [CollectionMemberFilter.All]: 'All',
  [CollectionMemberFilter.Owned]: 'Owned',
  [CollectionMemberFilter.Missing]: 'Missing',
  [CollectionMemberFilter.Acquiring]: 'Acquiring',
  [CollectionMemberFilter.Ignored]: 'Ignored',
  [CollectionMemberFilter.GoneFromSource]: 'GoneFromSource'
};

export enum CollectionMembershipOrigin {
  Manual = 1,
  Subscription = 2
}

export const collectionMembershipOrigins = [
  { label: 'Manual', value: CollectionMembershipOrigin.Manual },
  { label: 'Subscription', value: CollectionMembershipOrigin.Subscription }
] as const;

export const CollectionMembershipOriginLabel: Record<CollectionMembershipOrigin, string> = {
  [CollectionMembershipOrigin.Manual]: 'Manual',
  [CollectionMembershipOrigin.Subscription]: 'Subscription'
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

export enum DataCardMatchMode {
  Any = 1,
  All = 2
}

export const dataCardMatchModes = [
  { label: 'Any', value: DataCardMatchMode.Any },
  { label: 'All', value: DataCardMatchMode.All }
] as const;

export const DataCardMatchModeLabel: Record<DataCardMatchMode, string> = {
  [DataCardMatchMode.Any]: 'Any',
  [DataCardMatchMode.All]: 'All'
};

export enum FederationEndpointKind {
  Local = 0,
  Public = 1,
  Export = 2
}

export const federationEndpointKinds = [
  { label: 'Local', value: FederationEndpointKind.Local },
  { label: 'Public', value: FederationEndpointKind.Public },
  { label: 'Export', value: FederationEndpointKind.Export }
] as const;

export const FederationEndpointKindLabel: Record<FederationEndpointKind, string> = {
  [FederationEndpointKind.Local]: 'Local',
  [FederationEndpointKind.Public]: 'Public',
  [FederationEndpointKind.Export]: 'Export'
};

export enum FederationSharingRequirement {
  Library = 0,
  DataSync = 1,
  Either = 2,
  GrantScope = 3
}

export const federationSharingRequirements = [
  { label: 'Library', value: FederationSharingRequirement.Library },
  { label: 'DataSync', value: FederationSharingRequirement.DataSync },
  { label: 'Either', value: FederationSharingRequirement.Either },
  { label: 'GrantScope', value: FederationSharingRequirement.GrantScope }
] as const;

export const FederationSharingRequirementLabel: Record<FederationSharingRequirement, string> = {
  [FederationSharingRequirement.Library]: 'Library',
  [FederationSharingRequirement.DataSync]: 'DataSync',
  [FederationSharingRequirement.Either]: 'Either',
  [FederationSharingRequirement.GrantScope]: 'GrantScope'
};

export enum ResourceMatcherLeafKind {
  Property = 1,
  File = 2
}

export const resourceMatcherLeafKinds = [
  { label: 'Property', value: ResourceMatcherLeafKind.Property },
  { label: 'File', value: ResourceMatcherLeafKind.File }
] as const;

export const ResourceMatcherLeafKindLabel: Record<ResourceMatcherLeafKind, string> = {
  [ResourceMatcherLeafKind.Property]: 'Property',
  [ResourceMatcherLeafKind.File]: 'File'
};

export enum ComparisonOperator {
  Equals = 1,
  NotEquals = 2,
  GreaterThan = 3,
  LessThan = 4,
  GreaterThanOrEquals = 5,
  LessThanOrEquals = 6
}

export const comparisonOperators = [
  { label: 'Equals', value: ComparisonOperator.Equals },
  { label: 'NotEquals', value: ComparisonOperator.NotEquals },
  { label: 'GreaterThan', value: ComparisonOperator.GreaterThan },
  { label: 'LessThan', value: ComparisonOperator.LessThan },
  { label: 'GreaterThanOrEquals', value: ComparisonOperator.GreaterThanOrEquals },
  { label: 'LessThanOrEquals', value: ComparisonOperator.LessThanOrEquals }
] as const;

export const ComparisonOperatorLabel: Record<ComparisonOperator, string> = {
  [ComparisonOperator.Equals]: 'Equals',
  [ComparisonOperator.NotEquals]: 'NotEquals',
  [ComparisonOperator.GreaterThan]: 'GreaterThan',
  [ComparisonOperator.LessThan]: 'LessThan',
  [ComparisonOperator.GreaterThanOrEquals]: 'GreaterThanOrEquals',
  [ComparisonOperator.LessThanOrEquals]: 'LessThanOrEquals'
};

export enum PredicateMediaType {
  All = 0,
  Video = 1,
  Image = 2,
  Audio = 3,
  Text = 4,
  Application = 5
}

export const predicateMediaTypes = [
  { label: 'All', value: PredicateMediaType.All },
  { label: 'Video', value: PredicateMediaType.Video },
  { label: 'Image', value: PredicateMediaType.Image },
  { label: 'Audio', value: PredicateMediaType.Audio },
  { label: 'Text', value: PredicateMediaType.Text },
  { label: 'Application', value: PredicateMediaType.Application }
] as const;

export const PredicateMediaTypeLabel: Record<PredicateMediaType, string> = {
  [PredicateMediaType.All]: 'All',
  [PredicateMediaType.Video]: 'Video',
  [PredicateMediaType.Image]: 'Image',
  [PredicateMediaType.Audio]: 'Audio',
  [PredicateMediaType.Text]: 'Text',
  [PredicateMediaType.Application]: 'Application'
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

export enum SubscriptionSourceKind {
  PlatformHolding = 1,
  Catalog = 2,
  SharingChannel = 3
}

export const subscriptionSourceKinds = [
  { label: 'PlatformHolding', value: SubscriptionSourceKind.PlatformHolding },
  { label: 'Catalog', value: SubscriptionSourceKind.Catalog },
  { label: 'SharingChannel', value: SubscriptionSourceKind.SharingChannel }
] as const;

export const SubscriptionSourceKindLabel: Record<SubscriptionSourceKind, string> = {
  [SubscriptionSourceKind.PlatformHolding]: 'PlatformHolding',
  [SubscriptionSourceKind.Catalog]: 'Catalog',
  [SubscriptionSourceKind.SharingChannel]: 'SharingChannel'
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

export enum DecompressionStatus {
  Pending = 1,
  Decompressing = 2,
  Success = 3,
  Error = 4
}

export const decompressionStatuses = [
  { label: 'Pending', value: DecompressionStatus.Pending },
  { label: 'Decompressing', value: DecompressionStatus.Decompressing },
  { label: 'Success', value: DecompressionStatus.Success },
  { label: 'Error', value: DecompressionStatus.Error }
] as const;

export const DecompressionStatusLabel: Record<DecompressionStatus, string> = {
  [DecompressionStatus.Pending]: 'Pending',
  [DecompressionStatus.Decompressing]: 'Decompressing',
  [DecompressionStatus.Success]: 'Success',
  [DecompressionStatus.Error]: 'Error'
};

export enum CompressedFileDetectionResultStatus {
  Init = 1,
  Inprogress = 2,
  Complete = 3,
  Error = 4
}

export const compressedFileDetectionResultStatuses = [
  { label: 'Init', value: CompressedFileDetectionResultStatus.Init },
  { label: 'Inprogress', value: CompressedFileDetectionResultStatus.Inprogress },
  { label: 'Complete', value: CompressedFileDetectionResultStatus.Complete },
  { label: 'Error', value: CompressedFileDetectionResultStatus.Error }
] as const;

export const CompressedFileDetectionResultStatusLabel: Record<CompressedFileDetectionResultStatus, string> = {
  [CompressedFileDetectionResultStatus.Init]: 'Init',
  [CompressedFileDetectionResultStatus.Inprogress]: 'Inprogress',
  [CompressedFileDetectionResultStatus.Complete]: 'Complete',
  [CompressedFileDetectionResultStatus.Error]: 'Error'
};

export enum ClientForwardingFailure {
  None = 0,
  NotConnected = 1,
  ServerUnreachable = 2,
  ForeignCaller = 3,
  NeedsNewerClient = 4,
  PathNotMapped = 5,
  WrongServer = 6
}

export const clientForwardingFailures = [
  { label: 'None', value: ClientForwardingFailure.None },
  { label: 'NotConnected', value: ClientForwardingFailure.NotConnected },
  { label: 'ServerUnreachable', value: ClientForwardingFailure.ServerUnreachable },
  { label: 'ForeignCaller', value: ClientForwardingFailure.ForeignCaller },
  { label: 'NeedsNewerClient', value: ClientForwardingFailure.NeedsNewerClient },
  { label: 'PathNotMapped', value: ClientForwardingFailure.PathNotMapped },
  { label: 'WrongServer', value: ClientForwardingFailure.WrongServer }
] as const;

export const ClientForwardingFailureLabel: Record<ClientForwardingFailure, string> = {
  [ClientForwardingFailure.None]: 'None',
  [ClientForwardingFailure.NotConnected]: 'NotConnected',
  [ClientForwardingFailure.ServerUnreachable]: 'ServerUnreachable',
  [ClientForwardingFailure.ForeignCaller]: 'ForeignCaller',
  [ClientForwardingFailure.NeedsNewerClient]: 'NeedsNewerClient',
  [ClientForwardingFailure.PathNotMapped]: 'PathNotMapped',
  [ClientForwardingFailure.WrongServer]: 'WrongServer'
};

export const ExtensionMediaTypes: Record<string, MediaType> = {
  ".3gp": MediaType.Video,
  ".avi": MediaType.Video,
  ".bmp": MediaType.Image,
  ".exe": MediaType.Application,
  ".flac": MediaType.Audio,
  ".flv": MediaType.Video,
  ".gif": MediaType.Image,
  ".ico": MediaType.Image,
  ".jpeg": MediaType.Image,
  ".jpg": MediaType.Image,
  ".m4a": MediaType.Audio,
  ".mid": MediaType.Audio,
  ".midi": MediaType.Audio,
  ".mkv": MediaType.Video,
  ".mov": MediaType.Video,
  ".mp3": MediaType.Audio,
  ".mp4": MediaType.Video,
  ".mpeg": MediaType.Video,
  ".ogg": MediaType.Audio,
  ".png": MediaType.Image,
  ".rmvb": MediaType.Video,
  ".svg": MediaType.Image,
  ".tiff": MediaType.Image,
  ".ts": MediaType.Video,
  ".txt": MediaType.Text,
  ".wav": MediaType.Audio,
  ".weba": MediaType.Audio,
  ".webm": MediaType.Video,
  ".webp": MediaType.Image,
  ".wmv": MediaType.Video
};

export const AvSourceIds: readonly string[] = [
  "airav",
  "avsex",
  "avsox",
  "cnmdb",
  "dahlia",
  "dmm",
  "fc2",
  "fc2hub",
  "faleno",
  "fantastica",
  "freejavbt",
  "getchudl",
  "iqqtv",
  "jav321",
  "javbus",
  "javday",
  "javdb",
  "javlibrary",
  "lulubar",
  "mmtv"
] as const;

export interface ProxyTestSite { id: string; name: string; url: string }

export const ProxyTestSites: readonly ProxyTestSite[] = [
  { id: "google", name: "Google", url: "https://www.google.com/generate_204" },
  { id: "bing", name: "Bing", url: "https://www.bing.com" },
  { id: "youtube", name: "YouTube", url: "https://www.youtube.com/generate_204" },
  { id: "twitter", name: "X (Twitter)", url: "https://x.com" },
  { id: "facebook", name: "Facebook", url: "https://www.facebook.com" },
  { id: "instagram", name: "Instagram", url: "https://www.instagram.com" },
  { id: "openai", name: "OpenAI", url: "https://api.openai.com" },
  { id: "claude", name: "Claude", url: "https://api.anthropic.com" },
  { id: "github", name: "GitHub", url: "https://api.github.com" },
  { id: "exhentai", name: "ExHentai", url: "https://exhentai.org" }
] as const;

export const DefaultProxyTestSiteIds: readonly string[] = ["google", "youtube", "github"] as const;

export const ProxyCapableThirdPartyIds: readonly ThirdPartyId[] = [
  ThirdPartyId.ExHentai,
  ThirdPartyId.Pixiv,
  ThirdPartyId.Bilibili,
  ThirdPartyId.DLsite,
  ThirdPartyId.Bangumi,
  ThirdPartyId.SoulPlus,
  ThirdPartyId.Tmdb
] as const;

export const DataSyncKinds: readonly string[] = ["extensionGroup", "customProperty"] as const;
export const DataSyncContractVersion = 1;
export const DataSyncMaxOptionsPerProperty = 20000;
