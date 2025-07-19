"use client";

import type { BRjsfProps } from "@/components/BRjsf";

import { Popover, Button, Input, Select, Table } from "@/components/bakaui";
import React, { useState } from "react";
import i18n from "i18next";
import { useUpdateEffect } from "react-use";

import BRjsf from "@/components/BRjsf";
import { FileSystemSelectorButton } from "@/components/FileSystemSelector";
import { MdPlayCircle } from 'react-icons/md';

const CommandTemplatePlaceholder = i18n.t<string>(
  "Default is `{0}`. {0} will be replaced by filename",
);

const commandTemplateTip = (
  <>
    {i18n.t<string>(
      "You can change the command template for some specific scenarios. The `{0}` will be replaced by filename and the default command template is `{0}`.",
    )}
    {i18n.t<string>("For example")}
    <br />
    {i18n.t<string>(
      "If the command template is `-i {0} --windowed`, the full command at runtime will be",
    )}{" "}
    <br />
    "C:\Program Files\DAUM\PotPlayer\PotPlayerMini64.exe" -i "D:\anime\dragon
    ball\1.mp4" --windowed'
  </>
);

export default React.forwardRef((props: BRjsfProps, ref) => {
  const value = props.value || {};
  const defaultValue = props.defaultValue || {};

  return (
    <BRjsf
      {...props}
      ref={ref}
      properties={{
        executable: {
          Component: (defaultValue, onChange) => (
            <FileSystemSelectorButton
              fileSystemSelectorProps={{
                targetType: 'file',
                onSelected: e => onChange(e.path),
                defaultSelectedPath: defaultValue,
              }}
            />
          ),
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
                    title={i18n.t<string>('Extensions')}
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
                    title={i18n.t<string>('Executable')}
                    dataIndex={'executable'}
                    cell={(f, i, r) => {
                      return (
                        <FileSystemSelectorButton
                          targetType={'file'}
                          defaultSelectedPath={f}
                          onSelected={e => setValue(prev => {
                            const newValue = [...prev];
                            newValue[i].executable = e.path;
                            return newValue;
                          })}
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
                        {i18n.t<string>('CommandTemplate')}
                        <Popover
                          triggerType={'hover'}
                          align={'t'}
                          style={{ maxWidth: 'unset' }}
                          trigger={<MdPlayCircle />}
                        >
                          {commandTemplateTip}
                        </Popover>
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
                    title={i18n.t<string>('Operations')}
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
                          <MdPlayCircle />
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
                >{i18n.t<string>('Add')}</Button>
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
