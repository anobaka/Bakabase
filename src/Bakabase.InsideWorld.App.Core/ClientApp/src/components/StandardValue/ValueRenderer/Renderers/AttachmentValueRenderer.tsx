import { useTranslation } from 'react-i18next';
import type { ValueRendererProps } from '../models';

type AttachmentValueRendererProps = Omit<ValueRendererProps<string[]>, 'variant'> & {
  variant: ValueRendererProps<string[]>['variant'];
};

export default ({ value, variant, editor, ...props }: AttachmentValueRendererProps) => {
  const { t } = useTranslation();

  const v = variant ?? 'default';

  return (
    <span>{t('Not supported')}</span>
  );
};
