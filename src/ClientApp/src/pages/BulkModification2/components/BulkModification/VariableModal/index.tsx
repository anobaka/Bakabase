import { CardHeader } from '@nextui-org/react';
import { QuestionCircleOutlined } from '@ant-design/icons';
import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import type { BulkModificationVariable } from '@/pages/BulkModification2/components/BulkModification/models';
import { Button, Card, CardBody, Input, Modal, Select, Tooltip } from '@/components/bakaui';
import PropertySelector from '@/components/PropertySelector';
import { PropertyPool, propertyValueScopes } from '@/sdk/constants';
import ProcessStep from '@/pages/BulkModification2/components/BulkModification/ProcessStep';
import ProcessStepModel from '@/pages/BulkModification2/components/BulkModification/ProcessStepModel';
import type { DestroyableProps } from '@/components/bakaui/types';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import { buildLogger } from '@/components/utils';

type Props = {
  variable?: Partial<BulkModificationVariable>;
  onChange?: (variable: BulkModificationVariable) => any;
} & DestroyableProps;

const validate = (v?: Partial<BulkModificationVariable>) => !(!v || !v.name || !v.propertyId || !v.propertyPool || v.scope == undefined);

const log = buildLogger('VariableModal');

export default ({
                  variable: propsVariable,
                  onDestroyed,
                  onChange,
                }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [variable, setVariable] = useState<Partial<BulkModificationVariable>>(propsVariable ?? {});

  useUpdateEffect(() => {
    setVariable(propsVariable ?? {});
  }, [propsVariable]);

  log(variable, propertyValueScopes);

  return (
    <Modal
      title={t('Setting variable')}
      size={'xl'}
      onDestroyed={onDestroyed}
      defaultVisible
      footer={{
        actions: ['cancel', 'ok'],
        okProps: {
          isDisabled: !validate(variable),
        },
      }}
      onOk={() => {
        if (!validate(variable)) {
          throw new Error('Invalid variable');
        }
        onChange?.(variable as BulkModificationVariable);
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
                      multiple: false,
                      onSubmit: async (ps) => {
                        const p = ps[0];
                        setVariable({
                          ...variable,
                          propertyPool: p.pool,
                          propertyId: p.id,
                          property: p,
                        });
                      },
                    },
                  );
                }}
              >
                {variable?.property ? variable.property.name : t('Select a property')}
              </Button>
            </div>
            <div className={'text-right'}>
              {t('Scope')}
            </div>
            <div>
              <Select
                size="sm"
                dataSource={propertyValueScopes.map(s => ({
                  label: s.label,
                  value: s.value,
                }))}
                selectedKeys={variable?.scope == undefined ? undefined : [variable.scope.toString()]}
                selectionMode={'single'}
                disallowEmptySelection
                onSelectionChange={v => {
                  const scope = Array.from(v ?? [])[0] as number;
                  setVariable({
                    ...variable,
                    scope,
                  });
                }}
                placeholder={t('Select a scope for property value')}
              />
            </div>
            <div className={'text-right'}>
              {t('Name')}
            </div>
            <div>
              <Input
                size={'sm'}
                isRequired
                isClearable
                value={variable?.name}
                placeholder={t('Set a name for this variable')}
                onValueChange={v => {
                  setVariable({
                    ...variable,
                    name: v,
                  });
                }}
              />
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
          {(variable?.preprocesses && variable.preprocesses.length > 0) ? (
            <div className={'flex flex-col gap-1'}>
              {variable.preprocesses.map((step, i) => {
                return (
                  <ProcessStep
                    no={i + 1}
                    step={step}
                    property={variable.property!}
                  />
                );
              })}
            </div>
          ) : (
            <div>
              <Button
                size={'sm'}
                isDisabled={!variable?.property}
                color={'secondary'}
                variant={'ghost'}
                onClick={() => {
                  if (variable?.property) {
                    createPortal(
                      ProcessStepModel, {
                        property: variable.property,
                        onSubmit: (operation: number, options: any) => {
                          if (!variable.preprocesses) {
                            variable.preprocesses = [];
                          }
                          variable.preprocesses.push({
                            operation,
                            options,
                          });
                          setVariable({
                            ...variable,
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
          )}
        </CardBody>
      </Card>
    </Modal>
  );
};
