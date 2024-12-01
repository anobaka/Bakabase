import { useTranslation } from 'react-i18next';
import React, { useState } from 'react';
import { CardHeader } from '@nextui-org/react';
import { QuestionCircleOutlined } from '@ant-design/icons';
import type { DestroyableProps } from '@/components/bakaui/types';
import { Button, Card, CardBody, Modal, Tooltip } from '@/components/bakaui';
import PropertySelector from '@/components/PropertySelector';
import { BulkModificationProcessorValueType, PropertyPool } from '@/sdk/constants';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import type {
  BulkModificationProcess,
  BulkModificationVariable,
} from '@/pages/BulkModification2/components/BulkModification/models';
import ProcessStep from '@/pages/BulkModification2/components/BulkModification/ProcessStep';
import ProcessStepModel from '@/pages/BulkModification2/components/BulkModification/ProcessStepModel';
import store from '@/store';

type Props = {
  process?: Partial<BulkModificationProcess>;
  variables?: BulkModificationVariable[];
  onSubmit?: (process: BulkModificationProcess) => void;
} & DestroyableProps;

const validate = (p?: Partial<BulkModificationProcess>) => !(!p || !p.propertyId || !p.propertyPool || !p.steps || p.steps.length === 0);

const AllBulkModificationValueTypes = [BulkModificationProcessorValueType.Static, BulkModificationProcessorValueType.Dynamic, BulkModificationProcessorValueType.Variable];

export default ({
                  onDestroyed,
                  process: propsProcess,
                  onSubmit,
                  variables,
                }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const bmInternals = store.getModelState('bulkModificationInternals');

  const [process, setProcess] = useState<Partial<BulkModificationProcess>>(propsProcess ?? {});

  return (
    <Modal
      title={t('Setting process')}
      size={'xl'}
      onDestroyed={onDestroyed}
      defaultVisible
      footer={{
        actions: ['cancel', 'ok'],
        okProps: {
          isDisabled: !validate(process),
        },
      }}
      onOk={() => {
        if (!validate(process)) {
          throw new Error('Invalid process');
        }
        onSubmit?.(process as BulkModificationProcess);
      }}
    >
      <Card>
        <CardBody>
          <div className={'grid items-center gap-2'} style={{ gridTemplateColumns: 'auto 1fr' }}>
            <div className={'text-right'}>{t('Property')}</div>
            <div>
              <Button
                size="sm"
                color={'primary'}
                variant={'light'}
                onClick={() => {
                  createPortal(
                    PropertySelector, {
                      pool: PropertyPool.All,
                      disabledKeys: Object.keys(bmInternals.disabledPropertyKeys || {}).reduce<{
                        pool: PropertyPool;
                        id: number;
                      }[]>((s, t) => {
                        s.push(...bmInternals.disabledPropertyKeys[t].map(id => ({
                          id,
                          pool: parseInt(t, 10) as PropertyPool,
                        })));
                        return s;
                      }, []),
                      multiple: false,
                      onSubmit: async (ps) => {
                        const p = ps[0];
                        setProcess({
                          ...process,
                          propertyPool: p.pool,
                          propertyId: p.id,
                          property: p,
                        });
                      },
                    },
                  );
                }}
              >
                {process?.property ? process.property.name : t('Select a property')}
              </Button>
            </div>
          </div>
        </CardBody>
      </Card>
      <Card>
        <CardHeader>
          <div className={'flex items-center gap-1'}>
            <div>{t('Preprocessing')}</div>
            <Tooltip content={(
              <div>
                <div>{t('If a preprocessing procedure is set, the variables will be preprocessed first before being used.')}</div>
                <div>{t('You can add multiple preprocessing steps.')}</div>
              </div>
            )}
            >
              <QuestionCircleOutlined className={'text-base'} />
            </Tooltip>
          </div>
        </CardHeader>
        <CardBody>
          {(process?.steps && process.steps.length > 0) && (
            <div className={'flex flex-col gap-1 mb-2'}>
              {process.steps.map((step, i) => {
                return (
                  <ProcessStep
                    no={`${i + 1}`}
                    step={step}
                    variables={variables}
                    property={process.property!}
                    editable
                    availableValueTypes={AllBulkModificationValueTypes}
                    onChange={(newStep) => {
                      process.steps![i] = newStep;
                      setProcess({
                        ...process,
                      });
                    }}
                    onDelete={() => {
                      process.steps?.splice(i, 1);
                      setProcess({
                        ...process,
                      });
                    }}
                  />
                );
              })}
            </div>
          )}
          <div>
            <Button
              size={'sm'}
              isDisabled={!process?.property}
              color={'secondary'}
              variant={'ghost'}
              onClick={() => {
                if (process?.property) {
                  createPortal(
                    ProcessStepModel, {
                      property: process.property,
                      variables,
                      availableValueTypes: AllBulkModificationValueTypes,
                      onSubmit: (operation: number, options: any) => {
                        if (!process.steps) {
                          process.steps = [];
                        }
                        process.steps.push({
                          operation,
                          options,
                        });
                        setProcess({
                          ...process,
                        });
                      },
                    },
                  );
                }
              }}
            >
              {t('Add a preprocess')}
            </Button>
          </div>
        </CardBody>
      </Card>
    </Modal>
  );
};
