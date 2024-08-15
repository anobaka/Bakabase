import { ExclamationCircleOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { Tooltip } from '@/components/bakaui';
import type {
  CategoryEnhancerFullOptions,
  EnhancerTargetFullOptions,
} from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models';
import type { EnhancerDescriptor } from '@/components/EnhancerSelectorV2/models';

interface IProps {
  options?: CategoryEnhancerFullOptions;
  enhancer: EnhancerDescriptor;
}

enum Status {
  AllSet = 1,
  NotAllSet = 2,
  NotSet = 3,
}

const isSet = (options: EnhancerTargetFullOptions | undefined, isDefaultOfDynamic: boolean): boolean => {
  return (options != undefined && (options.propertyId != undefined && options.propertyId > 0 || options.autoGenerateProperties || isDefaultOfDynamic)) ?? false;
};

export default ({ options, enhancer }: IProps) => {
  const { t } = useTranslation();

  // console.log(options, enhancer);

  if (options?.active != true) {
    return null;
  }

  const tom = options?.options?.targetOptions ?? [];
  const setTargets: number[] = [];

  for (const target of enhancer.targets) {
    if (target.isDynamic) {
      const tos = tom.filter(x => x.target == target.id);
      let bad = false;
      for (const to of tos) {
        if (!isSet(to, to.dynamicTarget == undefined)) {
          bad = true;
          break;
        }
      }
      if (!bad) {
        setTargets.push(target.id);
      }
    } else {
      const to = tom.find(x => x.target == target.id);
      if (isSet(to, false)) {
        setTargets.push(target.id);
      }
    }
  }

  const status: Status = setTargets.length == 0 ? Status.NotSet : setTargets.length == enhancer.targets.length ? Status.AllSet : Status.NotAllSet;

  switch (status) {
    case Status.AllSet:
      return null;
    case Status.NotAllSet:
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
    case Status.NotSet:
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
};
