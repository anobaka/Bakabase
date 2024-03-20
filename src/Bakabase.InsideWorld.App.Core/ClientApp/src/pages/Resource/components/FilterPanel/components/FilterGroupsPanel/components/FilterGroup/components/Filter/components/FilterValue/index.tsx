import { Input } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { useUpdateEffect } from 'react-use';
import type { IFilter } from '../../../../../../models';
import styles from './index.module.scss';
import { SearchOperation, StandardValueType } from '@/sdk/constants';

interface IProps {
  filter: IFilter;
}

export default ({ filter: propsFilter }: IProps) => {
  const { t } = useTranslation();
  const [filter, setFilter] = useState(propsFilter);

  useUpdateEffect(() => {
    setFilter(propsFilter);
  }, [propsFilter]);

  switch (filter.valueType!) {
    case StandardValueType.SingleLineText:
    case StandardValueType.MultilineText:
    case StandardValueType.Link:
    {
      switch (filter.operation!) {
        case SearchOperation.Equals:
        case SearchOperation.NotEquals:
        case SearchOperation.Contains:
        case SearchOperation.NotContains:
        case SearchOperation.StartsWith:
        case SearchOperation.NotStartsWith:
        case SearchOperation.EndsWith:
        case SearchOperation.NotEndsWith:
        case SearchOperation.Matches:
        case SearchOperation.NotMatches:
          return (
            <Input
              size={'small'}
              value={filter.value}
              onChange={v => {
              setFilter({
                ...filter,
                value: v,
              });
            }}
            />
          );
        case SearchOperation.IsNull:
        case SearchOperation.IsNotNull:
          return;
      }
      break;
    }
    case StandardValueType.SingleChoice:
      break;
    case StandardValueType.MultipleChoice:
      break;
    case StandardValueType.Number:
      break;
    case StandardValueType.Percentage:
      break;
    case StandardValueType.Rating:
      break;
    case StandardValueType.Boolean:
      break;
    case StandardValueType.Attachment:
      break;
    case StandardValueType.Date:
      break;
    case StandardValueType.DateTime:
      break;
    case StandardValueType.Time:
      break;
    case StandardValueType.Formula:
      break;
    case StandardValueType.Multilevel:
      break;
  }

  return (
    <div className={styles.value} />
  );
};
