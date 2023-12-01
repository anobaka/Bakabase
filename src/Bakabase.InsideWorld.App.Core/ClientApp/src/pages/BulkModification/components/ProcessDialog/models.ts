import { BulkModificationProperty } from '@/sdk/constants';

const P = BulkModificationProperty;

export const availablePropertiesForProcessing: BulkModificationProperty[] = [
  // P.Category,
  // P.MediaLibrary,
  P.Name,
  P.FileName,
  // P.DirectoryPath,
  P.ReleaseDt,
  P.CreateDt,
  P.FileCreateDt,
  P.FileModifyDt,
  P.Publisher,
  P.Language,
  P.Volume,
  P.Original,
  P.Series,
  P.Tag,
  P.Introduction,
  P.Rate,
  P.CustomProperty,
];
