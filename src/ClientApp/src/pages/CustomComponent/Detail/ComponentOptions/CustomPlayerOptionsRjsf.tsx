import type { FieldProps } from '@rjsf/utils';
import { Balloon, Button, Input, NumberPicker, Select, Table } from '@alifd/next';
import React, { useState } from 'react';
import i18n from 'i18next';
import { useUpdateEffect } from 'react-use';
import type { BRjsfProps } from '@/components/BRjsf';
import BRjsf from '@/components/BRjsf';
import FileSelector from '@/components/FileSelector';
import CustomIcon from '@/components/CustomIcon';

const CommandTemplatePlaceholder = i18n.t('Default is `{0}`. {0} will be replaced by filename');

const commandTemplateTip = (<>
  {i18n.t('You can change the command template for some specific scenarios. The `{0}` will be replaced by filename and the default command template is `{0}`.')}{i18n.t('For example')}
  <br />
  {i18n.t('If the command template is `-i {0} --windowed`, the full command at runtime will be')} <br />
  "C:\Program Files\DAUM\PotPlayer\PotPlayerMini64.exe" -i "D:\anime\dragon ball\1.mp4" --windowed'
</>);


export default React.forwardRef((props: BRjsfProps, ref) => {
  const value = props.value || {};
  const defaultValue = props.defaultValue || {};

  return (
    <BRjsf
      {...props}
      ref={ref}
      properties={{
        executable: {
          Component: FileSelector,
          componentProps: {
            size: 'small',
            multiple: false,
            type: 'file',
          },
        },
        commandTemplate: {
          Component: Input,
          componentProps: {
            size: 'small',
            placeholder: CommandTemplatePlaceholder,
          },
          tip: commandTemplateTip,
        },
        subPlayers: {
          Component: (props) => {
            const [value, setValue] = useState(props.defaultValue || []);

            console.log(value);

            useUpdateEffect(() => {
              if (props.onChange) {
                props.onChange(value);
              }
            }, [value]);

            return (
              <div className={'sub-options-list'} style={{ width: '100%' }}>
                <Table
                  dataSource={value}
                  size={'small'}
                >
                  <Table.Column
                    title={i18n.t('Extensions')}
                    dataIndex={'extensions'}
                    cell={(data, i, r) => {
                      return (
                        <Select
                          size={'small'}
                          mode={'tag'}
                          value={data}
                          onChange={v => {
                            value[i].extensions = v.map(a => {
                              if (!a?.startsWith('.')) {
                                return `.${a}`;
                              }
                              return a;
                            });
                            setValue([...value]);
                          }}
                        />
                      );
                    }}
                  />
                  <Table.Column
                    title={i18n.t('Executable')}
                    dataIndex={'executable'}
                    cell={(f, i, r) => {
                      return (
                        <FileSelector
                          size={'small'}
                          type={'file'}
                          value={f}
                          onChange={v => {
                            value[i].executable = v;
                            setValue([...value]);
                          }}
                        />
                      );
                    }}
                  />
                  <Table.Column
                    title={(
                      <div style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: 5,
                      }}
                      >
                        {i18n.t('CommandTemplate')}
                        <Balloon.Tooltip
                          triggerType={'hover'}
                          align={'t'}
                          style={{ maxWidth: 'unset' }}
                          trigger={<CustomIcon type={'question-circle'} />}
                        >
                          {commandTemplateTip}
                        </Balloon.Tooltip>
                      </div>
                    )}
                    dataIndex={'commandTemplate'}
                    cell={(c, i, r) => {
                      return (
                        <Input
                          size={'small'}
                          value={c}
                          placeholder={CommandTemplatePlaceholder}
                          onChange={v => {
                            value[i].commandTemplate = v;
                            setValue([...value]);
                          }}
                        />
                      );
                    }}
                  />
                  <Table.Column
                    title={i18n.t('Operations')}
                    cell={(_, i, r) => {
                      return (
                        <Button
                          size={'small'}
                          style={{
                            padding: '0 5px',
                            display: 'flex',
                            alignItems: 'center',
                          }}
                          warning
                          onClick={() => {
                            value.splice(i, 1);
                            setValue([...value]);
                          }}
                        >
                          <CustomIcon type={'delete'} />
                        </Button>
                      );
                    }}
                  />
                </Table>
                <Button
                  type={'normal'}
                  size={'small'}
                  style={{ marginTop: '5px' }}
                  onClick={() => {
                    const newValue = value || [];
                    newValue.push({});
                    setValue([...newValue]);
                  }}
                >{i18n.t('Add')}</Button>
              </div>
            );
          },
        },
      }}
      // value={value}
      defaultValue={defaultValue}
    />
  );
});
