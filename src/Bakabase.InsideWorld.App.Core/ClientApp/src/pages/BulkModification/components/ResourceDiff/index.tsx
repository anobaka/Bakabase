import { Icon } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import CustomIcon from '@/components/CustomIcon';
import {
  type BulkModificationDiffOperation,
  BulkModificationDiffType,
  BulkModificationProperty,
  ResourceLanguage,
} from '@/sdk/constants';
import SimpleLabel from '@/components/SimpleLabel';
import type { IPublisherDiffValue } from '@/pages/BulkModification/components/ResourceDiff/models';
import { ResourceDiffUtils } from '@/pages/BulkModification/components/ResourceDiff/models';

export interface IResourceDiff {
  property: BulkModificationProperty;
  propertyKey?: string;
  type: BulkModificationDiffType;
  currentValue?: any;
  newValue?: any;
  operation?: BulkModificationDiffOperation;
}

interface IProps {
  diff: IResourceDiff;
  displayDataSources?: { [key in BulkModificationProperty]?: { [key in any]: string } };
}

const TypeSimpleLabelStatusMap: { [key in BulkModificationDiffType]?: 'default' | 'primary' | 'success' | 'warning' | 'info' | 'danger' } = {
  [BulkModificationDiffType.New]: 'success',
  [BulkModificationDiffType.Changed]: 'warning',
  [BulkModificationDiffType.Removed]: 'danger',
};

export default ({
                  diff,
                  displayDataSources,
                }: IProps) => {
  const { t } = useTranslation();

  const renderPublishers = (publishers: IPublisherDiffValue[]): any => {
    return publishers.map((publisher, i) => {
      const {
        name,
        children,
      } = publisher;
      return (
        <div className={'publisher'} key={i}>
          <div className="name">{name}</div>
          {children && children.length > 0 && (
            <>
              <CustomIcon size={'xs'} type={'expand'} />
              <div className="publishers">
                {renderPublishers(children)}
              </div>
            </>
          )}
        </div>
      );
    });
  };

  const renderValue = (jsonStr: string): any => {
    const value = buildValue(jsonStr);

    if (value == undefined) {
      return (
        <div>{t('Empty value')}</div>
      );
    }
    return value;
  };

  const buildValue = (rawDiffValue?: string): any => {
    switch (diff.property) {
      case BulkModificationProperty.Category: {
        const value = ResourceDiffUtils.parseCategory(rawDiffValue);
        if (value == undefined) {
          return;
        }
        return displayDataSources?.[BulkModificationProperty.Category]?.[value] ?? t('Invalid data');
      }
      case BulkModificationProperty.MediaLibrary: {
        const value = ResourceDiffUtils.parseMediaLibrary(rawDiffValue);
        if (value == undefined) {
          return;
        }
        return displayDataSources?.[BulkModificationProperty.MediaLibrary]?.[value] ?? t('Invalid data');
      }
      case BulkModificationProperty.Name:
      {
        return ResourceDiffUtils.parseName(rawDiffValue);
      }
      case BulkModificationProperty.FileName: {
        return ResourceDiffUtils.parseFileName(rawDiffValue);
      }
      case BulkModificationProperty.DirectoryPath:
      {
        return ResourceDiffUtils.parseDirectoryPath(rawDiffValue);
      }
      case BulkModificationProperty.ReleaseDt:
      {
        return ResourceDiffUtils.parseReleaseDt(rawDiffValue);
      }
      case BulkModificationProperty.CreateDt:
      {
        return ResourceDiffUtils.parseCreateDt(rawDiffValue);
      }
      case BulkModificationProperty.FileCreateDt:
      {
        return ResourceDiffUtils.parseFileCreateDt(rawDiffValue);
      }
      case BulkModificationProperty.FileModifyDt:
      {
        return ResourceDiffUtils.parseFileModifyDt(rawDiffValue);
      }
      case BulkModificationProperty.Publisher: {
        const publishers = ResourceDiffUtils.parsePublisher(rawDiffValue);
        if (publishers && publishers.length > 0) {
          return (
            <div className={'publishers'}>
              {renderPublishers(publishers)}
            </div>
          );
        }
        break;
      }
      case BulkModificationProperty.Language: {
        const value = ResourceDiffUtils.parseLanguage(rawDiffValue);
        if (value == undefined) {
          return;
        }
        return t(ResourceLanguage[value]);
      }
      case BulkModificationProperty.Volume: {
        const volume = ResourceDiffUtils.parseVolume(rawDiffValue);
        if (volume) {
          return (
            <div className={'volume'}>
              <div className="name">
                <SimpleLabel status={'default'}>{t('Name')}</SimpleLabel>
                {volume.name}
              </div>
              <div className="title">
                <SimpleLabel status={'default'}>{t('Title')}</SimpleLabel>
                {volume.title}
              </div>
              <div className="index">
                <SimpleLabel status={'default'}>{t('Index')}</SimpleLabel>
                {volume.index}
              </div>
            </div>
          );
        } else {
          return t('Invalid data');
        }
      }
      case BulkModificationProperty.Original:
      {
        return ResourceDiffUtils.parseOriginal(rawDiffValue)?.join(', ');
      }
      case BulkModificationProperty.Series: {
        return ResourceDiffUtils.parseSeries(rawDiffValue);
      }
      case BulkModificationProperty.Tag: {
        const tagIds = ResourceDiffUtils.parseTag(rawDiffValue);
        const names = tagIds?.map(id => displayDataSources?.[BulkModificationProperty.Tag]?.[id]);
        if (names && names.length > 0) {
          return names.join(', ');
        } else {
          return t('Invalid data');
        }
      }
      case BulkModificationProperty.Introduction: {
        return ResourceDiffUtils.parseIntroduction(rawDiffValue);
      }
      case BulkModificationProperty.Rate: {
        return ResourceDiffUtils.parseRate(rawDiffValue);
      }
      case BulkModificationProperty.CustomProperty: {
        return ResourceDiffUtils.parseCustomProperty(rawDiffValue);
      }
    }
  };


  return (
    <div className="diff">
      <div className="property">
        <CustomIcon type={'segment'} size={'xs'} />
        {t(BulkModificationProperty[diff.property])}
        {diff.propertyKey != undefined ? `:${diff.propertyKey}` : undefined}
      </div>
      <SimpleLabel className="type" status={TypeSimpleLabelStatusMap[diff.type]}>
        {t(BulkModificationDiffType[diff.type])}
      </SimpleLabel>
      <div className="current">{renderValue(diff.currentValue)}</div>
      <Icon type="arrow-double-right" size={'small'} />
      <div className="new">{renderValue(diff.newValue)}</div>
    </div>
  );
};
