import { useTranslation } from 'react-i18next';
import { Checkbox, Tooltip } from '@/components/bakaui';

interface IProps {
  integrateWithAlias: boolean;
}

export default ({ integrateWithAlias }: IProps) => {
  const { t } = useTranslation();
  return (
    <>
      <Tooltip
        content={t('If checked, the value produced will be replaced with the preferred alias.')}
      >
        <Checkbox
          size={'sm'}
          checked={integrateWithAlias}
        >
          {t('Integrate with alias')}
        </Checkbox>
      </Tooltip>
    </>
  );
};
