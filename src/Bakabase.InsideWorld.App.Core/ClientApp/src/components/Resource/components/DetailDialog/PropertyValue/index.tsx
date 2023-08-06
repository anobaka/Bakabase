import React, { useState } from 'react';
import { Button, Icon } from '@alifd/next';
import { RemoveResourceCustomProperty, PatchResource } from '@/sdk/apis';
import CustomIcon from '@/components/CustomIcon';

export default ({
  renderValue: propsRenderValue,
  editable = false,
  convertToRequesting = (v) => v,
  resourceId = undefined,
  requestKey = undefined,
  initValue = undefined,
  isCustomProperty = false,
  EditComponent = undefined,
  editComponentProps = {},
  defaultValueKeyOfEditComponent = 'defaultValue',
  reloadResource = (cb) => {},
  className = '' }: {
  renderValue: () => any;
  resourceId: number | undefined;
  convertToRequesting: ((v: any) => any) | undefined;
  reloadResource: ((cb: Function) => void) | undefined;
  initValue: any;
  isCustomProperty: boolean;
  EditComponent: React.Component | any;
  editComponentProps: any | undefined;
  defaultValueKeyOfEditComponent: string | undefined;
  editable: boolean;
  requestKey: string| undefined;
  className: string | undefined;
}) => {
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState<any>();

  const renderValue = () => {
    if (editing) {
      const reservedProps = {
        [defaultValueKeyOfEditComponent]: value,
        onChange: (v) => {
          // console.log('1321312123', v);
          if (v == value && Array.isArray(v)) {
            setValue([...v]);
          } else {
            setValue(v);
          }
        },
      };
      return (
        <div className={'value'} style={{ flex: 1 }}>
          <EditComponent {...editComponentProps} {...reservedProps} />
        </div>
      );
    } else {
      const rv = propsRenderValue();
      // console.log(requestKey, rv, typeof rv, Array.isArray(rv), rv == null || rv == undefined || rv === '');
      const isEmpty = Array.isArray(rv) ? rv.length == 0 : (rv == null || rv == undefined || rv === '');
      return (
        <div className={`value ${isEmpty ? 'empty' : ''}`}>
          {rv}
        </div>
      );
    }
  };

  const renderOpts = () => {
    if (editing) {
      return (
        <div className={'edit-opts'}>
          <Button
            size={'small'}
            type={'secondary'}
            className={'submit'}
            onClick={() => {
              let model = value;
              if (convertToRequesting) {
                model = convertToRequesting(model);
              }

              PatchResource({
                id: resourceId,
                model: {
                  [requestKey]: model,
                },
              })
                .invoke((x) => {
                  if (!x.code) {
                    reloadResource(() => {
                      setEditing(false);
                      setValue(undefined);
                    });
                  }
                });
            }}
          >
            <Icon type="select" />
          </Button>
          <Button
            size={'small'}
            onClick={() => {
              setEditing(false);
              setValue(undefined);
            }}
            warning
            className={'cancel'}
          >
            <Icon type="close" />
          </Button>
        </div>
      );
    } else {
      return (
        <>
          {
            editable && (
              <>
                <CustomIcon
                  size={'small'}
                  type={'edit-square'}
                  onClick={() => {
                    setEditing(true);
                    setValue(initValue);
                    console.log(`Set edit target [${requestKey}] with default value `, initValue);
                  }}
                />
              </>
            )
          }
          {
            isCustomProperty && (
              <CustomIcon
                size={'small'}
                type={'delete'}
                onClick={() => {
                  if (confirm('Sure to delete?')) {
                    RemoveResourceCustomProperty({
                      id: resourceId,
                      propertyKey: requestKey,
                    }).invoke((x) => {
                      if (!x.code) {
                        reloadResource(() => {
                          setEditing(false);
                          setValue(undefined);
                        });
                      }
                    });
                  }
                }}
              />
            )
          }
        </>
      );
    }
  };

  return (
    <div className={`property-value ${className}`}>
      {renderValue()}
      {renderOpts()}
    </div>
  );
};
