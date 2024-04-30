import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
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
import {
  CommonTargetOptions,
} from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/components/TargetOptions';
import { IntegrateWithSpecialTextLabel } from '@/components/SpecialText';

const StdValueSpecialTextIntegrationMap: { [key in StandardValueType]?: SpecialTextType } = {
  [StandardValueType.DateTime]: SpecialTextType.DateTime,
};

interface IProps {
  enhancer: EnhancerDescriptor;
  categoryId: number;
}

const CategoryEnhancerOptionsDialog = ({ enhancer, categoryId }: IProps) => {
  const { t } = useTranslation();

  const [categoryName, setCategoryName] = useState('');

  useEffect(() => {
    BApi.resourceCategory.getResourceCategory(categoryId).then(r => {
      setCategoryName(r.data!.name!);
    });
  }, []);

  return (
    <Modal
      size={'xl'}
      title={t('Configure enhancer:{{enhancerName}} for category:{{categoryName}}', { enhancerName: enhancer.name, categoryName })}
      defaultVisible
    >
      <div className={'font-bold text-large'}>
        {t('Common options')}
      </div>
      <div>
        1231321312
      </div>
      <div className={'font-bold text-large'}>
        {t('Options for targets')}
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
              return (
                <TableRow key={target.id}>
                  <TableCell>
                    <div className={'flex flex-col gap-2'}>
                      <div>
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
                          pool: 'custom',
                        });
                      }}
                    >
                      {t('Select a property')}
                    </Button>
                  </TableCell>
                  <TableCell>
                    <CommonTargetOptions integrateWithAlias />
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
