'use strict';
import { Trans, useTranslation } from 'react-i18next';
import { useState } from 'react';
import { Input, NumberInput, Select, Textarea } from '@/components/bakaui';
import { buildLogger } from '@/components/utils';
import DirectionSelector from '@/pages/BulkModification2/components/BulkModification/DirectionSelector';
import type { BulkModificationProcessorValueType } from '@/sdk/constants';
import { TextProcessingOperation, textProcessingOperations } from '@/sdk/constants';
import type { BulkModificationVariable } from '@/pages/BulkModification2/components/BulkModification/models';
import ValueWithMultipleType from '@/pages/BulkModification2/components/BulkModification/ValueWithMultipleType';

type TextProcessorOptions = {
  value?: string;
  index?: number;
  isOperationDirectionReversed?: boolean;
  isPositioningDirectionReversed?: boolean;
  count?: number;
  find?: string;
  replace?: string;
  valueType?: BulkModificationProcessorValueType;
};

type Props = {
  useTextarea?: boolean;
  operation?: TextProcessingOperation;
  options?: TextProcessorOptions;
  variables?: BulkModificationVariable[];
  availableValueTypes?: BulkModificationProcessorValueType[];
  onChange?: (operation: TextProcessingOperation, options: TextProcessorOptions) => any;
};

const log = buildLogger('TextProcessor');

const validate = (operation: TextProcessingOperation, options?: TextProcessorOptions): boolean => {
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

const Options = ({
                   useTextarea,
                   operation: propsOperation,
                   options: propsOptions,
                   onChange,
                   variables,
                   availableValueTypes,
                 }: Props) => {
  const { t } = useTranslation();
  const [options, setOptions] = useState<TextProcessorOptions>(propsOptions ?? {});
  const [operation, setOperation] = useState<TextProcessingOperation>(propsOperation ?? TextProcessingOperation.SetWithFixedValue);

  const changeOptions = (patches: Partial<TextProcessorOptions>) => {
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

  const renderValueCell = () => {
    return (
      <ValueWithMultipleType
        valueTypes={availableValueTypes}
        Component={({ onChange }) => (useTextarea ? (
          <Textarea
            value={options.value}
            onValueChange={value => onChange(value, value)}
          />
        ) : (
          <Input
            value={options.value}
            onValueChange={value => onChange(value, value)}
          />
        ))}
        onChange={(valueType, value) => changeOptions({
          value,
          valueType,
        })}
        variables={variables}
      />
    );
  };

  const renderSubOptions = (options: TextProcessorOptions) => {
    log('renderOptions', options);

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
          comp: (
            <Input
              value={options.value}
              onValueChange={value => changeOptions({ value })}
            />
          ),
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
                <Input
                  className={'grow'}
                  // className={'w-auto'}
                  value={options.value}
                  onValueChange={value => changeOptions({ value })}
                />
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
                <Input
                  onValueChange={replace => changeOptions({ replace })}
                  value={options.replace}
                />
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
        onSelectionChange={keys => {
          changeOperation(parseInt(Array.from(keys || [])[0] as string, 10) as TextProcessingOperation);
        }}
      />
      {renderSubOptions(options)}
    </div>
  );
};

const Demonstrator = ({
                        operation,
                        options,
                      }: Props) => {

};

export default class TextProcessor {
  static Options = Options;
  static Demonstrator = Demonstrator;
}
