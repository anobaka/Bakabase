import { NumberPicker, Select } from '@alifd/next';
import React, { useEffect, useRef, useState } from 'react';
import { useUpdateEffect } from 'react-use';
import type { BRjsfProps } from '@/components/BRjsf';
import BRjsf from '@/components/BRjsf';

export default React.forwardRef((props: BRjsfProps, ref) => {
  // const value = props.value || {};
  const defaultValue = props.defaultValue || {};

  const propertiesRef = useRef({
    extensions: {
      Component: (props) => {
        const [value, setValue] = useState(props.defaultValue);

        useUpdateEffect(() => {
          if (props.onChange) {
            props.onChange(value);
          }
        }, [value]);

        const convertValue = v => {
          if (v && v.length > 0) {
            for (let i = 0; i < v.length; i++) {
              if (v[i] != undefined && !v[i]?.startsWith('.')) {
                v[i] = `.${v[i]}`;
              }
            }
          }
          return v.filter(a => a != undefined);
        };

        return (
          <Select
            {...props}
            value={value}
            onChange={v => {
              const av = convertValue(v);
              setValue(av);
            }}
          />);
      },
      componentProps: {
        mode: 'tag',
        size: 'small',
      },
    },
    maxFileCount: {
      convertValue: v => parseInt(v),
      Component: NumberPicker,
      componentProps: {
        size: 'small',
      },
    },
  });

  for (const v of [defaultValue]) {
    if (!v.extensions) {
      v.extensions = [];
    }
  }

  return (
    <BRjsf
      {...props}
      ref={ref}
      properties={propertiesRef.current}
      defaultValue={defaultValue}
      // value={value}
    />
  );
});
