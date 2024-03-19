import { Dialog, Overlay } from '@alifd/next';
import type { DialogProps } from '@alifd/next/types/dialog';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import styles from './index.module.scss';
import type { IProperty } from '@/components/Property/models';
import Property from '@/components/Property';
import { createPortalOfComponent } from '@/components/utils';
import {
  CustomPropertyAdditionalItem,
  ResourceProperty as EnumResourceProperty,
  StandardValueType,
} from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import type { ICustomProperty } from '@/pages/CustomProperty/models';
import CustomProperty from '@/components/Property/CustomProperty';

const { Popup } = Overlay;

interface IPropertyKey {
  id: number;
  isReserved: boolean;
}

function equals(a: IPropertyKey, b: IPropertyKey) {
  return a.id === b.id && a.isReserved === b.isReserved;
}

interface IProps {
  selectedKeys?: IPropertyKey[];
  onSubmit?: (selectedReservedProperties: IProperty[], selectedCustomProperties: ICustomProperty[]) => Promise<any>;
  dialogProps?: DialogProps;
  multiple?: boolean;
  pool: 'reserved' | 'custom' | 'all';
}

const ReservedPropertyTypeMap: { [key in EnumResourceProperty]?: StandardValueType | undefined } = {
  [EnumResourceProperty.Name]: StandardValueType.SingleLineText,
  [EnumResourceProperty.FileName]: StandardValueType.SingleLineText,
  [EnumResourceProperty.DirectoryName]: StandardValueType.SingleLineText,
  [EnumResourceProperty.DirectoryPath]: StandardValueType.SingleLineText,
  [EnumResourceProperty.CreatedAt]: StandardValueType.DateTime,
  [EnumResourceProperty.ModifiedAt]: StandardValueType.DateTime,
  [EnumResourceProperty.FileCreatedAt]: StandardValueType.DateTime,
  [EnumResourceProperty.FileModifiedAt]: StandardValueType.DateTime,
  [EnumResourceProperty.Tag]: undefined,
};

const PropertySelector = ({
                            selectedKeys: propsSelectedKeys,
                            onSubmit: propsOnSubmit,
                            dialogProps,
                            multiple = true,
                            pool,
                          }: IProps) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);

  const reservedPropertiesRef = useRef(Object.keys(ReservedPropertyTypeMap).map<IProperty>(pStr => {
    const p = parseInt(pStr, 10) as EnumResourceProperty;
    return {
      id: p,
      name: t(EnumResourceProperty[p]),
      type: ReservedPropertyTypeMap[p],
      isReserved: true,
    };
  }));
  const [customProperties, setCustomProperties] = useState<ICustomProperty[]>([]);
  const [selectedKeys, setSelectedKeys] = useState<IPropertyKey[]>(propsSelectedKeys || []);
  const selectedKeysRef = useRef<IPropertyKey[]>(selectedKeys);
  const [allPropertyKeys, setAllPropertyKeys] = useState<IPropertyKey[]>([]);

  const loadProperties = async () => {
    if (pool == 'all' || pool == 'custom') {
      const rsp = await BApi.customProperty.getAllCustomPropertiesV2({ additionalItems: CustomPropertyAdditionalItem.Category });
      // @ts-ignore
      setCustomProperties((rsp.data || []));
    }
  };

  useUpdateEffect(() => {
    setAllPropertyKeys(reservedPropertiesRef.current.map(p => ({ id: p.id, isReserved: true })).concat(customProperties.map(p => ({ id: p.id!, isReserved: false }))));
  }, [customProperties]);

  useEffect(() => {
    loadProperties();
  }, []);

  useEffect(() => {
    selectedKeysRef.current = selectedKeys;
  }, [selectedKeys]);

  const close = () => {
    setVisible(false);
  };

  const renderProperty = (key: IPropertyKey) => {
    const selected = selectedKeys.some(p => equals(p, key));
    const compKey = `${key.isReserved}-${key.id}`;
    const onClick = async () => {
      console.log(12345, multiple, selected, selectedKeys);
      if (!multiple && !selected) {
        selectedKeysRef.current = [key];
        setSelectedKeys([key]);
        await onSubmit();
        close();
      } else {
        setSelectedKeys(selected
          ? multiple ? selectedKeys.filter(pId => !equals(pId, key)) : []
          : multiple ? [...selectedKeys, key] : [key]);
      }
    };
    if (key.isReserved) {
      const property = reservedPropertiesRef.current.find(p => p.id == key.id)!;
      return (
        <Property
          key={compKey}
          property={property}
          onClick={onClick}
        />
      );
    } else {
      const property = customProperties.find(p => p.id === key.id)!;
      return (
        <CustomProperty
          key={compKey}
          property={property}
          onClick={onClick}
        />
      );
    }
  };

  const onSubmit = async () => {
    if (propsOnSubmit) {
      const selectedReservedProperties = selectedKeysRef.current.filter(x => x.isReserved).map(x => reservedPropertiesRef.current.find(p => p.id === x.id)!);
      const selectedCustomProperties = selectedKeysRef.current.filter(x => !x.isReserved).map(x => customProperties.find(p => p.id === x.id)!);
      await propsOnSubmit(selectedReservedProperties, selectedCustomProperties);
    }
  };

  return (
    <Dialog
      visible={visible}
      className={styles.propertySelector}
      onClose={close}
      onCancel={close}
      v2
      closeMode={['close', 'mask', 'esc']}
      onOk={async () => {
        await onSubmit();
        close();
      }}
      title={t(multiple ? 'Select properties' : 'Select a property')}
      style={{ minWidth: '800px' }}
      footer={multiple}
      {...dialogProps}
    >
      <div className={styles.customProperties}>
        <div className={styles.selected}>
          <div className={styles.title}>{t('Selected')}</div>
          <div className={styles.list}>
            {selectedKeys?.map(key => renderProperty(key))}
          </div>
        </div>
        <div className={styles.notSelected}>
          <div className={styles.title}>{t('Not selected')}</div>
          <div className={styles.list}>
            {allPropertyKeys.filter(x => selectedKeys?.every(p => !equals(p, x))).map(key => renderProperty(key))}
          </div>
        </div>
      </div>
    </Dialog>
  );
};


PropertySelector.show = (props: IProps) => createPortalOfComponent(PropertySelector, props);

export default PropertySelector;
