import { useEffect, useState } from 'react';
import { Tag } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { set } from 'immer/dist/utils/common';
import { BulkModificationProperty, ResourceLanguage, TagAdditionalItem } from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import { Tag as TagDto } from '@/core/models/Tag';

interface IProps {
  property: BulkModificationProperty;
  target?: string;
}

export default ({
                  property,
                  target,
                }: IProps) => {
  const { t } = useTranslation();
  const [labels, setLabels] = useState<string[]>([]);

  useEffect(() => {
    const typedTarget = target == undefined ? [] : JSON.parse(target);
    const values: any[] = Array.isArray(typedTarget) ? typedTarget : typedTarget == undefined ? [] : [typedTarget];
    // console.log(target, property, values, t(ResourceLanguage[ResourceLanguage.NotSet]));

    switch (property) {
      case BulkModificationProperty.Category: {
        BApi.resourceCategory.getAllResourceCategories().then(r => {
          const newLabels = r.data?.filter(c => values.some(d => d == c.id!.toString())).map(item => item.name!);
          setLabels(newLabels || []);
        });
        break;
      }
      case BulkModificationProperty.MediaLibrary: {
        BApi.mediaLibrary.getAllMediaLibraries().then(r => {
          const newLabels = r.data?.filter(c => values.some(d => d == c.id!.toString())).map(item => `${item.categoryName}:${item.name}`);
          setLabels(newLabels || []);
        });
        break;
      }
      // case BulkModificationProperty.Tag: {
      //   // @ts-ignore
      //   BApi.tag.getTagByIds({ ids: values, additionalItems: (TagAdditionalItem.GroupName | TagAdditionalItem.PreferredAlias) }).then(r => {
      //     // @ts-ignore
      //     const newLabels = r.data?.filter(c => values.some(d => d == c.id!.toString())).map(item => new TagDto(item).displayName);
      //     setLabels(newLabels || []);
      //   });
      //   break;
      // }
      case BulkModificationProperty.Language: {
        setLabels(values.map(d => t(ResourceLanguage[d])));
        break;
      }
      case BulkModificationProperty.Tag:
      case BulkModificationProperty.CustomProperty:
      case BulkModificationProperty.Name:
      case BulkModificationProperty.FileName:
      case BulkModificationProperty.ReleaseDt:
      case BulkModificationProperty.CreateDt:
      case BulkModificationProperty.FileCreateDt:
      case BulkModificationProperty.FileModifyDt:
      case BulkModificationProperty.Publisher:
      case BulkModificationProperty.Volume:
      case BulkModificationProperty.Original:
      case BulkModificationProperty.Series:
      case BulkModificationProperty.Introduction:
      case BulkModificationProperty.Rate:
      default:
        setLabels(values);
        break;
    }
  }, [target]);


  return (
    <>{labels.join(',')}</>
  );
};
