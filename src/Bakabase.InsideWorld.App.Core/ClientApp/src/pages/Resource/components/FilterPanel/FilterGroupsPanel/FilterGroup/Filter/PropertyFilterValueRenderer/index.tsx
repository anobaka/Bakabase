import { useTranslation } from 'react-i18next';
import { CustomPropertyType, SearchOperation } from '@/sdk/constants';
import {
  FilterValueRendererType,
  getFilterValueRendererType,
} from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel/FilterGroup/Filter/helpers';
import type { IProperty } from '@/components/Property/models';
import type { Props as PropertyValueRendererProps } from '@/components/Property/components/PropertyValueRenderer';
import PropertyValueRenderer from '@/components/Property/components/PropertyValueRenderer';

type Props = {
  dbValue?: string;
  bizValue?: string;
  onValueChange: (dbValue?: string, bizValue?: string) => any;
  property: IProperty;
  operation: SearchOperation;
  dataPool?: PropertyValueRendererProps['dataPool'];
};

export default ({
                  onValueChange,
                  dbValue,
                  bizValue,
                  property,
                  operation,
                  dataPool,
                }: Props) => {
  const { t } = useTranslation();

  console.log('rendering PropertyFilterValueRenderer', bizValue, dbValue, property, operation);

  if (operation == SearchOperation.IsNull || operation == SearchOperation.IsNotNull) {
    return null;
  }

  const rt = getFilterValueRendererType(property);

  if (rt) {
    const shouldRender = () => {
      switch (rt) {
        case FilterValueRendererType.Number: {
          switch (operation) {
            case SearchOperation.Equals:
            case SearchOperation.NotEquals:
            case SearchOperation.GreaterThan:
            case SearchOperation.LessThan:
            case SearchOperation.GreaterThanOrEquals:
            case SearchOperation.LessThanOrEquals: {
              // Simple number input
              return true;
            }
          }
          break;
        }
        case FilterValueRendererType.String: {
          switch (operation) {
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
              // Simple text input
              return true;
          }
        }
        case FilterValueRendererType.DateTime: {
          switch (operation) {
            case SearchOperation.Equals:
            case SearchOperation.NotEquals:
            case SearchOperation.GreaterThan:
            case SearchOperation.LessThan:
            case SearchOperation.GreaterThanOrEquals:
            case SearchOperation.LessThanOrEquals:
              // Simple date input
              return true;
          }
        }
        case FilterValueRendererType.MediaLibrary:
          switch (operation) {
            case SearchOperation.In:
            case SearchOperation.NotIn:
              // media library selector
              return true;
          }
        case FilterValueRendererType.SingleChoice: {
          switch (operation) {
            case SearchOperation.In:
            case SearchOperation.NotIn:
            case SearchOperation.Equals:
            case SearchOperation.NotEquals:
              return true;
          }
        }
        case FilterValueRendererType.MultipleChoice:
          switch (operation) {
            case SearchOperation.Contains:
            case SearchOperation.NotContains:
              return true;
          }
        case FilterValueRendererType.Boolean: {
          switch (operation) {
            case SearchOperation.Equals:
            case SearchOperation.NotEquals:
              // boolean
              return true;
          }
        }
        case FilterValueRendererType.Time: {
          switch (operation) {
            case SearchOperation.GreaterThan:
            case SearchOperation.LessThan:
            case SearchOperation.GreaterThanOrEquals:
            case SearchOperation.LessThanOrEquals:
              return true;
          }
        }
        case FilterValueRendererType.Multilevel: {
          switch (operation) {
            case SearchOperation.Contains:
            case SearchOperation.NotContains:
              return true;
          }
        }
        case FilterValueRendererType.Tags: {
          switch (operation) {
            case SearchOperation.Contains:
            case SearchOperation.NotContains:
              return true;
          }
        }
      }
      return false;
    };

    if (shouldRender()) {
      const tp = { ...property };
      if (tp.type == CustomPropertyType.MultilineText) {
        tp.type = CustomPropertyType.SingleLineText;
      }

      return (
        <PropertyValueRenderer
          property={tp}
          variant={'light'}
          bizValue={bizValue}
          dbValue={dbValue}
          onValueChange={(dbValue, bizValue) => {
            onValueChange(dbValue, bizValue);
          }}
          dataPool={dataPool}
        />
      );
    }
  }

  return (
    <span>{t('Not supported')}</span>
  );
};
