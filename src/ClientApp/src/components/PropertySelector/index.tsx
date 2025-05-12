import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import PropertyModal from '../PropertyModal';
import type { IProperty } from '@/components/Property/models';
import Property from '@/components/Property';
import { buildLogger, createPortalOfComponent } from '@/components/utils';
import type { PropertyType } from '@/sdk/constants';
import { PropertyPool, StandardValueType } from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import { Button, Chip, Divider, Modal, Spacer, Tab, Tabs } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

type Key = {
  id: number;
  pool: PropertyPool;
};

interface IProps extends DestroyableProps{
  selection?: Key[];
  onSubmit?: (selectedProperties: IProperty[]) => Promise<any>;
  multiple?: boolean;
  pool: PropertyPool;
  valueTypes?: StandardValueType[];
  editable?: boolean;
  addable?: boolean;
  removable?: boolean;
  title?: any;
  isDisabled?: (p: IProperty) => boolean;
}

const log = buildLogger('PropertySelector');

const PropertySelector = (props: IProps) => {
  log('props', props);
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const {
    selection: propsSelection,
    onSubmit: propsOnSubmit,
    multiple = true,
    pool,
    valueTypes,
    addable,
    editable,
    removable,
    title,
    onDestroyed,
    isDisabled,
  } = props;

  const [properties, setProperties] = useState<IProperty[]>([]);
  const [selection, setSelection] = useState<Key[]>(propsSelection || []);

  // console.log('props selection', propsSelection, properties, addable, editable, removable);

  const loadProperties = async () => {
    const psr = (await BApi.property.getPropertiesByPool(pool)).data || [];
    // @ts-ignore
    setProperties(psr);
  };

  useEffect(() => {
    loadProperties();
  }, []);

  const renderProperty = (property: IProperty) => {
    const selected = selection.some(s => s.id == property.id && s.pool == property.pool);
    return (
      <Property
        key={`${property.id}-${property.pool}`}
        property={property}
        onClick={async () => {
          if (multiple) {
            if (selected) {
              setSelection(selection.filter(s => s.id != property.id && s.pool == property.pool));
            } else {
              setSelection([...selection, {
                id: property.id,
                pool: property.pool,
              }]);
            }
          } else {
            if (selected) {
              setSelection([]);
            } else {
              const ns: Key[] = [{
                id: property.id,
                pool: property.pool,
              }];
              setSelection(ns);
              await onSubmit(ns);
            }
          }
        }}
        editable={editable}
        removable={removable}
        onSaved={loadProperties}
        disabled={isDisabled?.(property)}
      />
    );
  };

  const onSubmit = async (selection: Key[]) => {
    // console.log(customProperties, selection);
    if (propsOnSubmit) {
      await propsOnSubmit(selection.map(s => properties.find(p => p.id == s.id && p.pool == s.pool))
        .filter(x => x != undefined) as IProperty[]);
    }
  };

  // console.log('render', reservedProperties, customProperties);

  const renderFilter = () => {
    const filters: any[] = [];
    if (pool != PropertyPool.All) {
      Object.keys(PropertyPool).forEach(k => {
        const v = parseInt(k, 10) as PropertyPool;
        if (Number.isNaN(v)) {
          return;
        }
        if (pool & v) {
          switch (v) {
            case PropertyPool.Internal:
            case PropertyPool.Reserved:
            case PropertyPool.Custom:
              filters.push(
                <Chip
                  key={'pool'}
                  size={'sm'}
                >{t(PropertyPool[v])}</Chip>,
              );
              break;
            default:
              break;
          }
        }
      });
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
  const selectedProperties = selection.map(s => filteredProperties.find(p => p.id == s.id && p.pool == s.pool))
    .filter(x => x).map(x => x!);
  const unselectedProperties = filteredProperties.filter(p => !selection.some(s => s.id == p.id && s.pool == p.pool));
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
              onPress={() => {
                createPortal(PropertyModal, {
                  onSaved: loadProperties,
                  validValueTypes: valueTypes?.map(v => v as unknown as PropertyType),
                });
              }}
            >
              {t('Add a property')}
            </Button>
          )}
        </div>
      );
    }

    // todo: make framer-motion up-to-date once https://github.com/heroui-inc/heroui/issues/4805 is resolved.

    return (
      <div className={'flex flex-col gap-2'}>
        <div className={'flex items-start gap-2'}>
          <div className={'w-[100px] min-w-[100px] text-medium'}>{`${t('Selected')}(${selectedProperties.length})`}</div>
          <div className={'flex flex-wrap gap-2 items-start'}>
            {selectedProperties.map(p => renderProperty(p))}
          </div>
        </div>
        <Divider />
        <div className={'flex items-start gap-2'}>
          <div className={'w-[100px] min-w-[100px] text-medium'}>{`${t('Not selected')}(${unselectedProperties.length})`}</div>
          <div className={'flex flex-wrap gap-2 items-start'}>
            {unselectedProperties.map(p => renderProperty(p))}
          </div>
        </div>
      </div>
    );
  };

  return (
    <Modal
      size={'xl'}
      defaultVisible
      onOk={async () => {
        await onSubmit(selection);
      }}
      onDestroyed={onDestroyed}
      title={title ?? t(multiple ? 'Select properties' : 'Select a property')}
      footer={(multiple && propertyCount > 0) ? true : (<Spacer />)}
    >
      <div>
        {renderFilter()}
        {renderProperties()}
        <div>
          {addable && (
            <Button
              color={'primary'}
              size={'sm'}
              className={'mt-2'}
              onPress={() => {
                createPortal(PropertyModal, {
                    onSaved: loadProperties,
                    validValueTypes: valueTypes?.map(v => v as unknown as PropertyType),
                  },
                );
              }}
            >
              {t('Add a property')}
            </Button>
          )}
        </div>
      </div>
    </Modal>
  );
};


PropertySelector.show = (props: IProps) => createPortalOfComponent(PropertySelector, props);

export default PropertySelector;
