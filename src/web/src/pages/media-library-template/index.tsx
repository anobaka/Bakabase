"use client";

import type { MediaLibraryTemplatePage } from "./models";
import type { PropertyMap } from "@/components/types";
import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";

import { useTranslation } from "react-i18next";
import React, { useEffect, useState } from "react";
import { ImportOutlined, QuestionCircleOutlined } from "@ant-design/icons";
import {
  AiOutlineEdit,
  AiOutlinePlusCircle,
  AiOutlineSearch,
} from "react-icons/ai";
import { useUpdate } from "react-use";
import _ from "lodash";
import { IoDuplicateOutline } from "react-icons/io5";
import { GoShareAndroid } from "react-icons/go";
import { MdDeleteOutline } from "react-icons/md";
import toast from "react-hot-toast";
import { PiEmpty } from "react-icons/pi";
import { BiCollapseVertical, BiExpandVertical } from "react-icons/bi";

import Template from "./components/Template";

import {
  Accordion,
  AccordionItem,
  Button,
  Chip,
  Input,
  Modal,
  Textarea,
  Tooltip,
  Autocomplete,
  AutocompleteItem,
} from "@/components/bakaui";
import { PropertyPool } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";
import ImportModal from "@/pages/media-library-template/components/ImportModal";
import AddModal from "@/pages/media-library-template/components/AddModal";

type SearchForm = {
  keyword?: string;
};
const MediaLibraryTemplatePage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const forceUpdate = useUpdate();

  const [templates, setTemplates] = useState<MediaLibraryTemplatePage[]>([]);
  const [enhancers, setEnhancers] = useState<EnhancerDescriptor[]>([]);
  const [propertyMap, setPropertyMap] = useState<PropertyMap>({});
  const [searchForm, setSearchForm] = useState<SearchForm>({});
  const [expandedTemplateIds, setExpandedTemplateIds] = useState<string[]>([]);

  const loadProperties = async () => {
    const psr =
      (await BApi.property.getPropertiesByPool(PropertyPool.All)).data || [];
    const ps = _.mapValues(
      _.groupBy(psr, (x) => x.pool),
      (v) => _.keyBy(v, (x) => x.id),
    );

    setPropertyMap(ps);
  };

  const loadTemplates = async () => {
    const r = await BApi.mediaLibraryTemplate.getAllMediaLibraryTemplates();
    const templates = r.data || [];

    if (!r.code) {
      setTemplates(templates);

      return templates;
    }

    return [];
  };

  // load one template
  const loadTemplate = async (id: number) => {
    const r = await BApi.mediaLibraryTemplate.getMediaLibraryTemplate(id);

    if (!r.code) {
      setTemplates(templates.map((t) => (t.id == r.data?.id ? r.data : t)));
    }
  };

  useEffect(() => {
    // createPortal(
    //   PathFilterModal, {
    //
    //   },
    // );
    BApi.enhancer.getAllEnhancerDescriptors().then((r) => {
      setEnhancers(r.data || []);
    });
    loadProperties();
    loadTemplates().then((tpls) =>
      setExpandedTemplateIds(tpls.map((tpl) => tpl.id.toString())),
    );
  }, []);

  const putTemplate = async (tpl: MediaLibraryTemplatePage) => {
    const r = await BApi.mediaLibraryTemplate.putMediaLibraryTemplate(
      tpl.id,
      tpl,
    );

    if (!r.code) {
      toast.success(t<string>("Saved successfully"));
      await loadTemplate(tpl.id);
    }
  };

  const filteredTemplates = templates.filter((tpl) => {
    if (!searchForm.keyword || searchForm.keyword.length == 0) {
      return true;
    }
    const keyword = searchForm.keyword.toLowerCase();

    return (
      tpl.name.toLowerCase().includes(keyword) ||
      (tpl.author && tpl.author.toLowerCase().includes(keyword)) ||
      (tpl.description && tpl.description.toLowerCase().includes(keyword))
    );
  });

  return (
    <div className={"h-full flex flex-col"}>
      <div className={"flex items-center gap-2 justify-between"}>
        <div className={"flex items-center gap-2"}>
          <Button
            color={"primary"}
            size={"sm"}
            onPress={() =>
              createPortal(AddModal, { onDestroyed: loadTemplates })
            }
          >
            <AiOutlinePlusCircle className={"text-base"} />
            {t<string>("Create a template")}
          </Button>
          <div>
            <Autocomplete
              endContent={
                filteredTemplates.length == templates.length
                  ? filteredTemplates.length
                  : `${filteredTemplates.length}/${templates.length}`
              }
              fullWidth={false}
              inputValue={searchForm.keyword}
              placeholder={t<string>("Search templates")}
              size={"sm"}
              startContent={<AiOutlineSearch className={"text-base"} />}
              onInputChange={(keyword) =>
                setSearchForm({
                  ...searchForm,
                  keyword,
                })
              }
            >
              {templates?.map((tpl) => (
                <AutocompleteItem key={tpl.id}>{tpl.name}</AutocompleteItem>
              ))}
            </Autocomplete>
          </div>
          {templates.length > 0 ? (
            expandedTemplateIds.length < templates.length ? (
              <Button
                color={"default"}
                size={"sm"}
                onPress={() =>
                  setExpandedTemplateIds(
                    templates.map((tpl) => tpl.id.toString()),
                  )
                }
              >
                <BiExpandVertical className={"text-base"} />
                {t<string>("Expand all")}
              </Button>
            ) : (
              <Button
                color={"default"}
                size={"sm"}
                onPress={() => setExpandedTemplateIds([])}
              >
                <BiCollapseVertical className={"text-base"} />
                {t<string>("Collapse all")}
              </Button>
            )
          ) : null}
        </div>
        <div>
          <Button
            color={"default"}
            size={"sm"}
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
            {t<string>("Import a template")}
          </Button>
        </div>
      </div>
      <div className={"mt-2 grow"}>
        {templates.length == 0 ? (
          <div
            className={"flex items-center gap-2 w-full h-full justify-center"}
          >
            <PiEmpty className={"text-2xl"} />
            {t<string>(
              "No templates found. You can create a template or import one from a share code.",
            )}
          </div>
        ) : (
          <Accordion
            selectedKeys={expandedTemplateIds}
            variant="splitted"
            onSelectionChange={(keys) => {
              const ids = Array.from(keys).map((k) => k as string);

              setExpandedTemplateIds(ids);
            }}
          >
            {filteredTemplates.map((tpl) => {
              return (
                <AccordionItem
                  key={tpl.id}
                  id={tpl.id.toString()}
                  title={
                    <div className={"flex items-center justify-between"}>
                      <div className={"flex items-center gap-1"}>
                        <Chip className={""} size={"sm"} variant={"flat"}>
                          #{tpl.id}
                        </Chip>
                        <div className={"font-bold"}>{tpl.name}</div>
                        {tpl.author && (
                          <div className={"opacity-60"}>{tpl.author}</div>
                        )}
                        {tpl.description && (
                          <Tooltip
                            content={tpl.description}
                            placement={"bottom"}
                          >
                            <QuestionCircleOutlined className={"text-base"} />
                          </Tooltip>
                        )}
                        <Button
                          isIconOnly
                          size={"sm"}
                          variant={"light"}
                          onPress={() => {
                            let { name, author, description } = tpl;

                            createPortal(Modal, {
                              defaultVisible: true,
                              title: t<string>("Edit template"),
                              children: (
                                <div className={"flex flex-col gap-2"}>
                                  <Input
                                    isRequired
                                    defaultValue={name}
                                    label={t<string>("Name")}
                                    placeholder={t<string>(
                                      "Enter template name",
                                    )}
                                    onValueChange={(v) => {
                                      name = v;
                                    }}
                                  />
                                  <Input
                                    defaultValue={author}
                                    label={`${t<string>("Author")}${t<string>("(optional)")}`}
                                    placeholder={t<string>("Enter author name")}
                                    onValueChange={(v) => {
                                      author = v;
                                    }}
                                  />
                                  <Textarea
                                    defaultValue={description}
                                    label={`${t<string>("Description")}${t<string>("(optional)")}`}
                                    placeholder={t<string>("Enter description")}
                                    onValueChange={(v) => {
                                      description = v;
                                    }}
                                  />
                                </div>
                              ),
                              size: "lg",
                              onOk: async () => {
                                tpl.name = name;
                                tpl.author = author;
                                tpl.description = description;
                                await putTemplate(tpl);
                                forceUpdate();
                              },
                            });
                          }}
                        >
                          <AiOutlineEdit className={"text-base"} />
                        </Button>
                      </div>
                      <div className={"flex items-center gap-1"}>
                        <Chip className={"opacity-60"} variant={"light"}>
                          {tpl.createdAt}
                        </Chip>
                        <Button
                          isIconOnly
                          size={"sm"}
                          variant={"light"}
                          onPress={async () => {
                            const r =
                              await BApi.mediaLibraryTemplate.getMediaLibraryTemplateShareCode(
                                tpl.id,
                              );

                            if (!r.code && r.data) {
                              const shareText = r.data;

                              await navigator.clipboard.writeText(shareText);
                              createPortal(Modal, {
                                defaultVisible: true,
                                title: t<string>("Share code has been copied"),
                                children: t<string>(
                                  "Share code has been copied to your clipboard, you can send it to your friends. (ctrl+v)",
                                ),
                                footer: {
                                  actions: ["ok"],
                                  okProps: {
                                    children: t<string>("I got it"),
                                  },
                                },
                              });
                            }
                          }}
                        >
                          <GoShareAndroid className={"text-large"} />
                        </Button>
                        <Button
                          isIconOnly
                          color={"default"}
                          size={"sm"}
                          variant={"light"}
                          onPress={() => {
                            let newName = "";

                            createPortal(Modal, {
                              defaultVisible: true,
                              title: t<string>("Duplicate current template"),
                              // children: (
                              //
                              // ),
                              onOk: async () => {
                                const r =
                                  await BApi.mediaLibraryTemplate.duplicateMediaLibraryTemplate(
                                    tpl.id,
                                  );

                                if (!r.code) {
                                  loadTemplates();
                                }
                              },
                            });
                          }}
                        >
                          <IoDuplicateOutline className={"text-base"} />
                        </Button>
                        <Button
                          isIconOnly
                          color={"danger"}
                          size={"sm"}
                          variant={"light"}
                          onPress={() => {
                            createPortal(Modal, {
                              defaultVisible: true,
                              title: t<string>("Deleting template {{name}}", {
                                name: tpl.name,
                              }),
                              children: t<string>(
                                "This action cannot be undone. Are you sure you want to delete this template?",
                              ),
                              onOk: async () => {
                                const r =
                                  await BApi.mediaLibraryTemplate.deleteMediaLibraryTemplate(
                                    tpl.id,
                                  );

                                if (!r.code) {
                                  loadTemplates();
                                }
                              },
                            });
                          }}
                        >
                          <MdDeleteOutline className={"text-large"} />
                        </Button>
                      </div>
                    </div>
                  }
                >
                  {propertyMap && enhancers && templates && (
                    <Template
                      enhancersCache={enhancers}
                      propertyMapCache={propertyMap}
                      template={tpl}
                      templatesCache={templates}
                      onChange={async () => {
                        await loadTemplate(tpl.id);
                      }}
                    />
                  )}
                </AccordionItem>
              );
            })}
          </Accordion>
          // <div className={'select-text'}>{JSON.stringify(templates)}</div>
        )}
      </div>
    </div>
  );
};

MediaLibraryTemplatePage.displayName = "MediaLibraryTemplatePage";

export default MediaLibraryTemplatePage;
