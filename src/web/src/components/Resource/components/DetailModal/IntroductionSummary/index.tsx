"use client";

import React, { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { EditOutlined, FileTextOutlined } from "@ant-design/icons";
import {
  Drawer,
  DrawerBody,
  DrawerContent,
  DrawerHeader,
} from "@heroui/react";

import type { Resource } from "@/core/models/Resource";
import { PropertyPool, ReservedProperty, PropertyValueScope } from "@/sdk/constants";
import { Button, Card, CardBody, Textarea } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { serializeStandardValue } from "@/components/StandardValue/helpers";
import { StandardValueType } from "@/sdk/constants";

interface Props {
  resource: Resource;
  onReload?: () => void;
}

const MAX_SUMMARY_LENGTH = 100;

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

    // Get the first available value (considering scope priority)
    const value = introProperty.values[0];
    return value?.bizValue as string | null;
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
  const isTruncated = hasIntroduction && introduction.length > MAX_SUMMARY_LENGTH;
  const summary = hasIntroduction
    ? (isTruncated ? `${introduction.slice(0, MAX_SUMMARY_LENGTH)}...` : introduction)
    : null;

  return (
    <>
      <Card
        isPressable
        className="w-full cursor-pointer hover:bg-default-100 transition-colors"
        onPress={handleOpenDrawer}
      >
        <CardBody className="py-2 px-3">
          <div className="flex items-start gap-2">
            {/* <FileTextOutlined className="text-default-500 mt-0.5 flex-shrink-0" /> */}
            <div className="flex-1 min-w-0">
              {hasIntroduction ? (
                <>
                  <p className="text-sm text-default-600 line-clamp-2">
                    {summary}
                  </p>
                  {isTruncated && (
                    <p className="text-xs text-primary mt-1">
                      {t("Click to view full introduction")}
                    </p>
                  )}
                </>
              ) : (
                <p className="text-sm text-default-400 italic">
                  {t("Click to add introduction")}
                </p>
              )}
            </div>
          </div>
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
              {t("Introduction")}
            </div>
          </DrawerHeader>
          <DrawerBody>
            {isEditing ? (
              <div className="flex flex-col gap-4 h-full">
                <Textarea
                  className="flex-1"
                  minRows={10}
                  placeholder={t<string>("Enter introduction...")}
                  value={editValue}
                  onValueChange={setEditValue}
                />
                <div className="flex gap-2 justify-end">
                  <Button
                    variant="light"
                    onPress={() => {
                      setIsEditing(false);
                      setEditValue(introduction || "");
                    }}
                  >
                    {t("Cancel")}
                  </Button>
                  <Button
                    color="primary"
                    isLoading={isSaving}
                    onPress={handleSave}
                  >
                    {t("Save")}
                  </Button>
                </div>
              </div>
            ) : (
              <div className="flex flex-col h-full">
                <div className="flex-1 whitespace-pre-wrap text-default-700">
                  {introduction || (
                    <span className="text-default-400 italic">
                      {t("No introduction yet")}
                    </span>
                  )}
                </div>
                <div className="flex justify-end pt-4">
                  <Button
                    color="primary"
                    startContent={<EditOutlined />}
                    variant="flat"
                    onPress={() => {
                      setEditValue(introduction || "");
                      setIsEditing(true);
                    }}
                  >
                    {t("Edit")}
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
