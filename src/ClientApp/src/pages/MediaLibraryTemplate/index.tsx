import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { ImportOutlined, QuestionCircleOutlined } from '@ant-design/icons';
import {
  AiOutlineDelete,
  AiOutlineEdit,
  AiOutlinePlusCircle,
  AiOutlineQuestionCircle,
  AiOutlineSisternode, AiOutlineSync,
} from 'react-icons/ai';
import { useUpdate } from 'react-use';
import _ from 'lodash';
import { IoLocate } from 'react-icons/io5';
import { CardHeader } from '@heroui/react';
import { CiFilter } from 'react-icons/ci';
import { FaRightLong } from 'react-icons/fa6';
import { TiChevronRightOutline } from 'react-icons/ti';
import type { PathFilter, PathLocator } from './models';
import { PathFilterFsType } from './models';
import { PathFilterDemonstrator, PathFilterModal } from './components/PathFilter';
import testData from './data.json';
import Block from './components/Block';
import {
  Accordion,
  AccordionItem,
  Button,
  Card,
  CardBody, Chip, Divider, Icon,
  Input,
  Modal,
  Popover,
  Select,
  Textarea, Tooltip,
} from '@/components/bakaui';
import { PathPositioner, PropertyPool, PropertyType } from '@/sdk/constants';
import type { IProperty } from '@/components/Property/models';
import { PropertyTypeIconMap } from '@/components/Property/models';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import type { IdName, PropertyMap } from '@/components/types';
import PropertySelector from '@/components/PropertySelector';
import Property from '@/components/Property';
import BApi from '@/sdk/BApi';
import { PathLocatorDemonstrator, PathLocatorModal } from '@/pages/MediaLibraryTemplate/components/PathLocator';
import EnhancerSelectorV2 from '@/components/EnhancerSelectorV2';
import type { EnhancerDescriptor } from '@/components/EnhancerSelectorV2/models';
import EnhancerSelectorModal from '@/pages/MediaLibraryTemplate/components/EnhancerSelectorModal';
import EnhancerOptionsModal from '@/components/EnhancerSelectorV2/components/EnhancerOptionsModal';
import type {
  EnhancerFullOptions,
} from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models';
import PlayableFileSelectorModal from '@/pages/MediaLibraryTemplate/components/PlayableFileSelectorModal';
import { EnhancerIcon } from '@/components/Enhancer';
import DisplayNameTemplateEditorModal from '@/pages/MediaLibraryTemplate/components/DisplayNameTemplateEditorModal';


enum FileExtensionGroup {
  Image = 1,
  Audio,
  Video,
  Document,
  Application,
  Archive,
}

type MediaLibraryTemplate = {
  id: number;
  name: string;
  resourceFilters?: PathFilter[];
  properties?: MediaLibraryTemplateProperty[];
  playableFileLocator?: MediaLibraryTemplatePlayableFileLocator;
  enhancers?: MediaLibraryTemplateEnhancerOptions[];
  displayNameTemplate?: string;
  samplePaths?: string[];
};


type MediaLibraryTemplateProperty = {
  pool: PropertyPool;
  id: number;
  property: IProperty;
  valueLocators?: PathLocator[];
};

type MediaLibraryTemplatePlayableFileLocator = {
  extensionGroupIds?: number[];
  extensions?: string[];
};

type MediaLibraryTemplateEnhancerOptions = {
  enhancerId: number;
  options?: EnhancerFullOptions;
};

const testFileExtensionsGroups: IdName[] = [
  {
    id: 1,
    name: 'Video files',
  },
  {
    id: 2,
    name: 'Audio files',
  },
  {
    id: 3,
    name: 'Image files',
  },
];


export default () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const forceUpdate = useUpdate();

  const [templates, setTemplates] = useState<MediaLibraryTemplate[]>(testData);
  const [fileExtensionsGroups, setFileExtensionsGroups] = useState<IdName[]>(testFileExtensionsGroups);
  const [enhancers, setEnhancers] = useState<EnhancerDescriptor[]>([]);
  const [propertyMap, setPropertyMap] = useState<PropertyMap>({});


  const loadProperties = async () => {
    const psr = (await BApi.property.getPropertiesByPool(PropertyPool.All)).data || [];
    const ps = _.mapValues(_.groupBy(psr, x => x.pool), (v) => _.keyBy(v, x => x.id));
    setPropertyMap(ps);
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
  }, []);

  return (
    <div>
      <div className={'flex items-center gap-2 justify-between'}>
        <Button
          size={'sm'}
          color={'primary'}
        >{t('Create a template')}</Button>
        <Button
          size={'sm'}
          color={'default'}
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
                  <div className={'font-bold'}>{tpl.name}</div>
                )}
              >
                <div className={'flex flex-col gap-2'}>
                  <Block
                    title={t('Resource filter')}
                    description={t('Determine which files or folders will be considered as resources')}
                    icon={<AiOutlinePlusCircle className={'text-medium'} />}
                    onIconPress={() => {
                      createPortal(
                        PathFilterModal, {
                          fileExtensionGroups: fileExtensionsGroups,
                          onSubmit: f => {
                            (tpl.resourceFilters ??= []).push(f);
                            // todo: update api
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
                                    fileExtensionGroups: fileExtensionsGroups,
                                    filter: f,
                                    onSubmit: nf => {
                                      Object.assign(f, nf);
                                      // todo: update api
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
                                  onOk: () => {
                                    tpl.resourceFilters?.splice(i, 1);
                                    // todo: update api
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
                    icon={<AiOutlineEdit className={'text-medium'} />}
                    onIconPress={() => {
                      createPortal(
                        PlayableFileSelectorModal, {
                          selection: tpl.playableFileLocator,
                          onSubmit: selection => {
                            tpl.playableFileLocator = selection;
                            forceUpdate();
                          },
                        },
                      );
                    }}
                  >
                    <div className={'flex items-center gap-1'}>
                      {tpl.playableFileLocator?.extensionGroupIds?.map(id => {
                        const group = fileExtensionsGroups.find(g => g.id == id);
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
                    icon={<AiOutlineEdit className={'text-medium'} />}
                    onIconPress={() => {
                      createPortal(
                        PropertySelector, {
                          selection: tpl.properties,
                          pool: PropertyPool.Reserved | PropertyPool.Custom,
                          onSubmit: async properties => {
                            tpl.properties = properties.map(p => ({
                              pool: p.pool,
                              id: p.id,
                              property: p,
                              valueLocators: [],
                            }));
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
                            <Chip
                              variant={'flat'}
                              size={'sm'}
                              radius={'sm'}
                            >
                              {t(`${PropertyPool[p.pool]} property`)}
                            </Chip>
                            {p.property.name}
                            <div className={'flex items-center'}>
                              <Icon type={PropertyTypeIconMap[p.property.type]!} className={'text-medium'} />
                              {p.property.typeName}
                            </div>
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
                                      onSubmit: vls => {
                                        p.valueLocators = vls;
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
                                    onOk: () => {
                                      tpl.properties?.splice(i, 1);
                                      // todo: update api
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
                    icon={<AiOutlineEdit className={'text-medium'} />}
                    onIconPress={() => {
                      createPortal(
                        EnhancerSelectorModal, {
                          selectedIds: tpl.enhancers?.map(e => e.enhancerId),
                          onSubmit: ids => {
                            tpl.enhancers = ids.map(id => ({
                              enhancerId: id,
                              options: tpl.enhancers?.find(x => x.enhancerId == id)?.options,
                            }));
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
                              <div className={'flex items-center gap-1'}>
                                <EnhancerIcon id={enhancer.id} />
                                {enhancer.name}
                              </div>
                              <Button
                                variant={'light'}
                                size={'sm'}
                                isIconOnly
                                onPress={() => {
                                  createPortal(
                                    EnhancerOptionsModal, {
                                      enhancer,
                                      options: e.options,
                                      onSubmit: options => {
                                        console.log(options);
                                        e.options = options;
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
                                    onOk: () => {
                                      tpl.enhancers?.splice(i, 1);
                                      // todo: update api
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
                                                  <div className={'flex items-center gap-1'}>
                                                    <Chip
                                                      variant={'flat'}
                                                      size={'sm'}
                                                      radius={'sm'}
                                                    >
                                                      {t(`${PropertyPool[property.pool]} property`)}
                                                    </Chip>
                                                    {property.name}
                                                    <div className={'flex items-center'}>
                                                      <Icon
                                                        type={PropertyTypeIconMap[property.type]!}
                                                        className={'text-medium'}
                                                      />
                                                      {property.typeName}
                                                    </div>
                                                  </div>
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
                                          <Chip
                                            size={'sm'}
                                            radius={'sm'}
                                            variant={'flat'}
                                          >
                                            {target.name}
                                          </Chip>
                                          {target.description && (
                                            <Popover
                                              trigger={<AiOutlineQuestionCircle className={'text-medium'} />}
                                            >
                                              {target.description}
                                            </Popover>
                                          )}
                                          <TiChevronRightOutline className={'text-medium'} />
                                          {to.autoBindProperty ? t('Auto bind property') : property ? (
                                            <div className={'flex items-center gap-1'}>
                                              <Chip
                                                variant={'flat'}
                                                size={'sm'}
                                                radius={'sm'}
                                              >
                                                {t(`${PropertyPool[property.pool]} property`)}
                                              </Chip>
                                              {property.name}
                                              <div className={'flex items-center'}>
                                                <Icon
                                                  type={PropertyTypeIconMap[property.type]!}
                                                  className={'text-medium'}
                                                />
                                                {property.typeName}
                                              </div>
                                            </div>
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
                    icon={<AiOutlineEdit className={'text-medium'} />}
                    onIconPress={() => {
                      createPortal(
                        DisplayNameTemplateEditorModal, {
                          properties: tpl.properties?.map(p => p.property),
                          template: tpl.displayNameTemplate,
                          onSubmit: template => {
                            tpl.displayNameTemplate = template;
                            forceUpdate();
                          },
                        },
                      );
                    }}
                  >
                    {tpl.displayNameTemplate}
                  </Block>
                  <Block
                    title={t('Preview')}
                    icon={<AiOutlineEdit className={'text-medium'} />}
                    onIconPress={() => {
                      let { samplePaths } = tpl;
                      createPortal(
                        Modal, {
                          defaultVisible: true,
                          title: t('Setup paths for preview'),
                          children: (
                            <div>
                              <div className={'opacity-60'}>
                                <div>{t('To create a preview, you must enter at least one path that will be applied to the current template. For a better preview effect, you can add as many representative subpaths as possible starting from the second line.')}</div>
                                <div>{t('If you have set up an enhancer to retrieve data from third parties, an excessive number of subpaths may slow down the preview creation process.')}</div>
                              </div>
                              <div>
                                <Textarea
                                  defaultValue={samplePaths?.join('\n')}
                                  onValueChange={v => samplePaths = v.split('\n')}
                                  placeholder={t('Paths separated by line')}
                                  fullWidth
                                  isMultiline
                                />
                              </div>
                            </div>
                          ),
                          size: 'lg',
                          onOk: () => {
                            tpl.samplePaths = samplePaths;
                            forceUpdate();
                          },
                        },
                      );
                    }}
                  >
                    {(!tpl.samplePaths || tpl.samplePaths.length == 0) ? (
                      <div>{t('This template cannot be previewed because no sample path has been configured.')}</div>
                    ) : (
                      <div>
                        <Button
                          size={'sm'}
                          isIconOnly
                          variant={'light'}
                          color={'secondary'}
                        >
                          <AiOutlineSync className={'text-medium'} />
                        </Button>
                      </div>
                    )}
                  </Block>
                </div>
              </AccordionItem>
            );
          })}
        </Accordion>
        <div className={'select-text'}>{JSON.stringify(templates)}</div>
      </div>
    </div>
  );
};
