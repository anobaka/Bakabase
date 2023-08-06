import React, { useState } from 'react';
import { SketchPicker } from 'react-color';

export default ({
  color: propColor,
  onChange = (hex) => {},
  panelOffset = { top: 0, left: '60px' },
}) => {
  const [color, setColor] = useState(propColor);
  const [colorPickerVisible, setColorPickerVisible] = useState(false);

  return (
    <div style={{ position: 'relative', width: 26, height: 26 }}>
      <div
        style={{
          padding: '5px',
          background: '#fff',
          borderRadius: '1px',
          boxShadow: '0 0 0 1px rgba(0,0,0,.1)',
          display: 'inline-block',
          cursor: 'pointer',
        }}
        onClick={() => setColorPickerVisible(true)}
      >
        <div style={{
          width: '16px',
          height: '16px',
          borderRadius: '2px',
          background: color,
        }}
        />
      </div>
      {colorPickerVisible ? (
        <div style={{ position: 'absolute', ...panelOffset, zIndex: '20' }}>
          <div
            style={{
              position: 'fixed',
              top: '0px',
              right: '0px',
              bottom: '0px',
              left: '0px',
            }}
            onClick={() => setColorPickerVisible(false)}
          />
          <SketchPicker
            color={{ hex: color }}
            onChange={(v) => {
              setColor(v.hex);
              onChange(v.hex);
            }}
          />
        </div>
      ) : null}
    </div>
  );
};
