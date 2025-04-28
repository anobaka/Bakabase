'use strict';

import { useTranslation } from 'react-i18next';
import { Checkbox, RadioGroup } from '@/components/bakaui';
import { enhancerIds } from '@/sdk/constants';

type Props = {
  subject: any;
  options: string[];
  onSelect?: (selector?: string, option?: string) => any;
  selectors?: string[];
};

const SubjectWidth = 80;

export default ({ subject, options, onSelect, selectors }: Props) => {
  const { t } = useTranslation();


  let template = `${SubjectWidth}%`;
  const optionWidth = (100 - SubjectWidth) / options.length;
  for (const {} of options) {
    template += ` ${optionWidth}%`;
  }

  return (
    <div className={'inline-grid gap-2 items-center'} style={{ gridTemplateColumns: '1fr 50px 50px' }}>
      <div className={'flex items-center justify-end gap-1'}>{subject}</div>
      {options.map(o => {
        return (
          <Checkbox className={'justify-self-center'} size={'sm'}>{o}</Checkbox>
        );
      })}
      {selectors?.map(selector => {
        return (
          <>
            <div className={'opacity-60 text-right'}>{selector}</div>
            {options.map(o => {
              return (
                <Checkbox className={'justify-self-center'} size={'sm'}>{o}</Checkbox>
              );
            })}
          </>
        );
      })}
    </div>
  );
};
