import React, { useState } from 'react';
import { Balloon, Checkbox, Dialog, Dropdown, Input, Menu, Message } from '@alifd/next';
import { SketchPicker } from 'react-color';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { useTranslation } from 'react-i18next';
import AddMediaLibraryInBulkDialog from './AddMediaLibraryInBulkDialog';
import { AddMediaLibrary, RemoveCategoryEnhancementRecords, UpdateResourceCategory } from '@/sdk/apis';
import CustomIcon from '@/components/CustomIcon';
import { ComponentType, componentTypes, CoverSelectOrder, coverSelectOrders } from '@/sdk/constants';
import SortableMediaLibraryList from '@/pages/Category/components/SortableMediaLibraryList';
import DragHandle from '@/components/DragHandle';
import BasicCategoryComponentSelector from '@/components/BasicCategoryComponentSelector';
import EnhancerSelector from '@/components/EnhancerSelector';
import BApi from '@/sdk/BApi';
import ClickableIcon from '@/components/ClickableIcon';
import SimpleLabel from '@/components/SimpleLabel';
import CategoryCustomPropertyBinderDialog from '@/pages/Category/components/CustomPropertyBinder';
import { Chip, Button, Spacer, Tooltip, Badge } from '@/components/bakaui';
import DisplayNameRuleEditorDialog from '@/pages/Category/components/DisplayNameRuleEditorDialog';
import EnhancerSelectorV2 from '@/components/EnhancerSelectorV2';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

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

  console.log(createPortal, 1234567);


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
  const enhancers = components.filter((e) => e.componentType == ComponentType.Enhancer);

  const componentKeys = components.map(a => a.componentKey);

  console.log('[SortableCategory]rendering', category, enhancers, componentKeys);

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
            return BApi.resourceCategory.configureResourceCategoryComponents(category.id, {
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

  const renderEnhancersSelector = () => {
    let eo = {
      ...(category.enhancementOptions || {}),
      enhancerKeys: category.componentsData?.filter(d => d.componentType == ComponentType.Enhancer).map(d => d.componentKey),
    };
    Dialog.show({
      v2: true,
      style: { minWidth: 1000 },
      closeMode: ['esc', 'close', 'mask'],
      title: t('Enhancers'),
      width: 'auto',
      // height: 'auto',
      content: (
        <EnhancerSelector
          defaultValue={eo}
          onChange={v => {
            eo = v;
          }}
        />
      ),
      onOk: () => new Promise((resolve, reject) => {
        return BApi.resourceCategory.configureResourceCategoryComponents(category.id, {
          componentKeys: eo.enhancerKeys,
          type: ComponentType.Enhancer,
          enhancementOptions: eo,
        }).then(a => {
          if (!a.code) {
            loadAllCategories();
            resolve(a);
          } else {
            reject();
          }
        });
      }),
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
                    UpdateResourceCategory({
                      id: category.id,
                      model: {
                        name,
                        color: categoryColor.hex,
                      },
                    })
                      .invoke((t) => {
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
                onClick={() => {
                  setName(category.name);
                  setCategoryColor({
                    hex: category.color,
                  });
                  // console.log(category.color);
                  setEditMode(EditMode.NameAndColor);
                }}
              >
                <span className="hover-area">
                  <span
                    style={{ color: category.color }}
                    title={category.id}
                  >{category.name}
                  </span>
                  &nbsp;
                  <ClickableIcon type={'edit-square'} colorType={'normal'} />
                </span>
              </span>
            )}
          </div>
          <Dropdown
            trigger={(
              <ClickableIcon
                type={'ellipsis-circle'}
                colorType={'normal'}
                className={'text-large'}
              />
            )}
            className={'category-page-category-more-operations-popup'}
            triggerType={['click']}
          >
            <Menu>
              <Menu.Item
                className={'warning'}
                onClick={() => {
                  Dialog.confirm({
                    title: `${t('Removing all enhancement records of resources under this category')}`,
                    closeable: true,
                    onOk: () => new Promise(((resolve, reject) => {
                      RemoveCategoryEnhancementRecords({
                        id: category.id,
                      })
                        .invoke((a) => {
                          if (!a.code || a.code == 304) {
                            resolve();
                          }
                        });
                    })),
                  });
                }}
              >
                <CustomIcon type="flashlight" />
                {t('Remove all enhancement records')}
              </Menu.Item>
              <Menu.Item
                className={'warning'}
                onClick={() => {
                  Dialog.confirm({
                    title: `${t('Deleting')} ${category.name}`,
                    content: t('All related data will be deleted too, are you sure?'),
                    closeable: true,
                    onOk: async () => {
                      const rsp = await BApi.resourceCategory.deleteResourceCategoryAndClearAllRelatedData(category.id);
                      if (!rsp.code) {
                        loadAllCategories();
                      }
                    },
                  });
                }}
              >
                <CustomIcon
                  type="delete"
                />
                {t('Remove')}
              </Menu.Item>
            </Menu>
          </Dropdown>
          <Spacer x={2} />
          <Tooltip
            content={t('Count of resources')}
          >
            <Badge
              content={libraries.reduce((s, t) => s + t.resourceCount, 0)}
              color={'primary'}
              variant={'flat'}
            >&nbsp;</Badge>
          </Tooltip>
        </div>
        <div className="right">
          <Button
            variant={'bordered'}
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
                  const rsp = await BApi.resourceCategory.duplicateResourceCategory(category.id, { name });
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
                    size={'small'}
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
                UpdateResourceCategory({
                  id: category.id,
                  model: {
                    coverSelectionOrder: order,
                  },
                })
                  .invoke((t) => {
                    if (!t.code) {
                      category.coverSelectionOrder = order;
                      forceUpdate();
                    }
                  });
              }}
            >
              <span>{t(CoverSelectOrder[category.coverSelectionOrder] ?? CoverSelectOrder[CoverSelectOrder.FilenameAscending])}</span>
              &nbsp;
              <CustomIcon type="sorting" size={'small'} />
            </span>
          </span>
        </div>
        <div className={'setting'}>
          <Tooltip
            content={t('You can share tags and rate of same physical filesystem item from different app instances by enabling this option, but it may cause poor performance of tag-related operations.')}
          >
            <Chip
              size={'sm'}
              radius={'sm'}
            >{t('Generate nfo')}</Chip>
          </Tooltip>
          &emsp;
          <Checkbox
            checked={category.generateNfo}
            onChange={(checked) => {
              UpdateResourceCategory({
                id: category.id,
                model: {
                  generateNfo: checked,
                },
              })
                .invoke((t) => {
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
              <Button
                variant={'light'}
                color={'primary'}
                size={'sm'}
                onClick={() => {
                  createPortal(EnhancerSelectorV2, {
                    categoryId: category.id,
                    // category: category,
                    // onSaved: loadAllCategories,
                  });
                }}
              >
                {t('Click to set')}
              </Button>
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
              content={t('You can set a rule to display name of resources. By default, file name will be used as display name')}
            >
              <Chip
                size={'sm'}
                radius={'sm'}
              >
                {t('Display name rule')}
              </Chip>
            </Tooltip>
            <Button
              variant={'light'}
              size={'sm'}
              color={'primary'}
              onClick={() => {
                DisplayNameRuleEditorDialog.show({
                  categoryId: category.id,
                });
              }}
            >
              {t('Click to set')}
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
            <Dropdown
              trigger={(
                <ClickableIcon
                  colorType={'normal'}
                  type={'plus-circle'}
                  onClick={() => {
                    let n;
                    Dialog.show({
                      title: t('Add media library'),
                      content: (
                        <Input
                          size={'large'}
                          placeholder={t('Name of media library')}
                          style={{ width: 600 }}
                          defaultValue={n}
                          onChange={(v) => {
                            n = v;
                          }}
                        />
                      ),
                      closeable: true,
                      onOk: () => new Promise(((resolve, reject) => {
                        if (n?.length > 0) {
                          AddMediaLibrary({
                            model: {
                              categoryId: category.id,
                              name: n,
                            },
                          })
                            .invoke((t) => {
                              if (!t.code) {
                                loadAllMediaLibraries();
                                resolve();
                              } else {
                                reject();
                              }
                            })
                            .catch(() => {
                              reject();
                            });
                        } else {
                          reject();
                          Message.error(t('Invalid data'));
                        }
                      })),
                    });
                  }}
                />
              )}
              triggerType={['hover']}
            >
              <Menu>
                <Menu.Item
                  className={'warning'}
                  onClick={() => {
                    AddMediaLibraryInBulkDialog.show({
                      categoryId: category.id,
                      onSubmitted: loadAllMediaLibraries,
                    });
                  }}
                >
                  <CustomIcon type="playlist_add" />
                  {t('Add in bulk')}
                </Menu.Item>
              </Menu>
            </Dropdown>
          </div>
          <div className="path-configuration header">
            <div className="path">{t('Root path')}</div>
            <div className="filter">{t('Resource discovery')}</div>
            <div className="tags">{t('Fixed tags')}</div>
            <div className="tags">{t('Additional properties')}</div>
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
