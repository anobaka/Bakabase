import React, { useState } from 'react';
import { Button, Message } from '@alifd/next';
import type { ButtonProps } from '@alifd/next/types/button';
import { useTranslation } from 'react-i18next';
import { buildLogger } from '@/components/utils';
import BApi from '@/sdk/BApi';


interface Props {
  size: ButtonProps['size'];
  type: 'file' | 'folder';
  defaultValue?: string | string[];
  value?: string | string[];
  multiple: boolean;
  onChange?: (value: string | string[]) => any;
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
                }: Props) => {
  const { t } = useTranslation();
  const [innerValue, setInnerValue] = useState(propsValue ?? propsDefaultValue);

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
      text={!!value}
      size={size}
      type={value ? 'primary' : 'normal'}
      onClick={() => {
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
    >{value ?? t('Select a path')}
    </Button>
  );
};
