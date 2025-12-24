"use client";

import type { MediaLibrary } from "./models";
import type { InputProps } from "@heroui/react";
import type { ButtonProps } from "@/components/bakaui";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { useUpdate } from "react-use";
import { AiOutlinePlusCircle, AiOutlineProduct, AiOutlineSearch } from "react-icons/ai";
import { MdOutlineDelete } from "react-icons/md";
import { HiOutlineCollection } from "react-icons/hi";
import { IoSettingsOutline } from "react-icons/io5";
import { useNavigate } from "react-router-dom";

import {
  Button,
  Card,
  CardBody,
  Input,
  Modal,
  Tooltip,
  ColorPicker,
  toast,
} from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";
import { InternalProperty, PropertyPool, SearchOperation } from "@/sdk/constants";
import { buildColorValueString } from "@/components/bakaui/components/ColorPicker";
import { EditableValue } from "@/components/EditableValue";
import { MediaLibraryTerm } from "@/components/Chips/Terms";

const MediaLibraryPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const navigate = useNavigate();

  const [mediaLibraries, setMediaLibraries] = useState<MediaLibrary[]>([]);
  const forceUpdate = useUpdate();

  const loadMediaLibraries = async (): Promise<MediaLibrary[]> => {
    const r = await BApi.mediaLibraryV2.getAllMediaLibraryV2();
    const list = r.data ?? [];

    setMediaLibraries(list);

    return list;
  };

  useEffect(() => {
    loadMediaLibraries();
  }, []);

  const addMediaLibrary = async () => {
    const numbers = mediaLibraries
      .map((ml) => {
        const match = ml.name.match(/(\d+)$/);
        return match ? parseInt(match[1], 10) : 0;
      })
      .filter((n) => !isNaN(n));
    const no = numbers.length > 0 ? Math.max(...numbers) + 1 : 1;
    const name = `${t("Media library")} ${no}`;

    await BApi.mediaLibraryV2.addMediaLibraryV2({
      name,
      paths: [],
    });
    await loadMediaLibraries();
  };

  const deleteMediaLibrary = (ml: MediaLibrary) => {
    createPortal(Modal, {
      defaultVisible: true,
      title: t("MediaLibrary.Confirm"),
      children: (
        <div>
          {t("MediaLibrary.DeleteConfirm")}
          <br />
          <span className="text-danger">{t("Be careful, this operation can not be undone")}</span>
        </div>
      ),
      onOk: async () => {
        await BApi.mediaLibraryV2.deleteMediaLibraryV2(ml.id);
        loadMediaLibraries();
      },
      footer: {
        actions: ["ok", "cancel"],
        okProps: {
          children: t("Delete"),
          color: "danger",
          autoFocus: true,
        },
        cancelProps: {
          children: t("MediaLibrary.Cancel"),
        },
      },
    });
  };

  const searchResources = (ml: MediaLibrary) => {
    createPortal(Modal, {
      title: t("MediaLibrary.Confirm"),
      children: t("MediaLibrary.LeavePageConfirm"),
      defaultVisible: true,
      onOk: async () => {
        const valuePropertyResponse = await BApi.resource.getFilterValueProperty({
          propertyPool: PropertyPool.Internal,
          propertyId: InternalProperty.MediaLibraryV2,
          operation: SearchOperation.Equals,
        });
        const searchForm = {
          group: {
            combinator: 1,
            disabled: false,
            filters: [
              {
                propertyPool: PropertyPool.Internal,
                propertyId: InternalProperty.MediaLibraryV2,
                operation: SearchOperation.Equals,
                dbValue: ml.id.toString(),
                bizValue: ml.name,
                valueProperty: valuePropertyResponse.data,
                disabled: false,
              },
            ],
          },
          page: 1,
          pageSize: 100,
        };
        const query = encodeURIComponent(JSON.stringify(searchForm));

        navigate(`/resource?query=${query}`);
      },
      footer: {
        actions: ["ok", "cancel"],
        okProps: {
          children: t("MediaLibrary.Continue"),
        },
        cancelProps: {
          children: t("MediaLibrary.Cancel"),
        },
      },
    });
  };

  const renderEmptyState = () => {
    return (
      <div className="flex flex-col items-center justify-center grow py-16 px-8">
        <div className="flex flex-col items-center gap-6 max-w-lg text-center">
          {/* Icon */}
          <div className="w-20 h-20 rounded-full bg-secondary/10 flex items-center justify-center">
            <HiOutlineCollection className="text-4xl text-secondary" />
          </div>

          {/* Title with MediaLibraryTerm */}
          <div className="space-y-2">
            <h2 className="text-xl font-semibold flex items-center justify-center gap-2">
              {t("MediaLibrary.EmptyState.Title")}
            </h2>
            <div className="text-default-500 text-sm leading-relaxed">
              {t("MediaLibrary.EmptyState.Description")}
            </div>
          </div>

          {/* Feature highlights */}
          <div className="w-full bg-default-100 rounded-lg p-4 text-left space-y-3">
            <div className="flex items-start gap-3">
              <div className="w-6 h-6 rounded bg-secondary/20 flex items-center justify-center flex-shrink-0 mt-0.5">
                <span className="text-secondary text-xs font-bold">1</span>
              </div>
              <div className="text-sm text-default-600">
                {t("MediaLibrary.EmptyState.Feature1")}
              </div>
            </div>
            <div className="flex items-start gap-3">
              <div className="w-6 h-6 rounded bg-secondary/20 flex items-center justify-center flex-shrink-0 mt-0.5">
                <span className="text-secondary text-xs font-bold">2</span>
              </div>
              <div className="text-sm text-default-600">
                {t("MediaLibrary.EmptyState.Feature2")}
              </div>
            </div>
            <div className="flex items-start gap-3">
              <div className="w-6 h-6 rounded bg-secondary/20 flex items-center justify-center flex-shrink-0 mt-0.5">
                <span className="text-secondary text-xs font-bold">3</span>
              </div>
              <div className="text-sm text-default-600">
                {t("MediaLibrary.EmptyState.Feature3")}
              </div>
            </div>
          </div>

          {/* Actions */}
          <div className="flex flex-col items-center gap-3 w-full">
            <Button color="primary" size="md" startContent={<AiOutlinePlusCircle className="text-lg" />} onPress={addMediaLibrary}>
              {t("MediaLibrary.CreateFirst")}
            </Button>
            <Button
              color="default"
              size="sm"
              startContent={<IoSettingsOutline className="text-base" />}
              variant="light"
              onPress={() => navigate("/path-mark-config")}
            >
              {t("MediaLibrary.GoToPathMarkConfig")}
            </Button>
          </div>
        </div>
      </div>
    );
  };

  const renderMediaLibraryCard = (ml: MediaLibrary) => {
    const getCssVariable = (variableName: string) => {
      if (typeof document === "undefined") return "";

      return getComputedStyle(document.documentElement).getPropertyValue(variableName);
    };
    const textColor = getCssVariable("--theme-text") || "#222";
    const libraryColor = ml.color ?? textColor;

    return (
      <Card
        key={ml.id}
        className="group hover:shadow-md transition-shadow"
        shadow="sm"
      >
        <CardBody className="p-4">
          <div className="flex items-center justify-between gap-3">
            {/* Left: Color indicator + Name */}
            <div className="flex items-center gap-3 min-w-0 flex-1">
              {/* Color indicator bar */}
              <div
                className="w-1 h-10 rounded-full flex-shrink-0"
                style={{ backgroundColor: libraryColor }}
              />

              {/* Name (editable) */}
              <EditableValue<string, InputProps, ButtonProps & { value: string }>
                Editor={Input}
                Viewer={({ value, ...props }) => (
                  <Button
                    className="whitespace-break-spaces h-auto text-left font-medium text-base px-2 min-w-0"
                    size="sm"
                    style={{ color: libraryColor }}
                    variant="light"
                    {...props}
                  >
                    <span className="truncate">{value}</span>
                  </Button>
                )}
                editorProps={{
                  size: "sm",
                  isRequired: true,
                }}
                trigger="viewer"
                value={ml.name}
                onSubmit={async (v) => {
                  if (v == undefined || v.length == 0) {
                    toast.danger(t("Name cannot be empty"));

                    return;
                  }
                  const rsp = await BApi.mediaLibraryV2.patchMediaLibraryV2(ml.id, {
                    name: v,
                  });

                  if (!rsp.code) {
                    ml.name = v;
                    forceUpdate();
                  }
                }}
              />

              {/* Color picker */}
              <ColorPicker
                color={libraryColor}
                onChange={async (color) => {
                  const strColor = buildColorValueString(color);

                  await BApi.mediaLibraryV2.putMediaLibraryV2(ml.id, {
                    ...ml,
                    color: strColor,
                  });
                  ml.color = strColor;
                  forceUpdate();
                }}
              />
            </div>

            {/* Right: Stats + Actions */}
            <div className="flex items-center gap-2 flex-shrink-0">
              {/* Resource count */}
              <Tooltip
                content={
                  ml.resourceCount > 0
                    ? t("{{count}} resource(s) loaded in this media library.", {
                        count: ml.resourceCount,
                      })
                    : t("No resource loaded in this media library, you can click the synchronize button to load resources.")
                }
                placement="top"
              >
                <div className="flex items-center gap-1 text-default-500 text-sm px-2 py-1 rounded bg-default-100">
                  <AiOutlineProduct className="text-base" />
                  <span>{ml.resourceCount}</span>
                </div>
              </Tooltip>

              {/* Search resources button */}
              {ml.resourceCount > 0 && (
                <Tooltip content={t("MediaLibrary.SearchResources")} placement="top">
                  <Button
                    isIconOnly
                    radius="sm"
                    size="sm"
                    variant="light"
                    onPress={() => searchResources(ml)}
                  >
                    <AiOutlineSearch className="text-lg" />
                  </Button>
                </Tooltip>
              )}

              {/* Delete button */}
              <Tooltip content={t("Delete this media library")} placement="top">
                <Button
                  isIconOnly
                  className="text-danger opacity-0 group-hover:opacity-100 transition-opacity"
                  radius="sm"
                  size="sm"
                  variant="light"
                  onPress={() => deleteMediaLibrary(ml)}
                >
                  <MdOutlineDelete className="text-lg" />
                </Button>
              </Tooltip>
            </div>
          </div>
        </CardBody>
      </Card>
    );
  };

  const renderContent = () => {
    if (mediaLibraries.length === 0) {
      return renderEmptyState();
    }

    return (
      <div className="flex flex-col gap-4 mt-4">
        {/* Header info */}
        <div className="flex items-center gap-2 text-sm text-default-500">
          <MediaLibraryTerm />
          <span>·</span>
          <span>{t("MediaLibrary.ListDescription")}</span>
        </div>

        {/* Media library cards */}
        <div className="grid gap-3">
          {mediaLibraries.map((ml) => renderMediaLibraryCard(ml))}
        </div>

        {/* Path mark config hint */}
        <div className="flex items-center justify-center gap-2 mt-4 p-3 bg-default-50 rounded-lg">
          <span className="text-sm text-default-500">{t("MediaLibrary.PathMarkConfigHint")}</span>
          <Button
            color="secondary"
            size="sm"
            variant="flat"
            onPress={() => navigate("/path-mark-config")}
          >
            {t("MediaLibrary.GoToPathMarkConfig")}
          </Button>
        </div>
      </div>
    );
  };

  return (
    <div className="h-full flex flex-col">
      {/* Header */}
      <div className="flex items-center justify-between">
        <h1 className="text-lg font-semibold">{t("Media Library")}</h1>
        {mediaLibraries.length > 0 && (
          <Button
            color="primary"
            size="sm"
            startContent={<AiOutlinePlusCircle className="text-lg" />}
            onPress={addMediaLibrary}
          >
            {t("MediaLibrary.Add")}
          </Button>
        )}
      </div>

      {renderContent()}
    </div>
  );
};

MediaLibraryPage.displayName = "MediaLibraryPage";

export default MediaLibraryPage;
