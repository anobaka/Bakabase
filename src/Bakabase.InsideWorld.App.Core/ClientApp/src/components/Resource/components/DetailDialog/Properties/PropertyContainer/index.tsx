import { EyeInvisibleOutlined, SwapOutlined } from '@ant-design/icons';
import React from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdate } from 'react-use';
import { Badge, Button, Chip, Listbox, ListboxItem, Popover } from '@/components/bakaui';
import { PropertyValueScope, propertyValueScopes, ResourceProperty } from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import type { Property } from '@/core/models/Resource';
import type { Props as PropertyValueRendererProps } from '@/components/Property/components/PropertyValueRenderer';
import PropertyValueRenderer from '@/components/Property/components/PropertyValueRenderer';
import type { IProperty } from '@/components/Property/models';
import { buildLogger } from '@/components/utils';
import { serializeStandardValue } from '@/components/StandardValue/helpers';

type Props = {
  valueScopePriority: PropertyValueScope[];
  onValueScopePriorityChange: (priority: PropertyValueScope[]) => any;
  property: IProperty;
  values?: Property['values'];
  onValueChange: (sdv?: string, sbv?: string) => any;
  dataPool?: PropertyValueRendererProps['dataPool'];
};

const log = buildLogger('PropertyContainer');

export default (props: Props) => {
  const forceUpdate = useUpdate();
  log(props);
  const {
    valueScopePriority,
    onValueScopePriorityChange,
    values,
    property,
    onValueChange,
    dataPool,
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
      <div className={'flex justify-end'}>
        <Chip
          size={'sm'}
          radius={'sm'}
        >
          {property.isCustom ? property.name : t(ResourceProperty[property.id])}
        </Chip>
      </div>
      {/* <Card> */}
      {/*   <CardBody> */}
      <div
        className={'flex items-center gap-2'}
      >
        <PropertyValueRenderer
          variant={'default'}
          property={property}
          bizValue={serializeStandardValue(bizValue, property.bizValueType)}
          dbValue={serializeStandardValue(dbValue, property.dbValueType)}
          onValueChange={onValueChange}
          dataPool={dataPool}
        />
        <Popover
          trigger={(
            <Button
              size={'sm'}
              variant={'light'}
              isIconOnly
            >
              {/* {t('Created by:')} */}
              {/* {t(`PropertyValueScope.${PropertyValueScope[scope]}`)} */}
              <SwapOutlined className={'text-sm'} />
            </Button>
          )}
        >
          <div>
            <div className={'italic mb-1 max-w-[400px] opacity-60'}>
              {t('You can set the priority for following scopes of values, the first not empty value in the priority queen will be displayed, and you also can hide values by deselecting their scopes.')}
            </div>
            <div className={'italic mb-1 max-w-[400px] opacity-60'}>
              {t('This priority will be applied for all properties and resources.')}
            </div>
            <div>
              <Listbox
                selectionMode={'multiple'}
                selectedKeys={valueScopePriority.map(x => x.toString())}
                disallowEmptySelection
                onSelectionChange={keys => {
                  const arr = Array.from(keys as Set<string>).map(x => parseInt(x, 10));
                  BApi.options.patchResourceOptions({
                    // @ts-ignore
                    propertyValueScopePriority: arr,
                  });
                  onValueScopePriorityChange(arr);
                }}
                items={scopedValueCandidates}
              >
                {item => {
                  const index = valueScopePriority.indexOf(item.scope);
                  const selectedIcon = index == -1 ? (<EyeInvisibleOutlined
                    className={'text-lg'}
                  />) : (<Chip
                    size={'sm'}
                    variant={'light'}
                    color={'success'}
                  >{index + 1}</Chip>);
                  const sbv = item.value?.aliasAppliedBizValue;
                  return (
                    <ListboxItem
                      classNames={{ selectedIcon: 'w-auto h-auto' }}
                      className={index == -1 ? 'opacity-30' : ''}
                      key={item.key.toString()}
                      description={`${t('Created by:')}${t(`PropertyValueScope.${PropertyValueScope[item.scope]}`)}`}
                      // onClick={e => {
                      //   // e.cancelable = true;
                      //   // e.stopPropagation();
                      //   // e.preventDefault();
                      //   alert('xxx');
                      // }}
                      selectedIcon={selectedIcon}
                    >
                      <PropertyValueRenderer
                        variant={'light'}
                        property={property}
                        bizValue={JSON.stringify(sbv)}
                        dataPool={dataPool}
                      />
                    </ListboxItem>
                  );
                }}
              </Listbox>
            </div>
          </div>
        </Popover>
      </div>
      {/*   </CardBody> */}
      {/* </Card> */}
    </>
  );
};