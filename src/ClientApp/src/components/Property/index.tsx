import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { DatabaseOutlined, DisconnectOutlined, LinkOutlined } from '@ant-design/icons';
import styles from './index.module.scss';
import type { IProperty } from './models';
import { PropertyTypeIconMap } from './models';
import Label from './components/Label';
import ClickableIcon from '@/components/ClickableIcon';
import PropertyDialog from '@/components/PropertyDialog';
import { Chip, Icon, Modal, Popover, Tooltip } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import { CustomPropertyType, ResourceProperty } from '@/sdk/constants';
import { StandardValueIcon } from '@/components/StandardValue';

interface IProps {
  property: IProperty;
  onClick?: () => any;

  removable?: boolean;
  editable?: boolean;
  editablePortal?: 'click' | 'edit-icon';
  onSaved?: (property: IProperty) => any;
  onRemoved?: () => any;
}

export {
  Label as PropertyLabel,
};

export default ({
                  property,
                  onClick,
                  editablePortal = 'edit-icon',
                  onSaved,
                  onRemoved,
                  ...props
                }: IProps) => {
  const { t } = useTranslation();

  const [removeConfirmingDialogVisible, setRemoveConfirmingDialogVisible] = useState(false);

  const editable = property.isCustom && props.editable;
  const removable = property.isCustom && props.removable;

  const icon = property.type == undefined ? undefined : PropertyTypeIconMap[property.type];

  const renderBottom = () => {
    const categories = property.categories || [];
    return (
      <div className={`${styles.bottom} mt-2 pt-2 flex flex-wrap gap-2`}>
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
            content={t('No category bound')}
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
    PropertyDialog.show({
      value: {
        ...property,
        type: property.type as unknown as CustomPropertyType,
      },
      onSaved: p => onSaved?.({
        ...p,
        isCustom: property.isCustom,
      }),
    });
  };

  return (
    <div
      key={property.id}
      className={`${styles.property} group`}
      onClick={() => {
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
      <div className={styles.line1}>
        <div className={`${styles.left} mr-2`}>
          <div className={styles.name}>{
            property.isCustom ? property.name : t(ResourceProperty[property.id])
          }</div>
          {property.isCustom ? (
            icon != undefined && (
              <Tooltip
                color={'foreground'}
                content={t(CustomPropertyType[property.type!])}
              >
                <div className={styles.type}>
                  <Icon
                    type={icon}
                    className={'text-base'}
                  />
                </div>
              </Tooltip>
            )
          ) : (
            <StandardValueIcon
              valueType={property.dbValueType}
              style={{ color: 'var(--bakaui-color)' }}
            />
          )}
        </div>
        <div className={'flex gap-0.5 items-center invisible group-hover:visible'}>
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
      </div>
      {renderBottom()}
    </div>
  );
};
