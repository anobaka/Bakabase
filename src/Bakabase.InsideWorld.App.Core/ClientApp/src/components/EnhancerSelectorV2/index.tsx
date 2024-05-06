import { useContext, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ExclamationCircleOutlined } from '@ant-design/icons';
import type { CategoryEnhancerFullOptions } from './components/CategoryEnhancerOptionsDialog/models';
import { Button, Checkbox, Chip, Divider, Link, Modal, Tooltip } from '@/components/bakaui';
import { createPortalOfComponent } from '@/components/utils';
import BApi from '@/sdk/BApi';
import type { EnhancerDescriptor } from '@/components/EnhancerSelectorV2/models';
import CustomIcon from '@/components/CustomIcon';
import { StandardValueIcon } from '@/components/StandardValue';
import { ResourceCategoryAdditionalItem, StandardValueType } from '@/sdk/constants';
import CategoryEnhancerOptionsDialog from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog';
import { BakabaseContext, useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import type { DestroyableProps } from '@/components/bakaui/types';

interface IProps extends DestroyableProps{
  categoryId: number;
}

const EnhancerSelector = ({
                            categoryId,
                            onDestroyed,
}: IProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [enhancers, setEnhancers] = useState<EnhancerDescriptor[]>([]);
  const [categoryEnhancerOptionsList, setCategoryEnhancerOptionsList] = useState<CategoryEnhancerFullOptions[]>([]);

  useEffect(() => {
    BApi.enhancer.getAllEnhancerDescriptors().then(r => {
      const data = r.data || [];
      // @ts-ignore
      setEnhancers(data);
    });

    BApi.resourceCategory.getResourceCategory(categoryId, { additionalItems: ResourceCategoryAdditionalItem.EnhancerOptions }).then(r => {
      const data = r.data || {};
      setCategoryEnhancerOptionsList(data.enhancerOptions?.map(eo => (eo as CategoryEnhancerFullOptions)) || []);
    });
  }, []);

  // console.log(createPortal, 1234567);

  const patchCategoryEnhancerOptions = (enhancerId: number, patches: Partial<CategoryEnhancerFullOptions>) => {
    let ceoIdx = categoryEnhancerOptionsList.findIndex(x => x.enhancerId == enhancerId);
    if (ceoIdx == -1) {
      categoryEnhancerOptionsList.push({
        categoryId,
        enhancerId,
        active: false,
        options: {},
      });
      ceoIdx = categoryEnhancerOptionsList.length - 1;
    }
    categoryEnhancerOptionsList[ceoIdx] = {
      ...categoryEnhancerOptionsList[ceoIdx],
      ...patches,
    };
    setCategoryEnhancerOptionsList([...categoryEnhancerOptionsList]);
  };

  // console.log(categoryEnhancerOptionsList);

  return (
    <Modal
      title={t('Enhancers')}
      defaultVisible
      size={'xl'}
      afterClose={onDestroyed}
    >
      {enhancers.map(e => {
        const ceo = categoryEnhancerOptionsList.find(x => x.enhancerId == e.id);
        const enhancerOptions = ceo?.options;
        const noTargetConfigured = ceo?.active == true && Object.keys(ceo.options?.targetOptionsMap ?? {}).length == 0;
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
              {noTargetConfigured && (
                <Tooltip
                  content={t('None of targets is mapped to a property, so no data will be enhanced.')}
                >
                  <ExclamationCircleOutlined
                    className={'text-small'}
                    style={{ color: 'var(--bakaui-danger)' }}
                  />
                </Tooltip>
              )}
              <Button
                size={'sm'}
                variant={'light'}
                color={'primary'}
                onClick={() => {
                  createPortal(CategoryEnhancerOptionsDialog, {
                    enhancer: e,
                    categoryId,
                    options: enhancerOptions,
                    onChanged: () => {
                      patchCategoryEnhancerOptions(e.id, { options: enhancerOptions });
                    } });
                }}
              >
                {t('Setup')}
              </Button>
              <Checkbox
                size={'sm'}
                isSelected={ceo?.active}
                onValueChange={(c) => {
                  BApi.resourceCategory.patchCategoryEnhancerOptions(categoryId, e.id, {
                    active: c,
                  }).then(() => {
                    patchCategoryEnhancerOptions(e.id, { active: c });
                  });
                }}
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
