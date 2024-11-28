import { Trans, useTranslation } from 'react-i18next';
import React, { useState } from 'react';
import type { TextProcessOptions } from './models';
import {
  type BulkModificationProcessorValueType,
  TextProcessingOperation,
  textProcessingOperations,
} from '@/sdk/constants';
import {
  ValueWithMultipleTypeEditor,
} from '@/pages/BulkModification2/components/BulkModification/ValueWithMultipleType';
import { Input, NumberInput, Select } from '@/components/bakaui';
import DirectionSelector from '@/pages/BulkModification2/components/BulkModification/DirectionSelector';
import type { IProperty } from '@/components/Property/models';
import type { BulkModificationVariable } from '@/pages/BulkModification2/components/BulkModification/models';
import { buildLogger } from '@/components/utils';


type Props = {
  property: IProperty;
  operation?: TextProcessingOperation;
  options?: TextProcessOptions;
  variables?: BulkModificationVariable[];
  availableValueTypes?: BulkModificationProcessorValueType[];
  onChange?: (operation: TextProcessingOperation, options: TextProcessOptions) => any;
};

const log = buildLogger('TextProcessorEditor');

const validate = (operation: TextProcessingOperation, options?: TextProcessOptions): boolean => {
  if (operation == TextProcessingOperation.Delete) {
    return true;
  }

  if (!options) {
    return false;
  }

  const {
    value,
    count,
    valueType,
    index,
    isPositioningDirectionReversed,
    isOperationDirectionReversed,
    replace,
    find,
  } = options;

  switch (operation) {
    case TextProcessingOperation.SetWithFixedValue:
    case TextProcessingOperation.AddToStart:
    case TextProcessingOperation.AddToEnd:
      return value != undefined && value.length > 0;
    case TextProcessingOperation.AddToAnyPosition:
      return value != undefined && value.length > 0 && index != undefined && index > -1;
    case TextProcessingOperation.RemoveFromStart:
    case TextProcessingOperation.RemoveFromEnd:
      return count != undefined && count > 0;
    case TextProcessingOperation.RemoveFromAnyPosition:
      return count != undefined && count > 0 && index != undefined && index > -1;
    case TextProcessingOperation.ReplaceFromStart:
    case TextProcessingOperation.ReplaceFromEnd:
    case TextProcessingOperation.ReplaceFromAnyPosition:
    case TextProcessingOperation.ReplaceWithRegex:
      return find != undefined && find.length > 0;
  }
};

export default ({
                  property,
                  operation: propsOperation,
                  options: propsOptions,
                  onChange,
                  variables,
                  availableValueTypes,
                }: Props) => {
  const { t } = useTranslation();
  const [options, setOptions] = useState<TextProcessOptions>(propsOptions ?? {});
  const [operation, setOperation] = useState<TextProcessingOperation>(propsOperation ?? TextProcessingOperation.SetWithFixedValue);

  log('operation', operation, 'options', options, typeof operation);

  const changeOptions = (patches: Partial<TextProcessOptions>) => {
    const newOptions = {
      ...options,
      ...patches,
    };
    setOptions(newOptions);

    if (validate(operation, newOptions)) {
      onChange?.(operation, newOptions);
    }
  };

  const changeOperation = (newOperation: TextProcessingOperation) => {
    setOperation(newOperation);
    const newOptions = {};
    setOptions(newOptions);

    if (validate(newOperation, newOptions)) {
      onChange?.(newOperation, newOptions);
    }
  };

  const renderValueCell = (field: string = 'value') => {
    return (
      <ValueWithMultipleTypeEditor
        valueTypes={availableValueTypes}
        value={options[field]}
        onChange={(valueType, value) => changeOptions({
          [field]: value,
          valueType,
        })}
        variables={variables}
        property={property}
      />
    );
  };

  const renderSubOptions = (options: TextProcessOptions) => {
    log('renderOptions', operation, options);

    if (!operation) {
      return null;
    }
    const components: { label: string; comp: any }[] = [];
    switch (operation) {
      case TextProcessingOperation.SetWithFixedValue:
        components.push({
          label: t('Value'),
          comp: renderValueCell(),
        });
        break;
      case TextProcessingOperation.Delete:
        break;
      case TextProcessingOperation.AddToStart:
      case TextProcessingOperation.AddToEnd:
        components.push({
          label: t('Value'),
          comp: renderValueCell(),
        });
        break;
      case TextProcessingOperation.AddToAnyPosition:
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
      case TextProcessingOperation.RemoveFromStart:
      case TextProcessingOperation.RemoveFromEnd:
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
      case TextProcessingOperation.RemoveFromAnyPosition:
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
      case TextProcessingOperation.ReplaceFromStart:
      case TextProcessingOperation.ReplaceFromEnd:
      case TextProcessingOperation.ReplaceFromAnyPosition:
      case TextProcessingOperation.ReplaceWithRegex:
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
                {renderValueCell('replace')}
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
        dataSource={textProcessingOperations.map(tpo => ({
          label: tpo.label,
          value: tpo.value,
        }))}
        selectionMode={'single'}
        selectedKeys={operation == undefined ? undefined : [operation.toString()]}
        onSelectionChange={keys => {
          changeOperation(parseInt(Array.from(keys || [])[0] as string, 10) as TextProcessingOperation);
        }}
      />
      {renderSubOptions(options)}
    </div>
  );
};
