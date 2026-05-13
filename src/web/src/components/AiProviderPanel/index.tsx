"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Button,
  Chip,
  Input,
  Modal,
  ModalBody,
  ModalContent,
  ModalFooter,
  ModalHeader,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  useDisclosure,
} from "@heroui/react";
import { AiOutlineDelete, AiOutlinePlus, AiOutlineUpload } from "react-icons/ai";

import { Select, toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ConfirmModal from "@/components/ConfirmModal";
import BApi from "@/sdk/BApi";
import type {
  BakabaseModulesAIModelsDbAiProviderDbModel,
  BakabaseModulesAIModelsDomainAiProviderKindInfo,
  BakabaseModulesAIModelsInputAiProviderTestResult,
  BakabaseModulesAIModelsInputAiProviderAddInputModel,
  BakabaseModulesAIModelsInputAiProviderUpdateInputModel,
  BakabaseModulesAIModelsDomainLlmModelInfo,
} from "@/sdk/Api";
import { AiProviderCapability } from "@/sdk/constants";
import JsonEditor, { stripJsonComments } from "@/components/JsonEditor";
import { AigcProviderConfigSamples } from "./samples";

type AiProvider = BakabaseModulesAIModelsDbAiProviderDbModel;
type KindInfo = BakabaseModulesAIModelsDomainAiProviderKindInfo;
type TestResult = BakabaseModulesAIModelsInputAiProviderTestResult;
type LlmModelInfo = BakabaseModulesAIModelsDomainLlmModelInfo;

const supportsLlm = (caps: number) =>
  (caps & AiProviderCapability.Llm) === AiProviderCapability.Llm;
const supportsAigc = (caps: number) =>
  (caps & AiProviderCapability.Aigc) === AiProviderCapability.Aigc;

const AiProviderPanel = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const { isOpen, onOpen, onClose } = useDisclosure();

  const [providers, setProviders] = useState<AiProvider[]>([]);
  const [kinds, setKinds] = useState<KindInfo[]>([]);
  const [editing, setEditing] = useState<Partial<AiProvider> | null>(null);
  const [isEditMode, setIsEditMode] = useState(false);
  const [testing, setTesting] = useState<Set<number>>(new Set());
  const [loadingModels, setLoadingModels] = useState<Record<number, boolean>>({});
  const [providerModels, setProviderModels] = useState<Record<number, LlmModelInfo[]>>({});
  const aigcFileInputRef = useRef<HTMLInputElement>(null);

  const load = useCallback(async () => {
    const [pr, kr] = await Promise.all([
      BApi.ai.getAllAiProviders(),
      BApi.ai.getAiProviderKinds(),
    ]);
    if (!pr.code && pr.data) setProviders(pr.data);
    if (!kr.code && kr.data) setKinds(kr.data);
  }, []);

  useEffect(() => {
    load();
  }, []);

  const kindInfo = (kind: number | undefined) => kinds.find((k) => k.kind === kind);
  const kindLabel = (kind: number) => kindInfo(kind)?.displayName ?? `#${kind}`;

  const handleAdd = () => {
    const firstKind = kinds[0];
    setEditing({
      kind: firstKind?.kind,
      name: "",
      endpoint: firstKind?.defaultEndpoint ?? "",
      apiKey: "",
      isEnabled: true,
      llmEnabled: firstKind ? supportsLlm(firstKind.capabilities) : false,
      aigcEnabled: false,
      aigcConfigJson: "",
    });
    setIsEditMode(false);
    onOpen();
  };

  const handleEdit = (p: AiProvider) => {
    setEditing({ ...p });
    setIsEditMode(true);
    onOpen();
  };

  const handleSave = async () => {
    if (!editing) return;
    if (!editing.kind) {
      toast.danger(t<string>("aiProvider.error.kindRequired"));
      return;
    }
    if (!editing.name?.trim()) {
      toast.danger(t<string>("aiProvider.error.nameRequired"));
      return;
    }
    const ki = kindInfo(editing.kind);
    if (!editing.llmEnabled && !editing.aigcEnabled) {
      toast.danger(t<string>("aiProvider.error.atLeastOneCapability"));
      return;
    }
    if (ki?.requiresApiKey && !editing.apiKey?.trim()) {
      toast.danger(t<string>("aiProvider.error.apiKeyRequired"));
      return;
    }
    if (ki?.requiresEndpoint && !editing.endpoint?.trim()) {
      toast.danger(t<string>("aiProvider.error.endpointRequired"));
      return;
    }

    const cleanAigcConfig = editing.aigcEnabled && editing.aigcConfigJson
      ? stripJsonComments(editing.aigcConfigJson)
      : null;

    if (isEditMode && editing.id) {
      const r = await BApi.ai.updateAiProvider(editing.id, {
        kind: editing.kind,
        name: editing.name,
        endpoint: editing.endpoint,
        apiKey: editing.apiKey,
        isEnabled: editing.isEnabled,
        llmEnabled: editing.llmEnabled,
        aigcEnabled: editing.aigcEnabled,
        aigcConfigJson: cleanAigcConfig,
      } as BakabaseModulesAIModelsInputAiProviderUpdateInputModel);
      if (!r.code) {
        toast.success(t<string>("common.success.saved"));
        onClose();
        await load();
      }
    } else {
      const r = await BApi.ai.addAiProvider({
        kind: editing.kind,
        name: editing.name!,
        endpoint: editing.endpoint,
        apiKey: editing.apiKey,
        isEnabled: editing.isEnabled ?? true,
        llmEnabled: editing.llmEnabled ?? false,
        aigcEnabled: editing.aigcEnabled ?? false,
        aigcConfigJson: cleanAigcConfig,
      } as BakabaseModulesAIModelsInputAiProviderAddInputModel);
      if (!r.code) {
        toast.success(t<string>("common.success.saved"));
        onClose();
        await load();
      }
    }
  };

  const handleDelete = (id: number) => {
    createPortal(ConfirmModal, {
      title: t<string>("common.action.delete"),
      message: t<string>("aiProvider.confirmDelete"),
      destructive: true,
      onConfirm: async () => {
        const r = await BApi.ai.deleteAiProvider(id);
        if (!r.code) {
          toast.success(t<string>("common.success.saved"));
          await load();
        } else if (r.message) {
          toast.danger(r.message);
        }
      },
    });
  };

  const handleTest = async (id: number) => {
    setTesting((prev) => new Set([...prev, id]));
    try {
      const r = await BApi.ai.testAiProvider(id);
      if (!r.code && r.data) {
        const result = r.data as TestResult;
        const lines: string[] = [];
        if (result.llm !== null && result.llm !== undefined) {
          lines.push(`LLM: ${result.llm ? "✓" : "✗"}${result.llmMessage ? ` (${result.llmMessage})` : ""}`);
        }
        if (result.aigc !== null && result.aigc !== undefined) {
          lines.push(`AIGC: ${result.aigc ? "✓" : "✗"}${result.aigcMessage ? ` (${result.aigcMessage})` : ""}`);
        }
        const ok = (result.llm ?? true) && (result.aigc ?? true);
        const msg = lines.join("\n") || t<string>("aiProvider.testNoCapability");
        if (ok) toast.success(msg); else toast.danger(msg);
      } else {
        toast.danger(t<string>("aiProvider.testFailed"));
      }
    } finally {
      setTesting((prev) => {
        const n = new Set(prev);
        n.delete(id);
        return n;
      });
    }
  };

  const handleLoadModels = async (id: number) => {
    setLoadingModels((prev) => ({ ...prev, [id]: true }));
    try {
      const r = await BApi.ai.getAiProviderLlmModels(id);
      if (!r.code && r.data) {
        setProviderModels((prev) => ({ ...prev, [id]: r.data! }));
      }
    } finally {
      setLoadingModels((prev) => ({ ...prev, [id]: false }));
    }
  };

  const handleUploadAigcConfig = (file: File) => {
    const reader = new FileReader();
    reader.onload = () => {
      const text = reader.result;
      if (typeof text !== "string" || !editing) return;
      setEditing({ ...editing, aigcConfigJson: text });
      toast.success(t<string>("aiProvider.aigcConfigUploaded"));
    };
    reader.onerror = () => toast.danger(t<string>("aiProvider.aigcConfigUploadFailed"));
    reader.readAsText(file);
  };

  const handleKindChange = (kind: number) => {
    if (!editing) return;
    const ki = kindInfo(kind);
    setEditing({
      ...editing,
      kind: kind as AiProvider["kind"],
      endpoint: ki?.defaultEndpoint ?? editing.endpoint ?? "",
      llmEnabled: ki ? supportsLlm(ki.capabilities) && (editing.llmEnabled ?? false) : false,
      aigcEnabled: ki ? supportsAigc(ki.capabilities) && (editing.aigcEnabled ?? false) : false,
    });
  };

  const editingKi = kindInfo(editing?.kind);
  const editingSupportsLlm = editingKi ? supportsLlm(editingKi.capabilities) : false;
  const editingSupportsAigc = editingKi ? supportsAigc(editingKi.capabilities) : false;
  const aigcSample = editing?.kind ? AigcProviderConfigSamples[editing.kind] : undefined;

  const renderProviderRow = (p: AiProvider) => {
    const models = providerModels[p.id];
    return (
      <div className="flex flex-col gap-2">
        <div className="flex items-center gap-2 flex-wrap">
          <Chip size="sm" variant="flat">{kindLabel(p.kind)}</Chip>
          <Chip size="sm" color={p.isEnabled ? "success" : "default"} variant="dot">
            {p.isEnabled ? t<string>("common.enabled") : t<string>("common.disabled")}
          </Chip>
          {p.llmEnabled && <Chip size="sm" color="primary" variant="flat">LLM</Chip>}
          {p.aigcEnabled && <Chip size="sm" color="secondary" variant="flat">AIGC</Chip>}
          {p.endpoint && (
            <span className="text-xs text-default-400 font-mono">{p.endpoint}</span>
          )}
        </div>

        {p.llmEnabled && models && models.length > 0 && (
          <div className="flex flex-wrap gap-1">
            {models.map((m) => (
              <Chip key={m.modelId} size="sm" variant="flat">{m.displayName}</Chip>
            ))}
          </div>
        )}
        {p.llmEnabled && models && models.length === 0 && (
          <div className="text-xs text-default-400">{t<string>("aiProvider.noModels")}</div>
        )}

        <div className="flex gap-2 flex-wrap">
          <Button size="sm" variant="flat" onPress={() => handleEdit(p)}>
            {t<string>("common.action.edit")}
          </Button>
          <Button
            size="sm"
            variant="flat"
            isLoading={testing.has(p.id)}
            onPress={() => handleTest(p.id)}
          >
            {testing.has(p.id) ? t<string>("aiProvider.testing") : t<string>("aiProvider.testConnection")}
          </Button>
          {p.llmEnabled && (
            <Button
              size="sm"
              variant="flat"
              isLoading={loadingModels[p.id]}
              onPress={() => handleLoadModels(p.id)}
            >
              {t<string>("aiProvider.loadModels")}
            </Button>
          )}
          <Button
            size="sm"
            variant="flat"
            color="danger"
            startContent={<AiOutlineDelete />}
            onPress={() => handleDelete(p.id)}
          >
            {t<string>("common.action.delete")}
          </Button>
        </div>
      </div>
    );
  };

  return (
    <>
      <Table removeWrapper>
        <TableHeader>
          <TableColumn width={200}>{t<string>("aiProvider.title")}</TableColumn>
          <TableColumn>&nbsp;</TableColumn>
        </TableHeader>
        <TableBody>
          {providers.map((p) => (
            <TableRow key={p.id} className="hover:bg-[var(--bakaui-overlap-background)]">
              <TableCell>
                <div className="flex items-center font-medium">{p.name}</div>
              </TableCell>
              <TableCell>{renderProviderRow(p)}</TableCell>
            </TableRow>
          ))}
          <TableRow className="hover:bg-[var(--bakaui-overlap-background)]">
            <TableCell>
              <Button
                color="primary"
                size="sm"
                startContent={<AiOutlinePlus />}
                onPress={handleAdd}
              >
                {t<string>("aiProvider.add")}
              </Button>
            </TableCell>
            <TableCell>&nbsp;</TableCell>
          </TableRow>
        </TableBody>
      </Table>

      <Modal isOpen={isOpen} onClose={onClose} size="3xl" scrollBehavior="inside">
        <ModalContent>
          <ModalHeader>
            {isEditMode ? t<string>("aiProvider.edit") : t<string>("aiProvider.add")}
          </ModalHeader>
          <ModalBody>
            {editing && (
              <div className="flex flex-col gap-3">
                {/* Row 1: Kind + Name */}
                <div className="grid grid-cols-2 gap-3">
                  <Select
                    label={t<string>("aiProvider.kind")}
                    size="sm"
                    isRequired
                    selectedKeys={editing.kind ? [String(editing.kind)] : []}
                    onSelectionChange={(keys) => {
                      const v = Array.from(keys)[0];
                      if (v) handleKindChange(Number(v));
                    }}
                    dataSource={kinds.map((k) => ({ label: k.displayName, value: String(k.kind) }))}
                  />
                  <Input
                    label={t<string>("aiProvider.name")}
                    size="sm"
                    isRequired
                    value={editing.name ?? ""}
                    onValueChange={(v) => setEditing({ ...editing, name: v })}
                  />
                </div>

                {/* Row 2: Endpoint + API Key */}
                <div className="grid grid-cols-2 gap-3">
                  <Input
                    label={t<string>("aiProvider.endpoint")}
                    size="sm"
                    isRequired={editingKi?.requiresEndpoint}
                    placeholder={editingKi?.defaultEndpoint ?? ""}
                    value={editing.endpoint ?? ""}
                    onValueChange={(v) => setEditing({ ...editing, endpoint: v })}
                  />
                  <Input
                    label={t<string>("aiProvider.apiKey")}
                    size="sm"
                    type="password"
                    isRequired={editingKi?.requiresApiKey}
                    value={editing.apiKey ?? ""}
                    onValueChange={(v) => setEditing({ ...editing, apiKey: v })}
                  />
                </div>

                {/* Row 3: Capabilities */}
                <div className="flex items-center gap-6">
                  <div className="flex items-center gap-2">
                    <Switch
                      size="sm"
                      isSelected={editing.isEnabled ?? true}
                      onValueChange={(v) => setEditing({ ...editing, isEnabled: v })}
                    />
                    <span className="text-sm">{t<string>("common.enabled")}</span>
                  </div>
                  <div className="flex items-center gap-2">
                    <Switch
                      size="sm"
                      isDisabled={!editingSupportsLlm}
                      isSelected={editing.llmEnabled ?? false}
                      onValueChange={(v) => setEditing({ ...editing, llmEnabled: v })}
                    />
                    <span className="text-sm">{t<string>("aiProvider.useForLlm")}</span>
                    {!editingSupportsLlm && (
                      <span className="text-xs text-default-400">
                        ({t<string>("aiProvider.notSupportedByKind")})
                      </span>
                    )}
                  </div>
                  <div className="flex items-center gap-2">
                    <Switch
                      size="sm"
                      isDisabled={!editingSupportsAigc}
                      isSelected={editing.aigcEnabled ?? false}
                      onValueChange={(v) => setEditing({ ...editing, aigcEnabled: v })}
                    />
                    <span className="text-sm">{t<string>("aiProvider.useForAigc")}</span>
                    {!editingSupportsAigc && (
                      <span className="text-xs text-default-400">
                        ({t<string>("aiProvider.notSupportedByKind")})
                      </span>
                    )}
                  </div>
                </div>

                {/* AIGC config (only visible if AIGC capability enabled) */}
                {editing.aigcEnabled && editingSupportsAigc && (
                  <div>
                    <div className="flex items-center justify-between mb-1">
                      <div className="text-xs text-default-500">
                        {t<string>("aiProvider.aigcConfigJson")}
                        <span className="ml-2 text-default-400">
                          {t<string>("aiProvider.aigcConfigJsonHelp")}
                        </span>
                      </div>
                      <Button
                        size="sm"
                        variant="flat"
                        startContent={<AiOutlineUpload />}
                        onPress={() => aigcFileInputRef.current?.click()}
                      >
                        {t<string>("aiProvider.uploadJson")}
                      </Button>
                      <input
                        ref={aigcFileInputRef}
                        type="file"
                        accept="application/json,.json"
                        className="hidden"
                        onChange={(e) => {
                          const f = e.target.files?.[0];
                          if (f) handleUploadAigcConfig(f);
                          e.target.value = "";
                        }}
                      />
                    </div>
                    <JsonEditor
                      key={`aigc-config-${editing.kind ?? 0}-${editing.id ?? "new"}`}
                      value={editing.aigcConfigJson ?? ""}
                      onChange={(v) => setEditing({ ...editing, aigcConfigJson: v })}
                      sampleHeader={aigcSample?.header}
                      sampleBody={aigcSample?.body}
                    />
                  </div>
                )}
              </div>
            )}
          </ModalBody>
          <ModalFooter>
            <Button variant="flat" onPress={onClose}>
              {t<string>("common.action.cancel")}
            </Button>
            <Button color="primary" onPress={handleSave}>
              {t<string>("common.action.save")}
            </Button>
          </ModalFooter>
        </ModalContent>
      </Modal>
    </>
  );
};

export default AiProviderPanel;
