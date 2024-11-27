import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { Input, NumberInput, Radio, RadioGroup } from '@/components/bakaui';
import DirectionSelector from '@/pages/BulkModification2/components/BulkModification/DirectionSelector';

enum Mode {
  All = 1,
  Containing = 2,
  Matching = 3,
  Index = 4,
}

type Options = {
  mode?: Mode;
  value?: string;
  isPositioningDirectionReversed?: boolean;
};

type Props = {
  title?: string;
  options?: Options;
};

export default ({ options: propsOptions, title }: Props) => {
  const { t } = useTranslation();

  const [options, setOptions] = useState(propsOptions || {});

  const renderSubOptions = (options: Options) => {
    if (!options.mode || options.mode == Mode.All) {
      return null;
    }

    switch (options.mode) {
      case Mode.Containing:
      case Mode.Matching:
        return (
          <>
            <div>
              {t('Value')}
            </div>
            <div>
              <Input
                value={options.value}
                onValueChange={value => setOptions({ value })}
              />
            </div>
          </>
        );
      case Mode.Index:
        let nv = 1;
        if (options.value != undefined) {
          const int = parseInt(options.value, 10);
          if (!Number.isNaN(int)) {
            nv = int;
          }
        }
        return (
          <>
            <div>
              {t('Sequence number')}
            </div>
            <div>
              <DirectionSelector
                subject={'positioning'}
                isReversed={options.isPositioningDirectionReversed}
                onChange={v => setOptions({
                  isPositioningDirectionReversed: v,
                })}
              />
              <NumberInput
                value={nv}
                onValueChange={value => setOptions({ value: value.toString() })}
              />
            </div>
          </>
        );
    }
  };

  return (
    <>
      <div>{t('Mode')}</div>
      <div>
        <RadioGroup
          orientation={'horizontal'}
          value={options.mode?.toString()}
          onValueChange={(mode) => setOptions({
            ...options,
            mode: parseInt(mode, 10),
          })}
        >
          {Object.keys(Mode).map((x) => (
            <Radio key={Mode[x]} value={Mode[x]}>{x}</Radio>
          ))}
        </RadioGroup>
      </div>
      {renderSubOptions(options)}
    </>
  );
};


