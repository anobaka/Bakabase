import { EyeInvisibleOutlined, SwapOutlined } from '@ant-design/icons';
import React from 'react';
import { useTranslation } from 'react-i18next';
import CustomProperty from './CustomProperty';
import { Button, Card, CardBody, Chip, Listbox, ListboxItem, Popover } from '@/components/bakaui';
import { CustomPropertyType, PropertyValueScope, propertyValueScopes, ResourceProperty } from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import type { Property } from '@/core/models/Resource';

type Props = {
  isCustomProperty: boolean;
  propertyId: number;
  customPropertyType?: CustomPropertyType;
  valueScopePriority: PropertyValueScope[];
  onValueScopePriorityChange: (priority: PropertyValueScope[]) => any;
  property: Property;
  onChanged: () => any;
};

export default ({ isCustomProperty, propertyId, valueScopePriority, onValueScopePriorityChange, property, customPropertyType, onChanged }: Props) => {
  const { t } = useTranslation();

  const scopedValueCandidates = propertyValueScopes.map(s => {
    return {
      key: s.value,
      scope: s.value,
      value: property.values?.find(x => x.scope == s.value),
    };
  });

  let displayValue: any | undefined;
  for (const scope of valueScopePriority) {
    const value = property.values?.find(v => v.scope == scope);
    if (value) {
      const dv = value.aliasAppliedBizValue ?? value.bizValue;
      if (dv) {
        displayValue = dv;
        break;
      }
    }
  }

  const renderValue = () => {
    if (!isCustomProperty) {
      const rp = propertyId as ResourceProperty;
      switch (rp) {
        case ResourceProperty.Introduction:
          return (
            <CustomProperty
              id={propertyId}
              variant={'default'}
              editable
              bizValue={displayValue}
              bizValueType={property.bizValueType}
              onValueChange={onChanged}
              type={CustomPropertyType.MultilineText}
            />
          );
        case ResourceProperty.Rating:
          return (
            <CustomProperty
              id={propertyId}
              variant={'default'}
              editable
              bizValue={displayValue}
              bizValueType={property.bizValueType}
              onValueChange={onChanged}
              type={CustomPropertyType.Rating}
            />
          );
        default:
          return (
            <span>{t('Not supported')}</span>
          );
      }
    } else {
      return (
        <CustomProperty
          id={propertyId}
          variant={'default'}
          editable
          bizValue={displayValue}
          bizValueType={property.bizValueType}
          onValueChange={onChanged}
          type={customPropertyType!}
        />
      );
    }
  };

  return (
    <>
      <Chip
        size={'sm'}
        radius={'sm'}
      >
        {isCustomProperty ? property.name : t(ResourceProperty[propertyId])}
      </Chip>

      <Card>
        <CardBody>
          <div
            className={'flex items-center gap-2'}
          >
            {renderValue()}
            <Popover
              trigger={(
                <Button
                  size={'sm'}
                  isIconOnly
                  variant={'light'}
                >
                  <SwapOutlined className={'text-lg'} />
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
                      return (
                        <ListboxItem
                          classNames={{ selectedIcon: 'w-auto h-auto' }}
                          className={index == -1 ? 'opacity-30' : ''}
                          key={item.key.toString()}
                          description={t(`PropertyValueScope.${PropertyValueScope[item.scope]}`)}
                          // onClick={e => {
                          //   // e.cancelable = true;
                          //   // e.stopPropagation();
                          //   // e.preventDefault();
                          //   alert('xxx');
                          // }}
                          selectedIcon={selectedIcon}
                        >
                          {t('Value')}
                        </ListboxItem>
                      );
                    }}
                  </Listbox>
                </div>
              </div>
            </Popover>
          </div>
        </CardBody>
      </Card>
    </>
  );
};
