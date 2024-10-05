import React, { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import BApi from '@/sdk/BApi';
import { createPortalOfComponent } from '@/components/utils';
import type { DestroyableProps } from '@/components/bakaui/types';
import {
  Button,
  Modal,
  Snippet,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tabs,
} from '@/components/bakaui';
import type { Enhancement } from '@/components/Enhancer/models';
import { EnhancementAdditionalItem, ReservedProperty, PropertyPool } from '@/sdk/constants';
import CategoryEnhancerOptionsDialog from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog';
import type { EnhancerDescriptor } from '@/components/EnhancerSelectorV2/models';
import PropertyValueRenderer from '@/components/Property/components/PropertyValueRenderer';
import { convertFromApiValue, serializeStandardValue } from '@/components/StandardValue/helpers';

interface Props extends DestroyableProps {
  resourceId: number;
}

type ResourceEnhancements = {
  enhancer: EnhancerDescriptor;
  enhancedAt: string;
  targets: {
    target: number;
    targetName: string;
    enhancement: Enhancement;
  }[];
  dynamicTargets: {
    target: number;
    targetName: string;
    enhancements: Enhancement[];
  }[];
};

function ResourceEnhancementsDialog({
                                      resourceId,
                                      ...props
                                    }: Props) {
  const [enhancements, setEnhancements] = useState<ResourceEnhancements[]>([]);
  const [resource, setResource] = useState<{
    path: string;
    categoryId: number;
  }>({
    path: '',
    categoryId: 0,
  });
  const { t } = useTranslation();
  const [enhancing, setEnhancing] = useState(false);

  useEffect(() => {
    loadEnhancements();

    BApi.resource.getResourcesByKeys({ ids: [resourceId] }).then((r) => {
      const data = r.data || [];
      setResource({
        path: data[0]?.path || '',
        categoryId: data[0]?.categoryId ?? 0,
      });
    });
  }, []);

  const loadEnhancements = useCallback(async () => {
    const r = await BApi.resource.getResourceEnhancements(resourceId, { additionalItem: EnhancementAdditionalItem.GeneratedPropertyValue });
    const data = r.data || [];

    for (const d of data) {
      d.dynamicTargets?.forEach(dt => {
        dt.enhancements?.forEach(e => {
          e.value = convertFromApiValue(e.value, e.valueType!);
          const v = e.customPropertyValue;
          const p = v?.property;
          if (p) {
            v.value = convertFromApiValue(v.value, p.dbValueType!);
            v.bizValue = convertFromApiValue(v.bizValue, p.bizValueType!);
          }
        });
      });
    }

    // @ts-ignore
    setEnhancements(data);
  }, []);

  console.log(enhancements);

  const renderConvertedValue = (e: Enhancement | undefined) => {
    if (!e || e.propertyPool == undefined || e.propertyId == undefined || e.property == undefined) {
      return;
    }

    const { property } = e;

    switch (e.propertyPool) {
      case PropertyPool.Reserved: {
        const rv = e.reservedPropertyValue;
        if (!rv) {
          return;
        }
        let bizRv: any;
        let dbRv: any;
        switch (e.propertyId as ReservedProperty) {
          case ReservedProperty.Introduction:
            bizRv = rv.introduction;
            dbRv = rv.introduction;
            break;
          case ReservedProperty.Rating:
            bizRv = rv.rating;
            dbRv = rv.rating;
            break;
          default:
            return t('Unsupported reserved property type: {{type}}', { type: e.propertyId });
        }
        return (
          <PropertyValueRenderer
            property={property}
            variant={'light'}
            bizValue={serializeStandardValue(bizRv, property.bizValueType)}
            dbValue={serializeStandardValue(dbRv, property.dbValueType)}
          />
        );
      }
      case PropertyPool.Custom: {
        const pv = e.customPropertyValue;
        if (!pv) {
          return;
        }
        return (
          <PropertyValueRenderer
            property={property}
            bizValue={serializeStandardValue(pv.bizValue, property!.bizValueType)}
            dbValue={serializeStandardValue(pv.value, property!.dbValueType)}
            variant={'light'}
          />
        );
      }
      case PropertyPool.Internal:
      case PropertyPool.All:
        return t('Unsupported property type: {{type}}', { type: e.propertyPool });
    }
  };

  return (
    <Modal
      size={'full'}
      title={t('Enhancement records')}
      defaultVisible
      footer={{
        actions: ['ok'],
      }}
      onDestroyed={props.onDestroyed}
    >
      <div className={'flex items-center gap-2'}>
        <div>
          {t('Path of resource')}
        </div>
        <Snippet size={'sm'} symbol={''}>{resource.path}</Snippet>
      </div>
      <Tabs
        aria-label="Enhancers"
        isVertical
        variant={'bordered'}
        classNames={{
          panel: 'grow min-w-0',
        }}
      >
        {enhancements.map(e => {
          const { targets } = e;
          return (
            <Tab key={e.enhancer.id} title={e.enhancer.name}>
              <div className={'flex items-center justify-between'}>
                <div className={'flex items-center gap-2'}>
                  <div>{e.enhancedAt ? t('This enhancer enhanced this resource at {{enhancedAt}}.', { enhancedAt: e.enhancedAt }) : t('This enhancer has not enhance this resource yet.')}</div>
                  <Button
                    size={'sm'}
                    variant={'light'}
                    isLoading={enhancing}
                    color={e.enhancedAt ? 'secondary' : 'primary'}
                    onClick={() => {
                      setEnhancing(true);
                      BApi.resource.createEnhancementForResourceByEnhancer(resourceId, e.enhancer.id).then(() => {
                        loadEnhancements();
                        setEnhancing(false);
                      });
                    }}
                  >
                    {t(e.enhancedAt ? 'Re-enhance now' : 'Enhance now')}
                  </Button>
                </div>
                <div className={'flex items-center gap-2'}>
                  <Button
                    size={'sm'}
                    variant={'light'}
                    color={e.enhancedAt ? 'secondary' : 'primary'}
                    onClick={() => {
                      CategoryEnhancerOptionsDialog.show({
                        categoryId: resource.categoryId,
                        enhancer: e.enhancer,
                      });
                    }}
                  >
                    {t('Check configuration')}
                  </Button>
                </div>
              </div>
              <div className={'flex flex-col gap-y-2 min-w-0'}>
                <Table isStriped className={'break-all'}>
                  <TableHeader>
                    <TableColumn>{t('Target')}</TableColumn>
                    <TableColumn>{t('Raw data')}</TableColumn>
                    <TableColumn>{t('Generated custom property value')}</TableColumn>
                  </TableHeader>
                  <TableBody>
                    {targets.map((e) => {
                      return (
                        <TableRow>
                          <TableCell>{e.targetName}</TableCell>
                          <TableCell>{JSON.stringify(e.enhancement?.value)}</TableCell>
                          <TableCell>{renderConvertedValue(e.enhancement)}</TableCell>
                        </TableRow>
                      );
                    })}
                  </TableBody>
                </Table>

                {e.dynamicTargets?.map(dt => {
                  return (
                    <Table isStriped className={'break-all'}>
                      <TableHeader>
                        <TableColumn>{dt.targetName}({dt.enhancements?.length ?? 0})</TableColumn>
                        <TableColumn>{t('Raw data')}</TableColumn>
                        <TableColumn>{t('Generated custom property value')}</TableColumn>
                      </TableHeader>
                      <TableBody>
                        {dt.enhancements?.map((e) => {
                          return (
                            <TableRow>
                              <TableCell>{e.dynamicTarget}</TableCell>
                              <TableCell>{JSON.stringify(e.value)}</TableCell>
                              <TableCell>{renderConvertedValue(e)}</TableCell>
                            </TableRow>
                          );
                        })}
                      </TableBody>
                    </Table>
                  );
                })}
              </div>
            </Tab>
          );
        })}

      </Tabs>
    </Modal>
  );
}

ResourceEnhancementsDialog.show = (props: Props) => createPortalOfComponent(ResourceEnhancementsDialog, props);

export default ResourceEnhancementsDialog;
