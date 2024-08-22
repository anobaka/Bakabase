import React from 'react';
import { useTranslation } from 'react-i18next';
import { Button, Card, CardBody, CardHeader, Radio, RadioGroup } from '@/components/bakaui';

type Props = {
  modeIsSelected: boolean;
  onSelectMode: () => void;
  onSelectLayer: (layer: number) => void;
  selectedLayer?: number;
  layers?: number[];
};

export default ({ modeIsSelected, onSelectMode, onSelectLayer, selectedLayer, layers }: Props) => {
  const { t } = useTranslation();

  if (!layers || layers.length == 0) {
    return null;
  }

  return (
    <Card
      isPressable
      className="mb-2 cursor-pointer w-full"
      onPress={() => {
        onSelectMode();
      }}
      isHoverable
    >
      <CardHeader className="text-lg font-bold">
        <RadioGroup
          value={modeIsSelected ? 'layer' : ''}
          onValueChange={onSelectMode}
        >
          <Radio value={'layer'} >
            {t('Set by {{thing}}', { thing: t('layer') })}
          </Radio>
        </RadioGroup>
      </CardHeader>
      <CardBody>
        <div className={'flex items-center flex-wrap gap-1'}>
          {layers.map(layer => {
            return (
              <Button
                key={layer}
                size={'sm'}
                className={''}
                color={selectedLayer == layer ? 'primary' : 'default'}
                variant={'bordered'}
                onClick={() => {
                  onSelectLayer(layer);
                }}
              >
                {layer < 0 ? t('The {{layer}} layer to the resource', { layer: -layer }) : t('The {{layer}} layer after root path', { layer: layer })}
              </Button>
            );
          })}
        </div>
      </CardBody>
    </Card>
  );
};
