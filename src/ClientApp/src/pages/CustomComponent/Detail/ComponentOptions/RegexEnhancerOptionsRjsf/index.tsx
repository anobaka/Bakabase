import type { FieldProps, FormValidation } from '@rjsf/utils';
import { Button, Input, NumberPicker, Select, Table } from '@alifd/next';
import type { CSSProperties } from 'react';
import React, { useCallback, useEffect, useImperativeHandle, useReducer, useRef, useState } from 'react';
import i18n from 'i18next';
import { useUpdateEffect } from 'react-use';
import type { BRjsfProps } from '@/components/BRjsf';
import BRjsf from '@/components/BRjsf';
import { reservedResourceProperties, ReservedProperty } from '@/sdk/constants';
import CustomIcon from '@/components/CustomIcon';
import { findCapturingGroupsInRegex } from '@/components/utils';

import './index.scss';

const typeDataSources = [
  {
    label: 'Reserved property',
    value: 1,
  },
  {
    label: 'Custom property',
    value: -1,
  },
].map(a => ({
  ...a,
  label: i18n.t(a.label)
    .toString(),
}));

const translatedReservedResourceProperties = reservedResourceProperties.map(a => ({
  ...a,
  label: i18n.t(a.label)
    .toString(),
}));


export default React.forwardRef((bRjsfProps: BRjsfProps, ref) => {
  const defaultBRjsfValue = bRjsfProps.defaultValue || {};

  const regexRef = useRef(defaultBRjsfValue.regex);
  const groupsComponentRef = useRef();
  const regexCapturingGroupsRef = useRef<string[]>(findCapturingGroupsInRegex(defaultBRjsfValue.regex));

  console.log(bRjsfProps, translatedReservedResourceProperties);

  useEffect(() => {
    console.log('[RegexEnhancerOptionsRjsf]Initialized');
  }, []);

  const propertiesRef = useRef({
    regex: {
      Component: (props) => {
        return (
          <Input
            {...props}
            onChange={v => {
              console.log(props);
              regexRef.current = v;
              const groups = findCapturingGroupsInRegex(v)
                .sort((a, b) => a.localeCompare(b));
              if (groups.length != regexCapturingGroupsRef.current.length || groups.some((ng, i) => ng != regexCapturingGroupsRef.current[i])) {
                regexCapturingGroupsRef.current = groups;
                groupsComponentRef.current?.forceUpdate();
              }
              if (props.onChange) {
                props.onChange(v);
              }
            }}
          />
        );
      },
      componentProps: {
        size: 'small',
      },
    },
    groups: {
      Component: React.forwardRef((props, ref) => {
        const [, forceUpdate] = useReducer((x) => x + 1, 0);
        const [value, setValue] = useState<any[]>(props.defaultValue && Object.prototype.toString.call(props.defaultValue) === '[object Array]' && props.defaultValue || []);

        const isValid = v => {
          if (v && v.every(t => t.name && t.isReserved != undefined && t.key != undefined && t.key.length > 0)) {
            return true;
          }
          return false;
        };

        useUpdateEffect(() => {
          console.log(value, isValid(value), props);
          if (isValid(value)) {
            if (props.onChange) {
              props.onChange(value);
            }
          }
        }, [value]);

        useImperativeHandle(ref, () => {
          return {
            forceUpdate: () => {
              console.log('force updated');
              forceUpdate();
            },
          };
        }, []);

        // useEffect(() => {
        //   setValue(convertPropsValue(props.value));
        // }, [props.value]);

        const definedGroupsInRegex = regexRef.current && regexRef.current.match(/\(\?<(\w+)>/g)
          ?.map(match => match.slice(3, -1)) || [];
        console.log('[RegexEnhancerOptionsRjsfGroups]rendering', value);

        return (
          <div
            className={'regex-groups'}
            style={{ width: '100%' }}
          >
            <Table
              dataSource={value}
              size={'small'}
            >
              <Table.Column
                title={i18n.t('Group name')}
                dataIndex={'name'}
                cell={(c, i, r) => {
                  return (
                    <div>
                      <Select
                        dataSource={definedGroupsInRegex.map(a => ({
                          label: a,
                          value: a,
                        }))}
                        onChange={v => {
                          r.name = v;
                          setValue([...value]);
                        }}
                        size={'small'}
                        value={c}
                      />
                      {regexCapturingGroupsRef.current?.indexOf(c) == -1 && (
                        <div className={'error'}>
                          {i18n.t('Invalid capturing group')}
                        </div>
                      )}
                    </div>
                  );
                }}
              />
              <Table.Column
                title={i18n.t('Type')}
                dataIndex={'isReserved'}
                cell={(c, i, r) => {
                  console.log('asdsdasasdasasda', c);
                  return (
                    <div>
                      <Select
                        dataSource={typeDataSources}
                        value={c == undefined ? undefined : c ? 1 : -1}
                        onChange={v => {
                          r.isReserved = v > 0;
                          setValue([...value]);
                        }}
                        size={'small'}
                      />
                      {c == undefined && (
                        <div className={'error'}>
                          {i18n.t('Type is not set')}
                        </div>
                      )}
                    </div>
                  );
                }}
              />
              <Table.Column
                title={i18n.t('Property')}
                dataIndex={'key'}
                cell={(c, i, r) => {
                  const error = c == undefined || c.length == 0;
                  return (
                    <div>
                      {r.isReserved ? (
                        <Select
                          dataSource={translatedReservedResourceProperties}
                          value={c ? ReservedProperty[c] : undefined}
                          onChange={v => {
                            r.key = ReservedProperty[v];
                            setValue([...value]);
                          }}
                          size={'small'}
                        />
                      ) : (
                        <Input
                          size={'small'}
                          style={{ width: 'auto' }}
                          trim
                          value={c}
                          onChange={v => {
                            r.key = v;
                            setValue([...value]);
                          }}
                        />
                      )}
                      {error && (
                        <div className={'error'}>
                          {i18n.t('Property is not set')}
                        </div>
                      )}
                    </div>
                  );
                }}
              />
              <Table.Column
                title={i18n.t('Operations')}
                cell={(_, i, r) => {
                  return (
                    <CustomIcon
                      type={'delete'}
                      onClick={() => {
                        value.splice(i, 1);
                        setValue([...value]);
                      }}
                    />
                  );
                }}
              />
            </Table>
            <Button
              size={'small'}
              type={'normal'}
              style={{ marginTop: 5 }}
              onClick={() => {
                const newValue = value || [];
                newValue.push({});
                setValue([...newValue]);
              }}
            >
              {i18n.t('Add')}
            </Button>
          </div>
        );
      }),
      componentProps: {
        size: 'small',
        ref: groupsComponentRef,
      }
      ,
    },
  });

  return (
    <BRjsf
      className={'regex-enhancer-options-rjsf'}
      {...bRjsfProps}
      ref={ref}
      customValidate={(formData, errors: FormValidation, uiSchema) => {
        console.log(formData);
        if (formData.groups) {
          if (!(formData.groups?.length > 0)) {
            errors.groups.addError(i18n.t('Invalid configuration'));
          } else {
            const invalidGroups = formData.groups.filter(a => regexCapturingGroupsRef.current?.indexOf(a.name) == -1);
            if (invalidGroups.length > 0) {
              errors.groups.addError(i18n.t('Multiple invalid capturing group names are found: {{names}}', { names: invalidGroups.map(a => a.name).join(',') }));
            } else {
              if (invalidGroups.some(d => {
                if (d.isReserved == undefined || d.key == undefined || d.key.length == 0 || d.name == undefined || d.name.length == 0) {
                  errors.groups.addError(i18n.t('Invalid group'));
                }
              })) {

              }
            }
          }
        }

        if (formData.regex) {
          if (regexCapturingGroupsRef.current.length == 0) {
            errors.regex.addError(i18n.t('No capturing groups are found'));
          }
        }

        return errors;
      }}
      properties={propertiesRef.current}
      defaultValue={
        defaultBRjsfValue
      }
    />
  );
});
