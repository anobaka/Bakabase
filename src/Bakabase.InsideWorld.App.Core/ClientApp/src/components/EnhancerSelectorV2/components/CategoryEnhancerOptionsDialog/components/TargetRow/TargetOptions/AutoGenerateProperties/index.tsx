import { useTranslation } from 'react-i18next';
import { Checkbox, Tooltip } from '@/components/bakaui';

interface IProps {
  autoGenerateProperties: boolean;
  onChange?: (integrateWithAlias: boolean) => void;
}

export default ({ autoGenerateProperties, onChange }: IProps) => {
  const { t } = useTranslation();
  return (
    <>
      <Tooltip
        content={
          <div>
            <div>{t('If checked, a default property for this target will be created automatically.')}</div>
            <div>{t('If checked in default options of a dynamic target, multiple properties will be created for all unlisted dynamic targets.')}</div>
          </div>
          }
      >
        <Checkbox
          size={'sm'}
          isSelected={autoGenerateProperties}
          onValueChange={onChange}
        >
          {t('Auto generate properties')}
        </Checkbox>
      </Tooltip>
    </>
  );
};
