import { useTranslation } from 'react-i18next';
import { Checkbox, Tooltip } from '@/components/bakaui';
import type { PropertyType } from '@/sdk/constants';

interface IProps {
  autoBindProperty: boolean;
  onChange?: (autoBindProperty: boolean) => void;
}

export default ({ autoBindProperty, onChange }: IProps) => {
  const { t } = useTranslation();
  return (
    <>
      <Tooltip
        content={
          <div>
            <div>{t('If this option is checked, a property with the same name and type of the target will be bound automatically.')}</div>
            <div>{t('If there isn\'t such a property, a new property will be created and bound to this target.')}</div>
            <div>{t('If this option is checked in default options of a dynamic target, all unlisted dynamic targets will be bound to properties of the same type with the same name.')}</div>
          </div>
        }
        placement={'left'}
      >
        <Checkbox
          size={'sm'}
          isSelected={autoBindProperty}
          onValueChange={onChange}
        >
          {t('Auto bind property')}
        </Checkbox>
      </Tooltip>
    </>
  );
};
