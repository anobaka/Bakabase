import { ExclamationCircleOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { Tooltip } from '@/components/bakaui';
import type {
  CategoryEnhancerFullOptions,
} from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models';
import type { EnhancerDescriptor } from '@/components/EnhancerSelectorV2/models';

interface IProps {
  options?: CategoryEnhancerFullOptions;
  enhancer: EnhancerDescriptor;
}

export default ({ options, enhancer }: IProps) => {
  const { t } = useTranslation();

  // console.log(options, enhancer);

  if (options?.active != true) {
    return null;
  }

  const tom = options?.options?.targetOptions ?? {};

  const configuredTargets = Object.keys(tom).filter(x => tom[x].propertyId > 0).map(x => parseInt(x, 10));

  if (enhancer.targets.every(t => configuredTargets.includes(t.id))) {
    return null;
  }

  if (configuredTargets.length == 0) {
    return (
      <Tooltip
        content={t('None of targets is mapped to a property, so no data will be enhanced.')}
      >
        <ExclamationCircleOutlined
          className={'text-small'}
          style={{ color: 'var(--bakaui-danger)' }}
        />
      </Tooltip>
    );
  }

  return (
    <Tooltip
      content={t('Some of targets are not mapped to a property, corresponding data will not be enhanced.')}
    >
      <ExclamationCircleOutlined
        className={'text-small'}
        style={{ color: 'var(--bakaui-warning)' }}
      />
    </Tooltip>
  );
};
