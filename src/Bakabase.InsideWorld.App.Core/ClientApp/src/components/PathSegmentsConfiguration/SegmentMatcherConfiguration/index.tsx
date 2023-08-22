import { Balloon, Button, Input, Radio, Tag } from '@alifd/next';
import React, { useState } from 'react';
import './index.scss';
import { useUpdateEffect } from 'react-use';
import IceLabel from '@icedesign/label';
import { useTranslation } from 'react-i18next';
import type { IMatcherValue } from '@/components/PathSegmentsConfiguration/models/MatcherValue';
import { ResourceMatcherValueType } from '@/components/PathSegmentsConfiguration/models/MatcherValue';
import { getResultFromExecAll } from '@/components/PathSegmentsConfiguration/utils';
import CustomIcon from '@/components/CustomIcon';
import { ResourceProperty } from '@/sdk/constants';

interface IValue {
  layer?: number;
  key?: string;
  regex?: string;
}

export class SegmentMatcherConfigurationModesData {
  layers: number[] = [];
  regex?: {
    text: string;
  };

  constructor(init?: Partial<SegmentMatcherConfigurationModesData>) {
    Object.assign(this, init);
  }

  get isConfigurable(): boolean {
    return this.layers.length > 0 || this.regex?.text != undefined;
  }

  get isFullConfigurable(): boolean {
    return this.layers.length > 0 && this.regex?.text != undefined;
  }
}

interface ISegmentMatcherConfiguration {
  defaultValue?: IValue;
  isCustomProperty: boolean;
  modesData?: SegmentMatcherConfigurationModesData;
  onSubmit: (value: IMatcherValue) => void;
  property: ResourceProperty;
  // onClose?: () => void;
}

const getDefaultValue = (modesData: ISegmentMatcherConfiguration['modesData']): IValue | undefined => {
  if (modesData) {
    if (modesData.layers.length == 1) {
      return {
        layer: modesData.layers[0],
      };
    }
  }
  return;
};

const SegmentMatcherConfiguration = (props: ISegmentMatcherConfiguration) => {
  const {
    defaultValue,
    modesData = new SegmentMatcherConfigurationModesData(),
    onSubmit,
    isCustomProperty,
    property,
    // onClose,
  } = props;
  const { t } = useTranslation();
  const defaultMode = modesData.layers.length > 0 ? 'layer' : Object.keys(modesData)[0] as ('layer' | 'regex');
  const [mode, setMode] = useState(defaultMode);
  const [value, setValue] = useState<IValue | undefined>(defaultValue ?? getDefaultValue(modesData));
  const [testResult, setTestResult] = useState<{
    success: boolean;
    error?: string;
    groups?: string[];
    text?: string;
  }>();

  console.log(props);

  useUpdateEffect(() => {
    switch (mode) {
      case 'layer':
        break;
      case 'regex':
        setValue({
          ...(value || {}),
          layer: undefined,
        });
        break;
    }
  }, [mode]);

  const renderLayerMode = () => {
    if (modesData.layers && modesData.layers.length > 0) {
      return (
        <div
          className={`by-layer mode ${mode == 'layer' ? 'active' : ''}`}
          onClick={() => {
            setMode('layer');
          }}
        >
          <div className="title">
            <Radio
              label={t('Set by {{thing}}', { thing: t('layer') })}
              checked={mode == 'layer'}
            />
          </div>
          <Tag.Group className="value">
            {modesData.layers.map(layer => {
              return (
                <Tag.Selectable
                  key={layer}
                  checked={value?.layer == layer}
                  onChange={c => {
                    if (c) {
                      setValue({
                        ...(value || {}),
                        layer,
                      });
                    }
                  }}
                >
                  {layer < 0 ? t('The {{layer}} layer to the resource', { layer: -layer }) : t('The {{layer}} layer after root path', { layer: layer })}
                </Tag.Selectable>
              );
            })}
          </Tag.Group>
        </div>
      );
    }
    return;
  };

  const renderTestResult = () => {
    // console.log(testResult);
    if (testResult) {
      if (!testResult.success) {
        return (
          <div className="test-result error">
            {testResult.error && <>{testResult.error}<br /></>}
            {t('Test failed, please check your regex')}
          </div>
        );
      }
      return (
        <div className="test-result values">
          {(testResult.groups ?? [testResult.text])!.map(v => (
            <IceLabel inverse={false} status={'default'}>
              {v}
            </IceLabel>
          ))}
        </div>
      );
    }
    return;
  };
  const renderRegexMode = () => {
    if (modesData.regex) {
      return (
        <div
          className={`by-regex mode ${mode == 'regex' ? 'active' : ''}`}
          onClick={() => {
            setMode('regex');
          }}
        >
          <div className="title">
            <Radio
              label={t('Set by {{thing}}', { thing: t('regex') })}
              checked={mode == 'regex'}
            />
            <Balloon.Tooltip
              trigger={(
                <CustomIcon type={'question-circle'} />
              )}
              triggerType={'hover'}
              align={'t'}
              v2
            >
              {t('/ is the directory separator always, not \\')}
              <br />
              <br />
              {t('The whole matched text will be ignored if capturing groups are used')}
              {t('You should not use capturing groups on Resource property due to partial path is not available to match a file system entry')}
            </Balloon.Tooltip>
          </div>
          <div className="value">
            <div className="text">
              <span>{t('Text to be matched')}:</span>
              &nbsp;
              <IceLabel inverse={false} status={'default'}>
                {modesData.regex.text}
              </IceLabel>
            </div>
            <Input.Group addonAfter={(
              <Button
                disabled={value?.regex == undefined}
                type={'normal'}
                onClick={() => {
                  if (value?.regex) {
                    try {
                      const regex = new RegExp(value.regex);
                      const v = getResultFromExecAll(regex, modesData.regex!.text);
                      console.log(v, property);
                      if (v && v.groups && v.groups.length > 0 && property == ResourceProperty.Resource) {
                        throw new Error(t('Capturing groups are not allowed on Resource property'));
                      }
                      if (v) {
                        setTestResult({
                          success: true,
                          ...v,
                        });
                      } else {
                        setTestResult({
                          success: false,
                        });
                      }
                    } catch (e) {
                      setTestResult({
                        success: false,
                        error: e.message,
                      });
                    }
                  }
                }}
              >{t('Test')}</Button>
            )}
            >
              <Input
                hasClear
                value={value?.regex}
                style={{ width: '100%' }}
                aria-label="please input"
                onChange={v => {
                  setValue({
                    ...(value || {}),
                    regex: v,
                  });
                }}
              />
            </Input.Group>
            {renderTestResult()}
          </div>
        </div>
      );
    }
    return;
  };

  return (
    <div className={'psc-smc'}>
      {isCustomProperty && (
        <div className="custom-property">
          <Input
            placeholder={t('Type here')}
            value={value?.key}
            state={value?.key ? 'success' : 'error'}
            label={`${t('Custom property key')}:`}
            onChange={v => {
              setValue({
                ...value,
                key: v,
              });
            }}
          />
        </div>
      )}
      {renderLayerMode()}
      {renderRegexMode()}
      <div className="opts">
        <Button
          type={'primary'}
          disabled={!value || (mode == 'layer' && !value.layer) || (mode == 'regex' && (!value.regex || !testResult?.success)) || (isCustomProperty && !value.key)}
          onClick={() => {
            if (onSubmit) {
              onSubmit({
                ...value,
                type: mode == 'layer' ? ResourceMatcherValueType.Layer : ResourceMatcherValueType.Regex,
              });
            }
          }}
        >
          {t('Submit')}
        </Button>
        {/* <Button */}
        {/*   type={'normal'} */}
        {/*   onClick={onClose} */}
        {/* > */}
        {/*   {t('Close')} */}
        {/* </Button> */}
      </div>
    </div>
  );
};

export default SegmentMatcherConfiguration;
