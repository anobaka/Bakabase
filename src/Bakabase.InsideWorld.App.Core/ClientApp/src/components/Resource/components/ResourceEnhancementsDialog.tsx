import dayjs from 'dayjs';
import React, { useCallback, useEffect, useState } from 'react';
import { Trans, useTranslation } from 'react-i18next';
import BApi from '@/sdk/BApi';
import { createPortalOfComponent } from '@/components/utils';
import type { DestroyableProps } from '@/components/bakaui/types';
import { Button, Modal, Tab, Table, Tabs, TableBody, TableCell, TableColumn, TableHeader, TableRow, Snippet } from '@/components/bakaui';
import type { Enhancement } from '@/components/Enhancer/models';
import { EnhancementAdditionalItem } from '@/sdk/constants';
import StandardValueRenderer from '@/components/StandardValue/ValueRenderer';

interface Props extends DestroyableProps{
  resourceId: number;
}

type ResourceEnhancements = {
  enhancerId: number;
  enhancerName: string;
  enhancedAt: string;
  targets: {
    target: number;
    targetName: string;
    enhancement: Enhancement;
  }[];
};

function ResourceEnhancementsDialog({ resourceId, ...props }: Props) {
  const [enhancements, setEnhancements] = useState<ResourceEnhancements[]>([]);
  const [resource, setResource] = useState<{
    path: string;
  }>({ path: '' });
  const { t } = useTranslation();
  const [enhancing, setEnhancing] = useState(false);

  useEffect(() => {
    loadEnhancements();

    BApi.resource.getResourcesByKeys({ ids: [resourceId] }).then((r) => {
      const data = r.data || [];
      setResource({
        path: data[0]?.path || '',
      });
    });
  }, []);

  const loadEnhancements = useCallback(async () => {
    const r = await BApi.resource.getResourceEnhancements(resourceId, { additionalItem: EnhancementAdditionalItem.GeneratedCustomPropertyValue });
    const data = r.data || [];
    // @ts-ignore
    setEnhancements(data);
  }, []);

  return (
    <Modal
      size={'xl'}
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
          panel: 'grow',
        }}
      >
        {enhancements.map(e => {
            const { targets } = e;
            return (
              <Tab key={e.enhancerId} title={e.enhancerName}>
                <div className={'flex items-center gap-2'}>
                  <div>{e.enhancedAt ? t('This enhancer enhanced this resource at {{enhancedAt}}.', { enhancedAt: e.enhancedAt }) : t('This enhancer has not enhance this resource yet.')}</div>
                  <Button
                    size={'sm'}
                    variant={'light'}
                    isLoading={enhancing}
                    color={e.enhancedAt ? 'secondary' : 'primary'}
                    onClick={() => {
                      setEnhancing(true);
                      BApi.resource.createEnhancementForResourceByEnhancer(resourceId, e.enhancerId).then(() => {
                        loadEnhancements();
                        setEnhancing(false);
                      });
                    }}
                  >
                    {t(e.enhancedAt ? 'Re-enhance now' : 'Enhance now')}
                  </Button>
                </div>
                <Table isStriped>
                  <TableHeader>
                    <TableColumn>{t('Target')}</TableColumn>
                    <TableColumn>{t('Raw data')}</TableColumn>
                    <TableColumn>{t('Generated custom property value')}</TableColumn>
                  </TableHeader>
                  <TableBody>
                    {targets.map((e) => (
                      <TableRow>
                        <TableCell>{e.targetName}</TableCell>
                        <TableCell>{JSON.stringify(e.enhancement?.value)}</TableCell>
                        <TableCell>
                          {e.enhancement?.valueType == undefined ? (
                            JSON.stringify(e.enhancement?.value)
                          ) : (
                            <StandardValueRenderer
                              value={e.enhancement?.customPropertyValue?.value}
                              type={e.enhancement.valueType}
                              variant={'light'}
                            />
                          )}

                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </Tab>
            );
          })}
      </Tabs>
    </Modal>
  );
}

ResourceEnhancementsDialog.show = (props: Props) => createPortalOfComponent(ResourceEnhancementsDialog, props);

export default ResourceEnhancementsDialog;
