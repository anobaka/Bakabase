"use client";

import type { MediaLibraryTemplate } from "../models";
import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";
import type { PropertyMap } from "@/components/types";

import {
  IoLocate,
  IoPlayCircleOutline,
  IoRocketOutline,
} from "react-icons/io5";
import {
  AiOutlineDelete,
  AiOutlineEdit,
  AiOutlinePlusCircle,
  AiOutlineSisternode,
} from "react-icons/ai";
import { CiFilter } from "react-icons/ci";
import { TbDatabase } from "react-icons/tb";
import { QuestionCircleOutlined } from "@ant-design/icons";
import { TiChevronRightOutline, TiFlowChildren } from "react-icons/ti";
import { MdOutlineSubtitles } from "react-icons/md";
import React, { useEffect } from "react";
import { useTranslation } from "react-i18next";
import { useUpdate, useUpdateEffect } from "react-use";
import toast from "react-hot-toast";
import _ from "lodash";

import Block from "@/pages/media-library-template/components/Block";
import {
  PathFilterDemonstrator,
  PathFilterModal,
} from "@/pages/media-library-template/components/PathFilter";
import { Button, Chip, Modal, Select, Tooltip } from "@/components/bakaui";
import PlayableFileSelectorModal from "@/pages/media-library-template/components/PlayableFileSelectorModal";
import PropertySelector from "@/components/PropertySelector";
import { PropertyPool } from "@/sdk/constants";
import BriefProperty from "@/components/Chips/Property/BriefProperty";
import {
  PathPropertyExtractorDemonstrator,
  PathPropertyExtractorModal,
} from "@/pages/media-library-template/components/PathPropertyExtractor";
import EnhancerSelectorModal from "@/pages/media-library-template/components/EnhancerSelectorModal";
import BriefEnhancer from "@/components/Chips/Enhancer/BriefEnhancer";
import EnhancerOptionsModal from "@/components/EnhancerSelectorV2/components/EnhancerOptionsModal";
import DisplayNameTemplateEditorModal from "@/pages/media-library-template/components/DisplayNameTemplateEditorModal";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";
import { willCauseCircleReference } from "@/components/utils";

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

  const [enhancers, setEnhancers] = React.useState<EnhancerDescriptor[]>(
    enhancersCache ?? [],
  );
  const [propertyMap, setPropertyMap] = React.useState<PropertyMap>(
    propertyMapCache ?? {},
  );
  const [templates, setTemplates] = React.useState<MediaLibraryTemplate[]>(
    templatesCache ?? [],
  );

  useEffect(() => {
    if (!enhancersCache) {
      BApi.enhancer.getAllEnhancerDescriptors().then((r) => {
        setEnhancers(r.data ?? []);
      });
    }
    if (!propertyMapCache) {
      loadProperties();
    }
    if (!templatesCache) {
      BApi.mediaLibraryTemplate.getAllMediaLibraryTemplates().then((r) => {
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
    const psr =
      (await BApi.property.getPropertiesByPool(PropertyPool.All)).data || [];
    const ps = _.mapValues(
      _.groupBy(psr, (x) => x.pool),
      (v) => _.keyBy(v, (x) => x.id),
    );

    setPropertyMap(ps);
  };

  const putTemplate = async (tpl: MediaLibraryTemplate) => {
    const r = await BApi.mediaLibraryTemplate.putMediaLibraryTemplate(
      tpl.id,
      tpl,
    );

    if (!r.code) {
      toast.success(t<string>("Saved successfully"));
      onChange?.();
      forceUpdate();
    }
  };

  const renderChildSelector = (tpl: MediaLibraryTemplate) => {
    const willCauseLoopKeys = new Set<string>(
      templates
        .filter((t1) => {
          return willCauseCircleReference(
            tpl,
            t1.id,
            templates,
            (x) => x.id,
            (x) => x.childTemplateId,
            (x, k) => (x.childTemplateId = k),
          );
        })
        .map((x) => x.id.toString()),
    );

    console.log(
      tpl.childTemplateId,
      tpl.childTemplateId ? [tpl.childTemplateId.toString()] : undefined,
    );

    return (
      <div className={"inline-flex items-center gap-2"}>
        <Select
          className={"min-w-[320px]"}
          dataSource={templates.map((t1) => {
            const hasLoop = willCauseLoopKeys.has(t1.id.toString());

            return {
              label: `[#${t1.id}] ${t1.name}${hasLoop ? ` (${t<string>("Loop detected")})` : ""}`,
              value: t1.id.toString(),
            };
          })}
          disabledKeys={willCauseLoopKeys}
          fullWidth={false}
          label={`${t<string>("Child template")} (${t<string>("optional")})`}
          placeholder={t<string>("Select a child template")}
          selectedKeys={
            tpl.childTemplateId ? [tpl.childTemplateId.toString()] : []
          }
          size={"sm"}
          onSelectionChange={(keys) => {
            const id = parseInt(Array.from(keys)[0] as string, 10);

            tpl.childTemplateId = id;
            putTemplate(tpl);
          }}
        />
        {tpl.childTemplateId && (
          <Button
            isIconOnly
            color={"danger"}
            // size={'sm'}
            variant={"light"}
            onPress={() => {
              tpl.childTemplateId = undefined;
              tpl.child = undefined;
              putTemplate(tpl);
            }}
          >
            <AiOutlineDelete className={"text-base"} />
          </Button>
        )}
      </div>
    );
  };

  return (
    <div className={"flex flex-col gap-2"}>
      <Block
        description={t<string>(
          "Determine which files or folders will be considered as resources",
        )}
        leftIcon={<IoLocate className={"text-large"} />}
        rightIcon={<AiOutlinePlusCircle className={"text-large"} />}
        title={t<string>("Resource filter")}
        onRightIconPress={() => {
          createPortal(PathFilterModal, {
            onSubmit: async (f) => {
              (template.resourceFilters ??= []).push(f);
              await putTemplate(template);
              forceUpdate();
            },
          });
        }}
      >
        <div>
          {template.resourceFilters?.map((f, i) => {
            return (
              <div className={"flex items-center gap-1"}>
                <CiFilter className={"text-base"} />
                <PathFilterDemonstrator filter={f} />
                <Button
                  isIconOnly
                  color={"default"}
                  size={"sm"}
                  variant={"light"}
                  onPress={() => {
                    createPortal(PathFilterModal, {
                      filter: f,
                      onSubmit: async (nf) => {
                        Object.assign(f, nf);
                        await putTemplate(template);
                        forceUpdate();
                      },
                    });
                  }}
                >
                  <AiOutlineEdit className={"text-base"} />
                </Button>
                <Button
                  isIconOnly
                  color={"danger"}
                  size={"sm"}
                  variant={"light"}
                  onPress={() => {
                    createPortal(Modal, {
                      defaultVisible: true,
                      title: t<string>("Delete resource filter"),
                      children: t<string>("Sure to delete?"),
                      onOk: async () => {
                        template.resourceFilters?.splice(i, 1);
                        await putTemplate(template);
                        forceUpdate();
                      },
                    });
                  }}
                >
                  <AiOutlineDelete className={"text-base"} />
                </Button>
              </div>
            );
          })}
        </div>
      </Block>
      <Block
        description={t<string>(
          "Determine which files be considered as playable files",
        )}
        leftIcon={<IoPlayCircleOutline className={"text-large"} />}
        rightIcon={<AiOutlineEdit className={"text-base"} />}
        title={t<string>("Playable(Runnable) files")}
        onRightIconPress={() => {
          createPortal(PlayableFileSelectorModal, {
            selection: template.playableFileLocator,
            onSubmit: async (selection) => {
              template.playableFileLocator = selection;
              await putTemplate(template);
              forceUpdate();
            },
          });
        }}
      >
        <div className={"flex items-center gap-1"}>
          {template.playableFileLocator?.extensionGroups?.map((group) => {
            return (
              <Chip radius={"sm"} size={"sm"} variant={"flat"}>
                {group?.name}
              </Chip>
            );
          })}
          {template.playableFileLocator?.extensions?.map((ext) => {
            return (
              <Chip size={"sm"} variant={"flat"}>
                {ext}
              </Chip>
            );
          })}
          {template.playableFileLocator?.maxFileCount && (
            <Chip color={"warning"} size={"sm"} variant={"flat"}>
              {t<string>("Max: {{count}}", {
                count: template.playableFileLocator.maxFileCount,
              })}
            </Chip>
          )}
        </div>
      </Block>
      <Block
        description={t<string>(
          "You can configure which properties your resource includes",
        )}
        leftIcon={<TbDatabase className={"text-large"} />}
        rightIcon={<AiOutlineEdit className={"text-large"} />}
        title={t<string>("Properties")}
        onRightIconPress={() => {
          createPortal(PropertySelector, {
            v2: true,
            selection: template.properties,
            pool: PropertyPool.Reserved | PropertyPool.Custom,
            editable: true,
            addable: true,
            onSubmit: async (properties) => {
              template.properties = properties.map((p) => ({
                pool: p.pool,
                id: p.id,
                property: p,
                valueLocators: [],
              }));
              await putTemplate(template);
              forceUpdate();
            },
          });
        }}
      >
        <div className={"flex flex-col gap-1"}>
          {template.properties?.map((p, i) => {
            return (
              <div className={"flex items-center gap-2"}>
                <BriefProperty
                  fields={["name", "pool", "type"]}
                  property={p.property}
                />
                <div className={"flex items-center gap-1"}>
                  {p.valueLocators?.map((v) => {
                    return (
                      <Chip radius={"sm"} size={"sm"} variant={"bordered"}>
                        <PathPropertyExtractorDemonstrator locator={v} />
                      </Chip>
                    );
                  })}
                </div>
                <div className={"flex items-center gap-1"}>
                  <Button
                    isIconOnly
                    size={"sm"}
                    variant={"light"}
                    onPress={() => {
                      createPortal(PathPropertyExtractorModal, {
                        locators: p.valueLocators,
                        onSubmit: async (vls) => {
                          p.valueLocators = vls;
                          await putTemplate(template);
                          forceUpdate();
                        },
                      });
                    }}
                  >
                    <IoLocate className={"text-base"} />
                  </Button>
                  <Button
                    isIconOnly
                    color={"danger"}
                    size={"sm"}
                    variant={"light"}
                    onPress={() => {
                      createPortal(Modal, {
                        defaultVisible: true,
                        title: t<string>("Delete resource filter"),
                        children: t<string>("Sure to delete?"),
                        onOk: async () => {
                          template.properties?.splice(i, 1);
                          await putTemplate(template);
                          forceUpdate();
                        },
                      });
                    }}
                  >
                    <AiOutlineDelete className={"text-base"} />
                  </Button>
                </div>
              </div>
            );
          })}
        </div>
      </Block>
      <Block
        description={t<string>(
          "You can use enhancers to automatically populate resource information or files",
        )}
        leftIcon={<IoRocketOutline className={"text-large"} />}
        rightIcon={<AiOutlineEdit className={"text-large"} />}
        title={t<string>("Enhancers")}
        onRightIconPress={() => {
          createPortal(EnhancerSelectorModal, {
            selectedIds: template.enhancers?.map((e) => e.enhancerId),
            onSubmit: async (ids) => {
              template.enhancers = ids.map((id) => ({
                enhancerId: id,
                options: template.enhancers?.find((x) => x.enhancerId == id),
              }));
              await putTemplate(template);
              forceUpdate();
            },
          });
        }}
      >
        <div className={"flex flex-col gap-1"}>
          {template.enhancers?.map((e, i) => {
            const enhancer = enhancers.find((x) => x.id == e.enhancerId);

            if (!enhancer) {
              return t<string>("Unknown enhancer");
            }

            return (
              <div>
                <div className={"flex items-center gap-1"}>
                  <BriefEnhancer enhancer={enhancer} />
                  <Button
                    isIconOnly
                    size={"sm"}
                    variant={"light"}
                    onPress={async () => {
                      createPortal(EnhancerOptionsModal, {
                        enhancer,
                        options: e,
                        onSubmit: async (options) => {
                          Object.assign(e, options);
                          await putTemplate(template);
                          forceUpdate();
                        },
                      });
                    }}
                  >
                    <AiOutlineEdit className={"text-base"} />
                  </Button>
                  <Button
                    isIconOnly
                    color={"danger"}
                    size={"sm"}
                    variant={"light"}
                    onPress={() => {
                      createPortal(Modal, {
                        defaultVisible: true,
                        title: t<string>("Delete resource filter"),
                        children: t<string>("Sure to delete?"),
                        onOk: async () => {
                          template.enhancers?.splice(i, 1);
                          await putTemplate(template);
                          forceUpdate();
                        },
                      });
                    }}
                  >
                    <AiOutlineDelete className={"text-base"} />
                  </Button>
                </div>
                {e.expressions && e.expressions.length > 0 && (
                  <div>
                    <Chip radius={"sm"} size={"sm"} variant={"flat"}>
                      {t<string>("Expressions")}
                    </Chip>
                    {e.expressions.map((x) => (
                      <div>{x}</div>
                    ))}
                  </div>
                )}
                <div>
                  {e.targetOptions ? (
                    <div className={"flex flex-col gap-1"}>
                      {enhancer?.targets?.map((target) => {
                        const tos = e.targetOptions!.filter(
                          (x) => x.target == target.id,
                        );

                        if (!tos || tos.length == 0) {
                          return null;
                        }
                        if (target.isDynamic) {
                          const mainOptions = tos.find(
                            (x) => x.dynamicTarget == undefined,
                          );
                          const otherOptions = tos.filter(
                            (x) => x != mainOptions,
                          );

                          return (
                            <>
                              <div className={"flex items-center gap-1"}>
                                <Chip
                                  radius={"sm"}
                                  size={"sm"}
                                  variant={"flat"}
                                >
                                  {target.name}
                                </Chip>
                                {target.description && (
                                  <Tooltip content={target.description}>
                                    <QuestionCircleOutlined
                                      className={"text-base"}
                                    />
                                  </Tooltip>
                                )}
                                {mainOptions?.autoBindProperty &&
                                  t<string>("Auto bind property")}
                                {mainOptions?.autoMatchMultilevelString &&
                                  t<string>("Auto match multilevel string")}
                              </div>
                              {otherOptions.map((to) => {
                                const property =
                                  to.propertyPool != undefined &&
                                  to.propertyId != undefined
                                    ? propertyMap[to.propertyPool]?.[
                                        to.propertyId
                                      ]
                                    : undefined;

                                return (
                                  <div className={"flex items-center gap-1"}>
                                    <AiOutlineSisternode
                                      className={"text-base"}
                                    />
                                    <Chip
                                      radius={"sm"}
                                      size={"sm"}
                                      variant={"flat"}
                                    >
                                      {to.dynamicTarget}
                                    </Chip>
                                    <TiChevronRightOutline
                                      className={"text-base"}
                                    />
                                    {to.autoBindProperty ? (
                                      t<string>("Auto bind property")
                                    ) : property ? (
                                      <BriefProperty property={property} />
                                    ) : (
                                      t<string>("Unknown property")
                                    )}
                                    {to.autoMatchMultilevelString &&
                                      t<string>("Auto match multilevel string")}
                                  </div>
                                );
                              })}
                            </>
                          );
                        } else {
                          const to = tos[0]!;
                          const property =
                            to.propertyPool != undefined &&
                            to.propertyId != undefined
                              ? propertyMap[to.propertyPool]?.[to.propertyId]
                              : undefined;

                          return (
                            <div className={"flex items-center gap-1"}>
                              {target.description ? (
                                <Tooltip content={target.description}>
                                  <Chip
                                    radius={"sm"}
                                    size={"sm"}
                                    variant={"flat"}
                                  >
                                    {target.name}
                                  </Chip>
                                </Tooltip>
                              ) : (
                                <Chip
                                  radius={"sm"}
                                  size={"sm"}
                                  variant={"flat"}
                                >
                                  {target.name}
                                </Chip>
                              )}
                              <TiChevronRightOutline
                                className={"text-base"}
                              />
                              {to.autoBindProperty ? (
                                t<string>("Auto bind property")
                              ) : property ? (
                                <BriefProperty property={property} />
                              ) : (
                                t<string>("Unknown property")
                              )}
                              {to.autoMatchMultilevelString &&
                                t<string>("Auto match multilevel string")}
                            </div>
                          );
                        }
                      })}
                    </div>
                  ) : (
                    t<string>("Not configured")
                  )}
                </div>
              </div>
            );
          })}
        </div>
      </Block>
      <Block
        description={t<string>(
          "You can customize the display name of the resource",
        )}
        leftIcon={<MdOutlineSubtitles className={"text-large"} />}
        rightIcon={<AiOutlineEdit className={"text-large"} />}
        title={t<string>("Display name template")}
        onRightIconPress={() => {
          createPortal(DisplayNameTemplateEditorModal, {
            properties: template.properties?.map((p) => p.property!) ?? [],
            template: template.displayNameTemplate,
            onSubmit: async (templateStr) => {
              template.displayNameTemplate = templateStr;
              await putTemplate(template);
              forceUpdate();
            },
          });
        }}
      >
        {template.displayNameTemplate}
      </Block>
      <Block
        description={t<string>('You can create cascading resources through sub-templates, where the rules of the sub-template will use the path of the resource determined by the current template as the root directory.')}
        descriptionPlacement={'bottom'}
        title={t<string>('Subresource')}
        leftIcon={<TiFlowChildren className={"text-large"} />}
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
      {/*   title={t<string>('Preview')} */}
      {/*   icon={<AiOutlineEdit className={'text-base'} />} */}
      {/*   onIconPress={() => { */}
      {/*     let { samplePaths } = tpl; */}
      {/*     createPortal( */}
      {/*       Modal, { */}
      {/*         defaultVisible: true, */}
      {/*         title: t<string>('Setup paths for preview'), */}
      {/*         children: ( */}
      {/*           <div> */}
      {/*             <div className={'opacity-60'}> */}
      {/*               <div>{t<string>('To create a preview, you must enter at least one path that will be applied to the current template. For a better preview effect, you can add as many representative subpaths as possible starting from the second line.')}</div> */}
      {/*               <div>{t<string>('If you have set up an enhancer to retrieve data from third parties, an excessive number of subpaths may slow down the preview creation process.')}</div> */}
      {/*             </div> */}
      {/*             <div> */}
      {/*               <Textarea */}
      {/*                 defaultValue={samplePaths?.join('\n')} */}
      {/*                 onValueChange={v => samplePaths = v.split('\n')} */}
      {/*                 placeholder={t<string>('Paths separated by line')} */}
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
      {/*     <div>{t<string>('This template cannot be previewed because no sample path has been configured.')}</div> */}
      {/*   ) : ( */}
      {/*     <div> */}
      {/*       <Button */}
      {/*         size={'sm'} */}
      {/*         isIconOnly */}
      {/*         variant={'light'} */}
      {/*         color={'secondary'} */}
      {/*       > */}
      {/*         <AiOutlineSync className={'text-base'} /> */}
      {/*       </Button> */}
      {/*     </div> */}
      {/*   )} */}
      {/* </Block> */}
    </div>
  );
};
