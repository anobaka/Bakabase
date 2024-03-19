import { Button, Dialog, Input, Overlay, Progress, Select, Switch } from '@alifd/next';
import type { DialogProps } from '@alifd/next/types/dialog';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import PropertySelector from '@/components/PropertySelector';
import { createPortalOfComponent } from '@/components/utils';
import { CustomPropertyAdditionalItem, CustomPropertyType } from '@/sdk/constants';
import CustomIcon from '@/components/CustomIcon';
import BApi from '@/sdk/BApi';
import type { ICustomProperty } from '@/pages/CustomProperty/models';
import SingleProperty from '@/pages/CustomProperty/components/SingleProperty';

const { Popup } = Overlay;

interface IProps extends DialogProps {
  category: { id: number; name: string; customProperties: {id: number}[] };
  onSaved?: () => any;
  dialogProps?: DialogProps;
}

const CategoryCustomPropertyBinderDialog = ({
                                              category,
                                              onSaved,
                                              ...dialogProps
                                            }: IProps) => {
  const { t } = useTranslation();

  return (
    <PropertySelector
      multiple
      pool={'custom'}
      selectedIds={category.customProperties?.map(c => c.id)}
      dialogProps={{
        title: t('Binding custom properties to category {{categoryName}}', { categoryName: category.name }),
        ...dialogProps,
      }}
      onSubmit={async (properties) => {
        const rsp = await BApi.resourceCategory.bindCustomPropertiesToCategory(category.id, { customPropertyIds: properties.map(p => p.id) });
        if (!rsp.code) {
          onSaved?.();
        } else {
          throw rsp;
        }
      }}
    />
  );
};


CategoryCustomPropertyBinderDialog.show = (props: IProps) => createPortalOfComponent(CategoryCustomPropertyBinderDialog, props);

export default CategoryCustomPropertyBinderDialog;
