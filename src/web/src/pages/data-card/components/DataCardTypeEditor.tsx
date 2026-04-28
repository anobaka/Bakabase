"use client";

import React, { useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { EditOutlined, PlusCircleOutlined } from "@ant-design/icons";

import BApi from "@/sdk/BApi";
import {
  Button,
  Checkbox,
  Chip,
  Input,
  Modal,
  Select,
  Textarea,
} from "@/components/bakaui";
import type { IProperty } from "@/components/Property/models";
import type { DestroyableProps } from "@/components/bakaui/types";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import PropertySelector from "@/components/PropertySelector";
import { PropertyPool } from "@/sdk/constants";

interface DataCardTypeEditorProps extends DestroyableProps {
  cardType?: {
    id: number;
    name: string;
    propertyIds?: number[];
    identityPropertyIds?: number[];
    nameTemplate?: string;
    matchRules?: any;
  };
  allProperties: IProperty[];
  onSaved?: () => void;
}

const NAME_TEMPLATE_TOKEN = /\{([^{}]+)\}/g;

const DataCardTypeEditor = ({
  cardType,
  allProperties,
  onSaved,
  onDestroyed,
}: DataCardTypeEditorProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const isEditing = !!cardType;

  const [name, setName] = useState(cardType?.name || "");
  const [selectedPropertyIds, setSelectedPropertyIds] = useState<number[]>(
    cardType?.propertyIds || [],
  );
  const [identityPropertyIds, setIdentityPropertyIds] = useState<number[]>(
    cardType?.identityPropertyIds || [],
  );
  const [nameTemplate, setNameTemplate] = useState<string>(
    cardType?.nameTemplate || "",
  );
  const [matchPropertyIds, setMatchPropertyIds] = useState<number[]>(
    cardType?.matchRules?.matchProperties || [],
  );
  const [matchMode, setMatchMode] = useState<number>(
    cardType?.matchRules?.matchMode ?? 1,
  );
  const [allowCreate, setAllowCreate] = useState<boolean>(
    cardType?.matchRules?.allowCreate ?? true,
  );
  const [allowUpdate, setAllowUpdate] = useState<boolean>(
    cardType?.matchRules?.allowUpdate ?? true,
  );

  const templateTextareaRef = useRef<HTMLTextAreaElement>(null);

  const propertyMap = useMemo(() => {
    const map = new Map<number, IProperty>();
    for (const p of allProperties) map.set(p.id!, p);
    return map;
  }, [allProperties]);

  const selectedProperties = useMemo(
    () => selectedPropertyIds
      .map((id) => propertyMap.get(id))
      .filter(Boolean) as IProperty[],
    [selectedPropertyIds, propertyMap],
  );

  const identityProperties = useMemo(
    () => identityPropertyIds
      .map((id) => propertyMap.get(id))
      .filter(Boolean) as IProperty[],
    [identityPropertyIds, propertyMap],
  );

  // matchPropertyIds must be a subset of identityPropertyIds; non-identity
  // selections are ignored (user may have deselected an identity after
  // picking it as a match).
  const effectiveMatchPropertyIds = useMemo(
    () => matchPropertyIds.filter((id) => identityPropertyIds.includes(id)),
    [matchPropertyIds, identityPropertyIds],
  );

  const isNameValid = name.trim().length > 0;
  const isPropertiesValid = selectedPropertyIds.length > 0;
  const isIdentityValid = identityPropertyIds.length > 0;
  const isValid = isNameValid && isPropertiesValid && isIdentityValid;

  const openPropertiesSelector = () => {
    createPortal(PropertySelector, {
      pool: PropertyPool.Custom,
      multiple: true,
      addable: true,
      selection: selectedPropertyIds.map((id) => ({ id, pool: PropertyPool.Custom })),
      onSubmit: async (properties: IProperty[]) => {
        const ids = properties.map((p) => p.id!);
        setSelectedPropertyIds(ids);
        setIdentityPropertyIds((prev) => prev.filter((id) => ids.includes(id)));
        setMatchPropertyIds((prev) => prev.filter((id) => ids.includes(id)));
      },
    });
  };

  const toggleId = (
    id: number,
    list: number[],
    setter: (ids: number[]) => void,
  ) => {
    if (list.includes(id)) {
      setter(list.filter((x) => x !== id));
    } else {
      setter([...list, id]);
    }
  };

  const insertTokenAtCursor = (token: string) => {
    const ta = templateTextareaRef.current;
    if (!ta) {
      setNameTemplate((prev) => prev + token);
      return;
    }
    const start = ta.selectionStart;
    const end = ta.selectionEnd;
    const next = nameTemplate.slice(0, start) + token + nameTemplate.slice(end);
    setNameTemplate(next);
    setTimeout(() => {
      ta.focus();
      ta.setSelectionRange(start + token.length, start + token.length);
    }, 0);
  };

  const templatePreviewSegments = useMemo(() => {
    if (!nameTemplate) return [];
    const validNames = new Set(selectedProperties.map((p) => p.name!));
    const segments: Array<{ type: "text" | "property" | "invalid"; content: string }> = [];
    let last = 0;
    let m: RegExpExecArray | null;
    NAME_TEMPLATE_TOKEN.lastIndex = 0;
    while ((m = NAME_TEMPLATE_TOKEN.exec(nameTemplate)) !== null) {
      if (m.index > last) {
        segments.push({ type: "text", content: nameTemplate.slice(last, m.index) });
      }
      const propName = m[1];
      segments.push({
        type: validNames.has(propName) ? "property" : "invalid",
        content: propName,
      });
      last = NAME_TEMPLATE_TOKEN.lastIndex;
    }
    if (last < nameTemplate.length) {
      segments.push({ type: "text", content: nameTemplate.slice(last) });
    }
    return segments;
  }, [nameTemplate, selectedProperties]);

  const handleSave = async () => {
    if (!isValid) return;

    const finalMatchPropertyIds = matchPropertyIds.filter((id) =>
      identityPropertyIds.includes(id),
    );
    const matchRules = {
      autoBindEnabled: finalMatchPropertyIds.length > 0,
      matchProperties: finalMatchPropertyIds,
      matchMode: matchMode as 1 | 2,
      allowCreate,
      allowUpdate,
    };

    const payload = {
      name,
      propertyIds: selectedPropertyIds,
      identityPropertyIds,
      nameTemplate: nameTemplate || undefined,
      matchRules,
    };

    if (isEditing) {
      await BApi.dataCardType.updateDataCardType(cardType!.id, payload);
    } else {
      await BApi.dataCardType.addDataCardType(payload);
    }

    onSaved?.();
  };

  const renderSelectedPropertyChips = () => (
    <div className="flex flex-wrap gap-1.5 items-center">
      {selectedProperties.map((p) => (
        <Chip
          key={p.id}
          size="sm"
          variant="flat"
          color="primary"
          className="cursor-pointer"
          onClick={openPropertiesSelector}
        >
          {p.name}
        </Chip>
      ))}
      <Button
        size="sm"
        variant="light"
        startContent={<PlusCircleOutlined />}
        onPress={openPropertiesSelector}
      >
        {selectedProperties.length === 0
          ? t("dataCard.type.properties.select")
          : t("common.action.edit")}
      </Button>
    </div>
  );

  const renderPropertyCheckboxes = (
    list: number[],
    setter: (ids: number[]) => void,
    emptyHint?: string,
  ) => {
    if (selectedProperties.length === 0) {
      return (
        <span className="text-xs text-default-400">
          {emptyHint || t("dataCard.type.properties.selectFirst")}
        </span>
      );
    }
    return (
      <div className="flex flex-wrap gap-3">
        {selectedProperties.map((p) => (
          <Checkbox
            key={p.id}
            size="sm"
            isSelected={list.includes(p.id!)}
            onValueChange={() => toggleId(p.id!, list, setter)}
          >
            <span className="text-sm">{p.name}</span>
          </Checkbox>
        ))}
      </div>
    );
  };

  return (
    <Modal
      defaultVisible
      size="5xl"
      title={isEditing ? t("dataCard.type.edit") : t("dataCard.type.create")}
      onOk={handleSave}
      okProps={{ isDisabled: !isValid }}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-4">
        {/* Name */}
        <Input
          label={t("dataCard.type.name")}
          value={name}
          onValueChange={setName}
          isRequired
          isInvalid={!isNameValid}
          errorMessage={!isNameValid ? t("dataCard.validation.nameRequired") : undefined}
        />

        {/* Properties */}
        <div>
          <label className="text-sm font-medium block mb-1">
            {t("dataCard.type.properties")}
            <span className="text-danger ml-1">*</span>
          </label>
          {!isPropertiesValid && (
            <p className="text-xs text-danger mb-1">
              {t("dataCard.validation.propertiesRequired")}
            </p>
          )}
          {renderSelectedPropertyChips()}
        </div>

        {/* Basic Config Section */}
        <div className="border border-default-200 rounded-lg p-4 flex flex-col gap-4">
          <div className="text-sm font-semibold">
            {t("dataCard.type.section.basic")}
          </div>

          {/* Identity Properties (checkbox list) */}
          <div>
            <label className="text-sm font-medium block mb-1">
              {t("dataCard.matchRules.identityProperties")}
              <span className="text-danger ml-1">*</span>
            </label>
            <p className="text-xs text-default-400 mb-2">
              {t("dataCard.matchRules.identityProperties.description")}
            </p>
            {!isIdentityValid && isPropertiesValid && (
              <p className="text-xs text-danger mb-1">
                {t("dataCard.validation.identityPropertiesRequired")}
              </p>
            )}
            {renderPropertyCheckboxes(identityPropertyIds, setIdentityPropertyIds)}
          </div>

          {/* Name Template (inline) */}
          <div>
            <label className="text-sm font-medium block mb-1">
              {t("dataCard.type.nameTemplate")}
            </label>
            <p className="text-xs text-default-400 mb-2">
              {t("dataCard.type.nameTemplate.description")}
            </p>
            {selectedProperties.length > 0 && (
              <div className="flex flex-wrap gap-1 mb-2">
                {selectedProperties.map((p) => (
                  <Chip
                    key={p.id}
                    size="sm"
                    color="primary"
                    variant="flat"
                    className="cursor-pointer hover:opacity-80"
                    onClick={() => insertTokenAtCursor(`{${p.name}}`)}
                  >
                    {p.name}
                  </Chip>
                ))}
              </div>
            )}
            <Textarea
              ref={templateTextareaRef}
              placeholder={t("dataCard.type.nameTemplate.placeholder")}
              value={nameTemplate}
              onValueChange={setNameTemplate}
              minRows={2}
              maxRows={4}
              classNames={{ input: "font-mono" }}
            />
            {nameTemplate && (
              <div className="mt-2">
                <div className="text-xs text-default-500 mb-1">
                  {t("dataCard.type.nameTemplate.preview")}
                </div>
                <div className="p-2 bg-default-100 rounded text-sm flex flex-wrap items-center gap-0.5">
                  {templatePreviewSegments.map((seg, idx) => {
                    if (seg.type === "text") return <span key={idx}>{seg.content}</span>;
                    if (seg.type === "property") {
                      return (
                        <Chip key={idx} size="sm" color="primary" variant="flat" className="h-5">
                          {seg.content}
                        </Chip>
                      );
                    }
                    return (
                      <Chip key={idx} size="sm" color="danger" variant="flat" className="h-5">
                        {seg.content}
                      </Chip>
                    );
                  })}
                </div>
              </div>
            )}
          </div>
        </div>

        {/* Auto-associate Section */}
        <div className="border border-default-200 rounded-lg p-4 flex flex-col gap-3">
          <div className="text-sm font-semibold">
            {t("dataCard.type.section.autoBind")}
          </div>
          <p className="text-xs text-default-400">
            {t("dataCard.matchRules.autoBind.description")}
          </p>

          {!isIdentityValid ? (
            <p className="text-xs text-warning-600">
              {t("dataCard.matchRules.autoBind.needsIdentity")}
            </p>
          ) : (
            <>
              <div>
                <label className="text-sm font-medium block mb-1">
                  {t("dataCard.matchRules.matchProperties")}
                </label>
                <div className="flex flex-wrap gap-3">
                  {identityProperties.map((p) => (
                    <Checkbox
                      key={p.id}
                      isSelected={matchPropertyIds.includes(p.id!)}
                      size="sm"
                      onValueChange={() =>
                        toggleId(p.id!, matchPropertyIds, setMatchPropertyIds)
                      }
                    >
                      <span className="text-sm">{p.name}</span>
                    </Checkbox>
                  ))}
                </div>
                {effectiveMatchPropertyIds.length === 0 && (
                  <p className="text-xs text-warning-600 mt-2">
                    {t("dataCard.matchRules.autoBind.noMatchSelected")}
                  </p>
                )}
              </div>

              {effectiveMatchPropertyIds.length > 0 && (
                <Select
                  description={
                    matchMode === 1
                      ? t("dataCard.matchRules.matchMode.any.description")
                      : t("dataCard.matchRules.matchMode.all.description")
                  }
                  label={t("dataCard.matchRules.matchMode")}
                  selectedKeys={[String(matchMode)]}
                  size="sm"
                  onSelectionChange={(keys) => {
                    const key = Array.from(keys)[0];
                    if (key) setMatchMode(Number(key));
                  }}
                  dataSource={[
                    { value: "1", label: t("dataCard.matchRules.matchMode.any") },
                    { value: "2", label: t("dataCard.matchRules.matchMode.all") },
                  ]}
                />
              )}
            </>
          )}
        </div>

        {/* Upsert Section (two checkboxes) */}
        <div className="border border-default-200 rounded-lg p-4 flex flex-col gap-3">
          <div className="text-sm font-semibold">
            {t("dataCard.type.section.upsert")}
          </div>
          <p className="text-xs text-default-400 whitespace-pre-line">
            {t("dataCard.matchRules.upsertRule.description")}
          </p>

          <div className="flex flex-wrap gap-6">
            <Checkbox
              size="sm"
              isSelected={allowCreate}
              onValueChange={setAllowCreate}
            >
              <span className="text-sm">{t("dataCard.matchRules.allowCreate")}</span>
            </Checkbox>
            <Checkbox
              size="sm"
              isSelected={allowUpdate}
              onValueChange={setAllowUpdate}
            >
              <span className="text-sm">{t("dataCard.matchRules.allowUpdate")}</span>
            </Checkbox>
          </div>
          {!allowCreate && !allowUpdate && (
            <p className="text-xs text-warning-600">
              {t("dataCard.matchRules.upsertRule.neitherHint")}
            </p>
          )}
        </div>
      </div>
    </Modal>
  );
};

export default DataCardTypeEditor;
