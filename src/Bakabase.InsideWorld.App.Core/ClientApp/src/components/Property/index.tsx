import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import styles from './index.module.scss';
import type { IProperty } from './models';
import { PropertyTypeIconMap } from './models';
import Label from './components/Label';
import CustomIcon from '@/components/CustomIcon';
import ClickableIcon from '@/components/ClickableIcon';
import PropertyDialog from '@/components/PropertyDialog';
import { Modal, Tooltip } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import SimpleLabel from '@/components/SimpleLabel';
import { CustomPropertyType, ResourceProperty } from '@/sdk/constants';
import type { StandardValueType } from '@/sdk/constants';
import { StandardValueIcon } from '@/components/StandardValue';

interface IProps {
  property: IProperty;
  onClick?: () => any;

  removable?: boolean;
  editable?: boolean;
  onSaved?: (property: IProperty) => any;
  onRemoved?: () => any;
}

export {
  Label as PropertyLabel,
};

export default ({
                  property,
                  onClick,
                  onSaved,
                  onRemoved,
                  ...props
                }: IProps) => {
  const { t } = useTranslation();

  const [removeConfirmingDialogVisible, setRemoveConfirmingDialogVisible] = useState(false);

  const editable = !property.isReserved && props.editable;
  const removable = !property.isReserved && props.removable;

  const icon = property.valueType == undefined ? undefined : PropertyTypeIconMap[property.valueType];

  return (
    <div
      key={property.id}
      className={`${styles.property} group`}
      onClick={onClick}
    >
      <Modal
        visible={removeConfirmingDialogVisible}
        onClose={() => setRemoveConfirmingDialogVisible(false)}
        onOk={async () => {
          await BApi.customProperty.removeCustomProperty(property.id);
          onRemoved?.();
        }}
        title={t('Removing property')}
      >
        {t('This operation can not be undone, are you sure?')}
      </Modal>
      <div className={styles.line1}>
        <div className={`${styles.left} mr-2`}>
          <div className={styles.name}>{
            property.isReserved ? t(ResourceProperty[property.id]) : property.name
          }</div>
          {property.isReserved ? (
            <StandardValueIcon
              valueType={property.valueType}
              style={{ color: 'var(--bakaui-color)' }}
            />
          ) : (
            icon != undefined && (
              <Tooltip
                color={'foreground'}
                content={t(CustomPropertyType[property.type!])}
              >
                <div className={styles.type}>
                  <CustomIcon
                    type={icon}
                    className={'text-small'}
                  />
                </div>
              </Tooltip>
            )
          )}
        </div>
        <div className={'flex gap-0.5 items-center invisible group-hover:visible'}>
          {editable && (
            <ClickableIcon
              colorType={'normal'}
              className={'text-medium'}
              type={'edit-square'}
              onClick={e => {
                e.preventDefault();
                e.stopPropagation();
                PropertyDialog.show({
                  value: {
                    ...property,
                    type: property.type as unknown as CustomPropertyType,
                  },
                  onSaved: p => onSaved?.({
                    ...p,
                    valueType: property.valueType as unknown as StandardValueType,
                    isReserved: property.isReserved,
                  }),
                });
              }}
            />
          )}
          {removable && (
            <ClickableIcon
              colorType={'danger'}
              className={'text-medium'}
              type={'delete'}
              onClick={async (e) => {
                e.preventDefault();
                e.stopPropagation();
                setRemoveConfirmingDialogVisible(true);
              }}
            />
          )}
        </div>
      </div>
      <div className={`${styles.categories} flex flex-wrap gap-1`}>
        {property.categories?.map(c => {
          return (
            <SimpleLabel key={c.id} className={styles.category}>
              {c.name}
            </SimpleLabel>
          );
        }) ?? t('No category bound')}
      </div>
    </div>
  );
};
