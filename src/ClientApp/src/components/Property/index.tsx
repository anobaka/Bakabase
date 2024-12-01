import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { DatabaseOutlined, DisconnectOutlined, LinkOutlined } from '@ant-design/icons';
import { useBakabaseContext } from '../ContextProvider/BakabaseContextProvider';
import styles from './index.module.scss';
import type { IProperty } from './models';
import { PropertyTypeIconMap } from './models';
import Label from './components/Label';
import ClickableIcon from '@/components/ClickableIcon';
import PropertyDialog from '@/components/PropertyDialog';
import { Chip, Icon, Modal, Tooltip } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import { PropertyPool, PropertyType, ResourceProperty } from '@/sdk/constants';

type Props = {
  property: IProperty;
  onClick?: () => any;
  disabled?: boolean;

  removable?: boolean;
  editable?: boolean;
  editablePortal?: 'click' | 'edit-icon';
  onSaved?: (property: IProperty) => any;
  onRemoved?: () => any;

  onDialogDestroyed?: () => any;
};

export {
  Label as PropertyLabel,
};

export default ({
                  property,
                  onClick,
                  editablePortal = 'edit-icon',
                  onSaved,
                  onRemoved,
                  onDialogDestroyed,
                  disabled,
                  ...props
                }: Props) => {
  const { t } = useTranslation();

  const [removeConfirmingDialogVisible, setRemoveConfirmingDialogVisible] = useState(false);
  const { createPortal } = useBakabaseContext();

  const editable = property.pool == PropertyPool.Custom && props.editable;
  const removable = property.pool == PropertyPool.Custom && props.removable;

  const icon = property.type == undefined ? undefined : PropertyTypeIconMap[property.type];

  const renderBottom = () => {
    if (property.pool != PropertyPool.Custom) {
      return null;
    }
    const categories = property.categories || [];
    return (
      <div className={`${styles.bottom} mt-1 pt-1 flex flex-wrap gap-2`}>
        {categories.length > 0 ? (
          <Tooltip
            placement={'bottom'}
            content={<div className={'flex flex-wrap gap-1 max-w-[600px]'}>
              {categories.map(c => {
                return (
                  <Chip
                    size={'sm'}
                    radius={'sm'}
                    key={c.id}
                  >
                    {c.name}
                  </Chip>
                );
              })}
            </div>}
          >
            <div className={'flex gap-0.5 items-center'}>
              <LinkOutlined className={'text-sm'} />
              {categories.length}
            </div>
            {/* <Chip */}
            {/*   radius={'sm'} */}
            {/*   size={'sm'} */}
            {/*   classNames={{}} */}
            {/* >{t('{{count}} categories', { count: categories.length })}</Chip> */}
          </Tooltip>
        ) : (
          <Tooltip
            placement={'bottom'}
            content={(
              <div>
                <div>{t('No category bound')}</div>
                <div>{t('You can bind properties in category page')}</div>
              </div>
            )}
          >
            <DisconnectOutlined className={'text-sm'} />
          </Tooltip>
        )}
        {property.valueCount != undefined && (
          <Tooltip
            placement={'bottom'}
            content={t('{{count}} values', { count: property.valueCount })}
          >
            <div className={'flex gap-0.5 items-center'}>
              <DatabaseOutlined className={'text-sm'} />
              {property.valueCount}
            </div>
          </Tooltip>
        )}
      </div>
    );
  };

  const showDetail = () => {
    createPortal(PropertyDialog, {
      value: {
        ...property,
        type: property.type as unknown as PropertyType,
      },
      onSaved: p => onSaved?.({
        ...p,
      }),
      onDestroyed: onDialogDestroyed,
    });
  };

  return (
    <div
      key={property.id}
      className={`${styles.property} group px-2 py-1 rounded ${disabled ? 'cursor-not-allowed opacity-60' : 'cursor-pointer hover:bg-[var(--bakaui-overlap-background)]'}`}
      onClick={() => {
        if (disabled) {
          return;
        }
        if (editable && editablePortal == 'click') {
          showDetail();
        }
        onClick?.();
      }}
    >
      <Modal
        visible={removeConfirmingDialogVisible}
        onClose={() => setRemoveConfirmingDialogVisible(false)}
        onOk={async () => {
          await BApi.customProperty.removeCustomProperty(property.id);
          onRemoved?.();
        }}
        title={t('Delete a property')}
      >
        {t('This operation can not be undone, are you sure?')}
      </Modal>
      <div className={`${styles.line1} flex item-center justify-between gap-1`}>
        <div className={`${styles.left}`}>
          <div className={styles.name}>{property.name}</div>
          <Tooltip
            color={'foreground'}
            content={t(PropertyType[property.type!])}
          >
            <div className={styles.type}>
              <Icon
                type={icon}
                className={'text-base'}
              />
            </div>
          </Tooltip>
        </div>
        {property.pool == PropertyPool.Custom && (
          <div className={'ml-1 flex gap-0.5 items-center invisible group-hover:visible'}>
            {editable && editablePortal == 'edit-icon' && (
              <ClickableIcon
                colorType={'normal'}
                className={'text-medium'}
                type={'edit-square'}
                onClick={e => {
                  e.preventDefault();
                  e.stopPropagation();
                  showDetail();
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
        )}
      </div>
      {renderBottom()}
    </div>
  );
};

