'use strict';

import { useTranslation } from 'react-i18next';
import { Checkbox } from '@/components/bakaui';

type Props = {
  subject: any;
  isSelected?: boolean;
  onSelect?: (isSelected?: boolean) => any;
  isSecondary?: boolean;
};

const Options: boolean[] = [true, false];

export default ({
                  subject,
                  isSelected,
                  onSelect,
                  isSecondary,
                }: Props) => {
  const { t } = useTranslation();

  return (
    <div className={'inline-grid gap-2 items-center'} style={{ gridTemplateColumns: '1fr 50px 50px' }}>
      <div className={`flex items-center justify-end gap-1 ${isSecondary ? 'opacity-80' : ''}`}>{subject}</div>
      {Options.map(o => {
        return (
          <Checkbox
            isSelected={isSelected === o}
            onValueChange={x => {
              if (x) {
                onSelect?.(o);
              } else {
                onSelect?.(undefined);
              }
            }}
            className={'justify-self-center'}
            size={'sm'}
          >{t(o ? 'Yes' : 'No')}</Checkbox>
        );
      })}
    </div>
  );
};
