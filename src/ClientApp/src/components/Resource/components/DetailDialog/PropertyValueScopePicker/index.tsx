import { TableBody, TableCell, TableColumn, TableHeader, TableRow } from '@nextui-org/react';
import { useTranslation } from 'react-i18next';
import type { CSSProperties } from 'react';
import React, { useCallback, useEffect, useState } from 'react';
import { CheckCircleOutlined, CloseCircleOutlined, QuestionCircleOutlined } from '@ant-design/icons';
import type { Property, Resource as ResourceModel } from '@/core/models/Resource';
import store from '@/store';
import { Button, Chip, Modal, Table, Tooltip } from '@/components/bakaui';
import { PropertyPool, PropertyValueScope, propertyValueScopes } from '@/sdk/constants';
import type { DestroyableProps } from '@/components/bakaui/types';
import { buildLogger } from '@/components/utils';
import type { IProperty } from '@/components/Property/models';
import BApi from '@/sdk/BApi';
import PropertyValueRenderer from '@/components/Property/components/PropertyValueRenderer';
import { serializeStandardValue } from '@/components/StandardValue/helpers';

type Props = DestroyableProps & {
  resource: ResourceModel;
};

type PropertyMap = { [key in PropertyPool]?: Record<number, IProperty> };

const log = buildLogger('PropertyValueScopePicker');

export default (props: Props) => {
  const { t } = useTranslation();

  const { resource } = props;

  const resourceOptions = store.useModelState('resourceOptions');

  const [propertyMap, setPropertyMap] = useState<PropertyMap>({});

  const [visibleScopes, setVisibleScopes] = useState<number[]>(propertyValueScopes.map(s => s.value));

  const init = useCallback(async () => {
    // @ts-ignore
    const properties = (await BApi.property.getPropertiesByPool(PropertyPool.Custom | PropertyPool.Reserved)).data ?? [];
    const propertyMap: PropertyMap = {};
    properties.forEach(p => {
      if (!propertyMap[p.pool]) {
        propertyMap[p.pool] = {};
      }
      propertyMap[p.pool]![p.id] = p;
    });
    setPropertyMap(propertyMap);
  }, []);

  useEffect(() => {
    init();
  }, []);

  const renderHeader = () => {
    const columns: any[] = [
      <TableColumn>{t('Property')}</TableColumn>,
      <TableColumn>{t('Display value')}</TableColumn>,
      ...visibleScopes.map(scope => {
        const priority = resourceOptions.propertyValueScopePriority ?? [];
        const index = priority.findIndex(p => p == scope);
        return (
          <TableColumn
            key={scope}
          >
            <Button
              size={'sm'}
              onClick={() => {
                const arr: PropertyValueScope[] = index > -1 ? priority.filter(p => p != scope) : [...priority, scope];
                BApi.options.patchResourceOptions({
                  propertyValueScopePriority: arr,
                });
              }}
            >
              {t(`PropertyValueScope.${PropertyValueScope[scope]}`)}
              <Tooltip content={(
                <div className={'flex flex-col gap-2'}>
                  <div>
                    {t('The numbers represent the priority of property dimensions, and the property values will be displayed according to the priority of the dimensions.')}
                  </div>
                  <div className={'flex items-center gap-2'}>
                    <CloseCircleOutlined className={'text-base'} />
                    {t('indicates this scope is not enabled')}
                  </div>
                </div>
              )}
              >
                <Chip
                  color="success"
                  variant={'light'}
                  size={'sm'}
                >
                  {index == -1 ? <CloseCircleOutlined className={'text-base'} /> : index + 1}
                </Chip>
              </Tooltip>
            </Button>
          </TableColumn>
        );
      }),
    ];

    return columns;
  };

  const renderRows = () => {
    const ps: ResourceModel['properties'] = resource.properties ?? {};
    const rows: any[] = [];
    const priority = resourceOptions.propertyValueScopePriority ?? [];
    Object.keys(ps).forEach(pool => {
      const propertyValueMap: Record<number, Property> = ps[pool]!;
      Object.keys(propertyValueMap).forEach(id => {
        const propertyValues: Property = propertyValueMap[id];
        const row: any[] = [];
        row.push(
          <TableCell key={'Property'}>
            <div className={'flex justify-end'}>
              <Chip
                size={'sm'}
                radius={'sm'}
              >
                {propertyValues.name}
              </Chip>
            </div>
          </TableCell>,
        );
        let finalValue;
        let finalScope;
        if (propertyValues.values) {
          for (const scope of priority) {
            const t = propertyValues.values.find(v => v.scope == scope);
            if (t?.value) {
              finalValue = t.aliasAppliedBizValue;
              finalScope = scope;
              break;
            }
          }
        }

        const property = propertyMap[pool]?.[id];
        row.push(
          <TableCell key={'Display value'}>
            {property && (
              <PropertyValueRenderer
                variant={'light'}
                property={property}
                bizValue={serializeStandardValue(finalValue, propertyValues.bizValueType)}
              />
            )}
          </TableCell>,
        );

        visibleScopes.forEach(scope => {
          const value = propertyValues.values?.find(v => v.scope == scope)?.aliasAppliedBizValue;
          const isHidden = priority.findIndex(p => p == scope) == -1;
          let className = '';
          const style: CSSProperties = {};
          const isFinal = scope == finalScope;
          if (isFinal) {
            // className += ' border-1 rounded';
            // style.borderColor = 'var(--bakaui-success)';
          }
          row.push(
            <TableCell key={`scope-${scope}`} className={className} style={style} >
              <div className={`relative ${isHidden ? 'opacity-60' : ''}`}>
                {scope == finalScope && (
                  <CheckCircleOutlined className={'absolute top-0 right-0 text-[var(--bakaui-success)]'} />
                )}
                {property && (
                  <PropertyValueRenderer
                    variant={'light'}
                    property={property}
                    bizValue={serializeStandardValue(value, propertyValues.bizValueType)}
                  />
                )}
              </div>
            </TableCell>,
          );
        });
        rows.push(
          <TableRow key={id}>
            {row}
          </TableRow>,
        );
      });
    });
    return rows;
  };

  return (
    <Modal
      defaultVisible
      size={'full'}
      footer={false}
      title={t('Setup priority of property value scopes')}
    >
      <div className={'flex items-center gap-2'}>
        <div className={'flex items-center gap-1'}>
          {t('Visible scopes')}
          <Tooltip content={t('This filter is used for quick check and does not change the priority of property value scopes.')}>
            <QuestionCircleOutlined className={'text-base'} />
          </Tooltip>
        </div>
        <div className={'flex flex-wrap gap-1'}>
          {propertyValueScopes.map(s => {
            const isVisible = visibleScopes.includes(s.value);
            return (
              <Chip
                size={'sm'}
                radius={'sm'}
                className={'cursor-pointer'}
                color={isVisible ? 'primary' : 'default'}
                onClick={() => {
                  if (isVisible) {
                    setVisibleScopes(visibleScopes.filter(v => v != s.value));
                  } else {
                    setVisibleScopes([...visibleScopes, s.value]);
                  }
                }}
              >
                {t(`PropertyValueScope.${PropertyValueScope[s.value]}`)}
              </Chip>
            );
          })}
        </div>
      </div>
      <div>
        {t('You can click on the scope button to change the priority of the property value scopes.')}
      </div>
      <Table removeWrapper>
        <TableHeader>
          {renderHeader()}
        </TableHeader>
        <TableBody>
          {renderRows()}
        </TableBody>
      </Table>
    </Modal>
  );
};
