import React, { useCallback, useState } from 'react';
import { Balloon, Button, Dialog, Input, Message, Select } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import SimpleLabel from '@/components/SimpleLabel';
import './index.scss';
import ClickableIcon from '@/components/ClickableIcon';

enum VariableSource {
  None = 1,
  FileName,
  FileNameWithoutExtension,
  FullPath,
  DirectoryName,
  Name,
}

export interface IVariable {
  name?: string;
  key: string;
  source: VariableSource;
  find?: string;
  value: string;
}

interface IEditingVariable {
  name?: string;
  key?: string;
  source?: VariableSource;
  find?: string;
  value?: string;
  index?: number;
}

interface IProps {
  variables?: IVariable[];
  onChange?: (variables: IVariable[]) => void;
  editable: boolean;
}

const variableSources = Object.keys(VariableSource).filter(a => Number.isNaN(parseInt(a, 10)));

export default ({
                  variables: propsVariable,
                  onChange,
                  editable,
                }: IProps) => {
  const { t } = useTranslation();

  const [variables, setVariables] = useState<IVariable[]>(propsVariable || []);
  const [editingVariable, setEditingVariable] = useState<IEditingVariable>();

  const closeDialog = useCallback(() => {
    setEditingVariable(undefined);
  }, []);

  return (
    <div className={'bulk-modification-variables'}>
      <Dialog
        visible={!!editingVariable}
        title={t('Setting variable')}
        width={'auto'}
        closeMode={['close', 'esc', 'mask']}
        onClose={closeDialog}
        onCancel={closeDialog}
        className={'bulk-modification-variable-dialog'}
        onOk={() => {
          // console.log(editingVariable);
          if (!editingVariable || editingVariable.key == undefined || editingVariable.key.length == 0 ||
            editingVariable.value == undefined || editingVariable.value.length == 0 ||
            variables.some((v, i) => v != editingVariable && v.key === editingVariable?.key && editingVariable.index != i) ||
            variableSources.includes(editingVariable.key) ||
            editingVariable.source == undefined || !variableSources.includes(VariableSource[editingVariable?.source])
          ) {
            return Message.error(t('Invalid data'));
          }

          if (editingVariable.index != undefined) {
            variables[editingVariable.index] = editingVariable as IVariable;
          } else {
            variables.push(editingVariable as IVariable);
          }
          const nv = [...variables];
          setVariables(nv);
          onChange?.(nv);
          closeDialog();
        }}
      >
        <div className="variable-form">
          <div className="label">
            {t('Source')}
          </div>
          <div className="value">
            <Select
              dataSource={variableSources.map(s => ({
                label: t(s),
                value: VariableSource[s],
              }))}
              value={editingVariable?.source}
              onChange={v => {
                setEditingVariable({
                  ...editingVariable,
                  source: v,
                });
              }}
              style={{ width: '100%' }}
            />
          </div>
          <div className="label">
            {t('Name')}
          </div>
          <div className="value">
            <Input
              value={editingVariable?.name}
              onChange={v => {
                setEditingVariable({
                  ...editingVariable,
                  name: v,
                });
              }}
            />
          </div>
          <div className="label">
            {t('Variable name')}
          </div>
          <div className="value">
            <Input
              state={editingVariable?.key == undefined || variables.some((v, i) => v != editingVariable && v.key === editingVariable?.key && editingVariable.index != i) ||
              variableSources.includes(editingVariable?.key) ? 'error' : undefined}
              value={editingVariable?.key}
              onChange={v => {
                setEditingVariable({
                  ...editingVariable,
                  key: v,
                });
              }}
            />
          </div>
          <div className="label">
            {t('Find')}
          </div>
          <div className="value">
            <Input
              value={editingVariable?.find}
              onChange={v => {
                setEditingVariable({
                  ...editingVariable,
                  find: v,
                });
              }}
            />
          </div>
          <div className="label">
            {t('Value')}
          </div>
          <div className="value">
            <Input
              value={editingVariable?.value}
              state={editingVariable?.value == undefined || editingVariable.value.length == 0 ? 'error' : undefined}
              onChange={v => {
                setEditingVariable({
                  ...editingVariable,
                  value: v,
                });
              }}
            />
          </div>
        </div>
      </Dialog>
      <div className={'values'}>
        {variableSources.filter(key => VariableSource[key] != VariableSource.None).map((key: any, i) => {
          return (
            <div className={'value'} key={key}>
              <SimpleLabel
                status={'default'}
                key={key}
              >
                {key}
              </SimpleLabel>
              {t(key)}
            </div>
          );
        })}
        {variables.map((v, i) => {
          return (
            <Balloon.Tooltip
              key={i}
              trigger={(
                <div
                  className={'value custom'}
                  key={i}
                  onClick={() => {
                    if (editable) {
                      setEditingVariable({
                        ...v,
                        index: i,
                      });
                    }
                  }}
                >
                  <SimpleLabel
                    status={'primary'}
                    key={i}
                  >
                    {v.key}
                  </SimpleLabel>
                  {v.name}
                  {editable && (
                    <ClickableIcon
                      size={'small'}
                      colorType={'danger'}
                      type={'delete'}
                      onClick={e => {
                        e.stopPropagation();
                        variables.splice(i, 1);
                        setVariables([...variables]);
                        onChange?.(variables);
                      }}
                    />
                  )}
                </div>
              )}
              triggerType={'hover'}
              align={'t'}
              v2
            >
              [{t('Source')}] {t(VariableSource[v.source])}, [{t('Find')}] {v.find}, [{t('Value')}] {v.value}
            </Balloon.Tooltip>
          );
        })}
      </div>
      {editable && (
        <Button
          size={'small'}
          type={'primary'}
          onClick={() => {
            setEditingVariable({});
          }}
        >
          {t('Add')}
        </Button>
      )}
    </div>
  );
};
