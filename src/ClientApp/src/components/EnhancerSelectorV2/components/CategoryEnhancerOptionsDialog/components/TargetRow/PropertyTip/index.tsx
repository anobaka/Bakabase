import { ExclamationCircleOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import type { ReactElement } from 'react';
import React from 'react';
import { Button, Modal, Tooltip } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import type { IProperty } from '@/components/Property/models';
import { PropertyType, PropertyPool } from '@/sdk/constants';

interface IProps {
  onAllowAddingNewDataDynamicallyEnabled?: () => any;
  onPropertyBoundToCategory?: () => any;
  property: IProperty;
  category: {name: string; id: number; customPropertyIds?: number[]};
}

const PropertyTypesWithDynamicData = [
  PropertyType.Multilevel,
  PropertyType.MultipleChoice,
  PropertyType.SingleChoice,
  PropertyType.Tags,
];

export default ({ onAllowAddingNewDataDynamicallyEnabled, onPropertyBoundToCategory, property, category }: IProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const renderTips = () => {
    const tips: ReactElement[] = [];
    const propertyChoiceOptions = property?.options as {allowAddingNewDataDynamically: boolean};
    const allowAddingNewDataDynamicallyDisabled = property &&
      PropertyTypesWithDynamicData.includes(property.type!) &&
      !propertyChoiceOptions.allowAddingNewDataDynamically;
    if (allowAddingNewDataDynamicallyDisabled) {
      tips.push(
        <div className={'flex items-center gap-1'} key={1}>
          {t('Adding new data dynamically is disabled for this property, new data will not be saved.')}
          <Button
            size={'sm'}
            color={'primary'}
            variant={'light'}
            onClick={() => {
              createPortal(Modal, {
                title: t('Allow adding new data dynamically'),
                defaultVisible: true,
                onOk: async () => {
                  await BApi.customProperty.enableAddingNewDataDynamicallyForCustomProperty(property.id);
                  onAllowAddingNewDataDynamicallyEnabled?.();
                },
              });
            }}
          >
            {t('Click to enable')}
          </Button>
        </div>,
      );
    }

    if (category.customPropertyIds?.includes(property.id) != true && property.pool == PropertyPool.Custom) {
      tips.push(
        <div className={'flex items-center gap-1'} key={2}>
          {t('This property is not bound to the category, its data will not be displayed.')}
          <Button
            size={'sm'}
            color={'primary'}
            variant={'light'}
            onClick={() => {
              createPortal(Modal, {
                title: t('Bind property {{propertyName}} to category {{categoryName}}', {
                  propertyName: property.name,
                  categoryName: category.name,
                }),
                defaultVisible: true,
                onOk: async () => {
                  await BApi.category.bindCustomPropertyToCategory(category.id, property.id);
                  onPropertyBoundToCategory?.();
                },
              });
            }}
          >
            {t('Bind now')}
          </Button>
        </div>,
      );
    }

    return tips;
  };

  const tips = renderTips();
  if (tips.length) {
    return (
      <Tooltip
        content={(
          <div className={'flex flex-col gap-1'}>
            {tips}
          </div>
        )}
      >
        <ExclamationCircleOutlined
          className={'text-small cursor-pointer'}
          style={{ color: 'var(--bakaui-warning)' }}
        />
      </Tooltip>
    );
  }
  return null;
};
