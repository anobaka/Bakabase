import { useTranslation } from 'react-i18next';
import { PropertyType } from '@/sdk/constants';
import { Icon, Tooltip } from '@/components/bakaui';
import { PropertyTypeIconMap } from '@/components/Property/models';

type Props = {
  type: PropertyType;
};

export default ({ type }: Props) => {
  const { t } = useTranslation();
  const icon = type == undefined ? undefined : PropertyTypeIconMap[type];

  return (
    <Tooltip
      color={'foreground'}
      content={t(PropertyType[type])}
    >
      <Icon
        type={icon}
        className={'text-medium'}
      />
    </Tooltip>
  );
};
