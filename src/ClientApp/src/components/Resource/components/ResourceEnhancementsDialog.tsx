import React, { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiOutlined, QuestionCircleOutlined, ReloadOutlined, SyncOutlined } from '@ant-design/icons';
import BApi from '@/sdk/BApi';
import { createPortalOfComponent } from '@/components/utils';
import type { DestroyableProps } from '@/components/bakaui/types';
import {
  Button,
  Chip,
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
  Tooltip,
} from '@/components/bakaui';
import type { Enhancement } from '@/components/Enhancer/models';
import { EnhancementAdditionalItem, EnhancementRecordStatus, PropertyPool, ReservedProperty } from '@/sdk/constants';
import CategoryEnhancerOptionsDialog from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog';
import type { EnhancerDescriptor } from '@/components/EnhancerSelectorV2/models';
import PropertyValueRenderer from '@/components/Property/components/PropertyValueRenderer';
import { convertFromApiValue, serializeStandardValue } from '@/components/StandardValue/helpers';

interface Props extends DestroyableProps {
  resourceId: number;
}

type ResourceEnhancements = {
  enhancer: EnhancerDescriptor;
  contextCreatedAt?: string;
  contextAppliedAt?: string;
  status: EnhancementRecordStatus;
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
  const [applyingContext, setApplyingContext] = useState(false);

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
          const p = e.property;
          if (p && v) {
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
                  <div className={'flex items-center gap-2'}>
                    <Chip
                      // size={'sm'}
                      variant={'light'}
                      color={'secondary'}
                      radius={'sm'}
                    >
                      <div className={'flex items-center gap-1'}>
                        {t('Data created at')}
                        <Tooltip
                          color={'secondary'}
                          className={'max-w-[500px]'}
                          placement={'top'}
                          content={t('The data has been created, indicating that the enhancer has completed the data retrieval process, which is typically done by accessing third-party sites or executing specific internal logic. You can check the status of the data retrieval in the table below. Frequent repeated data retrieval attempts may result in access denial from third-party services.')}
                        >
                          <QuestionCircleOutlined className={'text-base'} />
                        </Tooltip>
                      </div>
                    </Chip>
                    {e.contextCreatedAt ?? t('None')}
                  </div>
                  <div className={'flex items-center gap-2'}>
                    <Chip
                      // size={'sm'}
                      variant={'light'}
                      color={'secondary'}
                      radius={'sm'}
                    >
                      <div className={'flex items-center gap-1'}>
                        {t('Data applied at')}
                        <Tooltip
                          color={'secondary'}
                          className={'max-w-[500px]'}
                          placement={'top'}
                          content={t('The application of data indicates that the retrieved data has been successfully converted into attribute values. This step is conducted entirely within the program, without involving any third-party data exchange.')}
                        >
                          <QuestionCircleOutlined className={'text-base'} />
                        </Tooltip>
                      </div>
                    </Chip>
                    {e.contextAppliedAt ?? t('None')}
                  </div>
                  <Tooltip
                    content={t('Retrieve data then apply data. To reduce the possibility of access denial from third-party services, it is recommended to apply data only after all data has been retrieved. Configuring enhancer options in category won\'t affect the data retrieval process, it only affects the data applying process.')}
                    color={'secondary'}
                    className={'max-w-[500px]'}
                  >
                    <Button
                      size={'sm'}
                      variant={'light'}
                      isLoading={enhancing}
                      color={(e.status == EnhancementRecordStatus.ContextApplied || e.status == EnhancementRecordStatus.ContextCreated) ? 'warning' : 'primary'}
                      onClick={() => {
                        setEnhancing(true);
                        BApi.resource.enhanceResourceByEnhancer(resourceId, e.enhancer.id).then(() => {
                          loadEnhancements();
                          setEnhancing(false);
                        });
                      }}
                    >
                      <SyncOutlined className={'text-base'} />
                      {t(e.status == EnhancementRecordStatus.ContextApplied ? 'Re-enhance now' : 'Enhance now')}
                    </Button>
                  </Tooltip>
                  {(e.status == EnhancementRecordStatus.ContextApplied || e.status == EnhancementRecordStatus.ContextCreated) && (
                    <Tooltip
                      color={'secondary'}
                      content={t('Apply the data to property values of the resource.')}
                    >
                      <Button
                        size={'sm'}
                        variant={'light'}
                        isLoading={applyingContext}
                        color={'primary'}
                        onClick={() => {
                          setApplyingContext(true);
                          BApi.resource.applyEnhancementContextDataForResourceByEnhancer(resourceId, e.enhancer.id).then(() => {
                            loadEnhancements();
                            setApplyingContext(false);
                          });
                        }}
                      >
                        <ApiOutlined className={'text-base'} />
                        {t('Apply data')}
                      </Button>
                    </Tooltip>
                  )}
                </div>
                <div className={'flex items-center gap-2'}>
                  <Button
                    size={'sm'}
                    variant={'light'}
                    color={'primary'}
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
