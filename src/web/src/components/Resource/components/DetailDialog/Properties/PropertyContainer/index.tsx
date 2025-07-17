"use client";

import type { PropertyValueScope } from "@/sdk/constants";
import type { Property } from "@/core/models/Resource";
import type { IProperty } from "@/components/Property/models";

import React from "react";
import { useTranslation } from "react-i18next";
import { useUpdate } from "react-use";

import { propertyValueScopes } from "@/sdk/constants";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import { buildLogger } from "@/components/utils";
import { serializeStandardValue } from "@/components/StandardValue/helpers";
import BriefProperty from "@/components/Chips/Property/BriefProperty";

export type PropertyContainerProps = {
  valueScopePriority: PropertyValueScope[];
  onValueScopePriorityChange: (priority: PropertyValueScope[]) => any;
  property: IProperty;
  values?: Property["values"];
  onValueChange: (sdv?: string, sbv?: string) => any;
  hidePropertyName?: boolean;
  classNames?: {
    name?: string;
    value?: string;
  };
  isLinked?: boolean;
  categoryId: number;
};

const log = buildLogger("PropertyContainer");

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

  const scopedValueCandidates = propertyValueScopes.map((s) => {
    return {
      key: s.value,
      scope: s.value,
      value: values?.find((x) => x.scope == s.value),
    };
  });

  let bizValue: any | undefined;
  let dbValue: any | undefined;
  let scope: PropertyValueScope | undefined;

  for (const s of valueScopePriority) {
    const value = values?.find((v) => v.scope == s);

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
          <BriefProperty fields={["pool", "name"]} property={property} />
        </div>
        // <div className={`flex ${classNames?.name}`}>
        //   <Tooltip
        //     content={(
        //       <div>
        //         <div>{`${t<string>(PropertyPool[property.pool])}${t<string>('Property')}`}</div>
        //         <div className={'flex items-center gap-1'}>
        //           {isLinked ? t<string>('This property is linked to the current media library template or category(deprecated)') : t<string>('This property is not yet linked to the current media library template or category(deprecated)')}
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
        //               {t<string>('Unlink')}
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
        //               {t<string>('Link')}
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
        //     {/*   {property.pool == PropertyPool.Custom ? property.name : t<string>(ResourceProperty[property.id]!)} */}
        //     {/* </Chip> */}
        //   </Tooltip>
        // </div>
      )}
      <div className={`flex items-center gap-2 break-all ${classNames?.value}`}>
        <PropertyValueRenderer
          bizValue={serializeStandardValue(bizValue, property.bizValueType)}
          dbValue={serializeStandardValue(dbValue, property.dbValueType)}
          property={property}
          variant={"default"}
          onValueChange={onValueChange}
        />
      </div>
    </>
  );
};
