import React, { useState } from 'react';
import { Dialog, Input, Message } from '@alifd/next';
import { SketchPicker } from 'react-color';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { useTranslation } from 'react-i18next';
import { EditOutlined, SyncOutlined, UnorderedListOutlined } from '@ant-design/icons';
import AddMediaLibraryInBulkDialog from './AddMediaLibraryInBulkDialog';
import DisplayNameTemplateEditorDialog from './DisplayNameTemplateEditorDialog';
import CustomIcon from '@/components/CustomIcon';
import { ComponentType, componentTypes, CoverSelectOrder, coverSelectOrders } from '@/sdk/constants';
import SortableMediaLibraryList from '@/pages/Category/components/SortableMediaLibraryList';
import DragHandle from '@/components/DragHandle';
import BasicCategoryComponentSelector from '@/components/BasicCategoryComponentSelector';
import BApi from '@/sdk/BApi';
import ClickableIcon from '@/components/ClickableIcon';
import CategoryCustomPropertyBinderDialog from '@/pages/Category/components/CustomPropertyBinder';
import {
  Button,
  Checkbox,
  Chip,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Modal,
  Tooltip,
} from '@/components/bakaui';
import EnhancerSelectorV2 from '@/components/EnhancerSelectorV2';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import FeatureStatusTip from '@/components/FeatureStatusTip';

const EditMode = {
  CoverSelectOrder: 1,
  NameAndColor: 2,
};

const ComponentTips = {
  [ComponentType.PlayableFileSelector]: 'Determine which files will be treated as playable files',
  [ComponentType.Player]: 'Specify a player to play playable files',
  [ComponentType.Enhancer]: 'Expand properties of resources, such as publisher(property), publication date(property), tags(property), cover(file), etc',
};

export default (({
                   category,
                   loadAllCategories,
                   loadAllMediaLibraries,
                   libraries,
                   forceUpdate,
                   allComponents,
                   enhancers,
                 }) => {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
  } = useSortable({ id: category.id });
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  // console.log(createPortal, 1234567);


  const style = {
    transform: CSS.Translate.toString({
      ...transform,
      scaleY: 1,
    }),
    transition,
  };

  const [categoryColor, setCategoryColor] = useState(undefined);
  const [colorPickerVisible, setColorPickerVisible] = useState(false);
  const [editMode, setEditMode] = useState<number>();
  const [name, setName] = useState<string>();

  const clearEditMode = () => {
    setEditMode(undefined);
  };

  const components = (category.componentsData || []);
  // console.log('[SortableCategory]rendering', category, enhancers, componentKeys);

  const renderBasicComponentSelector = (componentType, componentKey = undefined) => {
    if (componentType == ComponentType.PlayableFileSelector || componentType == ComponentType.Player) {
      let newKey;
      const dialog = Dialog.show({
        height: 'auto',
        width: 'auto',
        v2: true,
        style: { minWidth: 1000 },
        closeMode: ['esc', 'close', 'mask'],
        title: t(ComponentType[componentType]),
        content: (
          <BasicCategoryComponentSelector
            onChange={(componentKeys) => {
              newKey = (componentKeys || [])[0];
            }}
            componentType={componentType}
            value={[componentKey]}
          />
        ),
        closeable: true,
        onOk: () => new Promise((resolve, reject) => {
          if (newKey) {
            return BApi.category.configureCategoryComponents(category.id, {
              componentKeys: [newKey],
              type: componentType,
            }).then(a => {
              if (!a.code) {
                loadAllCategories();
                resolve(a);
              } else {
                reject();
              }
            });
          } else {
            reject();
          }
        }),
      });
    }
  };

  const resourceCount = libraries.reduce((s, t) => s + t.resourceCount, 0);

  if (category.id == 14) {
    console.log(category);
  }

  return (
    <div
      className={'category-page-draggable-category'}
      key={category.id}
      id={`category-${category.id}`}
      ref={setNodeRef}
      style={style}
    >
      <div className="title-line">
        <div className="left">
          <DragHandle {...listeners} {...attributes} />
          <div className={'name'}>
            {(editMode == EditMode.NameAndColor) ? (
              <div className={'editing'}>
                <Input value={name} onChange={(v) => setName(v)} />
                &nbsp;
                <div style={{
                  position: 'relative',
                  width: 26,
                  height: 26,
                }}
                >
                  <div
                    style={{
                      padding: '5px',
                      background: '#fff',
                      borderRadius: '1px',
                      boxShadow: '0 0 0 1px rgba(0,0,0,.1)',
                      display: 'inline-block',
                      cursor: 'pointer',
                    }}
                    onClick={() => setColorPickerVisible(true)}
                  >
                    <div style={{
                      width: '16px',
                      height: '16px',
                      borderRadius: '2px',
                      background: categoryColor?.hex,
                    }}
                    />
                  </div>
                  {colorPickerVisible ? (
                    <div style={{
                      position: 'absolute',
                      top: '-80px',
                      left: '60px',
                      zIndex: '20',
                    }}
                    >
                      <div
                        style={{
                          position: 'fixed',
                          top: '0px',
                          right: '0px',
                          bottom: '0px',
                          left: '0px',
                        }}
                        onClick={() => setColorPickerVisible(false)}
                      />
                      <SketchPicker
                        color={categoryColor}
                        onChange={(v) => {
                          setCategoryColor(v);
                        }}
                      />
                    </div>
                  ) : null}
                </div>
                <ClickableIcon
                  className={'submit'}
                  colorType={'normal'}
                  size={'large'}
                  type="select"
                  useInBuildIcon
                  onClick={() => {
                    BApi.category.patchCategory(category.id,
                      {
                        name,
                        color: categoryColor.hex,
                      },
                    )
                      .then((t) => {
                        if (!t.code) {
                          category.name = name;
                          category.color = categoryColor.hex;
                          clearEditMode();
                        }
                      });
                  }}
                />
                <ClickableIcon
                  useInBuildIcon
                  colorType={'normal'}
                  className={'cancel'}
                  size={'large'}
                  type="close"
                  onClick={clearEditMode}
                />
              </div>
            ) : (
              <span
                className="editable"
              >
                <span className="hover-area">
                  <span
                    style={{ color: category.color }}
                    title={category.id}
                  >{category.name}
                  </span>
                  &nbsp;
                  <Button
                    size={'sm'}
                    isIconOnly
                    onClick={() => {
                      setName(category.name);
                      setCategoryColor({
                        hex: category.color,
                      });
                      // console.log(category.color);
                      setEditMode(EditMode.NameAndColor);
                    }}
                  >
                    <EditOutlined className={'text-base'} />
                  </Button>
                </span>
              </span>
            )}
          </div>
          <Dropdown placement={'bottom-start'}>
            <DropdownTrigger>
              <Button
                size={'sm'}
                isIconOnly
              >
                <UnorderedListOutlined className={'text-medium'} />
              </Button>
            </DropdownTrigger>
            <DropdownMenu
              aria-label={'static actions'}
              onAction={key => {
                switch (key) {
                  case 'enhance-manually': {
                    break;
                  }
                  case 'delete-enhancements': {
                    createPortal(Modal, {
                      defaultVisible: true,
                      title: t('Removing all enhancement records of resources under this category'),
                      children: t('This operation cannot be undone. Would you like to proceed?'),
                      onOk: async () => {
                        await BApi.category.deleteEnhancementsByCategory(category.id);
                      },
                    });
                    break;
                  }
                  case 'delete-category': {
                    createPortal(Modal, {
                      defaultVisible: true,
                      title: `${t('Deleting')} ${category.name}`,
                      children: (<>
                        <div>{t('All related data will be deleted too, are you sure?')}</div>
                        <div>{t('This operation cannot be undone. Would you like to proceed?')}</div>
                      </>),
                      onOk: async () => {
                        const rsp = await BApi.category.deleteCategory(category.id);
                        if (!rsp.code) {
                          loadAllCategories();
                        }
                      },
                    });
                    break;
                  }
                }
              }}
            >
              <DropdownItem
                className="text-danger"
                color="danger"
                key={'delete-enhancements'}
              >
                {t('Remove all enhancement records')}
              </DropdownItem>
              <DropdownItem
                className="text-danger"
                color="danger"
                key={'delete-category'}
              >
                {t('Delete category')}
              </DropdownItem>
            </DropdownMenu>
          </Dropdown>

          {resourceCount > 0 && (
            <>
              <Tooltip
                content={t('Count of resources')}
              >
                <Chip
                  size={'sm'}
                  color={'success'}
                  variant={'flat'}
                >{resourceCount}</Chip>
              </Tooltip>
            </>
          )}

        </div>
        <div className="right flex items-center gap-2">
          <Tooltip content={t('Sync now')}>
            <Button
              isIconOnly
              color={'secondary'}
              size={'sm'}
              variant={'light'}
              onClick={() => {
                BApi.category.startSyncingCategoryResources(category.id);
              }}
            >
              <SyncOutlined className={'text-base'} />
            </Button>
          </Tooltip>
          <Button
            variant={'bordered'}
            color={'default'}
            size={'sm'}
            onClick={() => {
              let name;
              Dialog.show({
                title: t('Duplicating a category'),
                content: (
                  <Input
                    size={'large'}
                    style={{ width: 400 }}
                    placeholder={t('Please input a new name for the duplicated category')}
                    onChange={v => name = v}
                  />
                ),
                v2: true,
                width: 'auto',
                closeMode: ['close', 'mask', 'esc'],
                onOk: async () => {
                  const rsp = await BApi.category.duplicateCategory(category.id, { name });
                  if (!rsp.code) {
                    loadAllCategories();
                    loadAllMediaLibraries();
                  }
                },
              });
            }}
          >{t('Duplicate')}</Button>
        </div>
      </div>
      <div className="configuration-line block">
        {componentTypes.filter((t) => t.value != ComponentType.Enhancer)
          .map((type) => {
            const comp = components.find((a) => a.componentType == type.value);
            // console.log(components, comp);
            return (
              <div className={'component setting'} key={type.value}>
                <Tooltip
                  content={t(ComponentTips[type.value])}
                >
                  <Chip
                    size={'sm'}
                    radius={'sm'}
                  >{t(type.label)}
                  </Chip>
                </Tooltip>

                &emsp;
                {comp ? (
                  <span
                    className="editable"
                    onClick={() => {
                      renderBasicComponentSelector(type.value, comp.componentKey);
                    }}
                  >
                    <span className="hover-area">
                      {t(comp.descriptor?.name)}
                      &nbsp;
                      <CustomIcon type="edit-square" size={'small'} />
                    </span>
                  </span>
                ) : (
                  <CustomIcon
                    onClick={() => {
                      renderBasicComponentSelector(type.value);
                    }}
                    type="edit-square"
                    className={'text-small'}
                  />
                )}
              </div>
            );
          })}
        <div className={'setting'}>
          <Chip
            size={'sm'}
            radius={'sm'}
          >{t('Priority on cover selection')}</Chip>
          &emsp;
          <span className="editable">
            <span
              className="hover-area"
              onClick={() => {
                const order = (category.coverSelectionOrder || CoverSelectOrder.FilenameAscending) % coverSelectOrders.length + 1;
                BApi.category.patchCategory(
                  category.id,
                  {
                    coverSelectionOrder: order,
                  },
                )
                  .then((t) => {
                    if (!t.code) {
                      category.coverSelectionOrder = order;
                      forceUpdate();
                    }
                  });
              }}
            >
              <span>{t(CoverSelectOrder[category.coverSelectionOrder] ?? CoverSelectOrder[CoverSelectOrder.FilenameAscending])}</span>
              &nbsp;
              <CustomIcon
                type="sorting"
                className={'text-small'}
              />
            </span>
          </span>
        </div>
        <div className={'setting'}>
          <Tooltip
            content={
              <div>
                {t('You can share tags and rate of same physical filesystem item from different app instances by enabling this option, but it may cause poor performance of tag-related operations.')}
                <FeatureStatusTip status={'developing'} name={t('NFO generator')} />
              </div>
            }
          >
            <Chip
              size={'sm'}
              radius={'sm'}
            >{t('Generate nfo')}</Chip>
          </Tooltip>
          &emsp;
          <Checkbox
            isDisabled
            checked={category.generateNfo}
            onValueChange={(checked) => {
                BApi.category.patchCategory(
                  category.id,
                  {
                    generateNfo: checked,
                  },
                )
                  .then((t) => {
                    if (!t.code) {
                      category.generateNfo = checked;
                      forceUpdate();
                    }
                  });
              }}
          />
        </div>
        <div className={'col-span-3'}>
          <div className={'flex items-center gap-2'}>
            <Tooltip
              content={t(ComponentTips[ComponentType.Enhancer])}
            >
              <Chip
                size={'sm'}
                radius={'sm'}
              >{t('Enhancers')}</Chip>
            </Tooltip>
            <div
              className="flex flex-wrap gap-1"
            >
              {
                category.enhancerOptions?.filter(eo => eo.active).length > 0 ? (
                  <div
                    className="flex flex-wrap gap-1"
                  >
                    {category.enhancerOptions?.filter(eo => eo.active).map((e, i) => {
                      const enhancer = enhancers?.find(eh => eh.id == e.enhancerId);
                      return (
                        <Button
                          size={'sm'}
                          variant={'flat'}
                          key={e.id}
                          onClick={() => {
                            // createPortal(EnhancerSelectorV2, {
                            //   categoryId: category.id,
                            //   onClose: loadAllCategories,
                            // });
                            createPortal(EnhancerSelectorV2, {
                              categoryId: category.id,
                              onClose: loadAllCategories,
                            });
                          }}
                        >
                          {enhancer?.name}
                        </Button>
                      );
                    })}
                  </div>
                ) : (
                  <Button
                    variant={'light'}
                    color={'primary'}
                    size={'sm'}
                    onClick={() => {
                      createPortal(EnhancerSelectorV2, {
                        categoryId: category.id,
                        onClose: loadAllCategories,
                      });
                    }}
                  >
                    {t('Click to set')}
                  </Button>
                )
              }

            </div>
          </div>
        </div>
        <div
          className={'col-span-3'}
        >
          <div className={'flex flex-wrap items-center gap-2'}>
            <Chip
              size={'sm'}
              radius={'sm'}
            >
              {t('Custom properties')}
            </Chip>
            {
              category.customProperties?.length > 0 ? (
                <div
                  className="flex flex-wrap gap-1"
                >
                  {category.customProperties?.map((e, i) => (
                    <Button
                      size={'sm'}
                      variant={'flat'}
                      key={e.id}
                      onClick={() => {
                        CategoryCustomPropertyBinderDialog.show({
                          category: category,
                          onSaved: loadAllCategories,
                        });
                      }}
                    >
                      {e.name}
                    </Button>
                  ))}
                </div>
              ) : (
                <Button
                  className={''}
                  variant={'light'}
                  size={'sm'}
                  color={'primary'}
                  onClick={() => {
                    CategoryCustomPropertyBinderDialog.show({
                      category: category,
                      onSaved: loadAllCategories,
                    });
                  }}
                >
                  {t('Click to set')}
                </Button>
              )
            }
          </div>
        </div>
        <div className={'col-span-3'}>
          <div className={'flex flex-wrap items-center gap-2'}>
            <Tooltip
              content={t('You can set a display name template for resources. By default, file name will be used as display name')}
            >
              <Chip
                size={'sm'}
                radius={'sm'}
              >
                {t('Display name template')}
              </Chip>
            </Tooltip>
            <Button
              variant={'light'}
              size={'sm'}
              onClick={() => {
                createPortal(DisplayNameTemplateEditorDialog, {
                  categoryId: category.id,
                  onSaved: () => {
                    loadAllCategories();
                  },
                });
              }}
            >
              {category.resourceDisplayNameTemplate ?? t('Click to set')}
            </Button>
          </div>
        </div>
      </div>
      <div className="libraries-line block">
        <div className="libraries-header">
          <div className="title-line ls">
            <div className="title">
              {t('Media libraries')}
            </div>
            <Tooltip
              content={t('Resources will not loaded automatically after modifying media libraries, ' +
                'you can click "sync button" at top-right of current page to load your resources immediately.')}
            >
              <CustomIcon type="warning-circle" />
            </Tooltip>
            <Dropdown placement={'bottom-start'}>
              <DropdownTrigger>
                <Button
                  size={'sm'}
                  isIconOnly
                >
                  <UnorderedListOutlined className={'text-medium'} />
                </Button>
              </DropdownTrigger>
              <DropdownMenu
                aria-label={'static actions'}
                onAction={key => {
                  switch (key) {
                    case 'add': {
                      let n;
                      createPortal(Modal, {
                        defaultVisible: true,
                        title: t('Add a media library'),
                        children: (
                          <Input
                            size={'large'}
                            placeholder={t('Name of media library')}
                            style={{ width: '100%' }}
                            defaultValue={n}
                            onChange={(v) => {
                              n = v;
                            }}
                          />
                        ),
                        onOk: async () => {
                          if (n?.length > 0) {
                            const r = await BApi.mediaLibrary.addMediaLibrary({
                              categoryId: category.id,
                              name: n,
                            });
                            if (!r.code) {
                              loadAllMediaLibraries();
                            } else {
                              throw new Error(r.message!);
                            }
                          } else {
                            Message.error(t('Invalid data'));
                            throw new Error('Invalid data');
                          }
                        },
                      });
                      break;
                    }
                    case 'add-in-bulk': {
                      AddMediaLibraryInBulkDialog.show({
                        categoryId: category.id,
                        onSubmitted: loadAllMediaLibraries,
                      });
                      break;
                    }
                  }
                }}
              >
                <DropdownItem
                  // className="text-danger"
                  // color="danger"
                  key={'add'}
                >
                  {t('Add a media library')}
                </DropdownItem>
                <DropdownItem
                  // className="text-danger"
                  // color="danger"
                  key={'add-in-bulk'}
                >
                  {t('Add in bulk')}
                </DropdownItem>
              </DropdownMenu>
            </Dropdown>
          </div>
          <div className="path-configuration header">
            <div className="path">{t('Root path')}</div>
            <div className="filter">{t('Resource discovery')}</div>
            {/* <div className="tags">{t('Fixed tags')}</div> */}
            <div className="tags">{t('Custom properties')}</div>
          </div>
        </div>
        <div className="libraries">
          <SortableMediaLibraryList
            libraries={libraries}
            loadAllMediaLibraries={loadAllMediaLibraries}
            forceUpdate={forceUpdate}
          />
        </div>
      </div>
    </div>
  );
});
