import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import PropertyDialog from '../PropertyDialog';
import type { IProperty } from '@/components/Property/models';
import Property from '@/components/Property';
import { createPortalOfComponent } from '@/components/utils';
import type {
  CustomPropertyType,
  ResourceProperty as EnumResourceProperty } from '@/sdk/constants';
import {
  CustomPropertyAdditionalItem,
  ResourcePropertyType,
  StandardValueType,
} from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import store from '@/store';
import { Button, Chip, Modal, Spacer, Tab, Tabs } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';

interface IKey {
  id: number;
  type: ResourcePropertyType;
}

interface IProps extends DestroyableProps{
  selection?: IKey[];
  onSubmit?: (selectedProperties: IProperty[]) => Promise<any>;
  multiple?: boolean;
  pool: ResourcePropertyType;
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

  const [currentTab, setCurrentTab] = useState<string>('selected');

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
    if (pool & ResourcePropertyType.Reserved) {
      const map = internalOptions.resource.reservedResourcePropertyDescriptorMap || {};
      arr.push(...Object.values(map));
    }
    if (pool & ResourcePropertyType.Internal) {
      const map = internalOptions.resource.internalResourcePropertyDescriptorMap || {};
      arr.push(...Object.values(map));
    }
    if (pool & ResourcePropertyType.Custom) {
      const rsp = await BApi.customProperty.getAllCustomProperties({ additionalItems: CustomPropertyAdditionalItem.Category });
      // @ts-ignore
      arr.push(...(rsp.data || []).map(d => ({
        ...d,
        type: ResourcePropertyType.Custom,
      })));
    }
    setProperties(arr);

    if (selection.length == 0) {
      setCurrentTab('notSelected');
    }

    console.log('reloaded', arr);
  };

  useEffect(() => {
  }, []);

  const close = () => {
    setVisible(false);
  };

  const renderProperty = (property: IProperty) => {
    const selected = selection.some(s => s.id == property.id && s.type == property.type);
    console.log(selection, property);
    return (
      <Property
        key={`${property.id}-${property.type}`}
        property={property}
        onClick={async () => {
          if (multiple) {
            if (selected) {
              setSelection(selection.filter(s => s.id != property.id && s.type == property.type));
            } else {
              setSelection([...selection, {
                id: property.id,
                type: property.type,
              }]);
            }
          } else {
            if (selected) {
              setSelection([]);
            } else {
              const ns: IKey[] = [{
                id: property.id,
                type: property.type,
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
      await propsOnSubmit(selection.map(s => properties.find(p => p.id == s.id && p.type == s.type)).filter(x => x != undefined) as IProperty[]);
    }
  };

  // console.log('render', reservedProperties, customProperties);

  const renderFilter = () => {
    const filters: any[] = [];
    if (pool != ResourcePropertyType.All) {
      Object.keys(ResourcePropertyType).forEach(k => {
        const v = parseInt(k, 10) as ResourcePropertyType;
        if (Number.isNaN(v)) {
          return;
        }
        if (pool & v) {
          switch (v) {
            case ResourcePropertyType.Internal:
            case ResourcePropertyType.Reserved:
            case ResourcePropertyType.Custom:
              filters.push(
                <Chip
                  key={'pool'}
                  size={'sm'}
                >{t(ResourcePropertyType[v])}</Chip>,
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
  const selectedProperties = selection.map(s => filteredProperties.find(p => p.id == s.id && p.type == s.type)).filter(x => x).map(x => x!);
  const unselectedProperties = filteredProperties.filter(p => !selection.some(s => s.id == p.id && s.type == p.type));
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
        <Tabs aria-label="Selectable" isVertical selectedKey={currentTab} onSelectionChange={key => setCurrentTab(key as string)}>
          <Tab key="selected" title={`${t('Selected')}(${selectedProperties.length})`}>
            <div className={'flex flex-wrap gap-2 items-start'}>
              {selectedProperties.map(p => renderProperty(p))}
            </div>
          </Tab>
          <Tab key="notSelected" title={`${t('Not selected')}(${unselectedProperties.length})`}>
            <div className={'flex flex-wrap gap-2 items-start'}>
              {unselectedProperties.map(p => renderProperty(p))}
            </div>
          </Tab>
        </Tabs>

        {/* <div className={'flex gap-2'}> */}
        {/*   <div className={'border-1 rounded p-2'}> */}
        {/*     <div className={'font-bold'}>{t('Selected')}</div> */}

        {/*   </div> */}
        {/*   <div className={'border-1 rounded p-2 flex-1 border-dashed'}> */}
        {/*     <div className={'font-bold'}>{t('Not selected')}</div> */}
        {/*     <div className={'mt-2 flex flex-wrap gap-2 items-start'}> */}
        {/*       {unselectedProperties.map(p => renderProperty(p))} */}
        {/*     </div> */}
        {/*   </div> */}
        {/* </div> */}

        {addable && (
          <Button
            color={'primary'}
            size={'sm'}
            className={'mt-2'}
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
