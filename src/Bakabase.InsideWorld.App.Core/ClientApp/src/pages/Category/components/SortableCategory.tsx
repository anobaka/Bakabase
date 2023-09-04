import React, { useReducer, useRef, useState } from 'react';
import { Badge, Balloon, Checkbox, Dialog, Dropdown, Icon, Input, Menu, Message } from '@alifd/next';
import i18n from 'i18next';
import IceLabel from '@icedesign/label';
import { SketchPicker } from 'react-color';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import {
  AddMediaLibrary, ConfigureResourceCategoryComponents,
  RemoveCategoryEnhancementRecords,
  RemoveResourceCategory,
  SortMediaLibrariesInCategory,
  UpdateResourceCategory,
} from '@/sdk/apis';
import CustomIcon from '@/components/CustomIcon';
import { CoverSelectOrder, coverSelectOrders, ComponentType, componentTypes } from '@/sdk/constants';
import SortableMediaLibraryList from '@/pages/Category/components/SortableMediaLibraryList';
import DragHandle from '@/components/DragHandle';
import BasicCategoryComponentSelector from '@/components/BasicCategoryComponentSelector';
import EnhancerSelector from '@/components/EnhancerSelector';
import BApi from '@/sdk/BApi';
import ClickableIcon from '@/components/ClickableIcon';
import SimpleLabel from '@/components/SimpleLabel';

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
        title: i18n.t(ComponentType[componentType]),
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
      title: i18n.t('Enhancers'),
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
        <Balloon.Tooltip
          trigger={(
            <Badge
              className={'count'}
              count={libraries.reduce((s, t) => s + t.resourceCount, 0)}
              overflowCount={9999999}
            />
          )}
          triggerType={'hover'}
          align={'t'}
        >
          {i18n.t('Count of resources')}
        </Balloon.Tooltip>
        <Dropdown
          trigger={(
            <ClickableIcon type={'ellipsis-circle'} colorType={'normal'} />
          )}
          className={'category-page-category-more-operations-popup'}
          triggerType={['click']}
        >
          <Menu>
            <Menu.Item
              className={'warning'}
              onClick={() => {
                Dialog.confirm({
                  title: `${i18n.t('Removing all enhancement records of resources under this category')}`,
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
              {i18n.t('Remove all enhancement records')}
            </Menu.Item>
            <Menu.Item
              className={'warning'}
              onClick={() => {
                Dialog.confirm({
                  title: `${i18n.t('Deleting')} ${category.name}`,
                  closeable: true,
                  onOk: () => new Promise(((resolve, reject) => {
                    RemoveResourceCategory({
                      id: category.id,
                    })
                      .invoke((a) => {
                        if (!a.code || a.code == 304) {
                          loadAllCategories();
                          resolve();
                        } else {
                          reject();
                        }
                      });
                  })),
                });
              }}
            >
              <CustomIcon
                type="delete"
              />
              {i18n.t('Remove')}
            </Menu.Item>
          </Menu>
        </Dropdown>
      </div>
      <div className="configuration-line block">
        {componentTypes.filter((t) => t.value != ComponentType.Enhancer)
          .map((type) => {
            const comp = components.find((a) => a.componentType == type.value);
            // console.log(components, comp);
            return (
              <div className={'component setting'} key={type.value}>
                <SimpleLabel
                  status={'default'}
                >{i18n.t(type.label)}
                  <Balloon.Tooltip
                    triggerType={'hover'}
                    align={'t'}
                    trigger={(
                      <CustomIcon type={'question-circle'} size={'xs'} />
                    )}
                  >
                    {i18n.t(ComponentTips[type.value])}
                  </Balloon.Tooltip>
                </SimpleLabel>
                &emsp;
                {comp ? (
                  <span
                    className="editable"
                    onClick={() => {
                      renderBasicComponentSelector(type.value, comp.componentKey);
                    }}
                  >
                    <span className="hover-area">
                      {i18n.t(comp.descriptor?.name)}
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
          <SimpleLabel status={'default'}>{i18n.t('Priority on cover selection')}</SimpleLabel>
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
              <span>{i18n.t(CoverSelectOrder[category.coverSelectionOrder] ?? CoverSelectOrder[CoverSelectOrder.FilenameAscending])}</span>
              &nbsp;
              <CustomIcon type="sorting" size={'small'} />
            </span>
          </span>
        </div>
        <div className={'setting'}>
          <SimpleLabel status={'default'}>
            {i18n.t('Generate nfo')}
            <Balloon.Tooltip
              trigger={<CustomIcon type={'question-circle'} size={'xs'} />}
              align={'t'}
            >{i18n.t('You can share tags and rate of same physical filesystem item from different app instances by enabling this option, but it may cause poor performance of tag-related operations.')}
            </Balloon.Tooltip>
          </SimpleLabel>
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
      </div>
      <div className="enhancers-line block">
        <div
          className="title-line e"
        >
          {i18n.t('Enhancers')}
          <Balloon.Tooltip
            trigger={(
              <CustomIcon
                type="question-circle"
              />
            )}
            triggerType={'hover'}
            align={'t'}
          >
            {i18n.t(ComponentTips[ComponentType.Enhancer])}
          </Balloon.Tooltip>
          <ClickableIcon
            colorType={'normal'}
            type="edit-square"
            onClick={() => {
              renderEnhancersSelector();
            }}
          />
        </div>
        <div
          className="enhancers"
          onClick={() => {
            renderEnhancersSelector();
          }}
        >
          {enhancers.map((e, i) => (
            <div className={'enhancer'}>
              <SimpleLabel
                key={i}
                style={{ margin: '0 5px 2px 0' }}
                status={'default'}
              >{i18n.t(e.descriptor?.name)}
              </SimpleLabel>
            </div>
          ))}
        </div>
      </div>
      <div className="libraries-line block">
        <div className="libraries-header">
          <div className="title-line ls">
            <div className="title">
              {i18n.t('Media libraries')}
            </div>
            <Balloon.Tooltip
              trigger={(
                <CustomIcon type="warning-circle" />
              )}
              triggerType={'hover'}
              align={'t'}
            >
              {i18n.t('Resources will not loaded automatically after modifying media libraries, ' +
                'you can click "sync button" at top-right of current page to load your resources immediately, ' +
                'or set a sync interval to load them periodically.')}
            </Balloon.Tooltip>
            <ClickableIcon
              colorType={'normal'}
              type={'plus-circle'}
              onClick={() => {
                let n;
                Dialog.show({
                  title: i18n.t('Add media library'),
                  content: (
                    <Input
                      size={'large'}
                      placeholder={i18n.t('Name of media library')}
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
                      Message.error(i18n.t('Invalid data'));
                    }
                  })),
                });
              }}
            />
          </div>
          <div className="path-configuration header">
            <div className="path">{i18n.t('Root path')}</div>
            <div className="filter">{i18n.t('Resource discovery')}</div>
            <div className="tags">{i18n.t('Fixed tags')}</div>
            <div className="tags">{i18n.t('Additional properties')}</div>
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
