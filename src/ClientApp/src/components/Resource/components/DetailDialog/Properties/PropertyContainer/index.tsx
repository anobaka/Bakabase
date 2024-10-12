import { EyeInvisibleOutlined, SwapOutlined } from '@ant-design/icons';
import React from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdate } from 'react-use';
import { Badge, Button, Chip, Listbox, ListboxItem, Popover } from '@/components/bakaui';
import type { PropertyValueScope } from '@/sdk/constants';
import { propertyValueScopes, ResourceProperty, PropertyPool } from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import type { Property } from '@/core/models/Resource';
import type { Props as PropertyValueRendererProps } from '@/components/Property/components/PropertyValueRenderer';
import PropertyValueRenderer from '@/components/Property/components/PropertyValueRenderer';
import type { IProperty } from '@/components/Property/models';
import { buildLogger } from '@/components/utils';
import { serializeStandardValue } from '@/components/StandardValue/helpers';

export type PropertyContainerProps = {
  valueScopePriority: PropertyValueScope[];
  onValueScopePriorityChange: (priority: PropertyValueScope[]) => any;
  property: IProperty;
  values?: Property['values'];
  onValueChange: (sdv?: string, sbv?: string) => any;
  hidePropertyName?: boolean;
  classNames?: {
    name?: string;
    value?: string;
  };
};

const log = buildLogger('PropertyContainer');

export default (props: PropertyContainerProps) => {
  const forceUpdate = useUpdate();
  log(props);
  const {
    valueScopePriority,
    onValueScopePriorityChange,
    values,
    property,
    onValueChange,
    hidePropertyName = false,
    classNames,
  } = props;
  const { t } = useTranslation();

  const scopedValueCandidates = propertyValueScopes.map(s => {
    return {
      key: s.value,
      scope: s.value,
      value: values?.find(x => x.scope == s.value),
    };
  });

  let bizValue: any | undefined;
  let dbValue: any | undefined;
  let scope: PropertyValueScope | undefined;
  for (const s of valueScopePriority) {
    const value = values?.find(v => v.scope == s);
    if (value) {
      const dv = value.aliasAppliedBizValue ?? value.bizValue;
      if (dv) {
        bizValue = dv;
        dbValue = value.value;
        scope = s;
        break;
      }
    }
  }
  scope ??= valueScopePriority[0];

  return (
    <>
      {!hidePropertyName && (
        <div className={`flex ${classNames?.name}`}>
          <Chip
            className={'whitespace-break-spaces py-1 h-auto break-all'}
            size={'sm'}
            radius={'sm'}
            color={'secondary'}
            // variant={'light'}
          >
            {property.pool == PropertyPool.Custom ? property.name : t(ResourceProperty[property.id])}
          </Chip>
        </div>
      )}
      {/* <Card> */}
      {/*   <CardBody> */}
      <div
        className={`flex items-center gap-2 break-all ${classNames?.value}`}
      >
        <PropertyValueRenderer
          variant={'default'}
          property={property}
          bizValue={serializeStandardValue(bizValue, property.bizValueType)}
          dbValue={serializeStandardValue(dbValue, property.dbValueType)}
          onValueChange={onValueChange}
        />
        {/* <Popover */}
        {/*   trigger={( */}
        {/*     <Button */}
        {/*       size={'sm'} */}
        {/*       variant={'light'} */}
        {/*       isIconOnly */}
        {/*     > */}
        {/*       /!* {t('Created by:')} *!/ */}
        {/*       /!* {t(`PropertyValueScope.${PropertyValueScope[scope]}`)} *!/ */}
        {/*       <SwapOutlined className={'text-sm'} /> */}
        {/*     </Button> */}
        {/*   )} */}
        {/* > */}
        {/*   <div> */}
        {/*     <div className={'italic mb-1 max-w-[400px] opacity-60'}> */}
        {/*       {t('You can set the priority for following scopes of values, the first not empty value in the priority queen will be displayed, and you also can hide values by deselecting their scopes.')} */}
        {/*     </div> */}
        {/*     <div className={'italic mb-1 max-w-[400px] opacity-60'}> */}
        {/*       {t('This priority will be applied for all properties and resources.')} */}
        {/*     </div> */}
        {/*     <div> */}
        {/*       <Listbox */}
        {/*         selectionMode={'multiple'} */}
        {/*         selectedKeys={valueScopePriority.map(x => x.toString())} */}
        {/*         disallowEmptySelection */}
        {/*         onSelectionChange={keys => { */}
        {/*           const arr = Array.from(keys as Set<string>).map(x => parseInt(x, 10)); */}
        {/*           BApi.options.patchResourceOptions({ */}
        {/*             // @ts-ignore */}
        {/*             propertyValueScopePriority: arr, */}
        {/*           }); */}
        {/*           onValueScopePriorityChange(arr); */}
        {/*         }} */}
        {/*         items={scopedValueCandidates} */}
        {/*       > */}
        {/*         {item => { */}
        {/*           const index = valueScopePriority.indexOf(item.scope); */}
        {/*           const selectedIcon = index == -1 ? (<EyeInvisibleOutlined */}
        {/*             className={'text-lg'} */}
        {/*           />) : (<Chip */}
        {/*             size={'sm'} */}
        {/*             variant={'light'} */}
        {/*             color={'success'} */}
        {/*           >{index + 1}</Chip>); */}
        {/*           const sbv = item.value?.aliasAppliedBizValue; */}
        {/*           return ( */}
        {/*             <ListboxItem */}
        {/*               classNames={{ selectedIcon: 'w-auto h-auto' }} */}
        {/*               className={`${index == -1 ? 'opacity-30' : ''} `} */}
        {/*               key={item.key.toString()} */}
        {/*               description={`${t('Created by:')}${t(`PropertyValueScope.${PropertyValueScope[item.scope]}`)}`} */}
        {/*               // onClick={e => { */}
        {/*               //   // e.cancelable = true; */}
        {/*               //   // e.stopPropagation(); */}
        {/*               //   // e.preventDefault(); */}
        {/*               //   alert('xxx'); */}
        {/*               // }} */}
        {/*               selectedIcon={selectedIcon} */}
        {/*             > */}
        {/*               <div className={'max-w-[600px] break-all text-ellipsis overflow-hidden'}> */}
        {/*                 <PropertyValueRenderer */}
        {/*                   variant={'light'} */}
        {/*                   property={property} */}
        {/*                   bizValue={serializeStandardValue(sbv, property.bizValueType)} */}
        {/*                 /> */}
        {/*               </div> */}
        {/*             </ListboxItem> */}
        {/*           ); */}
        {/*         }} */}
        {/*       </Listbox> */}
        {/*     </div> */}
        {/*   </div> */}
        {/* </Popover> */}
      </div>
      {/*   </CardBody> */}
      {/* </Card> */}
    </>
  );
};
