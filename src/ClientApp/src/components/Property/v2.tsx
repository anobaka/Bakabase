import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { DatabaseOutlined, DisconnectOutlined, LinkOutlined } from '@ant-design/icons';
import { AiOutlineDelete, AiOutlineEdit } from 'react-icons/ai';
import { useBakabaseContext } from '../ContextProvider/BakabaseContextProvider';
import styles from './index.module.scss';
import type { IProperty } from './models';
import Label from './components/Label';
import PropertyModal from '@/components/PropertyModal';
import { Button, Card, CardBody, Chip, Tooltip } from '@/components/bakaui';
import type { PropertyType } from '@/sdk/constants';
import { PropertyPool } from '@/sdk/constants';
import PropertyPoolIcon from '@/components/Property/components/PropertyPoolIcon';
import PropertyTypeIcon from '@/components/Property/components/PropertyTypeIcon';

type Props = {
  property: IProperty;
  onClick?: () => any;
  disabled?: boolean;

  hidePool?: boolean;
  hideType?: boolean;

  removable?: boolean;
  editable?: boolean;
  onSaved?: (property: IProperty) => any;
  onRemoved?: () => any;

  onDialogDestroyed?: () => any;
};

export {
  Label as PropertyLabel,
};

export default ({
                  property,
                  hidePool,
                  hideType,
                  onClick,
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
    createPortal(PropertyModal, {
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

  const actions: any[] = [];
  if (editable) {
    actions.push(
      <Button
        size={'sm'}
        variant={'light'}
        isIconOnly
        onPress={e => {
          showDetail();
        }}
      >
        <AiOutlineEdit className={'text-medium'} />
      </Button>,
    );
  }
  if (removable) {
    actions.push(
      <Button
        size={'sm'}
        color={'danger'}
        variant={'light'}
        isIconOnly
        onPress={e => {
          setRemoveConfirmingDialogVisible(true);
        }}
      >
        <AiOutlineDelete className={'text-medium'} />
      </Button>,
    );
  }

  if (actions.length > 0) {
    return (
      <Tooltip content={(
        <div className={'flex items-center gap-1'}>
          {actions}
        </div>
      )}
      >
        <Card isPressable onPress={onClick}>
          <CardBody className={'flex flex-col gap-1'}>
            {(hidePool || hideType) && (
              <div className="flex items-center gap-1">
                {!hideType && <PropertyTypeIcon type={property.type} />}
                {!hidePool && <PropertyPoolIcon pool={property.pool} />}
              </div>
            )}
            <div className={'text-medium text-left'}>{property.name}</div>
          </CardBody>
        </Card>
      </Tooltip>
    );
  }

  return (
    <Card isPressable onPress={onClick}>
      <CardBody className={'flex flex-col gap-1'}>
        {(hidePool || hideType) && (
          <div className="flex items-center gap-1">
            {!hideType && <PropertyTypeIcon type={property.type} />}
            {!hidePool && <PropertyPoolIcon pool={property.pool} />}
          </div>
        )}
        <div className={'text-medium text-left'}>{property.name}</div>
      </CardBody>
    </Card>
  );
};

