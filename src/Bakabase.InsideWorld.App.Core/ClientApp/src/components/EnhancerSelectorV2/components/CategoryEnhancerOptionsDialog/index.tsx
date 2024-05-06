import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { Message as Notification } from '@alifd/next';
import {
  Button,
  Checkbox,
  Modal,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
} from '@/components/bakaui';
// import { Table, TableHeader, TableColumn, TableBody, TableRow, TableCell, Modal } from '@nextui-org/react';
import { createPortalOfComponent } from '@/components/utils';
import type { EnhancerDescriptor } from '@/components/EnhancerSelectorV2/models';
import { StandardValueIcon } from '@/components/StandardValue';
import { SpecialTextType, StandardValueType } from '@/sdk/constants';
import PropertySelector from '@/components/PropertySelector';
import BApi from '@/sdk/BApi';
import { IntegrateWithSpecialTextLabel } from '@/components/SpecialText';
import type {
  EnhancerFullOptions, EnhancerTargetFullOptions } from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models';
import { defaultCategoryEnhancerTargetOptions,
} from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models';
import type { IProperty } from '@/components/Property/models';
import { PropertyLabel } from '@/components/Property';
import TargetOptions
  from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/components/TargetOptions';
import type { DestroyableProps } from '@/components/bakaui/types';

const StdValueSpecialTextIntegrationMap: { [key in StandardValueType]?: SpecialTextType } = {
  [StandardValueType.DateTime]: SpecialTextType.DateTime,
};

interface IProps extends DestroyableProps{
  enhancer: EnhancerDescriptor;
  categoryId: number;
  options?: EnhancerFullOptions;
  onChanged?: (options: EnhancerFullOptions) => void;
}

const CategoryEnhancerOptionsDialog = ({
                                         enhancer,
                                         categoryId,
                                         options: propOptions,
                                         onDestroyed,
                                         onChanged,
                                       }: IProps) => {
  const { t } = useTranslation();

  const [categoryName, setCategoryName] = useState('');

  const [options, setOptions] = useState<EnhancerFullOptions>(propOptions ?? {});
  const [propertyMap, setPropertyMap] = useState<Record<number, IProperty>>({});

  useEffect(() => {
    BApi.resourceCategory.getResourceCategory(categoryId).then(r => {
      setCategoryName(r.data!.name!);
    });

    BApi.customProperty.getAllCustomPropertiesV2().then(r => {
      setPropertyMap((r.data ?? []).reduce<Record<number, IProperty>>((s, t) => {
        s[t.id!] = t as IProperty;
        return s;
      }, {}));
    });
  }, []);

  return (
    <Modal
      size={'xl'}
      title={t('Configure enhancer:{{enhancerName}} for category:{{categoryName}}', {
        enhancerName: enhancer.name,
        categoryName,
      })}
      defaultVisible
      afterClose={onDestroyed}
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
          <TableBody items={enhancer.targets}>
            {(target) => {
              const integratedSpecialTextType = StdValueSpecialTextIntegrationMap[target.valueType];
              const targetOptions: EnhancerTargetFullOptions = options.targetOptionsMap?.[target.id] ?? defaultCategoryEnhancerTargetOptions();
              const property = targetOptions.propertyId > 0 ? propertyMap[targetOptions.propertyId] : undefined;
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
                    <Button
                      size={'sm'}
                      variant={'light'}
                      color={'primary'}
                      onClick={() => {
                        PropertySelector.show({
                          addable: true,
                          editable: true,
                          pool: 'custom',
                        });
                      }}
                    >
                      {property ? (
                        <PropertyLabel property={property} />
                      ) : t('Select a property')}
                    </Button>
                  </TableCell>
                  <TableCell>
                    <div className={'flex flex-col gap-1'}>
                      <TargetOptions
                        options={targetOptions}
                        optionsItems={target.optionsItems}
                        onChange={o => {
                          const no = {
                            ...options,
                            targetOptionsMap: {
                              ...options.targetOptionsMap,
                              [target.id]: o,
                            },
                          };
                          BApi.resourceCategory.patchCategoryEnhancerOptions(categoryId, enhancer.id, { options: no });
                          setOptions(no);
                        }}
                      />
                    </div>
                  </TableCell>
                </TableRow>
              );
            }}
          </TableBody>
        </Table>
      </div>
    </Modal>
  );
};

CategoryEnhancerOptionsDialog.show = (props: IProps) => createPortalOfComponent(CategoryEnhancerOptionsDialog, props);

export default CategoryEnhancerOptionsDialog;
