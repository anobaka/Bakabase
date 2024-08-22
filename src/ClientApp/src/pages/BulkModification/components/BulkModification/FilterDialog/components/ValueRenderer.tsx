import { useEffect, useState } from 'react';
import { Tag } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import {
  BulkModificationProperty,
  MediaLibraryAdditionalItem,
  ResourceLanguage,
  TagAdditionalItem,
} from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import { Tag as TagDto } from '@/core/models/Tag';

interface IProps {
  property: BulkModificationProperty;
  target?: string;
  onChange?: (value?: string) => any;
  removable?: boolean;
}

function buildDataSource(target?: string) {
  if (target == undefined) {
    return [];
  }
  const typedTarget = JSON.parse(target);
  const arr = Array.isArray(typedTarget) ? typedTarget : [typedTarget];
  const data = arr.map((item: string) => ({
    label: item,
    value: item,
  }));
  return data.filter((d, i) => data.findIndex(dd => dd.value == d.value) == i);
}

export default ({
                  property,
                  target,
                  onChange,
                  removable,
                }: IProps) => {
  const { t } = useTranslation();
  const [dataSource, setDataSource] = useState<{
    label: string;
    value: any;
  }[]>(buildDataSource(target));

  // console.log(target, buildDataSource(target), dataSource);

  useEffect(() => {
    const newDs = buildDataSource(target);
    console.log(newDs);
    switch (property) {
      case BulkModificationProperty.Category: {
        BApi.category.getAllResourceCategories().then(r => {
          const newDataSource = r.data?.filter(c => newDs.some(d => d.value == c.id!.toString())).map(item => ({
            label: item.name!,
            value: item.id!,
          }));
          setDataSource(newDataSource || []);
        });
        break;
      }
      case BulkModificationProperty.MediaLibrary: {
        BApi.mediaLibrary.getAllMediaLibraries({ additionalItems: MediaLibraryAdditionalItem.Category }).then(r => {
          const newDataSource = r.data?.filter(c => newDs.some(d => d.value == c.id!.toString())).map(item => ({
            label: `${item.category?.name}:${item.name}`,
            value: item.id!,
          }));
          setDataSource(newDataSource || []);
        });
        break;
      }
      // case BulkModificationProperty.Tag: {
      //   // @ts-ignore
      //   BApi.tag.getTagByIds({ ids: newDs.map(d => d.value), additionalItems: (TagAdditionalItem.GroupName | TagAdditionalItem.PreferredAlias) }).then(r => {
      //     const newDataSource = r.data?.map(item => ({
      //       // @ts-ignore
      //       label: new TagDto(item).displayName,
      //       value: item.id!,
      //     }));
      //     setDataSource(newDataSource || []);
      //   });
      //   break;
      // }
      case BulkModificationProperty.Language: {
        setDataSource(newDs.map(d => ({
          ...d,
          label: t(ResourceLanguage[d.value]),
        })));
        break;
      }
      case BulkModificationProperty.CustomProperty:
      case BulkModificationProperty.Name:
      case BulkModificationProperty.FileName:
      case BulkModificationProperty.DirectoryPath:
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
      case BulkModificationProperty.Tag:
      default:
        setDataSource(newDs || []);
        break;
    }
  }, [target]);


  if (dataSource.length == 0) {
    return null;
  } else {
    return (
      <Tag.Group>
        {dataSource.map(d => (
          removable ? (
            <Tag.Closeable
              size={'small'}
              key={d.value}
              onClose={f => {
                const newDataSource = dataSource.filter(item => item.value != d.value);
                setDataSource(newDataSource);
                onChange && onChange(JSON.stringify(newDataSource.map(item => item.value)));
                return true;
              }}
            >
              {d.label}
            </Tag.Closeable>
          ) : (
            <Tag.Selectable
              checked
              size={'small'}
              key={d.value}
            >
              {d.label}
            </Tag.Selectable>
          )
        ))}
      </Tag.Group>
    );
  }
};
