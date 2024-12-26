import { Trans, useTranslation } from 'react-i18next';
import React, { useEffect, useState } from 'react';
import type { StringProcessOptions } from './models';
import { validate } from './helpers';
import type {
  PropertyType } from '@/sdk/constants';
import {
  type BulkModificationProcessorValueType,
  BulkModificationStringProcessOperation,
  bulkModificationStringProcessOperations,
} from '@/sdk/constants';
import { ProcessValueEditor } from '@/pages/BulkModification2/components/BulkModification/ProcessValue';
import { Input, NumberInput, Select } from '@/components/bakaui';
import DirectionSelector from '@/pages/BulkModification2/components/BulkModification/DirectionSelector';
import type { BulkModificationVariable } from '@/pages/BulkModification2/components/BulkModification/models';
import { buildLogger } from '@/components/utils';


type Props = {
  operation?: BulkModificationStringProcessOperation;
  options?: StringProcessOptions;
  variables?: BulkModificationVariable[];
  availableValueTypes?: BulkModificationProcessorValueType[];
  onChange?: (operation: BulkModificationStringProcessOperation, options?: StringProcessOptions, error?: string) => any;
  propertyType: PropertyType;
};

const log = buildLogger('StringProcessorEditor');


export default ({
                  operation: propsOperation,
                  options: propsOptions,
                  onChange,
                  variables,
                  availableValueTypes,
                  propertyType,
                }: Props) => {
  const { t } = useTranslation();
  const [options, setOptions] = useState<StringProcessOptions>(propsOptions ?? {});
  const [operation, setOperation] = useState<BulkModificationStringProcessOperation>(propsOperation ?? BulkModificationStringProcessOperation.SetWithFixedValue);

  log('operation', operation, 'options', options, typeof operation);

  useEffect(() => {
    const error = validate(operation, options);
    onChange?.(operation, options, error);
  }, [options, operation]);

  const changeOptions = (patches: Partial<StringProcessOptions>) => {
    const newOptions = {
      ...options,
      ...patches,
    };
    setOptions(newOptions);
  };

  const changeOperation = (newOperation: BulkModificationStringProcessOperation) => {
    setOperation(newOperation);
    const newOptions = {};
    setOptions(newOptions);
  };

  const renderValueCell = () => {
    return (
      <ProcessValueEditor
        value={options.value}
        onChange={value => {
          changeOptions({ value });
        }}
        baseValueType={propertyType}
      />
    );
  };

  const renderSubOptions = (options: StringProcessOptions) => {
    log('renderOptions', operation, options);

    if (!operation) {
      return null;
    }
    const components: { label: string; comp: any }[] = [];
    switch (operation) {
      case BulkModificationStringProcessOperation.SetWithFixedValue:
        components.push({
          label: t('Value'),
          comp: renderValueCell(),
        });
        break;
      case BulkModificationStringProcessOperation.Delete:
        break;
      case BulkModificationStringProcessOperation.AddToStart:
      case BulkModificationStringProcessOperation.AddToEnd:
        components.push({
          label: t('Value'),
          comp: renderValueCell(),
        });
        break;
      case BulkModificationStringProcessOperation.AddToAnyPosition:
        components.push({
          label: t('Value'),
          comp: (
            <div className={'flex items-center gap-1 whitespace-nowrap'}>
              <Trans
                i18nKey={'BulkModification.Processor.Editor.Operation.AddToAnyPosition'}
                values={{
                  value: options.value,
                }}
              >
                {/* 0 */}
                Add&nbsp;
                {/* 1 */}
                {renderValueCell()}
                {/* 2 */}
                &nbsp;to the&nbsp;
                {/* 3 */}
                <NumberInput
                  className={'w-auto'}
                  style={{ width: 100 }}
                  placeholder={t('Starts from 0')}
                  onValueChange={index => changeOptions({ index })}
                  value={options.index}
                />
                {/* 4 */}
                (th)&nbsp;character from&nbsp;
                {/* 5 */}
                <DirectionSelector
                  subject={'positioning'}
                  isReversed={options.isPositioningDirectionReversed}
                  onChange={v => changeOptions({ isPositioningDirectionReversed: v })}
                />
              </Trans>
            </div>
          ),
        });
        break;
      case BulkModificationStringProcessOperation.RemoveFromStart:
      case BulkModificationStringProcessOperation.RemoveFromEnd:
        components.push({
          label: t('Count'),
          comp: (
            <NumberInput
              className={'w-auto'}
              style={{ width: 100 }}
              onValueChange={count => changeOptions({ count })}
              value={options.count}
            />
          ),
        });
        break;
      case BulkModificationStringProcessOperation.RemoveFromAnyPosition:
        // delete 6 characters forward from the fifth character from the end
        components.push({
          label: t('Value'),
          comp: (
            <div className={'flex items-center gap-1 whitespace-nowrap'}>
              <Trans
                i18nKey={'BulkModification.Processor.Editor.Operation.RemoveFromAnyPosition'}
                values={{
                  value: options.value,
                }}
              >
                {/* 0 */}
                Delete&nbsp;
                {/* 1 */}
                <NumberInput
                  className={'w-auto'}
                  style={{ width: 100 }}
                  onValueChange={count => changeOptions({ count })}
                  value={options.count}
                />
                {/* 2 */}
                &nbsp;characters&nbsp;
                {/* 3 */}
                <DirectionSelector
                  subject={'operation'}
                  isReversed={options.isPositioningDirectionReversed}
                  onChange={v => changeOptions({ isPositioningDirectionReversed: v })}
                />
                {/* 4 */}
                from the &nbsp;
                {/* 5 */}
                <NumberInput
                  className={'w-auto'}
                  style={{ width: 100 }}
                  placeholder={t('Starts from 0')}
                  onValueChange={index => changeOptions({ index })}
                  value={options.index}
                />
                {/* 6 */}
                (th)&nbsp;charater from&nbsp;
                {/* 7 */}
                <DirectionSelector
                  subject={'positioning'}
                  isReversed={options.isPositioningDirectionReversed}
                  onChange={v => changeOptions({ isPositioningDirectionReversed: v })}
                />
              </Trans>
            </div>
          ),
        });
        break;
      case BulkModificationStringProcessOperation.ReplaceFromStart:
      case BulkModificationStringProcessOperation.ReplaceFromEnd:
      case BulkModificationStringProcessOperation.ReplaceFromAnyPosition:
      case BulkModificationStringProcessOperation.ReplaceWithRegex:
        components.push({
          label: t('Value'),
          comp: (
            <div className={'flex items-center gap-1 whitespace-nowrap'}>
              <Trans
                i18nKey={'BulkModification.Processor.Editor.Operation.Replace'}
              >
                {/* 0 */}
                Replace&nbsp;
                {/* 1 */}
                <Input
                  onValueChange={find => changeOptions({ find })}
                  value={options.find}
                />
                {/* 2 */}
                &nbsp;with&nbsp;
                {/* 3 */}
                {renderValueCell()}
              </Trans>
            </div>
          ),
        });
        break;
    }
    return components.map((c, i) => {
      return (
        <>
          <div>{c.label}</div>
          <div>{c.comp}</div>
        </>
      );
    });
  };

  return (
    <div className={'grid items-center gap-2'} style={{ gridTemplateColumns: 'auto minmax(0, 1fr)' }}>
      <div>
        {t('Operation')}
      </div>
      <Select
        dataSource={bulkModificationStringProcessOperations.map(tpo => ({
          label: t(tpo.label),
          value: tpo.value,
        }))}
        selectionMode={'single'}
        selectedKeys={operation == undefined ? undefined : [operation.toString()]}
        onSelectionChange={keys => {
          changeOperation(parseInt(Array.from(keys || [])[0] as string, 10) as BulkModificationStringProcessOperation);
        }}
      />
      {renderSubOptions(options)}
    </div>
  );
};
