import type { IResourceDiff } from './index';
import type {
  ResourceLanguage,
} from '@/sdk/constants';
import {
  BulkModificationDiffOperation,
  BulkModificationDiffType,
  BulkModificationProperty,
} from '@/sdk/constants';

export interface IVolumeDiffValue {
  title?: string;
  name?: string;
  index: number;
}

export interface IPublisherDiffValue {
  id: number;
  name: string;
  children?: IPublisherDiffValue[];
}

export class ResourceDiffUtils {
  // Category

  static buildCategory(currentId?: number | null, newId?: number | null): IResourceDiff | undefined {
    return ResourceDiffUtils.build(BulkModificationProperty.Category, currentId, newId);
  }

  static parseCategory(rawDiffValue?: string): number | undefined {
    return ResourceDiffUtils.deserializeRawDiffValue(rawDiffValue) as number;
  }

  // MediaLibrary

  static buildMediaLibrary(currentId?: number | null, newId?: number | null): IResourceDiff | undefined {
    return ResourceDiffUtils.build(BulkModificationProperty.MediaLibrary, currentId, newId);
  }

  static parseMediaLibrary(rawDiffValue?: string): number | undefined {
    return ResourceDiffUtils.deserializeRawDiffValue(rawDiffValue) as number;
  }

  // Name

  static buildName(currentName?: string | null, newName?: string | null): IResourceDiff | undefined {
    return ResourceDiffUtils.build(BulkModificationProperty.Name, currentName, newName);
  }

  static parseName(rawDiffValue?: string): string | undefined {
    return ResourceDiffUtils.deserializeRawDiffValue(rawDiffValue) as string;
  }

  // FileName

  static buildFileName(currentFileName?: string | null, newFileName?: string | null): IResourceDiff | undefined {
    return ResourceDiffUtils.build(BulkModificationProperty.FileName, currentFileName, newFileName);
  }

  static parseFileName(rawDiffValue?: string): string | undefined {
    return ResourceDiffUtils.deserializeRawDiffValue(rawDiffValue) as string;
  }

  // DirectoryPath

  static buildDirectoryPath(currentDirectoryPath?: string | null, newDirectoryPath?: string | null): IResourceDiff | undefined {
    return ResourceDiffUtils.build(BulkModificationProperty.DirectoryPath, currentDirectoryPath, newDirectoryPath);
  }

  static parseDirectoryPath(rawDiffValue?: string): string | undefined {
    return ResourceDiffUtils.deserializeRawDiffValue(rawDiffValue) as string;
  }

  // ReleaseDt

  static buildReleaseDt(currentReleaseDt?: string | null, newReleaseDt?: string | null): IResourceDiff | undefined {
    return ResourceDiffUtils.build(BulkModificationProperty.ReleaseDt, currentReleaseDt, newReleaseDt);
  }

  static parseReleaseDt(rawDiffValue?: string): string | undefined {
    return ResourceDiffUtils.deserializeRawDiffValue(rawDiffValue) as string;
  }

  // CreateDt

  static buildCreateDt(currentCreateDt?: string | null, newCreateDt?: string | null): IResourceDiff | undefined {
    return ResourceDiffUtils.build(BulkModificationProperty.CreateDt, currentCreateDt, newCreateDt);
  }

  static parseCreateDt(rawDiffValue?: string): string | undefined {
    return ResourceDiffUtils.deserializeRawDiffValue(rawDiffValue) as string;
  }

  // FileCreateDt

  static buildFileCreateDt(currentFileCreateDt?: string | null, newFileCreateDt?: string | null): IResourceDiff | undefined {
    return ResourceDiffUtils.build(BulkModificationProperty.FileCreateDt, currentFileCreateDt, newFileCreateDt);
  }

  static parseFileCreateDt(rawDiffValue?: string): string | undefined {
    return ResourceDiffUtils.deserializeRawDiffValue(rawDiffValue) as string;
  }

// FileModifyDt

  static buildFileModifyDt(currentFileModifyDt?: string | null, newFileModifyDt?: string | null): IResourceDiff | undefined {
    return ResourceDiffUtils.build(BulkModificationProperty.FileModifyDt, currentFileModifyDt, newFileModifyDt);
  }

  static parseFileModifyDt(rawDiffValue?: string): string | undefined {
    return ResourceDiffUtils.deserializeRawDiffValue(rawDiffValue) as string;
  }

  // Publisher

  static buildPublisher(currentPublishers?: IPublisherDiffValue[] | null, newPublishers?: IPublisherDiffValue[] | null): IResourceDiff | undefined {
    return ResourceDiffUtils.build(BulkModificationProperty.Publisher, currentPublishers, newPublishers);
  }

  static parsePublisher(rawDiffValue?: string): IPublisherDiffValue[] | undefined {
    return ResourceDiffUtils.deserializeRawDiffValue(rawDiffValue) as IPublisherDiffValue[];
  }

  // Language

  static buildLanguage(currentLanguage?: ResourceLanguage | null, newLanguage?: ResourceLanguage | null): IResourceDiff | undefined {
    return ResourceDiffUtils.build(BulkModificationProperty.Language, currentLanguage, newLanguage);
  }

  static parseLanguage(rawDiffValue?: string): ResourceLanguage | undefined {
    return ResourceDiffUtils.deserializeRawDiffValue(rawDiffValue) as ResourceLanguage;
  }

  // Volume

  static buildVolume(currentValue?: IVolumeDiffValue | null, newValue?: IVolumeDiffValue | null): IResourceDiff | undefined {
    return ResourceDiffUtils.build(BulkModificationProperty.Volume, currentValue, newValue);
  }

  static parseVolume(rawDiffValue?: string): IVolumeDiffValue | undefined {
    return ResourceDiffUtils.deserializeRawDiffValue(rawDiffValue) as IVolumeDiffValue;
  }

  // Original

  static buildOriginal(currentOriginal?: {id: number; name: string}[] | null, newOriginal?: {name: string}[] | null): IResourceDiff | undefined {
    return ResourceDiffUtils.build(BulkModificationProperty.Original, currentOriginal, newOriginal);
  }

  static parseOriginal(rawDiffValue?: string): {id: number; name: string}[] | undefined {
    return ResourceDiffUtils.deserializeRawDiffValue(rawDiffValue) as {id: number; name: string}[];
  }

  // Series

  static buildSeries(currentSeries?: {name: string} | null, newSeries?: {name: string} | null): IResourceDiff | undefined {
    return ResourceDiffUtils.build(BulkModificationProperty.Series, currentSeries, newSeries);
  }

  static parseSeries(rawDiffValue?: string): {name: string} | undefined {
    return ResourceDiffUtils.deserializeRawDiffValue(rawDiffValue) as {name: string};
  }

  // Tag

  static buildTag(currentValue?: {name: string; id: number}[] | null, newValue?: {name: string; id: number}[] | null): IResourceDiff | undefined {
    return ResourceDiffUtils.build(BulkModificationProperty.Tag, currentValue, newValue);
  }

  static parseTag(rawDiffValue?: string): {name: string; id: number}[] | undefined {
    return ResourceDiffUtils.deserializeRawDiffValue(rawDiffValue) as {name: string; id: number}[];
  }

  // Introduction

  static buildIntroduction(currentIntroduction?: string, newIntroduction?: string): IResourceDiff | undefined {
    return ResourceDiffUtils.build(BulkModificationProperty.Introduction, currentIntroduction, newIntroduction);
  }

  static parseIntroduction(rawDiffValue?: string): string | undefined {
    return ResourceDiffUtils.deserializeRawDiffValue(rawDiffValue) as string;
  }

  // Rate

  static buildRate(currentRate?: number, newRate?: number): IResourceDiff | undefined {
    return ResourceDiffUtils.build(BulkModificationProperty.Rate, currentRate, newRate);
  }

  static parseRate(rawDiffValue?: string): number | undefined {
    return ResourceDiffUtils.deserializeRawDiffValue(rawDiffValue) as number;
  }

// CustomProperty

  static buildCustomProperty(propertyKey: string, currentCustomProperty?: any, newCustomProperty?: any): IResourceDiff | undefined {
    return ResourceDiffUtils.build(BulkModificationProperty.CustomProperty, currentCustomProperty, newCustomProperty, propertyKey);
  }

  static parseCustomProperty(rawDiffValue?: string): string | undefined {
    return ResourceDiffUtils.deserializeRawDiffValue(rawDiffValue) as string;
  }

  static deserializeRawDiffValue(jsonStr?: string): any | undefined {
    let value: any;
    if (jsonStr != undefined && jsonStr.length > 0) {
      value = JSON.parse(jsonStr);
    }
    return value;
  }

  private static build(property: BulkModificationProperty, currentValue?: any, newValue?: any, propertyKey?: string): IResourceDiff | undefined {
    if (currentValue == newValue) {
      return;
    }

    let type: BulkModificationDiffType;
    if (currentValue == undefined) {
      type = BulkModificationDiffType.Added;
    } else if (newValue == undefined) {
      type = BulkModificationDiffType.Removed;
    } else {
      type = BulkModificationDiffType.Modified;
    }

    return ({
      property,
      propertyKey,
      type,
      currentValue: currentValue == undefined ? undefined : JSON.stringify(currentValue),
      newValue: newValue == undefined ? undefined : JSON.stringify(newValue),
      operation: BulkModificationDiffOperation.None,
    });
  }
}
