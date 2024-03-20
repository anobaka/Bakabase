import { Button, Dialog, Input, Overlay, Progress, Select, Switch } from '@alifd/next';
import type { DialogProps } from '@alifd/next/types/dialog';
import { useTranslation } from 'react-i18next';
import PropertySelector from '@/components/PropertySelector';
import { createPortalOfComponent } from '@/components/utils';
import BApi from '@/sdk/BApi';

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
      selection={{ customPropertyIds: category.customProperties?.map(c => c.id) }}
      dialogProps={{
        title: t('Binding custom properties to category {{categoryName}}', { categoryName: category.name }),
        ...dialogProps,
      }}
      onSubmit={async (selectedProperties) => {
        const rsp = await BApi.resourceCategory.bindCustomPropertiesToCategory(category.id, { customPropertyIds: selectedProperties?.customProperties?.map(p => p.id) });
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
