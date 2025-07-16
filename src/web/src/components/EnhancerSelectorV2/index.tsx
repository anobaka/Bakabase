"use client";

import type { CategoryEnhancerFullOptions } from "./components/CategoryEnhancerOptionsDialog/models";
import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { DeleteOutlined, FireOutlined, MoreOutlined } from "@ant-design/icons";

import BetaChip from "../Chips/BetaChip";

import {
  Button,
  Checkbox,
  Divider,
  Listbox,
  ListboxItem,
  Modal,
  Popover,
  Tooltip,
} from "@/components/bakaui";
import { createPortalOfComponent } from "@/components/utils";
import BApi from "@/sdk/BApi";
import { StandardValueIcon } from "@/components/StandardValue";
import {
  CategoryAdditionalItem,
  EnhancerId,
  StandardValueType,
} from "@/sdk/constants";
import CategoryEnhancerOptionsDialog from "@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { EnhancerIcon, EnhancerTargetNotSetupTip } from "@/components/Enhancer";
import DeleteEnhancementsModal from "@/pages/category/components/DeleteEnhancementsModal";

interface IProps extends DestroyableProps {
  categoryId: number;
  onClose?: () => any;
}

type Category = {
  id: number;
  name: string;
};

const EnhancerSelector = ({ categoryId, onDestroyed, onClose }: IProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [enhancers, setEnhancers] = useState<EnhancerDescriptor[]>([]);
  const [category, setCategory] = useState<Category>();
  const [categoryEnhancerOptionsList, setCategoryEnhancerOptionsList] =
    useState<CategoryEnhancerFullOptions[]>([]);

  const init = async () => {
    await BApi.enhancer.getAllEnhancerDescriptors().then((r) => {
      const data = r.data || [];

      // @ts-ignore
      setEnhancers(data);
    });

    // @ts-ignore
    await BApi.category
      .getCategory(categoryId, {
        additionalItems:
          CategoryAdditionalItem.EnhancerOptions |
          CategoryAdditionalItem.CustomProperties,
      })
      .then((r) => {
        const data = r.data!;

        setCategory({
          id: data.id!,
          name: data.name!,
        });
        setCategoryEnhancerOptionsList(
          data.enhancerOptions?.map(
            (eo) => eo as CategoryEnhancerFullOptions,
          ) || [],
        );
      });
  };

  useEffect(() => {
    init();
  }, []);

  // console.log(createPortal, 1234567);

  const patchCategoryEnhancerOptions = (
    enhancerId: number,
    patches: Partial<CategoryEnhancerFullOptions>,
  ) => {
    let ceoIdx = categoryEnhancerOptionsList.findIndex(
      (x) => x.enhancerId == enhancerId,
    );

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
      defaultVisible
      footer={{
        actions: ["cancel"],
      }}
      size={"xl"}
      title={t<string>("Enhancers")}
      onClose={onClose}
      onDestroyed={onDestroyed}
    >
      <div
        className={"grid gap-4 flex-wrap justify-center justify-items-center"}
        style={{
          gridTemplateColumns: "repeat(auto-fill, minmax(300px, 300px)",
        }}
      >
        {enhancers.map((e) => {
          const ceo = categoryEnhancerOptionsList.find(
            (x) => x.enhancerId == e.id,
          );

          return (
            <div
              key={e.id}
              className={
                "max-w-[280px] border-1 rounded-lg pl-3 pr-3 pt-2 pb-2 flex flex-col"
              }
              style={{ borderColor: "rgba(255, 255, 255, 0.3)" }}
            >
              <div className={"text-base font-bold flex items-center gap-2"}>
                <EnhancerIcon id={e.id} />
                {e.name}
                {e.id == EnhancerId.Kodi && <BetaChip />}
              </div>
              {e.description && (
                <div className={"opacity-60 grow break-all"}>
                  {e.description}
                </div>
              )}
              <Divider />
              <div className={"mt-2 mb-2 grow"}>
                <div className={" italic"}>
                  {t<string>(
                    "This enhancer can produce the following property values",
                  )}
                </div>
                <div className={"flex flex-wrap gap-x-3 gap-y-1 mt-1"}>
                  {e.targets.map((target) => {
                    return (
                      <Tooltip
                        key={target.id}
                        content={
                          <div>
                            <div>{target.description}</div>
                            <div>
                              {t<string>("The value type of this target is")}
                              &nbsp;
                              <span className={"font-bold"}>
                                <StandardValueIcon
                                  className={"text-small"}
                                  valueType={target.valueType}
                                />
                                &nbsp;
                                {t<string>(
                                  `StandardValueType.${StandardValueType[target.valueType]}`,
                                )}
                              </span>
                            </div>
                          </div>
                        }
                      >
                        <div
                          className={"flex items-center gap-1"}
                          style={
                            {
                              // color: 'var(--bakaui-primary)'
                            }
                          }
                        >
                          <StandardValueIcon
                            className={"text-small"}
                            valueType={target.valueType}
                          />
                          <span className={"break-all"}>{target.name}</span>
                        </div>
                      </Tooltip>
                    );
                  })}
                </div>
              </div>
              <Divider />
              <div className={"flex items-center justify-between gap-1 mt-1"}>
                <div className="flex items-center gap-1 mt-1">
                  <Popover
                    placement={"top"}
                    trigger={
                      <Button isIconOnly size={"sm"} variant={"light"}>
                        <MoreOutlined />
                      </Button>
                    }
                  >
                    <Listbox
                      aria-label="Delete enhancement records"
                      onAction={(key) => {
                        let title: string;
                        let callApi: (() => Promise<any>) | undefined;

                        switch (key) {
                          case "Category":
                            createPortal(DeleteEnhancementsModal, {
                              title: t<string>(
                                "Delete all enhancement records of this enhancer for category {{categoryName}}",
                                { categoryName: category!.name },
                              ),
                              onOk: async (deleteEmptyOnly) =>
                                await BApi.category.deleteEnhancementsByCategoryAndEnhancer(
                                  category!.id,
                                  e.id,
                                  { deleteEmptyOnly },
                                ),
                            });
                            break;
                          case "All":
                            createPortal(DeleteEnhancementsModal, {
                              title: t<string>(
                                "Delete all enhancement records of this enhancer",
                              ),
                              onOk: async (deleteEmptyOnly) =>
                                await BApi.enhancer.deleteEnhancementsByEnhancer(
                                  e!.id,
                                  { deleteEmptyOnly },
                                ),
                            });
                            break;
                          case "ApplyContext":
                            createPortal(Modal, {
                              defaultVisible: true,
                              title: t<string>(
                                "Re-apply all enhancement data of this enhancer for category {{categoryName}}",
                                { categoryName: category?.name },
                              ),
                              onOk: async () =>
                                await BApi.category.applyEnhancementContextDataByEnhancerAndCategory(
                                  category!.id,
                                  e.id,
                                ),
                            });
                            break;
                          default:
                            return;
                        }
                      }}
                    >
                      <ListboxItem
                        key={"Category"}
                        className={"text-danger"}
                        color={"danger"}
                        startContent={
                          <DeleteOutlined className={"text-base"} />
                        }
                      >
                        {t<string>(
                          "Delete all enhancement records of this enhancer for category {{categoryName}}",
                          { categoryName: category?.name },
                        )}
                      </ListboxItem>
                      <ListboxItem
                        key={"All"}
                        className={"text-danger"}
                        color={"danger"}
                        startContent={
                          <DeleteOutlined className={"text-base"} />
                        }
                      >
                        {t<string>(
                          "Delete all enhancement records of this enhancer",
                        )}
                      </ListboxItem>
                      <ListboxItem
                        key={"ApplyContext"}
                        className={"text-warning"}
                        color={"warning"}
                        startContent={<FireOutlined className={"text-base"} />}
                      >
                        {t<string>(
                          "Re-apply all enhancement data of this enhancer for category {{categoryName}}",
                          { categoryName: category?.name },
                        )}
                      </ListboxItem>
                    </Listbox>
                  </Popover>
                </div>
                <div className="flex items-center gap-1 mt-1">
                  <EnhancerTargetNotSetupTip enhancer={e} options={ceo} />
                  <Button
                    color={"primary"}
                    size={"sm"}
                    variant={"light"}
                    onClick={() => {
                      createPortal(CategoryEnhancerOptionsDialog, {
                        enhancer: e,
                        categoryId,
                        onDestroyed: init,
                      });
                    }}
                  >
                    {t<string>("Setup")}
                  </Button>
                  <Checkbox
                    isSelected={ceo?.active}
                    size={"sm"}
                    onValueChange={(c) => {
                      BApi.category
                        .patchCategoryEnhancerOptions(categoryId, e.id, {
                          active: c,
                        })
                        .then(() => {
                          patchCategoryEnhancerOptions(e.id, { active: c });
                        });
                    }}
                  >
                    {t<string>("Enable")}
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

EnhancerSelector.show = (props: IProps) =>
  createPortalOfComponent(EnhancerSelector, props);

export default EnhancerSelector;
