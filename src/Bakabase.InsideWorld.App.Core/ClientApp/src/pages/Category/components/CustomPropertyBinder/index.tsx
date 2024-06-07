import { Button, Dialog, Input, Overlay, Progress, Select, Switch } from '@alifd/next';
import type { DialogProps } from '@alifd/next/types/dialog';
import { useTranslation } from 'react-i18next';
import PropertySelector from '@/components/PropertySelector';
import { createPortalOfComponent } from '@/components/utils';
import BApi from '@/sdk/BApi';

const { Popup } = Overlay;

interface IProps {
  category: { id: number; name: string; customProperties: {id: number}[] };
  onSaved?: () => any;
}

const CategoryCustomPropertyBinderDialog = ({
                                              category,
                                              onSaved,
                                            }: IProps) => {
  const { t } = useTranslation();

  return (
    <PropertySelector
      multiple
      pool={'custom'}
      selection={category.customProperties?.map(c => ({ id: c.id, isReserved: false }))}
      title={t('Binding custom properties to category {{categoryName}}', { categoryName: category.name })}
      onSubmit={async (properties) => {
        const rsp = await BApi.category.bindCustomPropertiesToCategory(category.id, { customPropertyIds: properties?.map(p => p.id) });
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
