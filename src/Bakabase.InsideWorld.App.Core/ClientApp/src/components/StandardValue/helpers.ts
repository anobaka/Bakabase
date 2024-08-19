import type { TFunction } from 'react-i18next';
import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';
import type { Duration } from 'dayjs/plugin/duration';
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

export const convertFromApiValue = (value: any | null, type: StandardValueType): any | undefined => {
  if (value == undefined) {
    return undefined;
  }

  switch (type) {
    case StandardValueType.String:
    case StandardValueType.ListString:
    case StandardValueType.Decimal:
    case StandardValueType.Link:
    case StandardValueType.Boolean:
    case StandardValueType.ListListString:
    case StandardValueType.ListTag:
      return value;
    case StandardValueType.DateTime:
      return dayjs(value);
    case StandardValueType.Time:
      return dayjs.duration(value);
  }
};

export const deserializeStandardValue = (value: string | null, type: StandardValueType): any | undefined => {
  if (value == undefined) {
    return undefined;
  }

  switch (type) {
    case StandardValueType.String:
    case StandardValueType.ListString:
    case StandardValueType.Decimal:
    case StandardValueType.Link:
    case StandardValueType.Boolean:
    case StandardValueType.ListListString:
    case StandardValueType.ListTag:
      return JSON.parse(value);
    case StandardValueType.DateTime:
      return dayjs(value);
    case StandardValueType.Time:
      return dayjs.duration(value);
  }
};

export const serializeStandardValue = (value: any | null, type: StandardValueType): string | undefined => {
  if (value == undefined) {
    return undefined;
  }

  switch (type) {
    case StandardValueType.String:
    case StandardValueType.ListString:
    case StandardValueType.Decimal:
    case StandardValueType.Link:
    case StandardValueType.Boolean:
    case StandardValueType.ListListString:
    case StandardValueType.ListTag:
      return JSON.stringify(value);
    case StandardValueType.DateTime:
      return (value as Dayjs).format('YYYY-MM-DD HH:mm:ss');
    case StandardValueType.Time:
      return (value as Duration).format('HH:mm:ss');
  }
};
