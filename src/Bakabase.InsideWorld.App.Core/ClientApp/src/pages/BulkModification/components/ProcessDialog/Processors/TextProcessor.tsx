import React, { useState } from 'react';
import { Trans, useTranslation } from 'react-i18next';
import { Input, NumberPicker, Select } from '@alifd/next';
import { useUpdateEffect } from 'react-use';
import type { IVariable } from '../../Variables';

interface IProps {
  variables: IVariable[];
  onChange?: (value: IValue) => any;
}

enum Operation {
  AddToStart = 1,
  AddToEnd,
  AddToAnyPosition,
  RemoveFromStart,
  RemoveFromEnd,
  RemoveFromAnyPosition,
  ReplaceFromStart,
  ReplaceFromEnd,
  ReplaceFromAnyPosition,
  ReplaceWithRegex,
}

interface IValue {
  operation?: Operation;
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
                }: IProps) => {
  const { t } = useTranslation();
  const [variables, setVariables] = useState<IVariable[]>(propsVariables || []);
  const [value, setValue] = useState<IValue>({});

  const operationDataSource = Object.keys(Operation).filter(k => Number.isNaN(parseInt(k, 10))).map(x => ({
    label: t(x),
    value: Operation[x],
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
    switch (value.operation) {
      case Operation.AddToStart:
      case Operation.AddToEnd:
        components.push({
          label: 'Value',
          comp: (
            <Input
              value={value.value}
              onChange={value => changeValue({ value })}
            />
          ),
        });
        break;
      case Operation.AddToAnyPosition:
        components.push({
          label: 'Value',
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
                />
                {/* 4 */}
                (th)&nbsp;character from&nbsp;
                {/* 5 */}
                <Select
                  dataSource={positionFromDataSource}
                  style={{ width: 150 }}
                  onChange={reverse => changeValue({ reverse })}
                />

              </Trans>
            </Input.Group>
          ),
        });
        break;
      case Operation.RemoveFromStart:
      case Operation.RemoveFromEnd:
        components.push({
          label: 'Count',
          comp: (
            <NumberPicker
              style={{ width: 200 }}
              onChange={count => changeValue({ count })}
            />
          ),
        });
        break;
      case Operation.RemoveFromAnyPosition:
        // delete 6 characters forward from the fifth character from the end
        components.push({
          label: 'Value',
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
                />
                {/* 2 */}
                &nbsp;characters&nbsp;
                {/* 3 */}
                <Select
                  dataSource={directionDataSource}
                  style={{ width: 150 }}
                  onChange={removeBefore => changeValue({ removeBefore })}
                />
                {/* 4 */}
                from the &nbsp;
                {/* 5 */}
                <NumberPicker
                  style={{ width: 100 }}
                  placeholder={t('Starts from 0')}
                  onChange={position => changeValue({ position })}
                />
                {/* 6 */}
                (th)&nbsp;charater from&nbsp;
                {/* 7 */}
                <Select
                  dataSource={positionFromDataSource}
                  style={{ width: 150 }}
                  onChange={reverse => changeValue({ reverse })}
                />
              </Trans>
            </Input.Group>
          ),
        });
        break;
      case Operation.ReplaceFromStart:
      case Operation.ReplaceFromEnd:
      case Operation.ReplaceFromAnyPosition:
      case Operation.ReplaceWithRegex:
        components.push({
          label: 'Value',
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
                />
                {/* 2 */}
                &nbsp;with&nbsp;
                {/* 3 */}
                <Input
                  onChange={replace => changeValue({ replace })}
                />
              </Trans>
            </Input.Group>
          ),
        });
        break;
    }
    return components.map(c => {
      return (
        <div className="block">
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


const Demonstrator = ({ value }: { value: IValue }) => {
  const { t } = useTranslation();


  switch (value.operation) {
    case Operation.AddToStart:
    case Operation.AddToEnd:
      return (
        <Trans
          i18nKey={'BulkModification.Processor.Demonstrator.Operation.AddToStartOrEnd'}
          values={{
            direction: value.operation == Operation.AddToStart ? t('Position.Beginning') : t('Position.End'),
            value: value.value,
          }}
        >
          Add
          <span className="secondary">{value.value}</span>
          to
          <span className="primary">beginning or end</span>
        </Trans>
      );
    case Operation.AddToAnyPosition:
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
          <div className="primary">{value.position}</div>
          position from the
          <div className="primary">end</div>
        </Trans>
      );
    case Operation.RemoveFromStart:
    case Operation.RemoveFromEnd:
      return (
        <>
          <Trans
            i18nKey={'BulkModification.Processor.Demonstrator.Operation.RemoveFromStartOrEnd'}
            values={{
              direction: value.operation == Operation.RemoveFromStart ? t('Position.Beginning') : t('Position.End'),
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
    case Operation.RemoveFromAnyPosition: {
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
            <span className="primary">{texts.count}</span>
            characters
            <span className="primary">{texts.removeDirection}</span>
            the
            <span className={'primary'}>{texts.position}</span>
            character from the
            <span className="primary">{texts.direction}</span>
          </Trans>
        </>
      );
    }
    case Operation.ReplaceFromStart:
    case Operation.ReplaceFromEnd: {
      const texts = {
        direction: value.operation == Operation.ReplaceFromEnd ? t('Position.End') : t('Position.Beginning'),
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
            <div className="primary">{texts.find}</div>
            {/* 2 */}
            with
            {/* 3 */}
            <div className={'primary'}>{texts.replace}</div>
            {/* 4 */}
            from
            {/* 5 */}
            <div className="primary">{texts.direction}</div>
          </Trans>
        </>
      );
    }
    case Operation.ReplaceFromAnyPosition: {
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
          <div className="primary">{texts.find}</div>
          {/* 2 */}
          with
          {/* 3 */}
          <div className={'primary'}>{texts.replace}</div>
        </Trans>
      );
    }
    case Operation.ReplaceWithRegex: {
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
          <div className="primary">{texts.find}</div>
          {/* 5 */}
          with
          {/* 6 */}
          <div className={'primary'}>{texts.replace}</div>
        </Trans>
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
