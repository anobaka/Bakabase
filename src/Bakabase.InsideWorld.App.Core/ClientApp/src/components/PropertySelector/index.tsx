import { Dialog, Overlay } from '@alifd/next';
import type { DialogProps } from '@alifd/next/types/dialog';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
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
import store from '@/store';

const { Popup } = Overlay;

interface ISelection {
  reservedPropertyIds?: number[];
  customPropertyIds?: number[];
}

interface IProps {
  selection?: ISelection;
  onSubmit?: (selectedProperties: {
    reservedProperties?: IProperty[];
    customProperties?: ICustomProperty[];
  }) => Promise<any>;
  dialogProps?: DialogProps;
  multiple?: boolean;
  pool: 'reserved' | 'custom' | 'all';
}

const PropertySelector = ({
                            selection: propsSelection,
                            onSubmit: propsOnSubmit,
                            dialogProps,
                            multiple = true,
                            pool,
                          }: IProps) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);

  const reservedOptions = store.useModelState('reservedOptions');

  const [reservedProperties, setReservedProperties] = useState<IProperty[]>([]);
  const [customProperties, setCustomProperties] = useState<ICustomProperty[]>([]);

  const [selection, setSelection] = useState<ISelection>(propsSelection || {});

  useEffect(() => {
    const map = reservedOptions.resource.reservedResourcePropertyAndValueTypeMap || {};
    setReservedProperties(Object.keys(map).map<IProperty>(pStr => {
      const p = parseInt(pStr, 10) as EnumResourceProperty;
      return {
        id: p,
        name: t(EnumResourceProperty[p]),
        type: map[p],
        isReserved: true,
      };
    }));
  }, [reservedOptions]);

  const loadProperties = async () => {
    if (pool == 'all' || pool == 'custom') {
      const rsp = await BApi.customProperty.getAllCustomPropertiesV2({ additionalItems: CustomPropertyAdditionalItem.Category });
      // @ts-ignore
      setCustomProperties((rsp.data || []));
    }
  };

  useEffect(() => {
    loadProperties();
  }, []);

  const close = () => {
    setVisible(false);
  };

  const renderProperty = (id: number, isReserved: boolean) => {
    const selectedIds = (isReserved ? selection?.reservedPropertyIds : selection?.customPropertyIds) || [];
    const selected = selectedIds?.includes(id);
    const compKey = `${isReserved}-${id}`;
    const onClick = async () => {
      const newSelection: ISelection = {
        ...selection,
        [isReserved ? 'reservedPropertyIds' : 'customPropertyIds']:
          multiple
            ? selected
              ? selectedIds.filter(x => x != id) : [id, ...(selectedIds)]
            : selected
              ? [] : [id],
      };

      setSelection(newSelection);
      if (!multiple && !selected) {
        await onSubmit(newSelection);
        close();
      }
    };

    // console.log(id, isReserved, reservedProperties, customProperties);

    if (isReserved) {
      const property = reservedProperties.find(p => p.id == id)!;
      if (property) {
        return (
          <Property
            key={compKey}
            property={property}
            onClick={onClick}
          />
        );
      }
    } else {
      const property = customProperties.find(p => p.id === id)!;
      if (property) {
        return (
          <CustomProperty
            key={compKey}
            property={property}
            onClick={onClick}
          />
        );
      }
    }
    return;
  };

  const onSubmit = async (selection: ISelection) => {
    // console.log(customProperties, selection);
    if (propsOnSubmit) {
      const rps = selection.reservedPropertyIds?.map(x => reservedProperties.find(p => p.id === x)).filter(x => x).map(x => x!);
      const cps = selection.customPropertyIds?.map(x => customProperties?.find(p => p.id === x)).filter(x => x).map(x => x!);
      await propsOnSubmit({
        reservedProperties: rps,
        customProperties: cps,
      });
    }
  };

  console.log('render', reservedProperties, customProperties);

  return (
    <Dialog
      visible={visible}
      className={styles.propertySelector}
      onClose={close}
      onCancel={close}
      v2
      closeMode={['close', 'mask', 'esc']}
      onOk={async () => {
        await onSubmit(selection);
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
            {selection?.reservedPropertyIds?.map(id => renderProperty(id, true))}
            {selection?.customPropertyIds?.map(id => renderProperty(id, false))}
          </div>
        </div>
        <div className={styles.notSelected}>
          <div className={styles.title}>{t('Not selected')}</div>
          <div className={styles.list}>
            {(pool == 'all' || pool == 'reserved') && (
              reservedProperties.filter(x => !selection?.reservedPropertyIds?.includes(x.id)).map(r => renderProperty(r.id, true))
            )}
            {(pool == 'all' || pool == 'custom') && (
              customProperties.filter(x => !selection?.customPropertyIds?.includes(x.id)).map(r => renderProperty(r.id, false))
            )}
          </div>
        </div>
      </div>
    </Dialog>
  );
};


PropertySelector.show = (props: IProps) => createPortalOfComponent(PropertySelector, props);

export default PropertySelector;
