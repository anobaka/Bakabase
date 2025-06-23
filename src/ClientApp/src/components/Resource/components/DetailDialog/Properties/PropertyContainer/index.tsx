import { ApiOutlined, DisconnectOutlined } from '@ant-design/icons';
import React from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdate } from 'react-use';
import { Button, Chip, Tooltip } from '@/components/bakaui';
import type { PropertyValueScope } from '@/sdk/constants';
import { PropertyPool, propertyValueScopes, ResourceProperty } from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import type { Property } from '@/core/models/Resource';
import PropertyValueRenderer from '@/components/Property/components/PropertyValueRenderer';
import type { IProperty } from '@/components/Property/models';
import { buildLogger } from '@/components/utils';
import { serializeStandardValue } from '@/components/StandardValue/helpers';
import { PropertyLabel } from '@/components/Property/v2';
import BriefProperty from '@/components/Chips/Property/BriefProperty';

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
  isLinked?: boolean;
  categoryId: number;
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
    isLinked: propsIsLinked,
    categoryId,
  } = props;
  const { t } = useTranslation();

  const [isLinked, setIsLinked] = React.useState(propsIsLinked);

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
          <BriefProperty property={property} hideType />
        </div>
        // <div className={`flex ${classNames?.name}`}>
        //   <Tooltip
        //     content={(
        //       <div>
        //         <div>{`${t(PropertyPool[property.pool])}${t('Property')}`}</div>
        //         <div className={'flex items-center gap-1'}>
        //           {isLinked ? t('This property is linked to the current media library template or category(deprecated)') : t('This property is not yet linked to the current media library template or category(deprecated)')}
        //           {isLinked ? (
        //             <Button
        //               size={'sm'}
        //               variant={'light'}
        //               color={'warning'}
        //               onPress={async () => {
        //                 await BApi.category.unlinkCustomPropertyFromCategory(categoryId, property.id);
        //                 setIsLinked(!isLinked);
        //               }}
        //             >
        //               <DisconnectOutlined className={'text-small'} />
        //               {t('Unlink')}
        //             </Button>
        //           ) : (
        //             <Button
        //               size={'sm'}
        //               variant={'light'}
        //               color={'secondary'}
        //               onPress={async () => {
        //                 await BApi.category.bindCustomPropertyToCategory(categoryId, property.id);
        //                 setIsLinked(!isLinked);
        //               }}
        //             >
        //               <ApiOutlined className={'text-small'} />
        //               {t('Link')}
        //             </Button>
        //           )}
        //         </div>
        //       </div>
        //     )}
        //   >
        //     <BriefProperty property={property} hideType />
        //     {/* <Chip */}
        //     {/*   className={'whitespace-break-spaces py-1 h-auto break-all'} */}
        //     {/*   size={'sm'} */}
        //     {/*   radius={'sm'} */}
        //     {/*   color={property.pool == PropertyPool.Custom ? 'secondary' : 'default'} */}
        //     {/*   // variant={'light'} */}
        //     {/* > */}
        //     {/*   {property.pool == PropertyPool.Custom ? property.name : t(ResourceProperty[property.id]!)} */}
        //     {/* </Chip> */}
        //   </Tooltip>
        // </div>
      )}
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
      </div>
    </>
  );
};
