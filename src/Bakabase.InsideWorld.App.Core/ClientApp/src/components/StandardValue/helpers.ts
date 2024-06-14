import type { TFunction } from 'react-i18next';
import type { LinkValue, MultilevelData } from './models';
import { StandardValueType } from '@/sdk/constants';

export const filterMultilevelData = <V>(data: MultilevelData<V>[], keyword: string): MultilevelData<V>[] => {
  if (!keyword) {
    return data;
  }

  const result: MultilevelData<V>[] = [];
  for (const d of data) {
    if (d.label.toLowerCase().includes(keyword)) {
      result.push(d);
    } else {
      if (d.children && d.children.length > 0) {
        const children = filterMultilevelData(d.children, keyword);
        if (children.length > 0) {
          result.push({ ...d, children });
        }
      }
    }
  }
  return result;
};

export const findNodeChainInMultilevelData = <V>(data: MultilevelData<V>[], value: V): MultilevelData<V>[] | undefined => {
  for (const d of data) {
    if (d.value === value) {
      return [d];
    } else {
      if (d.children && d.children.length > 0) {
        const children = findNodeChainInMultilevelData(d.children, value);
        if (children !== undefined) {
          if (children?.length > 0) {
            return [d, ...children];
          }
        }
      }
    }
  }
  return;
};
