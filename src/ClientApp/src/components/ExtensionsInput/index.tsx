import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import { Chip, Textarea } from '@/components/bakaui';

type ExtensionsInputProps = {
  defaultValue?: string[];
  onValueChange?: (extensions: string[]) => void;
  label?: string;
  placeholder?: string;
};

function extractExtensions(text: string): string[] {
  return Array.from(
    new Set(
      text
        .replace(/\n/g, ' ')
        .split(' ')
        .map((x) => {
          let trimmed = x.trim();
          while (true) {
            const n = trimmed.replace(/^\.+|\.+$/g, '');
            if (n.length == trimmed.length) {
              break;
            } else {
              trimmed = n;
            }
          }
          return trimmed;
        })
        .filter((x) => x.length > 0)
        .map((x) => `.${x}`),
    ),
  );
}

const ExtensionsInput: React.FC<ExtensionsInputProps> = ({
                                                           label,
                                                           placeholder,
                                                           defaultValue,
                                                           onValueChange,
                                                         }) => {
  const { t } = useTranslation();
  const [text, setText] = useState(defaultValue?.join(' '));
  const [extensions, setExtensions] = useState<string[] | undefined>(defaultValue);

  return (
    <div>
      <Textarea
        isClearable
        label={label ?? t('Extensions')}
        placeholder={placeholder ?? t('Separate by space or newline')}
        value={text}
        onValueChange={(v) => {
          setText(v);
          const newExtensions = extractExtensions(v);
          setExtensions(newExtensions);
          onValueChange?.(newExtensions);
        }}
        isMultiline
        fullWidth
        minRows={4}
      />
      {extensions && extensions.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-1">
          {extensions.map((ext, i) => (
            <Chip key={i} size="sm" variant="flat" radius="sm">
              {ext}
            </Chip>
          ))}
        </div>
      )}
    </div>
  );
};

export default ExtensionsInput;
