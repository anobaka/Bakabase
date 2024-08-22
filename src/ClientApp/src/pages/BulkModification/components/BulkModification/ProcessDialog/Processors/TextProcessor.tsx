import React, { useState } from 'react';
import { Trans, useTranslation } from 'react-i18next';
import { Input, NumberPicker, Select } from '@alifd/next';
import { useUpdateEffect } from 'react-use';
import type { IVariable } from '../../Variables';
import { TextProcessOperation } from '@/sdk/constants';

interface IProps {
  variables: IVariable[];
  onChange?: (value: ITextProcessorValue) => any;
  value?: ITextProcessorValue;
  removable?: boolean;
  longText?: boolean;
}

// enum Operation {
//   Remove = 1,
//   SetWithFixedValue,
//   AddToStart,
//   AddToEnd,
//   AddToAnyPosition,
//   RemoveFromStart,
//   RemoveFromEnd,
//   RemoveFromAnyPosition,
//   ReplaceFromStart,
//   ReplaceFromEnd,
//   ReplaceFromAnyPosition,
//   ReplaceWithRegex,
// }

export interface ITextProcessorValue {
  operation?: TextProcessOperation;
  find?: string;
  replace?: string;
  value?: string;
  position?: number;
  reverse?: boolean;
  removeBefore?: boolean;
  count?: number;
  start?: number;
  end?: number;
}

const Editor = ({
                  variables: propsVariables,
                  onChange,
                  value: propsValue,
                  removable = true,
                  longText = false,
                }: IProps) => {
  const { t } = useTranslation();
  const [variables, setVariables] = useState<IVariable[]>(propsVariables || []);
  const [value, setValue] = useState<ITextProcessorValue>(propsValue ?? {});

  const operationDataSource = Object.keys(TextProcessOperation).filter(label => {
    const textValue = parseInt(label, 10);
    return Number.isNaN(textValue) && (removable || TextProcessOperation[label] != TextProcessOperation.Remove);
  }).map(x => ({
    label: t(x),
    value: TextProcessOperation[x],
  }));
  const positionFromDataSource = [{
    label: t('Position.Beginning'),
    value: false,
  }, {
    label: t('Position.End'),
    value: true,
  }];

  const directionDataSource = [{
    label: t('Forward'),
    value: false,
  }, {
    label: t('Backward'),
    value: true,
  }];

  useUpdateEffect(() => {
    if (onChange) {
      onChange(value);
    }
  }, [value]);

  const changeValue = (patches: Record<any, any>) => {
    setValue({
      ...value,
      ...patches,
    });
  };

  const renderValueComp = () => {
    const components: { label: string; comp: any }[] = [];
    if (longText) {
      console.log('longlonglong', value.operation);
    }
    switch (value.operation) {
      case TextProcessOperation.SetWithFixedValue:
        components.push({
          label: t('Value'),
          comp: longText ? (
            <Input.TextArea
              value={value.value}
              onChange={value => changeValue({ value })}
              autoHeight={{ minRows: 2, maxRows: 10 }}
            />
          ) : (
            <Input
              value={value.value}
              onChange={value => changeValue({ value })}
            />
          ),
        });
        break;
      case TextProcessOperation.Remove:
        break;
      case TextProcessOperation.AddToStart:
      case TextProcessOperation.AddToEnd:
        components.push({
          label: t('Value'),
          comp: (
            <Input
              value={value.value}
              onChange={value => changeValue({ value })}
            />
          ),
        });
        break;
      case TextProcessOperation.AddToAnyPosition:
        components.push({
          label: t('Value'),
          comp: (
            <Input.Group>
              <Trans
                i18nKey={'BulkModification.Processor.Editor.Operation.AddToAnyPosition'}
                values={{
                  value: value.value,
                }}
              >
                {/* 0 */}
                Add&nbsp;
                {/* 1 */}
                <Input
                  value={value.value}
                  onChange={value => changeValue({ value })}
                />
                {/* 2 */}
                &nbsp;to the&nbsp;
                {/* 3 */}
                <NumberPicker
                  style={{ width: 100 }}
                  placeholder={t('Starts from 0')}
                  onChange={position => changeValue({ position })}
                  value={value.position}
                />
                {/* 4 */}
                (th)&nbsp;character from&nbsp;
                {/* 5 */}
                <Select
                  dataSource={positionFromDataSource}
                  style={{ width: 150 }}
                  onChange={reverse => changeValue({ reverse })}
                  value={value.reverse}
                />

              </Trans>
            </Input.Group>
          ),
        });
        break;
      case TextProcessOperation.RemoveFromStart:
      case TextProcessOperation.RemoveFromEnd:
        components.push({
          label: t('Count'),
          comp: (
            <NumberPicker
              style={{ width: 200 }}
              onChange={count => changeValue({ count })}
              value={value.count}
            />
          ),
        });
        break;
      case TextProcessOperation.RemoveFromAnyPosition:
        // delete 6 characters forward from the fifth character from the end
        components.push({
          label: t('Value'),
          comp: (
            <Input.Group>
              <Trans
                i18nKey={'BulkModification.Processor.Editor.Operation.RemoveFromAnyPosition'}
                values={{
                  value: value.value,
                }}
              >
                {/* 0 */}
                Delete&nbsp;
                {/* 1 */}
                <NumberPicker
                  style={{ width: 200 }}
                  onChange={count => changeValue({ count })}
                  value={value.count}
                />
                {/* 2 */}
                &nbsp;characters&nbsp;
                {/* 3 */}
                <Select
                  dataSource={directionDataSource}
                  style={{ width: 150 }}
                  onChange={removeBefore => changeValue({ removeBefore })}
                  value={value.removeBefore}
                />
                {/* 4 */}
                from the &nbsp;
                {/* 5 */}
                <NumberPicker
                  style={{ width: 100 }}
                  placeholder={t('Starts from 0')}
                  onChange={position => changeValue({ position })}
                  value={value.position}
                />
                {/* 6 */}
                (th)&nbsp;charater from&nbsp;
                {/* 7 */}
                <Select
                  dataSource={positionFromDataSource}
                  style={{ width: 150 }}
                  onChange={reverse => changeValue({ reverse })}
                  value={value.reverse}
                />
              </Trans>
            </Input.Group>
          ),
        });
        break;
      case TextProcessOperation.ReplaceFromStart:
      case TextProcessOperation.ReplaceFromEnd:
      case TextProcessOperation.ReplaceFromAnyPosition:
      case TextProcessOperation.ReplaceWithRegex:
        components.push({
          label: t('Value'),
          comp: (
            <Input.Group>
              <Trans
                i18nKey={'BulkModification.Processor.Editor.Operation.Replace'}
              >
                {/* 0 */}
                Replace&nbsp;
                {/* 1 */}
                <Input
                  onChange={find => changeValue({ find })}
                  value={value.find}
                />
                {/* 2 */}
                &nbsp;with&nbsp;
                {/* 3 */}
                <Input
                  onChange={replace => changeValue({ replace })}
                  value={value.replace}
                />
              </Trans>
            </Input.Group>
          ),
        });
        break;
    }
    return components.map((c, i) => {
      return (
        <div className="block" key={i}>
          <div className="label">{c.label}</div>
          <div className="value">{c.comp}</div>
        </div>
      );
    });
  };

  return (
    <>
      <div className="block">
        <div className={'label'}>
          {t('Operation')}
        </div>
        <div className="value">
          <Select
            style={{ width: 300 }}
            dataSource={operationDataSource}
            value={value.operation}
            onChange={v => setValue({
              operation: v,
            })}
          />
        </div>
      </div>
      {renderValueComp()}
    </>
  );
};


const Demonstrator = ({ value }: { value: ITextProcessorValue }) => {
  const { t } = useTranslation();


  switch (value.operation!) {
    case TextProcessOperation.SetWithFixedValue: {
      return (
        <>
          <Trans
            i18nKey={'BulkModification.Processor.Demonstrator.Operation.SetWithFixedValue'}
          >
            <div className="primary" />
            with fixed value
          </Trans>
          <div className="secondary pre">{value.value}</div>
        </>
      );
    }
    case TextProcessOperation.AddToStart:
    case TextProcessOperation.AddToEnd:
      return (
        <Trans
          i18nKey={'BulkModification.Processor.Demonstrator.Operation.AddToStartOrEnd'}
          values={{
            direction: value.operation == TextProcessOperation.AddToStart ? t('Position.Beginning') : t('Position.End'),
            value: value.value,
          }}
        >
          Add
          <span className="secondary">{value.value}</span>
          to
          <span className="primary">beginning or end</span>
        </Trans>
      );
    case TextProcessOperation.AddToAnyPosition:
      return (
        <Trans
          i18nKey={'BulkModification.Processor.Demonstrator.Operation.AddToAnyPosition'}
          values={{
            direction: value.reverse ? t('Position.End') : t('Position.Beginning'),
            position: value.position,
            value: value.value,
          }}
        >
          Add
          <div className="secondary">{value.value}</div>
          to the
          <div className="secondary">{value.position}</div>
          position from the
          <div className="primary">end</div>
        </Trans>
      );
    case TextProcessOperation.RemoveFromStart:
    case TextProcessOperation.RemoveFromEnd:
      return (
        <>
          <Trans
            i18nKey={'BulkModification.Processor.Demonstrator.Operation.RemoveFromStartOrEnd'}
            values={{
              direction: value.operation == TextProcessOperation.RemoveFromStart ? t('Position.Beginning') : t('Position.End'),
              count: value.count,
            }}
          >
            Remove
            <span className="secondary">{value.count}</span>
            characters from
            <span className="primary">beginning or end</span>
          </Trans>
        </>
      );
    case TextProcessOperation.RemoveFromAnyPosition: {
      const texts = {
        direction: value.reverse ? t('Position.End') : t('Position.Beginning'),
        position: value.position,
        count: value.count,
        removeDirection: value.removeBefore ? t('TextOperation.Backward') : t('TextOperation.Forward'),
      };
      return (
        <>
          <Trans
            i18nKey={'BulkModification.Processor.Demonstrator.Operation.RemoveFromAnyPosition'}
            values={texts}
          >
            {/* delete 6 characters forward from the fifth character from the end */}
            Delete
            <span className="secondary">{texts.count}</span>
            characters
            <span className="primary">{texts.removeDirection}</span>
            the
            <span className={'secondary'}>{texts.position}</span>
            character from the
            <span className="primary">{texts.direction}</span>
          </Trans>
        </>
      );
    }
    case TextProcessOperation.ReplaceFromStart:
    case TextProcessOperation.ReplaceFromEnd: {
      const texts = {
        direction: value.operation == TextProcessOperation.ReplaceFromEnd ? t('Position.End') : t('Position.Beginning'),
        replace: value.replace,
        find: value.find,
      };
      return (
        <>
          <Trans
            i18nKey={'BulkModification.Processor.Demonstrator.Operation.ReplaceFromStartOrEnd'}
            values={texts}
          >
            {/* Replace xxx with yyy from start */}
            {/* 0 */}
            <div className="primary">Replace</div>
            {/* 1 */}
            <div className="secondary">{texts.find}</div>
            {/* 2 */}
            with
            {/* 3 */}
            <div className={'secondary'}>{texts.replace}</div>
            {/* 4 */}
            from
            {/* 5 */}
            <div className="primary">{texts.direction}</div>
          </Trans>
        </>
      );
    }
    case TextProcessOperation.ReplaceFromAnyPosition: {
      const texts = {
        direction: value.reverse ? t('end') : t('start'),
        replace: value.replace,
        find: value.find,
      };
      return (
        <Trans
          i18nKey={'BulkModification.Processor.Demonstrator.Operation.Replace'}
          values={texts}
        >
          {/* Replace xxx with yyy */}
          {/* 0 */}
          <div className="primary">Replace</div>
          {/* 1 */}
          <div className="secondary">{texts.find}</div>
          {/* 2 */}
          with
          {/* 3 */}
          <div className={'secondary'}>{texts.replace}</div>
        </Trans>
      );
    }
    case TextProcessOperation.ReplaceWithRegex: {
      const texts = {
        replace: value.replace,
        find: value.find,
      };
      return (
        <Trans
          i18nKey={'BulkModification.Processor.Demonstrator.Operation.ReplaceWithRegex'}
          values={texts}
        >
          {/* Use regex to replace xxx with yyy */}
          {/* 0 */}
          Use
          {/* 1 */}
          <div className="primary">regex</div>
          {/* 2 */}
          to
          {/* 3 */}
          <div className="primary">replace</div>
          {/* 4 */}
          <div className="secondary">{texts.find}</div>
          {/* 5 */}
          with
          {/* 6 */}
          <div className={'secondary'}>{texts.replace}</div>
        </Trans>
      );
    }
    case TextProcessOperation.Remove: {
      return (
        <div className={'primary'}>
          {t('Remove')}
        </div>
      );
    }
    default:
      return (
        <>
          {t('Unsupported value')}
        </>
      );
  }
};

export default class TextProcessor {
  static Editor = Editor;
  static Demonstrator = Demonstrator;
}
