import { BulkModificationProperty } from '@/sdk/constants';

const P = BulkModificationProperty;

export const availablePropertiesForProcessing: BulkModificationProperty[] = [
  // P.Category,
  // P.MediaLibrary,
  P.Name,
  // P.FileName,
  // P.DirectoryPath,
  P.ReleaseDt,
  // P.CreateDt,
  // P.FileCreateDt,
  // P.FileModifyDt,
  P.Publisher,
  P.Language,
  P.Volume,
  P.Original,
  P.Series,
  P.Tag,
  P.Introduction,
  P.Rate,
  // P.CustomProperty,
];

export enum ProcessorType {
  Text = 1,
  DateTime,
  Number,
  Language,
  Originals,
  Volume,
  Tag,
  Publisher,
}
export const PropertyProcessorTypeMap = {
  [P.Name]: ProcessorType.Text,
  [P.Introduction]: ProcessorType.Text,
  [P.FileName]: ProcessorType.Text,
  [P.CustomProperty]: ProcessorType.Text,
  [P.Series]: ProcessorType.Text,
  [P.ReleaseDt]: ProcessorType.DateTime,
  [P.Rate]: ProcessorType.Number,
  [P.Language]: ProcessorType.Language,
  [P.Original]: ProcessorType.Originals,
  [P.Volume]: ProcessorType.Volume,
  [P.Tag]: ProcessorType.Tag,
  [P.Publisher]: ProcessorType.Publisher,
};

export const removableProperties = [
  P.Introduction, P.CustomProperty, P.Tag, P.Series, P.ReleaseDt, P.Rate, P.Language, P.Original, P.Volume, P.Publisher,
];

export const longTextProperties = [P.Introduction];
