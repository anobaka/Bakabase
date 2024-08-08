import { useTranslation } from 'react-i18next';
import { useEffect, useRef, useState } from 'react';
import { DeleteOutlined, PlusCircleOutlined } from '@ant-design/icons';
import { useUpdate } from 'react-use';
import DynamicTargets from './components/DynamicTargets';
import FixedTargets from './components/FixedTargets';
import {
  Button,
  Input,
  Modal,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
} from '@/components/bakaui';
import { createPortalOfComponent } from '@/components/utils';
import type { EnhancerDescriptor } from '@/components/EnhancerSelectorV2/models';
import { CategoryAdditionalItem, EnhancerTargetType, SpecialTextType, StandardValueType } from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import type { EnhancerFullOptions } from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models';
import type { IProperty } from '@/components/Property/models';
import type { DestroyableProps } from '@/components/bakaui/types';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

interface IProps extends DestroyableProps {
  enhancer: EnhancerDescriptor;
  categoryId: number;
}

const CategoryEnhancerOptionsDialog = ({
                                         enhancer,
                                         categoryId,
                                         onDestroyed,
                                       }: IProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const forceUpdate = useUpdate();

  const [category, setCategory] = useState<{ id: number; name: string; customPropertyIds: number[] }>({
    id: 0,
    name: '',
    customPropertyIds: [],
  });

  const [options, setOptions] = useState<EnhancerFullOptions>();
  const [propertyMap, setPropertyMap] = useState<Record<number, IProperty>>({});

  const init = async () => {
    await loadCategory();
    await loadAllProperties();
    await loadOptions();
  };

  useEffect(() => {
    init();
  }, []);

  const loadOptions = async () => {
    const data = (await BApi.category.getCategoryEnhancerOptions(categoryId, enhancer.id)).data ?? {};
    // @ts-ignore
    setOptions(data.options);
  };

  const loadCategory = async () => {
    const r = await BApi.category.getCategory(categoryId,
      // @ts-ignore
      { additionalItems: CategoryAdditionalItem.EnhancerOptions | CategoryAdditionalItem.CustomProperties });
    setCategory({
      id: r.data?.id ?? 0,
      name: r.data?.name ?? '',
      customPropertyIds: (r.data?.customProperties ?? []).map(p => p.id!),
    });
    // @ts-ignore
    const ceo = (r.data?.enhancerOptions?.find(x => x.enhancerId == enhancer.id)?.options ?? {}) as EnhancerFullOptions;
    setOptions(ceo);
  };

  const loadAllProperties = async () => {
    const r = await BApi.customProperty.getAllCustomProperties();
    const pm = (r.data ?? []).reduce<Record<number, IProperty>>((s, t) => {
      s[t.id!] = t as IProperty;
      return s;
    }, {});
    // console.log(pm, r.data ?? []);
    setPropertyMap(pm);
    console.log('loadAllProperties', pm);
  };

  console.log(options);

  return (
    <Modal
      size={'xl'}
      title={t('Configure enhancer:{{enhancerName}} for category:{{categoryName}}', {
        enhancerName: enhancer.name,
        categoryName: category.name,
      })}
      defaultVisible
      onDestroyed={onDestroyed}
      footer={{
        actions: ['cancel'],
      }}
    >
      {options && (
        <div>
          <FixedTargets
            category={category}
            enhancer={enhancer}
            options={options}
            propertyMap={propertyMap}
          />
          <DynamicTargets
            category={category}
            enhancer={enhancer}
            options={options}
            propertyMap={propertyMap}
          />
        </div>
      )}
    </Modal>
  );
};

CategoryEnhancerOptionsDialog.show = (props: IProps) => createPortalOfComponent(CategoryEnhancerOptionsDialog, props);

export default CategoryEnhancerOptionsDialog;
