import { useTranslation } from 'react-i18next';
import { Checkbox, Tooltip } from '@/components/bakaui';

interface IProps {
  integrateWithAlias: boolean;
  onChange?: (integrateWithAlias: boolean) => void;
}

export default ({ integrateWithAlias, onChange }: IProps) => {
  const { t } = useTranslation();
  return (
    <>
      <Tooltip
        content={t('If checked, the value produced will be replaced with the preferred alias.')}
        placement={'left'}
      >
        <Checkbox
          size={'sm'}
          isSelected={integrateWithAlias}
          onValueChange={onChange}
        >
          {t('Integrate with alias')}
        </Checkbox>
      </Tooltip>
    </>
  );
};
