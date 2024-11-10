import React, { useState } from 'react';
import { Dialog, Message } from '@alifd/next';
import { SketchPicker } from 'react-color';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { useTranslation } from 'react-i18next';
import { CopyOutlined, EditOutlined, SyncOutlined, UnorderedListOutlined } from '@ant-design/icons';
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
  DropdownTrigger, Input,
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

type CategoryColor = {
  hex?: string;
};

type Props = {
  category: any;
  libraries: any[];
  loadAllCategories: () => void;
  loadAllMediaLibraries: () => void;
  reloadCategory: (id: number) => any;
  reloadMediaLibrary: (id: number) => any;
  allComponents: any[];
  enhancers: any[];
  forceUpdate: () => void;
};

export default (({
                   category,
                   loadAllCategories,
                   loadAllMediaLibraries,
                   reloadCategory,
                   reloadMediaLibrary,
                   libraries,
                   forceUpdate,
                   allComponents,
                   enhancers,
                 }: Props) => {
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

  const [categoryColor, setCategoryColor] = useState<CategoryColor>(undefined);
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
                reloadCategory(category.id);
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

  const renderMediaLibraryAddModal = () => {
    let n;
    createPortal(Modal, {
      defaultVisible: true,
      title: t('Add a media library'),
      children: (
        <Input
          placeholder={t('Name of media library')}
          style={{ width: '100%' }}
          defaultValue={n}
          onValueChange={(v) => {
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
  };

  const renderMediaLibraryAddInBulkModal = () => {
    AddMediaLibraryInBulkDialog.show({
      categoryId: category.id,
      onSubmitted: loadAllMediaLibraries,
    });
  };

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
                <Input value={name} onValueChange={(v) => setName(v)} />
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
                  variant={'light'}
                >{resourceCount}</Chip>
              </Tooltip>
            </>
          )}

        </div>
        <div className="right flex items-center gap-2">
          <Tooltip content={t('Sync all media libraries in current category')}>
            <Button
              // isIconOnly
              color={'secondary'}
              size={'sm'}
              // variant={'light'}
              variant={'bordered'}
              onClick={() => {
                BApi.category.startSyncingCategoryResources(category.id);
              }}
            >
              <SyncOutlined className={'text-base'} />
              {t('Sync now')}
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
                    style={{ width: 400 }}
                    placeholder={t('Please input a new name for the duplicated category')}
                    onValueChange={v => name = v}
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
          >
            <CopyOutlined className={'text-base'} />
            {t('Duplicate')}
          </Button>
        </div>
      </div>
      <div className="configuration-line block">
        {componentTypes.filter((t) => t.value != ComponentType.Enhancer)
          .map((type) => {
            const comp = components.find((a) => a.componentType == type.value);
            // console.log(components, comp);
            return (
              <div className={'flex items-center gap-1'} key={type.value}>
                <Tooltip
                  content={t(ComponentTips[type.value])}
                >
                  <Chip
                    size={'sm'}
                    radius={'sm'}
                  >{t(type.label)}
                  </Chip>
                </Tooltip>
                <Button
                  size={'sm'}
                  color={comp ? 'default' : 'primary'}
                  onClick={() => {
                    renderBasicComponentSelector(type.value, comp?.componentKey);
                  }}
                  variant={'light'}
                >
                  {t(comp?.descriptor?.name ?? 'Click to set')}
                </Button>
              </div>
            );
          })}
        <div className={'flex items-center gap-1'}>
          <Chip
            size={'sm'}
            radius={'sm'}
          >{t('Priority on cover selection')}</Chip>
          <Dropdown>
            <DropdownTrigger>
              <Button
                variant={'light'}
                size={'sm'}
                color={category.coverSelectionOrder > 0 ? 'default' : 'primary'}
              >
                {category.coverSelectionOrder > 0 ? (
                  t(CoverSelectOrder[category.coverSelectionOrder])
                ) : (
                  t('Click to set')
                )}
              </Button>
            </DropdownTrigger>
            <DropdownMenu
              aria-label="Cover select order"
              variant="flat"
              disallowEmptySelection
              selectionMode="single"
              selectedKeys={[category.coverSelectionOrder]}
              onSelectionChange={keys => {
                const arr = Array.from(keys ?? []);
                const order = arr?.[0] as CoverSelectOrder;
                console.log(keys);
                BApi.category.patchCategory(category.id, { coverSelectionOrder: order })
                  .then((t) => {
                    if (!t.code) {
                      category.coverSelectionOrder = order;
                      forceUpdate();
                    }
                  });
              }}
            >
              {coverSelectOrders.map(c => {
                return (
                  <DropdownItem key={c.value}>{t(CoverSelectOrder[c.value])}</DropdownItem>
                );
              })}
            </DropdownMenu>
          </Dropdown>
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
                            createPortal(EnhancerSelectorV2, {
                              categoryId: category.id,
                              onClose: () => reloadCategory(category.id),
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
                        onClose: () => reloadCategory(category.id),
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
            <Tooltip content={t('Unassociated custom properties will not be displayed')}>
              <Chip
                size={'sm'}
                radius={'sm'}
              >
                {t('Custom properties')}
              </Chip>
            </Tooltip>
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
                          onSaved: () => reloadCategory(category.id),
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
                      onSaved: () => reloadCategory(category.id),
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
              color={category.resourceDisplayNameTemplate == undefined ? 'primary' : 'default'}
              onClick={() => {
                createPortal(DisplayNameTemplateEditorDialog, {
                  categoryId: category.id,
                  onSaved: () => reloadCategory(category.id),
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
                      renderMediaLibraryAddModal();
                      break;
                    }
                    case 'add-in-bulk': {
                      renderMediaLibraryAddInBulkModal();
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
            <div className="tags">{t('Generated properties')}</div>
          </div>
        </div>
        <div className="libraries">
          {libraries.length > 0 ? (
            <SortableMediaLibraryList
              libraries={libraries}
              loadAllMediaLibraries={loadAllMediaLibraries}
              reloadMediaLibrary={reloadMediaLibrary}
              forceUpdate={forceUpdate}
            />
          ) : (
            <div className={'flex flex-col gap-2'}>
              <div className={'text-center'}>
                {t('You should set up a media library first to visit your resources')}
              </div>
              <div className={'flex items-center gap-4 justify-center'}>
                <Button
                  color={'primary'}
                  size={'sm'}
                  onClick={() => {
                    renderMediaLibraryAddModal();
                  }}
                >
                  {t('Set up now')}
                </Button>
                <Button
                  color={'secondary'}
                  size={'sm'}
                  onClick={() => {
                    renderMediaLibraryAddInBulkModal();
                  }}
                >
                  {t('Set up in bulk')}
                </Button>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
});
