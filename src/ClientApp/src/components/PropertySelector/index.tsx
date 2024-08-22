import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import PropertyDialog from '../PropertyDialog';
import type { IProperty } from '@/components/Property/models';
import Property from '@/components/Property';
import { createPortalOfComponent } from '@/components/utils';
import type { CustomPropertyType,
  ResourceProperty as EnumResourceProperty } from '@/sdk/constants';
import {
  CustomPropertyAdditionalItem,
  StandardValueType,
} from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import store from '@/store';
import { Button, Chip, Modal, Spacer } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';

interface IKey {
  id: number;
  isCustom: boolean;
}

interface IProps extends DestroyableProps{
  selection?: IKey[];
  onSubmit?: (selectedProperties: IProperty[]) => Promise<any>;
  multiple?: boolean;
  pool: 'reserved' | 'custom' | 'all';
  valueTypes?: StandardValueType[];
  editable?: boolean;
  addable?: boolean;
  removable?: boolean;
  title?: any;
}

const PropertySelector = ({
                            selection: propsSelection,
                            onSubmit: propsOnSubmit,
                            multiple = true,
                            pool,
                            valueTypes,
                            addable,
                            editable,
                            removable,
                            title,
                            ...props
                          }: IProps) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);

  const internalOptions = store.useModelState('internalOptions');
  const [properties, setProperties] = useState<IProperty[]>([]);
  const initializedRef = useRef(false);
  const [selection, setSelection] = useState<IKey[]>(propsSelection || []);

  console.log('props selection', propsSelection, properties, addable, editable, removable);

  useEffect(() => {
    console.log(internalOptions.initialized, initializedRef.current);

    if (internalOptions.initialized && !initializedRef.current) {
      initializedRef.current = true;
      loadProperties();
    }
  }, [internalOptions]);

  const loadProperties = async () => {
    const arr: IProperty[] = [];
    if (pool == 'all' || pool == 'reserved') {
      const map = internalOptions.resource.reservedResourcePropertyAndValueTypesMap || {};
      arr.push(
        ...Object.keys(map).map<IProperty>(pStr => {
          const p = parseInt(pStr, 10) as EnumResourceProperty;
          return {
            id: p,
            dbValueType: map[p].dbValueType,
            isCustom: false,
            bizValueType: map[p].bizValueType,
          };
        }),
      );
    }
    if (pool == 'all' || pool == 'custom') {
      const rsp = await BApi.customProperty.getAllCustomProperties({ additionalItems: CustomPropertyAdditionalItem.Category });
      // @ts-ignore
      arr.push(...(rsp.data || []).map(d => ({
        ...d,
        isCustom: true,
      })));
    }
    setProperties(arr);
    console.log('reloaded', arr);
  };

  useEffect(() => {
  }, []);

  const close = () => {
    setVisible(false);
  };

  const renderProperty = (property: IProperty) => {
    const selected = selection.some(s => s.id == property.id && s.isCustom == property.isCustom);
    // console.log(id, isReserved, reservedProperties, customProperties);
    return (
      <Property
        key={`${property.id}-${property.isCustom}`}
        property={property}
        onClick={async () => {
          if (multiple) {
            if (selected) {
              setSelection(selection.filter(s => s.id != property.id && s.isCustom == property.isCustom));
            } else {
              setSelection([...selection, {
                id: property.id,
                isCustom: property.isCustom,
              }]);
            }
          } else {
            if (selected) {
              setSelection([]);
            } else {
              const ns: IKey[] = [{
                id: property.id,
                isCustom: property.isCustom,
              }];
              setSelection(ns);
              await onSubmit(ns);
              close();
            }
          }
        }}
        editable={editable}
        removable={removable}
        onSaved={loadProperties}
      />
    );
  };

  const onSubmit = async (selection: IKey[]) => {
    // console.log(customProperties, selection);
    if (propsOnSubmit) {
      await propsOnSubmit(selection.map(s => properties.find(p => p.id == s.id && p.isCustom == s.isCustom)).filter(x => x != undefined) as IProperty[]);
    }
  };

  // console.log('render', reservedProperties, customProperties);

  const renderFilter = () => {
    const filters: any[] = [];
    if (pool != 'all') {
      filters.push(
        <Chip
          key={'pool'}
          size={'sm'}
        >{t(pool == 'reserved' ? 'Reserved properties' : 'Custom properties')}</Chip>,
      );
    }
    if (valueTypes) {
      filters.push(
        ...valueTypes.map(vt => (<Chip
          key={vt}
          size={'sm'}
        >{t(StandardValueType[vt])}</Chip>)),
      );
    }

    if (filters.length > 0) {
      return (
        <div className={'flex gap-1 items-center mb-2 flex-wrap'}>
          {t('Filtering')}
          <Spacer />
          {filters}
        </div>
      );
    } else {
      return null;
    }
  };

  const filteredProperties = properties.filter(p => {
    if (valueTypes) {
      return valueTypes.includes(p.dbValueType);
    }
    return true;
  });
  const selectedProperties = selection.map(s => filteredProperties.find(p => p.id == s.id && p.isCustom == s.isCustom)).filter(x => x).map(x => x!);
  const unselectedProperties = filteredProperties.filter(p => !selection.some(s => s.id == p.id && s.isCustom == p.isCustom));
  const propertyCount = selectedProperties.length + unselectedProperties.length;

  const renderProperties = () => {
    if (propertyCount == 0) {
      return (
        <div className={'flex items-center justify-center gap-2 mt-6'}>
          {t('No properties available')}
          {addable && (
            <Button
              color={'primary'}
              size={'sm'}
              onClick={() => {
                PropertyDialog.show({
                  onSaved: loadProperties,
                  validValueTypes: valueTypes?.map(v => v as unknown as CustomPropertyType),
                });
              }}
            >
              {t('Add a property')}
            </Button>
          )}
        </div>
      );
    }

    return (
      <>
        {addable && (
          <Button
            color={'primary'}
            size={'sm'}
            className={'mb-2'}
            onClick={() => {
              PropertyDialog.show({
                onSaved: loadProperties,
                validValueTypes: valueTypes?.map(v => v as unknown as CustomPropertyType),
              });
            }}
          >
            {t('Add a property')}
          </Button>
        )}
        <div className={'flex gap-2'}>
          <div className={'border-1 rounded p-2'}>
            <div className={'font-bold'}>{t('Selected')}</div>
            <div className={'mt-2 flex flex-wrap gap-2 items-start'}>
              {selectedProperties.map(p => renderProperty(p))}
            </div>
          </div>
          <div className={'border-1 rounded p-2 flex-1 border-dashed'}>
            <div className={'font-bold'}>{t('Not selected')}</div>
            <div className={'mt-2 flex flex-wrap gap-2 items-start'}>
              {unselectedProperties.map(p => renderProperty(p))}
            </div>
          </div>
        </div>
      </>
    );
  };

  return (
    <Modal
      size={'xl'}
      visible={visible}
      onClose={close}
      onOk={async () => {
        await onSubmit(selection);
        close();
      }}
      onDestroyed={props.onDestroyed}
      title={title ?? t(multiple ? 'Select properties' : 'Select a property')}
      footer={(multiple === true && propertyCount > 0) ? true : (<Spacer />)}
    >
      <div>
        {renderFilter()}
        {renderProperties()}
      </div>
    </Modal>
  );
};


PropertySelector.show = (props: IProps) => createPortalOfComponent(PropertySelector, props);

export default PropertySelector;
