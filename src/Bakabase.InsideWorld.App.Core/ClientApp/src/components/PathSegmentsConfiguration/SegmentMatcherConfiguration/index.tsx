import React, { useState } from 'react';
import { useUpdateEffect } from 'react-use';
import { useTranslation } from 'react-i18next';
import ByLayer from './ByLayer';
import type { IMatcherValue } from '@/components/PathSegmentsConfiguration/models/MatcherValue';
import { ResourceMatcherValueType } from '@/components/PathSegmentsConfiguration/models/MatcherValue';
import { ResourceProperty } from '@/sdk/constants';
import { Chip, Modal } from '@/components/bakaui';
import ByRegex from '@/components/PathSegmentsConfiguration/SegmentMatcherConfiguration/ByRegex';
import type { DestroyableProps } from '@/components/bakaui/types';

interface IValue {
  layer?: number;
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

export type SegmentMatcherConfigurationProps = DestroyableProps & {
  defaultValue?: IValue;
  modesData?: SegmentMatcherConfigurationModesData;
  onSubmit: (value: IMatcherValue) => void;
  property: {
    isReserved: boolean;
    id: number;
    name: string;
  };
  segments: string[];
  segmentIndex: number;
};

const getDefaultValue = (modesData: SegmentMatcherConfigurationProps['modesData']): IValue | undefined => {
  if (modesData) {
    if (modesData.layers.length == 1) {
      return {
        layer: modesData.layers[0],
      };
    }
  }
  return;
};

const SegmentMatcherConfiguration = (props: SegmentMatcherConfigurationProps) => {
  const {
    defaultValue,
    modesData = new SegmentMatcherConfigurationModesData(),
    onSubmit,
    property,
    segments,
    segmentIndex,
  } = props;
  const { t } = useTranslation();
  const defaultMode = modesData.layers.length > 0 ? 'layer' : Object.keys(modesData)[0] as ('layer' | 'regex');
  const [mode, setMode] = useState(defaultMode);
  const [value, setValue] = useState<IValue | undefined>(defaultValue ?? getDefaultValue(modesData));

  // console.log(props);

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

  return (
    <Modal
      onDestroyed={props.onDestroyed}
      size={'lg'}
      defaultVisible
      title={t('Configure [{{property}}] property for path segment', { property: property.name })}
      onOk={async () => {
        onSubmit?.({
          ...value,
          type: mode == 'layer' ? ResourceMatcherValueType.Layer : ResourceMatcherValueType.Regex,
        });
      }}
      footer={{
        actions: ['ok', 'cancel'],
        okProps: {
          isDisabled: !value || (mode == 'layer' && !value.layer) || (mode == 'regex' && (!value.regex)),
        },
      }}
    >
      <div className={''}>
        <div className={'font-bold'}>{t('Path segment')}</div>
        <div className={'flex items-center gap-1 mb-2'}>
          {segments.map((segment, index) => {
            return (
              <>
                {index == segmentIndex ? (
                  <Chip
                    variant={'light'}
                    color={index == segmentIndex ? 'primary' : 'default'}
                  >
                    {segment}
                  </Chip>
                ) : (
                  <span>{segment}</span>
                )}
                {index != segments.length - 1 && <span className={''}>/</span>}
              </>
            );
          })}
        </div>
        <ByLayer
          layers={modesData.layers}
          onSelectLayer={layer => {
            if (mode != 'layer') {
              setMode('layer');
            }
            setValue({
              ...value,
              layer,
            });
          }}
          selectedLayer={value?.layer}
          modeIsSelected={mode == 'layer'}
          onSelectMode={() => {
            setMode('layer');
          }}
        />
        <ByRegex
          regex={value?.regex}
          modeIsSelected={mode == 'regex'}
          onSelectMode={() => {
            setMode('regex');
          }}
          textToBeMatched={modesData.regex?.text}
          onRegexChange={v => {
            setValue({
              ...value,
              regex: v,
            });
          }}
          isResourceProperty={property.id == ResourceProperty.Resource && property.isReserved}
        />
      </div>
    </Modal>
  );
};

export default SegmentMatcherConfiguration;
