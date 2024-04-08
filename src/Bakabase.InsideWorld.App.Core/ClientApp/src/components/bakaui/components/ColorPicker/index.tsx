import { SketchPicker } from 'react-color';
import React, { useState } from 'react';
import { Popover } from '@/components/bakaui';

type Color = 'string' |
  { r: number; g: number; b: number; a: number } |
  { h: number; s: number; l: number; a: number };

interface ColorPickerProps {
  trigger?: any;
  defaultColor?: Color;
  color?: Color;
  onChange?: (color: Color) => any;
}

function buildColorValueString(color: Color) {
  const strColor = color as string;
  if (strColor) {
    return strColor;
  }
  const rgbColor = color as { r: number; g: number; b: number; a: number };
  if (rgbColor) {
    return `rgba(${rgbColor.r},${rgbColor.g},${rgbColor.b},${rgbColor.a})`;
  }
  const hslColor = color as { h: number; s: number; l: number; a: number };
  if (hslColor) {
    return `hsla(${hslColor.h},${hslColor.s},${hslColor.l},${hslColor.a})`;
  }
  return '';
}

const ColorPicker = ({
                       trigger,
                       ...props
                     }: ColorPickerProps) => {
  const [panelVisible, setPanelVisible] = React.useState(false);
  const [color, setColor] = useState(props.defaultColor ?? props.color);

  return (
    <Popover
      closeMode={['mask', 'esc']}
      trigger={(
        trigger ?? (
          <div
            className={'inline-block rounded cursor-pointer border border-gray-300 p-0.5 text-center'}
            onClick={() => {
              setPanelVisible(true);
            }}
          >
            <div
              className={'p-2'}
              style={{
                backgroundColor: color ? buildColorValueString(color) : 'var(--bakaui-color)',
              }}
            />
          </div>
        )
      )}
      visible={panelVisible}
      onVisibleChange={setPanelVisible}
    >
      <SketchPicker
        color={color}
        onChange={(v) => {
          if (v.hex) {
            setColor(v.hex);
            props.onChange?.(v.hex);
          } else {
            if (v.rgb) {
              setColor(v.rgb);
              props.onChange?.(v.rgb);
            } else {
              if (v.hsl) {
                setColor(v.hsl);
                props.onChange?.(v.hsl);
              }
            }
          }
        }}
      />
    </Popover>
  );
};

export default ColorPicker;
