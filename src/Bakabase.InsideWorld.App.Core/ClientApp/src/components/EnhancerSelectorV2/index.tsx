import { useContext, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Button, Checkbox, Chip, Divider, Link, Modal, Tooltip } from '@/components/bakaui';
import { createPortalOfComponent } from '@/components/utils';
import BApi from '@/sdk/BApi';
import type { EnhancerDescriptor } from '@/components/EnhancerSelectorV2/models';
import CustomIcon from '@/components/CustomIcon';
import { StandardValueIcon } from '@/components/StandardValue';
import { StandardValueType } from '@/sdk/constants';
import CategoryEnhancerOptionsDialog from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog';
import { BakabaseContext, useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import type { DestroyableProps } from '@/components/bakaui/types';

interface IProps extends DestroyableProps{
  categoryId: number;
}

const EnhancerSelector = ({
                            categoryId,
                            onDestroy,
}: IProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [enhancers, setEnhancers] = useState<EnhancerDescriptor[]>([]);

  useEffect(() => {
    BApi.enhancer.getAllEnhancerDescriptors().then(r => {
      const data = r.data || [];
      // @ts-ignore
      setEnhancers(data);
    });
  }, []);

  // console.log(createPortal, 1234567);

  return (
    <Modal
      title={t('Enhancers')}
      defaultVisible
      size={'xl'}
      afterClose={onDestroy}
    >
      {enhancers.map(e => {
        return (
          <div className={'max-w-[280px] rounded border-1 pl-3 pr-3 pt-2 pb-2'}>
            <div className={'text-medium'}>
              {e.name}
            </div>
            <div className={'opacity-60'}>
              {e.description}
            </div>
            <Divider />
            <div className={'mt-2 mb-2'}>
              <div className={''}>
                {t('This enhancer can produce the following property values')}
              </div>
              <div className={'flex flex-wrap gap-x-3 gap-y-1 mt-1'}>
                {e.targets.map(target => {
                  return (
                    <Tooltip content={(
                      <div>
                        <div>{target.description}</div>
                        <div>
                          {t('The value type of this target is')}
                          &nbsp;
                          <span className={'font-bold'}>
                            <StandardValueIcon valueType={target.valueType} className={'text-small'} />
                            &nbsp;
                            {t(`StandardValueType.${StandardValueType[target.valueType]}`)}
                          </span>
                        </div>
                      </div>
                    )}
                    >
                      <div
                        className={'flex items-center gap-1'}
                        style={{ color: 'var(--bakaui-primary)' }}
                      >
                        <StandardValueIcon valueType={target.valueType} className={'text-small'} />
                        <span className={'break-all '}>
                          {target.name}
                        </span>
                      </div>
                    </Tooltip>
                  );
                })}
              </div>
            </div>
            <Divider />
            <div className={'flex items-center justify-end gap-1 mt-1'}>
              <Button
                size={'sm'}
                variant={'light'}
                color={'primary'}
                onClick={() => {
                  console.log(132456, createPortal);
                  createPortal(CategoryEnhancerOptionsDialog, { enhancer: e, categoryId });
                  // CategoryEnhancerOptionsDialog.show({ enhancer: e, categoryId });
                }}
              >
                {t('Setup')}
              </Button>
              <Checkbox
                size={'sm'}
              >
                {t('Enable')}
              </Checkbox>
            </div>
          </div>
        );
      })}
    </Modal>
  );
};

EnhancerSelector.show = (props: IProps) => createPortalOfComponent(EnhancerSelector, props);

export default EnhancerSelector;
