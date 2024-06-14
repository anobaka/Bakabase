import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { ExclamationCircleOutlined } from '@ant-design/icons';
import { useUpdate } from 'react-use';
import {
  Button,
  Modal, Popover,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tooltip,
} from '@/components/bakaui';
// import { Table, TableHeader, TableColumn, TableBody, TableRow, TableCell, Modal } from '@nextui-org/react';
import { createPortalOfComponent } from '@/components/utils';
import type { EnhancerDescriptor } from '@/components/EnhancerSelectorV2/models';
import { StandardValueIcon } from '@/components/StandardValue';
import {
  CustomPropertyType,
  CategoryAdditionalItem,
  SpecialTextType,
  StandardValueType,
} from '@/sdk/constants';
import PropertySelector from '@/components/PropertySelector';
import BApi from '@/sdk/BApi';
import { IntegrateWithSpecialTextLabel } from '@/components/SpecialText';
import type {
  EnhancerFullOptions,
  EnhancerTargetFullOptions,
} from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models';
import {
  defaultCategoryEnhancerTargetOptions,
} from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models';
import type { ChoicePropertyOptions, IProperty } from '@/components/Property/models';
import { PropertyLabel } from '@/components/Property';
import TargetOptions
  from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/components/TargetOptions';
import type { DestroyableProps } from '@/components/bakaui/types';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import PropertyTip
  from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/components/PropertyTip';

const StdValueSpecialTextIntegrationMap: { [key in StandardValueType]?: SpecialTextType } = {
  [StandardValueType.DateTime]: SpecialTextType.DateTime,
};

interface IProps extends DestroyableProps{
  enhancer: EnhancerDescriptor;
  categoryId: number;
  options?: EnhancerFullOptions;
}

const patchTargetOptions = (options: EnhancerFullOptions, targetId: number, changes: Partial<EnhancerTargetFullOptions>): EnhancerFullOptions => {
    return {
    ...options,
    targetOptionsMap: {
      ...options.targetOptionsMap,
      [targetId]: {
        ...options.targetOptionsMap?.[targetId],
        ...changes,
      },
    },
  };
};

const CategoryEnhancerOptionsDialog = ({
                                         enhancer,
                                         categoryId,
                                         onDestroyed,
                                       }: IProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const forceUpdate = useUpdate();

  const [category, setCategory] = useState<{id: number; name: string; customPropertyIds: number[]}>({
    id: 0,
    name: '',
    customPropertyIds: [],
  });

  const [options, setOptions] = useState<EnhancerFullOptions>({});
  const [propertyMap, setPropertyMap] = useState<Record<number, IProperty>>({});

  useEffect(() => {
    loadCategory();
    loadAllProperties();
  }, []);

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
    const r = await BApi.customProperty.getAllCustomPropertiesV2();
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
      {/* <div className={'font-bold text-large'}> */}
      {/*   {t('Common options')} */}
      {/* </div> */}
      {/* <div> */}
      {/*   1231321312 */}
      {/* </div> */}
      <div className={'font-bold text-large'}>
        {t('Targets of enhancer')}
      </div>
      <div>
        <Table>
          <TableHeader>
            <TableColumn>{t('Target')}</TableColumn>
            <TableColumn>{t('Save as property')}</TableColumn>
            <TableColumn>{t('Other options')}</TableColumn>
          </TableHeader>
          <TableBody>
            {enhancer.targets.map((target) => {
              const integratedSpecialTextType = StdValueSpecialTextIntegrationMap[target.valueType];
              options.targetOptionsMap ??= {};
              let targetOptions: EnhancerTargetFullOptions = options.targetOptionsMap[target.id];
              if (targetOptions == undefined) {
                targetOptions = options.targetOptionsMap[target.id] = defaultCategoryEnhancerTargetOptions(target.optionsItems);
              }
              const property = targetOptions.propertyId != undefined ? propertyMap[targetOptions.propertyId] : undefined;
              return (
                <TableRow key={target.id}>
                  <TableCell>
                    <div className={'flex flex-col gap-2'}>
                      <div className={'flex items-center gap-1'}>
                        {target.name}
                        {integratedSpecialTextType && (
                          <IntegrateWithSpecialTextLabel type={integratedSpecialTextType} />
                        )}
                      </div>
                      <div className={'flex items-center gap-1 opacity-60'}>
                        <StandardValueIcon valueType={target.valueType} className={'text-small'} />
                        {t(`StandardValueType.${StandardValueType[target.valueType]}`)}
                      </div>
                    </div>
                  </TableCell>
                  <TableCell>
                    <div className={'flex items-center gap-1'}>
                      <Button
                        // size={'sm'}
                        variant={'light'}
                        color={property ? 'success' : 'primary'}
                        onClick={() => {
                          PropertySelector.show({
                            addable: true,
                            editable: true,
                            pool: 'custom',
                            multiple: false,
                            onSubmit: async properties => {
                              const no = patchTargetOptions(options, target.id, { propertyId: properties[0].id });
                              // console.log(no);
                              await BApi.category.patchCategoryEnhancerOptions(categoryId, enhancer.id, { options: no });
                              await loadAllProperties();
                              setOptions(no);
                            },
                          });
                        }}
                      >
                        {property ? (
                          <PropertyLabel property={property} />
                        ) : t('Select a property')}
                      </Button>
                      {property && (
                        <PropertyTip
                          property={property}
                          category={category}
                          onAllowAddingNewDataDynamicallyEnabled={loadAllProperties}
                          onPropertyBoundToCategory={loadCategory}
                        />
                      )}
                    </div>
                  </TableCell>
                  <TableCell>
                    <div className={'flex flex-col gap-1'}>
                      <TargetOptions
                        options={targetOptions}
                        optionsItems={target.optionsItems}
                        onChange={o => {
                          delete o.propertyId;
                          const no = patchTargetOptions(options, target.id, o);
                          // console.log(options, target.id, o, no);
                          BApi.category.patchCategoryEnhancerOptions(categoryId, enhancer.id, { options: no });
                          setOptions(no);
                        }}
                      />
                    </div>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </div>
    </Modal>
  );
};

CategoryEnhancerOptionsDialog.show = (props: IProps) => createPortalOfComponent(CategoryEnhancerOptionsDialog, props);

export default CategoryEnhancerOptionsDialog;
