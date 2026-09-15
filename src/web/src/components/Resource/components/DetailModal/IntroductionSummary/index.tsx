"use client";

import type { Resource } from "@/core/models/Resource";

import React, { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { EditOutlined, FileTextOutlined, RightOutlined } from "@ant-design/icons";
import { Drawer, DrawerBody, DrawerContent, DrawerHeader } from "@heroui/react";

import { isNonEmptyValue } from "@/core/models/Resource";
import { PropertyPool, ReservedProperty } from "@/sdk/constants";
import { Button, Card, CardBody, Textarea } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { serializeStandardValue } from "@/components/StandardValue";
import { StandardValueType } from "@/sdk/constants";

interface Props {
  resource: Resource;
  onReload?: () => void;
}

const MAX_SUMMARY_LENGTH = 100;
const contentClassName =
  "whitespace-pre-line break-words text-sm leading-relaxed text-default-700 [&_a]:text-primary [&_a]:underline [&_img]:max-w-full [&_p]:my-2 [&_p:first-child]:mt-0 [&_p:last-child]:mb-0";

const IntroductionSummary = ({ resource, onReload }: Props) => {
  const { t } = useTranslation();
  const [isDrawerOpen, setIsDrawerOpen] = useState(false);
  const [isEditing, setIsEditing] = useState(false);
  const [editValue, setEditValue] = useState("");
  const [isSaving, setIsSaving] = useState(false);

  const introduction = useMemo(() => {
    const reservedProps = resource.properties?.[PropertyPool.Reserved];

    if (!reservedProps) return null;

    const introProperty = reservedProps[ReservedProperty.Introduction];

    if (!introProperty?.values?.length) return null;

    // `values` has one entry per scope, including empty ones; the highest-priority scope can be
    // empty (e.g. a Manual-scope row from editing Rating/Cover) and would hide an enhancer value.
    const value = introProperty.values.find((v) =>
      isNonEmptyValue(v?.aliasAppliedBizValue ?? v?.bizValue),
    );

    return (value?.bizValue as string) ?? null;
  }, [resource]);

  const handleOpenDrawer = () => {
    setEditValue(introduction || "");
    setIsDrawerOpen(true);
  };

  const handleSave = async () => {
    setIsSaving(true);
    try {
      const serializedValue = editValue.trim()
        ? serializeStandardValue(editValue, StandardValueType.String)
        : undefined;

      await BApi.resource.putResourcePropertyValue(resource.id, {
        value: serializedValue,
        isCustomProperty: false,
        propertyId: ReservedProperty.Introduction,
      });
      setIsEditing(false);
      onReload?.();
    } finally {
      setIsSaving(false);
    }
  };

  const hasIntroduction = !!introduction;
  // Strip HTML tags to get plain text length for truncation check
  const plainTextIntroduction = introduction?.replace(/<[^>]*>/g, "") ?? null;
  const isTruncated = !!plainTextIntroduction && plainTextIntroduction.length > MAX_SUMMARY_LENGTH;

  return (
    <>
      <Card
        isPressable
        aria-label={t<string>("common.label.introduction")}
        className="w-full cursor-pointer border border-default-200 bg-default-50/60 text-left transition-colors hover:bg-default-100"
        radius="lg"
        shadow="none"
        onPress={handleOpenDrawer}
      >
        <CardBody className="gap-2 p-3">
          <div className="flex items-center gap-2 text-sm font-medium text-default-700">
            <FileTextOutlined aria-hidden className="text-default-500" />
            <span className="flex-1">{t<string>("common.label.introduction")}</span>
            <RightOutlined aria-hidden className="text-xs text-default-400" />
          </div>
          {hasIntroduction ? (
            <>
              <div
                dangerouslySetInnerHTML={{ __html: introduction }}
                className={`${contentClassName} line-clamp-2`}
              />
              {isTruncated && (
                <p className="text-xs text-primary">
                  {t("resource.tip.clickToViewFullIntroduction")}
                </p>
              )}
            </>
          ) : (
            <p className="text-sm leading-relaxed text-default-400">
              {t("resource.tip.clickToAddIntroduction")}
            </p>
          )}
        </CardBody>
      </Card>

      <Drawer
        isOpen={isDrawerOpen}
        placement="right"
        size="lg"
        onClose={() => {
          setIsDrawerOpen(false);
          setIsEditing(false);
        }}
      >
        <DrawerContent>
          <DrawerHeader>
            <div className="flex items-center gap-2">
              <FileTextOutlined />
              {t("common.label.introduction")}
            </div>
          </DrawerHeader>
          <DrawerBody className="pb-6">
            {isEditing ? (
              <div className="flex flex-col gap-4 h-full">
                <Textarea
                  aria-label={t<string>("common.label.introduction")}
                  className="flex-1"
                  minRows={10}
                  placeholder={t<string>("resource.placeholder.enterIntroduction")}
                  value={editValue}
                  onValueChange={setEditValue}
                />
                <div className="flex gap-2 justify-end">
                  <Button
                    size="sm"
                    variant="light"
                    onPress={() => {
                      setIsEditing(false);
                      setEditValue(introduction || "");
                    }}
                  >
                    {t("common.action.cancel")}
                  </Button>
                  <Button color="primary" isLoading={isSaving} size="sm" onPress={handleSave}>
                    {t("common.action.save")}
                  </Button>
                </div>
              </div>
            ) : (
              <div className="flex min-h-0 flex-1 flex-col gap-4">
                {hasIntroduction ? (
                  <div
                    dangerouslySetInnerHTML={{ __html: introduction }}
                    className={`flex-1 ${contentClassName}`}
                  />
                ) : (
                  <p className="flex-1 text-sm text-default-400">
                    {t<string>("resource.state.noIntroductionYet")}
                  </p>
                )}
                <div className="flex justify-end border-t border-default-200 pt-3">
                  <Button
                    color="primary"
                    size="sm"
                    startContent={<EditOutlined />}
                    variant="flat"
                    onPress={() => {
                      setEditValue(introduction || "");
                      setIsEditing(true);
                    }}
                  >
                    {t("common.action.edit")}
                  </Button>
                </div>
              </div>
            )}
          </DrawerBody>
        </DrawerContent>
      </Drawer>
    </>
  );
};

IntroductionSummary.displayName = "IntroductionSummary";

export default IntroductionSummary;
