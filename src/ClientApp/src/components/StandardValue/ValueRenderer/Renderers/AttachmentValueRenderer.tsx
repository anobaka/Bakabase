import { useTranslation } from 'react-i18next';
import type { ValueRendererProps } from '../models';
import serverConfig from '@/serverConfig';
import BApi from '@/sdk/BApi';

type AttachmentValueRendererProps = Omit<ValueRendererProps<string[]>, 'variant'> & {
  variant: ValueRendererProps<string[]>['variant'];
};

export default ({ value, variant, editor, ...props }: AttachmentValueRendererProps) => {
  const { t } = useTranslation();

  const v = variant ?? 'default';

  if (!value || value.length == 0) {
    return null;
  }

  switch (v) {
    case 'default':
      return (
        <div className={'flex items-center gap-2'}>
          {value.map(v => {
            return (
              <img src={`${serverConfig.apiEndpoint}/tool/thumbnail?path=${encodeURIComponent(v)}`} alt={v} />
            );
          })}
        </div>
      );
    case 'light':
      return (
        <span>
          {(value.join(','))}
        </span>
      );
  }
};
