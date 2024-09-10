import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { MoreOutlined } from '@ant-design/icons';
import type { CategoryEnhancerFullOptions } from './components/CategoryEnhancerOptionsDialog/models';
import { Button, Checkbox, Chip, Divider, Link, Listbox, ListboxItem, Modal, Popover, Tooltip } from '@/components/bakaui';
import { createPortalOfComponent } from '@/components/utils';
import BApi from '@/sdk/BApi';
import type { EnhancerDescriptor } from '@/components/EnhancerSelectorV2/models';
import CustomIcon from '@/components/CustomIcon';
import { StandardValueIcon } from '@/components/StandardValue';
import { CategoryAdditionalItem, StandardValueType } from '@/sdk/constants';
import CategoryEnhancerOptionsDialog from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog';
import { BakabaseContext, useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import type { DestroyableProps } from '@/components/bakaui/types';
import TargetNotSetupTip from '@/components/Enhancer/components/TargetNotSetupTip';
import { EnhancerTargetNotSetupTip } from '@/components/Enhancer';

interface IProps extends DestroyableProps {
  categoryId: number;
  onClose?: () => any;
}

type Category = {
  id: number;
  name: string;
};

const EnhancerSelector = ({
                            categoryId,
                            onDestroyed,
                            onClose,
                          }: IProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [enhancers, setEnhancers] = useState<EnhancerDescriptor[]>([]);
  const [category, setCategory] = useState<Category>();
  const [categoryEnhancerOptionsList, setCategoryEnhancerOptionsList] = useState<CategoryEnhancerFullOptions[]>([]);

  const init = async () => {
    await BApi.enhancer.getAllEnhancerDescriptors().then(r => {
      const data = r.data || [];
      // @ts-ignore
      setEnhancers(data);
    });

    // @ts-ignore
    await BApi.category.getCategory(categoryId, { additionalItems: CategoryAdditionalItem.EnhancerOptions | CategoryAdditionalItem.CustomProperties }).then(r => {
      const data = r.data || {};
      setCategory({ id: data.id!, name: data.name! });
      setCategoryEnhancerOptionsList(data.enhancerOptions?.map(eo => (eo as CategoryEnhancerFullOptions)) || []);
    });
  };

  useEffect(() => {
    init();
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

  // console.log(categoryEnhancerOptionsList, onDestroyed);

  return (
    <Modal
      title={t('Enhancers')}
      defaultVisible
      size={'xl'}
      onDestroyed={onDestroyed}
      onClose={onClose}
      footer={{
        actions: ['cancel'],
      }}
    >
      <div
        className={'grid gap-4 flex-wrap justify-center justify-items-center'}
        style={{ gridTemplateColumns: 'repeat(auto-fill, minmax(300px, 300px)' }}
      >
        {enhancers.map(e => {
          const ceo = categoryEnhancerOptionsList.find(x => x.enhancerId == e.id);
          return (
            <div
              key={e.id}
              className={'max-w-[280px] border-1 rounded-lg pl-3 pr-3 pt-2 pb-2 flex flex-col'}
              style={{ borderColor: 'rgba(255, 255, 255, 0.3)' }}
            >
              <div className={'text-medium font-bold'}>
                {e.name}
              </div>
              {e.description && (
                <div className={'opacity-60 grow'}>
                  {e.description}
                </div>
              )}
              <Divider />
              <div className={'mt-2 mb-2 grow'}>
                <div className={' italic'}>
                  {t('This enhancer can produce the following property values')}
                </div>
                <div className={'flex flex-wrap gap-x-3 gap-y-1 mt-1'}>
                  {e.targets.map(target => {
                    return (
                      <Tooltip
                        key={target.id}
                        content={(
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
                          style={{
                            // color: 'var(--bakaui-primary)'
                          }}
                        >
                          <StandardValueIcon valueType={target.valueType} className={'text-small'} />
                          <span className={'break-all'}>
                            {target.name}
                          </span>
                        </div>
                      </Tooltip>
                    );
                  })}
                </div>
              </div>
              <Divider />
              <div className={'flex items-center justify-between gap-1 mt-1'}>
                <div className="flex items-center gap-1 mt-1">
                  <Popover
                    trigger={(
                      <Button
                        isIconOnly
                        size={'sm'}
                        variant={'light'}
                      >
                        <MoreOutlined />
                      </Button>
                    )}
                    placement={'top'}
                  >
                    <Listbox
                      aria-label="Delete enhancement records"
                      onAction={(key) => {
                        let title: string;
                        let callApi: (() => Promise<any>) | undefined;

                        switch (key) {
                          case 'Category':
                            title = t('Delete all enhancement records of this enhancer for category {{categoryName}}', { categoryName: category!.name });
                            callApi = async () => await BApi.category.deleteEnhancementsByCategory(category!.id);
                            break;
                          case 'All':
                            title = t('Delete all enhancement records of this enhancer');
                            callApi = async () => await BApi.enhancer.deleteEnhancementsByEnhancer(e.id);
                            break;
                          default:
                            return;
                        }

                        createPortal(Modal, {
                          defaultVisible: true,
                          title,
                          onOk: callApi,
                        });
                      }}
                    >
                      <ListboxItem
                        key={'Category'}
                        color={'danger'}
                        className={'text-danger'}
                      >
                        {t('Delete all enhancement records of this enhancer for category {{categoryName}}', { categoryName: category?.name })}
                      </ListboxItem>
                      <ListboxItem
                        key={'All'}
                        color={'danger'}
                        className={'text-danger'}
                      >
                        {t('Delete all enhancement records of this enhancer')}
                      </ListboxItem>
                    </Listbox>
                  </Popover>
                </div>
                <div className="flex items-center gap-1 mt-1">
                  <EnhancerTargetNotSetupTip
                    enhancer={e}
                    options={ceo}
                  />
                  <Button
                    size={'sm'}
                    variant={'light'}
                    color={'primary'}
                    onClick={() => {
                      createPortal(CategoryEnhancerOptionsDialog, {
                        enhancer: e,
                        categoryId,
                        onDestroyed: init,
                      });
                    }}
                  >
                    {t('Setup')}
                  </Button>
                  <Checkbox
                    size={'sm'}
                    isSelected={ceo?.active}
                    onValueChange={(c) => {
                      BApi.category.patchCategoryEnhancerOptions(categoryId, e.id, {
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
            </div>
          );
        })}
      </div>
    </Modal>
  );
};

EnhancerSelector.show = (props: IProps) => createPortalOfComponent(EnhancerSelector, props);

export default EnhancerSelector;
