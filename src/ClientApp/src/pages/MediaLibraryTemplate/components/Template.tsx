import { IoLocate, IoPlayCircleOutline, IoRocketOutline } from 'react-icons/io5';
import { AiOutlineDelete, AiOutlineEdit, AiOutlinePlusCircle, AiOutlineSisternode } from 'react-icons/ai';
import { CiFilter } from 'react-icons/ci';
import { TbDatabase } from 'react-icons/tb';
import { QuestionCircleOutlined } from '@ant-design/icons';
import { TiChevronRightOutline, TiFlowChildren } from 'react-icons/ti';
import { MdOutlineSubtitles } from 'react-icons/md';
import React, { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdate, useUpdateEffect } from 'react-use';
import toast from 'react-hot-toast';
import _ from 'lodash';
import type { MediaLibraryTemplate } from '../models';
import Block from '@/pages/MediaLibraryTemplate/components/Block';
import { PathFilterDemonstrator, PathFilterModal } from '@/pages/MediaLibraryTemplate/components/PathFilter';
import { Button, Chip, Modal, Select, Tooltip } from '@/components/bakaui';
import PlayableFileSelectorModal from '@/pages/MediaLibraryTemplate/components/PlayableFileSelectorModal';
import PropertySelector from '@/components/PropertySelector';
import { PropertyPool } from '@/sdk/constants';
import BriefProperty from '@/components/Chips/Property/BriefProperty';
import { PathLocatorDemonstrator, PathLocatorModal } from '@/pages/MediaLibraryTemplate/components/PathLocator';
import EnhancerSelectorModal from '@/pages/MediaLibraryTemplate/components/EnhancerSelectorModal';
import BriefEnhancer from '@/components/Chips/Enhancer/BriefEnhancer';
import EnhancerOptionsModal from '@/components/EnhancerSelectorV2/components/EnhancerOptionsModal';
import DisplayNameTemplateEditorModal from '@/pages/MediaLibraryTemplate/components/DisplayNameTemplateEditorModal';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import BApi from '@/sdk/BApi';
import type { EnhancerDescriptor } from '@/components/EnhancerSelectorV2/models';
import type { PropertyMap } from '@/components/types';
import { willCauseCircleReference } from '@/components/utils';

type Props = {
  template: MediaLibraryTemplate;
  onChange?: () => any;
  enhancersCache?: EnhancerDescriptor[];
  propertyMapCache?: PropertyMap;
  templatesCache?: MediaLibraryTemplate[];
};

export default ({
                  template,
                  onChange,
                  enhancersCache,
                  propertyMapCache,
                  templatesCache,
                }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const forceUpdate = useUpdate();

  const [enhancers, setEnhancers] = React.useState<EnhancerDescriptor[]>(enhancersCache ?? []);
  const [propertyMap, setPropertyMap] = React.useState<PropertyMap>(propertyMapCache ?? {});
  const [templates, setTemplates] = React.useState<MediaLibraryTemplate[]>(templatesCache ?? []);

  useEffect(() => {
    if (!enhancersCache) {
      BApi.enhancer.getAllEnhancerDescriptors().then(r => {
        setEnhancers(r.data ?? []);
      });
    }
    if (!propertyMapCache) {
      loadProperties();
    }
    if (!templatesCache) {
      BApi.mediaLibraryTemplate.getAllMediaLibraryTemplates().then(r => {
        setTemplates(r.data ?? []);
      });
    }
  }, []);

  useUpdateEffect(() => {
    setEnhancers(enhancersCache ?? []);
  }, [enhancersCache]);

  useUpdateEffect(() => {
    setPropertyMap(propertyMapCache ?? {});
  }, [propertyMapCache]);

  useUpdateEffect(() => {
    setTemplates(templatesCache ?? []);
  }, [templatesCache]);

  const loadProperties = async () => {
    const psr = (await BApi.property.getPropertiesByPool(PropertyPool.All)).data || [];
    const ps = _.mapValues(_.groupBy(psr, x => x.pool), (v) => _.keyBy(v, x => x.id));
    setPropertyMap(ps);
  };

  const putTemplate = async (tpl: MediaLibraryTemplate) => {
    const r = await BApi.mediaLibraryTemplate.putMediaLibraryTemplate(tpl.id, tpl);
    if (!r.code) {
      toast.success(t('Saved successfully'));
      onChange?.();
      forceUpdate();
    }
  };

  const renderChildSelector = (tpl: MediaLibraryTemplate) => {
    const willCauseLoopKeys = new Set<string>(
      templates.filter(t1 => {
        return willCauseCircleReference(tpl, t1.id, templates,
          x => x.id,
          x => x.childTemplateId,
          (x, k) => x.childTemplateId = k,
        );
      }).map(x => x.id.toString()),
    );

    console.log(tpl.childTemplateId, tpl.childTemplateId ? [tpl.childTemplateId.toString()] : undefined);

    return (
      <div className={'inline-flex items-center gap-2'}>
        <Select
          className={'min-w-[320px]'}
          label={`${t('Child template')} (${t('optional')})`}
          placeholder={t('Select a child template')}
          size={'sm'}
          selectedKeys={tpl.childTemplateId ? [tpl.childTemplateId.toString()] : []}
          fullWidth={false}
          disabledKeys={willCauseLoopKeys}
          dataSource={templates.map(t1 => {
            const hasLoop = willCauseLoopKeys.has(t1.id.toString());
            return {
              label: `[#${t1.id}] ${t1.name}${hasLoop ? ` (${t('Loop detected')})` : ''}`,
              value: t1.id.toString(),
            };
          })}
          onSelectionChange={keys => {
            const id = parseInt(Array.from(keys)[0] as string, 10);
            tpl.childTemplateId = id;
            putTemplate(tpl);
          }}
        />
        {tpl.childTemplateId && (
          <Button
            isIconOnly
            color={'danger'}
            // size={'sm'}
            variant={'light'}
            onPress={() => {
              tpl.childTemplateId = undefined;
              tpl.child = undefined;
              putTemplate(tpl);
            }}
          >
            <AiOutlineDelete className={'text-base'} />
          </Button>
        )}
      </div>);
  };

  return (
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
                (template.resourceFilters ??= []).push(f);
                await putTemplate(template);
                forceUpdate();
              },
            },
          );
        }}
      >
        <div>
          {template.resourceFilters?.map((f, i) => {
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
                          await putTemplate(template);
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
                        template.resourceFilters?.splice(i, 1);
                        await putTemplate(template);
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
              selection: template.playableFileLocator,
              onSubmit: async selection => {
                template.playableFileLocator = selection;
                await putTemplate(template);
                forceUpdate();
              },
            },
          );
        }}
      >
        <div className={'flex items-center gap-1'}>
          {template.playableFileLocator?.extensionGroups?.map(group => {
            return (
              <Chip radius={'sm'} size={'sm'} variant={'flat'}>
                {group?.name}
              </Chip>
            );
          })}
          {template.playableFileLocator?.extensions?.map(ext => {
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
              selection: template.properties,
              pool: PropertyPool.Reserved | PropertyPool.Custom,
              editable: true,
              addable: true,
              onSubmit: async properties => {
                template.properties = properties.map(p => ({
                  pool: p.pool,
                  id: p.id,
                  property: p,
                  valueLocators: [],
                }));
                await putTemplate(template);
                forceUpdate();
              },
            },
          );
        }}
      >
        <div className={'flex flex-col gap-1'}>
          {template.properties?.map((p, i) => {
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
                            await putTemplate(template);
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
                          template.properties?.splice(i, 1);
                          await putTemplate(template);
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
              selectedIds: template.enhancers?.map(e => e.enhancerId),
              onSubmit: async ids => {
                template.enhancers = ids.map(id => ({
                  enhancerId: id,
                  options: template.enhancers?.find(x => x.enhancerId == id),
                }));
                await putTemplate(template);
                forceUpdate();
              },
            },
          );
        }}
      >
        <div className={'flex flex-col gap-1'}>
          {template.enhancers?.map((e, i) => {
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
                          options: e,
                          onSubmit: async options => {
                            Object.assign(e, options);
                            await putTemplate(template);
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
                          template.enhancers?.splice(i, 1);
                          await putTemplate(template);
                          forceUpdate();
                        },
                      });
                    }}
                  >
                    <AiOutlineDelete className={'text-medium'} />
                  </Button>
                </div>
                {(e.expressions && e.expressions.length > 0) && (
                  <div>
                    <Chip
                      size={'sm'}
                      radius={'sm'}
                      variant={'flat'}
                    >
                      {t('Expressions')}
                    </Chip>
                    {e.expressions.map(x => (
                      <div>{x}</div>
                    ))}
                  </div>
                )}
                <div>
                  {e.targetOptions ? (
                    <div className={'flex flex-col gap-1'}>
                      {enhancer?.targets?.map(target => {
                        const tos = e.targetOptions!.filter(x => x.target == target.id);
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
              properties: template.properties?.map(p => p.property!) ?? [],
              template: template.displayNameTemplate,
              onSubmit: async templateStr => {
                template.displayNameTemplate = templateStr;
                await putTemplate(template);
                forceUpdate();
              },
            },
          );
        }}
      >
        {template.displayNameTemplate}
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
        {renderChildSelector(template)}
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
  );
};
