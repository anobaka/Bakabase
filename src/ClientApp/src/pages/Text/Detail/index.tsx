import { useTranslation } from 'react-i18next';
import React, { useState } from 'react';
import { toast } from 'react-toastify';
import type { SpecialText } from '@/pages/Text/models';
import { Input } from '@/components/bakaui';
import { SpecialTextType } from '@/sdk/constants';
import update = toast.update;

interface Props {
  value: SpecialText;
  onChange: (value: SpecialText) => any;
}

export default ({ value: propsValue, onChange }: Props) => {
  const { t } = useTranslation();
  const [value, setValue] = useState<SpecialText>(JSON.parse(JSON.stringify(propsValue)));

  const change = (patches: Partial<SpecialText>) => {
    const nv = {
      ...value,
      ...patches,
    };
    setValue(nv);
    onChange(nv);
  };

  switch (value.type) {
    case SpecialTextType.DateTime:
    case SpecialTextType.Volume:
    case SpecialTextType.Useless:
    case SpecialTextType.Trim:
      return (
        <Input
          key="0"
          placeholder={t('Text')}
          className={'w-full'}
          required
          value={value.value1}
          onValueChange={(value1) => {
            change({ value1 });
          }}
        />
      );
    case SpecialTextType.Language:
    case SpecialTextType.Standardization:

      return (
        <>
          <Input
            key="0"
            placeholder={t('Source text')}
            required
            value={value.value1}
            onValueChange={(value1) => {
              change({ value1 });
            }}
          />
          <Input
            key="1"
            placeholder={t('Convert to')}
            required
            value={value.value2}
            onValueChange={(value2) => {
              change({ value2 });
            }}
          />
        </>
      );
    case SpecialTextType.Wrapper:
      return (
        <>
          <Input
            key="0"
            placeholder={t('Left wrapper')}
            required
            value={value.value1}
            onValueChange={(value1) => {
              change({ value1 });
            }}
          />
          <Input
            key="1"
            placeholder={t('Right wrapper')}
            required
            value={value.value2}
            onValueChange={(value2) => {
              change({ value2 });
            }}
          />
        </>
      );
    default:
      return null;
  }
};
