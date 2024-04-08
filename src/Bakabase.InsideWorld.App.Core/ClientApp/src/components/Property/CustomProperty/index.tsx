import { Dialog } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import Property from '../index';
import type { ICustomProperty } from '@/pages/CustomProperty/models';
import ClickableIcon from '@/components/ClickableIcon';
import PropertyDialog from '@/pages/CustomProperty/components/PropertyDialog';
import BApi from '@/sdk/BApi';
import styles from '@/components/Property/index.module.scss';
import SimpleLabel from '@/components/SimpleLabel';
import { convertCustomPropertyToProperty } from '@/components/Property/CustomProperty/helpers';

interface IProps {
  property: ICustomProperty;
  onSaved?: (property: ICustomProperty) => any;
  onRemoved?: () => any;
  onClick?: () => any;
}

export default ({
                  property,
                  onSaved,
                  onRemoved,
                  onClick,
                }: IProps) => {
  const { t } = useTranslation();
  return (
    <Property
      property={convertCustomPropertyToProperty(property)}
      onClick={onClick}
      renderBottom={() => (
        <div className={styles.categories}>
          {property.categories?.map(c => {
            return (
              <SimpleLabel key={c.id} className={styles.category}>
                {c.name}
              </SimpleLabel>
            );
          }) ?? t('No category bound')}
        </div>
      )}
      renderTopRight={() => (
        <>
          <ClickableIcon
            colorType={'normal'}
            className={'text-medium'}
            type={'edit-square'}
            onClick={e => {
              e.preventDefault();
              e.stopPropagation();
              PropertyDialog.show({
              value: property,
              onSaved: p => onSaved?.(p),
            });
          }}
          />
          <ClickableIcon
            colorType={'danger'}
            className={'text-medium'}
            type={'delete'}
            onClick={async (e) => {
            e.preventDefault();
            e.stopPropagation();
            Dialog.confirm({
              title: t('Removing property'),
              content: t('This operation can not be undone, are you sure?'),
              v2: true,
              width: 'auto',
              closeMode: ['close', 'mask', 'esc'],
              onOk: async () => {
                await BApi.customProperty.removeCustomProperty(property.id);
                onRemoved?.();
              },
            });
          }}
          />
        </>
    )}
    />
  );
};
