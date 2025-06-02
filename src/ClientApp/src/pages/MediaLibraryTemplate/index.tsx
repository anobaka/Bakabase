import { useTranslation } from 'react-i18next';
import React, { useEffect, useState } from 'react';
import { ImportOutlined, QuestionCircleOutlined } from '@ant-design/icons';
import { AiOutlineDelete, AiOutlineEdit, AiOutlinePlusCircle, AiOutlineSisternode } from 'react-icons/ai';
import { useUpdate } from 'react-use';
import _ from 'lodash';
import { IoDuplicateOutline, IoLocate, IoPlayCircleOutline, IoRocketOutline } from 'react-icons/io5';
import { CiFilter } from 'react-icons/ci';
import { TiChevronRightOutline, TiFlowChildren } from 'react-icons/ti';
import { GoShareAndroid } from 'react-icons/go';
import { MdDeleteOutline, MdOutlineSubtitles } from 'react-icons/md';
import toast from 'react-hot-toast';
import { TbDatabase } from 'react-icons/tb';
import type { MediaLibraryTemplate } from './models';
import { PathFilterDemonstrator, PathFilterModal } from './components/PathFilter';
import testData from './data.json';
import Block from './components/Block';
import { Accordion, AccordionItem, Button, Chip, Input, Modal, Select, Textarea, Tooltip } from '@/components/bakaui';
import { PropertyPool } from '@/sdk/constants';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import type { IdName, PropertyMap } from '@/components/types';
import PropertySelector from '@/components/PropertySelector';
import BApi from '@/sdk/BApi';
import { PathLocatorDemonstrator, PathLocatorModal } from '@/pages/MediaLibraryTemplate/components/PathLocator';
import type { EnhancerDescriptor } from '@/components/EnhancerSelectorV2/models';
import EnhancerSelectorModal from '@/pages/MediaLibraryTemplate/components/EnhancerSelectorModal';
import EnhancerOptionsModal from '@/components/EnhancerSelectorV2/components/EnhancerOptionsModal';
import PlayableFileSelectorModal from '@/pages/MediaLibraryTemplate/components/PlayableFileSelectorModal';
import DisplayNameTemplateEditorModal from '@/pages/MediaLibraryTemplate/components/DisplayNameTemplateEditorModal';
import ImportModal from '@/pages/MediaLibraryTemplate/components/ImportModal';
import BriefProperty from '@/components/Chips/Property/BriefProperty';
import BriefEnhancer from '@/components/Chips/Enhancer/BriefEnhancer';
import { willCauseCircleReference } from '@/components/utils';
import { animals } from '@/pages/Test/nextui';

export default () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const forceUpdate = useUpdate();

  const [templates, setTemplates] = useState<MediaLibraryTemplate[]>(testData);
  const [extensionGroups, setExtensionGroups] = useState<IdName[]>([]);
  const [enhancers, setEnhancers] = useState<EnhancerDescriptor[]>([]);
  const [propertyMap, setPropertyMap] = useState<PropertyMap>({});


  const loadProperties = async () => {
    const psr = (await BApi.property.getPropertiesByPool(PropertyPool.All)).data || [];
    const ps = _.mapValues(_.groupBy(psr, x => x.pool), (v) => _.keyBy(v, x => x.id));
    setPropertyMap(ps);
  };

  const loadTemplates = async () => {
    const r = await BApi.mediaLibraryTemplate.getAllMediaLibraryTemplates();
    if (!r.code) {
      setTemplates(r.data || []);
    }
  };

  // load one template
  const loadTemplate = async (id: number) => {
    const r = await BApi.mediaLibraryTemplate.getMediaLibraryTemplate(id);
    if (!r.code) {
      setTemplates(templates.map(t => (t.id == r.data?.id ? r.data : t)));
    }
  };

  useEffect(() => {
    // createPortal(
    //   PathFilterModal, {
    //
    //   },
    // );
    BApi.enhancer.getAllEnhancerDescriptors().then(r => {
      setEnhancers(r.data || []);
    });
    loadProperties();
    loadTemplates();
  }, []);

  const putTemplate = async (tpl: MediaLibraryTemplate) => {
    const r = await BApi.mediaLibraryTemplate.putMediaLibraryTemplate(tpl.id, tpl);
    if (!r.code) {
      toast.success(t('Saved successfully'));
      await loadTemplate(tpl.id);
    }
  };

  const renderChildSelector = (tpl: MediaLibraryTemplate) => {
    const willCauseLoopKeys = new Set<string>(
      templates.filter(t1 => {
        return willCauseCircleReference(tpl, t1.id, templates,
          x => x.id,
          x => x.childId,
          (x, k) => x.childId = k,
        );
      }).map(x => x.id.toString()),
    );
    return (
      <div className={'inline-block'}>
        <Select
          className={'min-w-[320px]'}
          label={`${t('Child template')} (${t('optional')})`}
          placeholder={t('Select a child template')}
          size={'sm'}
          fullWidth={false}
          disabledKeys={willCauseLoopKeys}
          dataSource={templates.map(t1 => {
            const hasLoop = willCauseLoopKeys.has(t1.id.toString());
            return {
              label: t1.name + (hasLoop ? ` (${t('Loop detected')})` : ''),
              value: t1.id.toString(),
            };
          })}
        />
      </div>

    );
  };

  return (
    <div>
      <div className={'flex items-center gap-2 justify-between'}>
        <Button
          size={'sm'}
          color={'primary'}
          onPress={() => {
            let name = '';
            createPortal(Modal, {
              defaultVisible: true,
              title: t('Create a template'),
              children: (
                <Input
                  label={t('Template name')}
                  placeholder={t('Enter template name')}
                  isRequired
                  onValueChange={v => {
                    name = v;
                  }}
                />
              ),
              onOk: async () => {
                const r = await BApi.mediaLibraryTemplate.addMediaLibraryTemplate({ name });
                if (!r.code) {
                  loadTemplates();
                }
              },
            });
          }}
        >{t('Create a template')}</Button>
        <Button
          size={'sm'}
          color={'default'}
          onPress={() => {
            createPortal(ImportModal, {
              onImported: async () => {
                await loadProperties();
                await loadTemplates();
                // await loadExtensionGroups();
              },
            });
          }}
        >
          <ImportOutlined />
          {t('Import a template')}
        </Button>
      </div>
      <div className={'mt-2'}>
        <Accordion
          variant="splitted"
          defaultSelectedKeys={templates.map(t => t.id.toString())}
        >
          {templates.map((tpl, i) => {
            return (
              <AccordionItem
                key={tpl.id}
                title={(
                  <div className={'flex items-center justify-between'}>
                    <div className={'flex items-center gap-1'}>
                      <div className={'font-bold'}>
                        {tpl.name}
                      </div>
                      {tpl.author && (
                        <div className={'opacity-60'}>
                          {tpl.author}
                        </div>
                      )}
                      {tpl.description && (
                        <Tooltip placement={'bottom'} content={tpl.description}>
                          <QuestionCircleOutlined className={'text-medium'} />
                        </Tooltip>
                      )}
                      <Button
                        size={'sm'}
                        isIconOnly
                        onPress={() => {
                        let { name, author, description } = tpl;
                        createPortal(Modal, {
                          defaultVisible: true,
                          title: t('Edit template'),
                          children: (
                            <div className={'flex flex-col gap-2'}>
                              <Input
                                label={t('Name')}
                                placeholder={t('Enter template name')}
                                isRequired
                                defaultValue={name}
                                onValueChange={v => {
                                  name = v;
                                }}
                              />
                              <Input
                                label={`${t('Author')}${t('(optional)')}`}
                                placeholder={t('Enter author name')}
                                defaultValue={author}
                                onValueChange={v => {
                                  author = v;
                                }}
                              />
                              <Textarea
                                label={`${t('Description')}${t('(optional)')}`}
                                placeholder={t('Enter description')}
                                defaultValue={description}
                                onValueChange={v => {
                                  description = v;
                                }}
                              />
                            </div>
                          ),
                          size: 'lg',
                          onOk: async () => {
                            tpl.name = name;
                            tpl.author = author;
                            tpl.description = description;
                            await putTemplate(tpl);
                            forceUpdate();
                          },
                        });
                      }}
                        variant={'light'}
                      >
                        <AiOutlineEdit className={'text-medium'} />
                      </Button>
                    </div>
                    <div className={'flex items-center gap-1'}>
                      <Chip variant={'light'} className={'opacity-60'}>
                        {tpl.createdAt}
                      </Chip>
                      <Button
                        size={'sm'}
                        isIconOnly
                        variant={'light'}
                        onPress={async () => {
                          const r = await BApi.mediaLibraryTemplate.getMediaLibraryTemplateShareCode(tpl.id);
                          if (!r.code && r.data) {
                            const shareText = r.data;
                            await navigator.clipboard.writeText(shareText);
                            createPortal(Modal, {
                              defaultVisible: true,
                              title: t('Share code has been copied'),
                              children: t('Share code has been copied to your clipboard, you can send it to your friends. (ctrl+v)'),
                              footer: {
                                actions: ['ok'],
                                okProps: {
                                  children: t('I got it'),
                                },
                              },
                            });
                          }
                        }}
                      >
                        <GoShareAndroid className={'text-large'} />
                      </Button>
                      <Button
                        color={'default'}
                        variant={'light'}
                        size={'sm'}
                        isIconOnly
                        onPress={() => {
                          let newName = '';
                          createPortal(
                            Modal, {
                              defaultVisible: true,
                              title: t('Duplicate current template'),
                              // children: (
                              //
                              // ),
                              onOk: async () => {
                                const r = await BApi.mediaLibraryTemplate.duplicateMediaLibraryTemplate(tpl.id);
                                if (!r.code) {
                                  loadTemplates();
                                }
                              },
                            },
                          );
                        }}
                      >
                        <IoDuplicateOutline className={'text-medium'} />
                      </Button>
                      <Button
                        size={'sm'}
                        color={'danger'}
                        isIconOnly
                        variant={'light'}
                        onPress={() => {
                          createPortal(Modal, {
                            defaultVisible: true,
                            title: t('Deleting template {{name}}', { name: tpl.name }),
                            children: t('This action cannot be undone. Are you sure you want to delete this template?'),
                            onOk: async () => {
                              const r = await BApi.mediaLibraryTemplate.deleteMediaLibraryTemplate(tpl.id);
                              if (!r.code) {
                                loadTemplates();
                              }
                            },
                          });
                        }}
                      >
                        <MdDeleteOutline className={'text-large'} />
                      </Button>
                    </div>
                  </div>
                )}
              >
                <div className={'flex flex-col gap-2'}>
                  <Block
                    leftIcon={<IoLocate className={'text-large'} />}
                    title={t('Resource filter')}
                    description={t('Determine which files or folders will be considered as resources')}
                    rightIcon={<AiOutlinePlusCircle className={'text-large'} />}
                    onRightIconPress={() => {
                      createPortal(
                        PathFilterModal, {
                          onSubmit: async f => {
                            (tpl.resourceFilters ??= []).push(f);
                            await putTemplate(tpl);
                            forceUpdate();
                          },
                        },
                      );
                    }}
                  >
                    <div>
                      {tpl.resourceFilters?.map((f, i) => {
                        return (
                          <div className={'flex items-center gap-1'}>
                            <CiFilter className={'text-medium'} />
                            <PathFilterDemonstrator filter={f} />
                            <Button
                              color={'default'}
                              variant={'light'}
                              size={'sm'}
                              isIconOnly
                              onPress={() => {
                                createPortal(
                                  PathFilterModal, {
                                    filter: f,
                                    onSubmit: async nf => {
                                      Object.assign(f, nf);
                                      await putTemplate(tpl);
                                      forceUpdate();
                                    },
                                  },
                                );
                              }}
                            >
                              <AiOutlineEdit className={'text-medium'} />
                            </Button>
                            <Button
                              color={'danger'}
                              variant={'light'}
                              size={'sm'}
                              isIconOnly
                              onPress={() => {
                                createPortal(Modal, {
                                  defaultVisible: true,
                                  title: t('Delete resource filter'),
                                  children: t('Sure to delete?'),
                                  onOk: async () => {
                                    tpl.resourceFilters?.splice(i, 1);
                                    await putTemplate(tpl);
                                    forceUpdate();
                                  },
                                });
                              }}
                            >
                              <AiOutlineDelete className={'text-medium'} />
                            </Button>
                          </div>
                        );
                      })}
                    </div>
                  </Block>
                  <Block
                    title={t('Playable(Runnable) files')}
                    description={t('Determine which files be considered as playable files')}
                    leftIcon={<IoPlayCircleOutline className={'text-large'} />}
                    rightIcon={<AiOutlineEdit className={'text-medium'} />}
                    onRightIconPress={() => {
                      createPortal(
                        PlayableFileSelectorModal, {
                          selection: tpl.playableFileLocator,
                          onSubmit: async selection => {
                            tpl.playableFileLocator = selection;
                            await putTemplate(tpl);
                            forceUpdate();
                          },
                        },
                      );
                    }}
                  >
                    <div className={'flex items-center gap-1'}>
                      {tpl.playableFileLocator?.extensionGroups?.map(group => {
                        return (
                          <Chip size={'sm'} variant={'flat'}>
                            {group?.name}
                          </Chip>
                        );
                      })}
                      {tpl.playableFileLocator?.extensions?.map(ext => {
                        return (
                          <Chip size={'sm'} variant={'flat'}>
                            {ext}
                          </Chip>
                        );
                      })}
                    </div>
                  </Block>
                  <Block
                    title={t('Properties')}
                    description={t('You can configure which properties your resource includes')}
                    leftIcon={<TbDatabase className={'text-large'} />}
                    rightIcon={<AiOutlineEdit className={'text-large'} />}
                    onRightIconPress={() => {
                      createPortal(
                        PropertySelector, {
                          v2: true,
                          selection: tpl.properties,
                          pool: PropertyPool.Reserved | PropertyPool.Custom,
                          editable: true,
                          addable: true,
                          onSubmit: async properties => {
                            tpl.properties = properties.map(p => ({
                              pool: p.pool,
                              id: p.id,
                              property: p,
                              valueLocators: [],
                            }));
                            await putTemplate(tpl);
                            forceUpdate();
                          },
                        },
                      );
                    }}
                  >
                    <div className={'flex flex-col gap-1'}>
                      {tpl.properties?.map((p, i) => {
                        return (
                          <div className={'flex items-center gap-2'}>
                            <BriefProperty property={p.property} />
                            <div className={'flex items-center gap-1'}>
                              {p.valueLocators?.map(v => {
                                return (
                                  <Chip
                                    size={'sm'}
                                    radius={'sm'}
                                    variant={'bordered'}
                                  >
                                    <PathLocatorDemonstrator locator={v} />
                                  </Chip>
                                );
                              })}
                            </div>
                            <div className={'flex items-center gap-1'}>
                              <Button
                                variant={'light'}
                                size={'sm'}
                                isIconOnly
                                onPress={() => {
                                  createPortal(
                                    PathLocatorModal, {
                                      locators: p.valueLocators,
                                      onSubmit: async vls => {
                                        p.valueLocators = vls;
                                        await putTemplate(tpl);
                                        forceUpdate();
                                      },
                                    },
                                  );
                                }}
                              >
                                <IoLocate className={'text-medium'} />
                              </Button>
                              <Button
                                isIconOnly
                                size={'sm'}
                                color={'danger'}
                                variant={'light'}
                                onPress={() => {
                                  createPortal(Modal, {
                                    defaultVisible: true,
                                    title: t('Delete resource filter'),
                                    children: t('Sure to delete?'),
                                    onOk: async () => {
                                      tpl.properties?.splice(i, 1);
                                      await putTemplate(tpl);
                                      forceUpdate();
                                    },
                                  });
                                }}
                              >
                                <AiOutlineDelete className={'text-medium'} />
                              </Button>
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  </Block>
                  <Block
                    title={t('Enhancers')}
                    description={t('You can use enhancers to automatically populate resource information or files')}
                    leftIcon={<IoRocketOutline className={'text-large'} />}
                    rightIcon={<AiOutlineEdit className={'text-large'} />}
                    onRightIconPress={() => {
                      createPortal(
                        EnhancerSelectorModal, {
                          selectedIds: tpl.enhancers?.map(e => e.enhancerId),
                          onSubmit: async ids => {
                            tpl.enhancers = ids.map(id => ({
                              enhancerId: id,
                              options: tpl.enhancers?.find(x => x.enhancerId == id)?.options,
                            }));
                            await putTemplate(tpl);
                            forceUpdate();
                          },
                        },
                      );
                    }}
                  >
                    <div className={'flex flex-col gap-1'}>
                      {tpl.enhancers?.map((e, i) => {
                        const enhancer = enhancers.find(x => x.id == e.enhancerId);
                        if (!enhancer) {
                          return t('Unknown enhancer');
                        }
                        return (
                          <div>
                            <div className={'flex items-center gap-1'}>
                              <BriefEnhancer enhancer={enhancer} />
                              <Button
                                variant={'light'}
                                size={'sm'}
                                isIconOnly
                                onPress={async () => {
                                  createPortal(
                                    EnhancerOptionsModal, {
                                      enhancer,
                                      options: e.options,
                                      onSubmit: async options => {
                                        e.options = options;
                                        await putTemplate(tpl);
                                        forceUpdate();
                                      },
                                    },
                                  );
                                }}
                              >
                                <AiOutlineEdit className={'text-medium'} />
                              </Button>
                              <Button
                                isIconOnly
                                size={'sm'}
                                color={'danger'}
                                variant={'light'}
                                onPress={() => {
                                  createPortal(Modal, {
                                    defaultVisible: true,
                                    title: t('Delete resource filter'),
                                    children: t('Sure to delete?'),
                                    onOk: async () => {
                                      tpl.enhancers?.splice(i, 1);
                                      await putTemplate(tpl);
                                      forceUpdate();
                                    },
                                  });
                                }}
                              >
                                <AiOutlineDelete className={'text-medium'} />
                              </Button>
                            </div>
                            <div>
                              {e.options?.targetOptions ? (
                                <div className={'flex flex-col gap-1'}>
                                  {enhancer?.targets?.map(target => {
                                    const tos = e.options?.targetOptions?.filter(x => x.target == target.id);
                                    if (!tos || tos.length == 0) {
                                      return null;
                                    }
                                    if (target.isDynamic) {
                                      const mainOptions = tos.find(x => x.dynamicTarget == undefined);
                                      const otherOptions = tos.filter(x => x != mainOptions);
                                      return (
                                        <>
                                          <div
                                            className={'flex items-center gap-1'}
                                          >
                                            <Chip
                                              size={'sm'}
                                              radius={'sm'}
                                              variant={'flat'}
                                            >
                                              {target.name}
                                            </Chip>
                                            {target.description && (
                                              <Tooltip content={target.description}>
                                                <QuestionCircleOutlined className={'text-medium'} />
                                              </Tooltip>
                                            )}
                                            {mainOptions?.autoBindProperty && t('Auto bind property')}
                                            {mainOptions?.autoMatchMultilevelString && t('Auto match multilevel string')}
                                          </div>
                                          {otherOptions.map(to => {
                                            const property = (to.propertyPool != undefined && to.propertyId != undefined)
                                              ? propertyMap[to.propertyPool]?.[to.propertyId] : undefined;
                                            return (
                                              <div className={'flex items-center gap-1'}>
                                                <AiOutlineSisternode className={'text-medium'} />
                                                <Chip
                                                  size={'sm'}
                                                  radius={'sm'}
                                                  variant={'flat'}
                                                >
                                                  {to.dynamicTarget}
                                                </Chip>
                                                <TiChevronRightOutline className={'text-medium'} />
                                                {to.autoBindProperty ? t('Auto bind property') : property ? (
                                                  <BriefProperty property={property} />
                                                ) : t('Unknown property')}
                                                {to.autoMatchMultilevelString && t('Auto match multilevel string')}
                                              </div>
                                            );
                                          })}
                                        </>
                                      );
                                    } else {
                                      const to = tos[0]!;
                                      const property = (to.propertyPool != undefined && to.propertyId != undefined)
                                        ? propertyMap[to.propertyPool]?.[to.propertyId] : undefined;
                                      return (
                                        <div className={'flex items-center gap-1'}>
                                          {target.description ? (
                                            <Tooltip
                                              content={target.description}
                                            >
                                              <Chip
                                                size={'sm'}
                                                radius={'sm'}
                                                variant={'flat'}
                                              >
                                                {target.name}
                                              </Chip>
                                            </Tooltip>
                                          ) : (
                                            <Chip
                                              size={'sm'}
                                              radius={'sm'}
                                              variant={'flat'}
                                            >
                                              {target.name}
                                            </Chip>
                                          )}
                                          <TiChevronRightOutline className={'text-medium'} />
                                          {to.autoBindProperty ? t('Auto bind property') : property ? (
                                            <BriefProperty property={property} />
                                          ) : t('Unknown property')}
                                          {to.autoMatchMultilevelString && t('Auto match multilevel string')}
                                        </div>
                                      );
                                    }
                                  })}
                                </div>
                              ) : t('Not configured')}
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  </Block>
                  <Block
                    title={t('Display name template')}
                    description={t('You can customize the display name of the resource')}
                    leftIcon={<MdOutlineSubtitles className={'text-large'} />}
                    rightIcon={<AiOutlineEdit className={'text-large'} />}
                    onRightIconPress={() => {
                      createPortal(
                        DisplayNameTemplateEditorModal, {
                          properties: tpl.properties?.map(p => p.property!) ?? [],
                          template: tpl.displayNameTemplate,
                          onSubmit: async template => {
                            tpl.displayNameTemplate = template;
                            await putTemplate(tpl);
                            forceUpdate();
                          },
                        },
                      );
                    }}
                  >
                    {tpl.displayNameTemplate}
                  </Block>
                  <Block
                    title={t('Subresource')}
                    description={t('You can create cascading resources through sub-templates, where the rules of the sub-template will use the path of the resource determined by the current template as the root directory.')}
                    descriptionPlacement={'bottom'}
                    leftIcon={<TiFlowChildren className={'text-large'} />}
                    // rightIcon={<AiOutlineEdit className={'text-large'} />}
                    // onRightIconPress={() => {
                    //   createPortal(
                    //     DisplayNameTemplateEditorModal, {
                    //       properties: tpl.properties?.map(p => p.property!) ?? [],
                    //       template: tpl.displayNameTemplate,
                    //       onSubmit: async template => {
                    //         tpl.displayNameTemplate = template;
                    //         await putTemplate(tpl);
                    //         forceUpdate();
                    //       },
                    //     },
                    //   );
                    // }}
                  >
                    {renderChildSelector(tpl)}
                  </Block>
                  {/* <Block */}
                  {/*   title={t('Preview')} */}
                  {/*   icon={<AiOutlineEdit className={'text-medium'} />} */}
                  {/*   onIconPress={() => { */}
                  {/*     let { samplePaths } = tpl; */}
                  {/*     createPortal( */}
                  {/*       Modal, { */}
                  {/*         defaultVisible: true, */}
                  {/*         title: t('Setup paths for preview'), */}
                  {/*         children: ( */}
                  {/*           <div> */}
                  {/*             <div className={'opacity-60'}> */}
                  {/*               <div>{t('To create a preview, you must enter at least one path that will be applied to the current template. For a better preview effect, you can add as many representative subpaths as possible starting from the second line.')}</div> */}
                  {/*               <div>{t('If you have set up an enhancer to retrieve data from third parties, an excessive number of subpaths may slow down the preview creation process.')}</div> */}
                  {/*             </div> */}
                  {/*             <div> */}
                  {/*               <Textarea */}
                  {/*                 defaultValue={samplePaths?.join('\n')} */}
                  {/*                 onValueChange={v => samplePaths = v.split('\n')} */}
                  {/*                 placeholder={t('Paths separated by line')} */}
                  {/*                 fullWidth */}
                  {/*                 isMultiline */}
                  {/*               /> */}
                  {/*             </div> */}
                  {/*           </div> */}
                  {/*         ), */}
                  {/*         size: 'lg', */}
                  {/*         onOk: () => { */}
                  {/*           tpl.samplePaths = samplePaths; */}
                  {/*           forceUpdate(); */}
                  {/*         }, */}
                  {/*       }, */}
                  {/*     ); */}
                  {/*   }} */}
                  {/* > */}
                  {/*   {(!tpl.samplePaths || tpl.samplePaths.length == 0) ? ( */}
                  {/*     <div>{t('This template cannot be previewed because no sample path has been configured.')}</div> */}
                  {/*   ) : ( */}
                  {/*     <div> */}
                  {/*       <Button */}
                  {/*         size={'sm'} */}
                  {/*         isIconOnly */}
                  {/*         variant={'light'} */}
                  {/*         color={'secondary'} */}
                  {/*       > */}
                  {/*         <AiOutlineSync className={'text-medium'} /> */}
                  {/*       </Button> */}
                  {/*     </div> */}
                  {/*   )} */}
                  {/* </Block> */}
                </div>
              </AccordionItem>
            );
          })}
        </Accordion>
        {/* <div className={'select-text'}>{JSON.stringify(templates)}</div> */}
      </div>
    </div>
  );
};
