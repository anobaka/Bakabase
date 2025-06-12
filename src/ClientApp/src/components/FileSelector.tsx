import React, { useState } from 'react';
import { Message } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import { buildLogger } from '@/components/utils';
import BApi from '@/sdk/BApi';
import type { ButtonProps } from '@/components/bakaui';
import { Button } from '@/components/bakaui';


interface Props {
  size: ButtonProps['size'];
  type: 'file' | 'folder';
  defaultValue?: string | string[];
  value?: string | string[];
  multiple: boolean;
  onChange?: (value: string | string[]) => any;
  defaultLabel?: string;
}

const log = buildLogger('FileSelector');
export default ({
                  defaultValue: propsDefaultValue,
                  value: propsValue,
                  type,
                  multiple = false,
                  size = 'medium',
                  onChange: propsOnChange = (paths) => {
                  },
                  defaultLabel,
                }: Props) => {
  const { t } = useTranslation();
  const [innerValue, setInnerValue] = useState(propsValue ?? propsDefaultValue);

  useUpdateEffect(() => {
    setInnerValue(propsValue);
  }, [propsValue]);
6;
  const onChange = v => {
    if (propsValue === undefined) {
      setInnerValue(v);
    }
    if (propsOnChange) {
      propsOnChange(v);
    }
  };

  const value = propsValue === undefined ? innerValue : propsValue;

  return (
    <Button
      size={size}
      color={value ? 'primary' : 'default'}
      variant={'light'}
      onPress={() => {
        switch (type) {
          case 'file':
            if (multiple) {
              BApi.gui.openFilesSelector()
                .then(a => {
                  if (a.data != undefined && a.data.length > 0) {
                    onChange(a.data);
                  }
                });
            } else {
              BApi.gui.openFileSelector()
                .then(a => {
                  if (a.data) {
                    onChange(a.data);
                  }
                });
            }
            break;
          case 'folder':
            if (multiple) {
              Message.error('Multi-folder selector is not supported');
            } else {
              BApi.gui.openFolderSelector()
                .then(a => {
                  if (a.data) {
                    onChange(a.data);
                  }
                });
            }
            break;
        }
      }}
    >{value ?? defaultLabel ?? t('Select a path')}
    </Button>
  );
};
