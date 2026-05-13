"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
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
  Textarea,
  useDisclosure,
} from "@heroui/react";
import {
  AiOutlineCloudUpload,
  AiOutlineDelete,
  AiOutlineFolderOpen,
  AiOutlinePlayCircle,
  AiOutlinePlus,
} from "react-icons/ai";

import { Select, toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";
import BApi from "@/sdk/BApi";
import { AiProviderKind, AiProviderKindLabel } from "@/sdk/constants";
import type {
  BakabaseModulesAIModelsDbAigcGeneratorDbModel,
  BakabaseModulesAIModelsDbAiProviderDbModel,
  BakabaseModulesAIModelsDomainAigcGeneratorView,
  BakabaseModulesAIModelsInputAigcGeneratorAddInputModel,
  BakabaseModulesAIModelsInputAigcGeneratorComfyUIImportItemResult,
  BakabaseModulesAIModelsInputAigcGeneratorComfyUIImportResult,
  BakabaseModulesAIModelsInputAigcGeneratorUpdateInputModel,
} from "@/sdk/Api";
import JsonEditor, { stripJsonComments } from "@/components/JsonEditor";
import ConfirmModal from "@/components/ConfirmModal";
import { AigcGeneratorParametersSamples } from "@/components/AiProviderPanel/samples";

type Generator = BakabaseModulesAIModelsDbAigcGeneratorDbModel;
type GeneratorView = BakabaseModulesAIModelsDomainAigcGeneratorView;
type Provider = BakabaseModulesAIModelsDbAiProviderDbModel;

const MEDIA_TYPES = [1, 2, 3, 4, 99];
const RESOURCE_MODES = [1, 2];

// Provider kinds that support importing AIGC configs from external artifacts.
// Add a kind here once its backend import endpoint exists and the i18n entries
// `aigc.configs.import.kind.{kindName}.{title,description,providerLabel,noProvider}`
// (and an optional `.status.4`) are defined.
const IMPORTABLE_KINDS: readonly AiProviderKind[] = [AiProviderKind.ComfyUI];

const presetSampleHeader =
  `// Property presets are applied to every Resource produced by this AIGC config.\n` +
  `// pool: 1 = Internal, 2 = Reserved, 3 = Custom\n` +
  `// serializedBizValue is parsed via PropertySystem.Value.Deserialize using the\n` +
  `// property's BizValueType (e.g. ListString uses comma-separated values).`;
const presetSampleBody = `[
  // Example: set a custom tag property to two values "AI,Generated"
  // {
  //   "pool": 3,
  //   "propertyId": 12,
  //   "serializedBizValue": "AI,Generated"
  // }
]`;

// SHA-256 hex via Web Crypto. Keeps the hash format aligned with the backend
// (Bakabase.Modules.AI.Services.AigcGeneratorService.ComputeSha256).
const sha256Hex = async (s: string): Promise<string> => {
  const bytes = new TextEncoder().encode(s);
  const buf = await crypto.subtle.digest("SHA-256", bytes);
  return Array.from(new Uint8Array(buf))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
};

// Extract the workflow object from ParametersJson for a ComfyUI generator.
// Returns null if the JSON is malformed.
const extractWorkflowFromParameters = (parametersJson: string | null | undefined): unknown => {
  if (!parametersJson) return null;
  try {
    const obj = JSON.parse(stripJsonComments(parametersJson));
    if (obj && typeof obj === "object" && "workflow" in obj) {
      return (obj as any).workflow;
    }
    return null;
  } catch {
    return null;
  }
};

const AigcConfigsPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const { isOpen, onOpen, onClose } = useDisclosure();
  const {
    isOpen: isImportOpen,
    onOpen: onImportOpen,
    onClose: onImportClose,
  } = useDisclosure();
  const {
    isOpen: isImportResultOpen,
    onOpen: onImportResultOpen,
    onClose: onImportResultClose,
  } = useDisclosure();

  const [views, setViews] = useState<GeneratorView[]>([]);
  const [providers, setProviders] = useState<Provider[]>([]);
  const [editing, setEditing] = useState<Partial<Generator> | null>(null);
  const [isEditMode, setIsEditMode] = useState(false);
  const [presetsText, setPresetsText] = useState("");
  /** ComfyUI workflow JSON shown in the editor for ComfyUI-kind generators (raw text, possibly invalid). */
  const [comfyuiWorkflowText, setComfyuiWorkflowText] = useState("");
  /** Import dialog state. */
  const [importKind, setImportKind] = useState<AiProviderKind>(IMPORTABLE_KINDS[0]);
  const [importProviderId, setImportProviderId] = useState<number | undefined>(undefined);
  const [importPaths, setImportPaths] = useState<string[]>([]);
  const [importInFlight, setImportInFlight] = useState(false);
  const [importResult, setImportResult] = useState<BakabaseModulesAIModelsInputAigcGeneratorComfyUIImportResult | null>(null);
  /** Kind that produced the currently-displayed result, used to localize per-kind statuses. */
  const [importResultKind, setImportResultKind] = useState<AiProviderKind | null>(null);

  const importableProvidersByKind = useMemo(() => {
    const map = new Map<AiProviderKind, Provider[]>();
    for (const k of IMPORTABLE_KINDS) {
      map.set(k, providers.filter((p) => p.kind === k && p.aigcEnabled));
    }
    return map;
  }, [providers]);
  const providersForImportKind = importableProvidersByKind.get(importKind) ?? [];

  const load = useCallback(async () => {
    const [gr, pr] = await Promise.all([
      BApi.aigc.getAllAigcGenerators(),
      BApi.aigc.getEnabledAigcProviders(),
    ]);
    if (!gr.code && gr.data) setViews(gr.data);
    if (!pr.code && pr.data) setProviders(pr.data);
  }, []);

  useEffect(() => {
    load();
  }, []);

  const providerName = (id: number) => providers.find((p) => p.id === id)?.name ?? `#${id}`;
  const providerKind = (id: number) => providers.find((p) => p.id === id)?.kind;

  const tMediaType = (n: number) => t<string>(`aigc.mediaType.${n}`);
  const tResourceMode = (n: number) => t<string>(`aigc.resourceMode.${n}`);

  const handleAdd = () => {
    setEditing({
      name: "",
      providerId: providers[0]?.id ?? 0,
      mediaType: 1,
      filenameTemplate: "{run}_{ordinal}_{timestamp}",
      resourceMode: 1,
      allowDeletion: true,
      isEnabled: true,
    });
    setPresetsText("");
    setComfyuiWorkflowText("");
    setIsEditMode(false);
    onOpen();
  };

  const handleEdit = (v: GeneratorView) => {
    setEditing({ ...v.generator });
    setPresetsText(v.propertyPresets.length > 0 ? JSON.stringify(v.propertyPresets, null, 2) : "");
    const wf = extractWorkflowFromParameters(v.generator.parametersJson);
    setComfyuiWorkflowText(wf ? JSON.stringify(wf, null, 2) : "");
    setIsEditMode(true);
    onOpen();
  };

  const buildPresetInputs = (): { ok: boolean; presets: any[] } => {
    if (!presetsText.trim()) return { ok: true, presets: [] };
    try {
      const cleaned = stripJsonComments(presetsText);
      if (!cleaned.trim()) return { ok: true, presets: [] };
      const arr = JSON.parse(cleaned);
      if (!Array.isArray(arr)) {
        toast.danger(t<string>("aigc.configs.error.presetsNotArray"));
        return { ok: false, presets: [] };
      }
      return {
        ok: true,
        presets: arr.map((p: any) => ({
          pool: p.pool,
          propertyId: p.propertyId,
          serializedBizValue: p.serializedBizValue ?? null,
        })),
      };
    } catch {
      toast.danger(t<string>("aigc.configs.error.presetsInvalidJson"));
      return { ok: false, presets: [] };
    }
  };

  const handleSave = async () => {
    if (!editing) return;
    if (!editing.name?.trim()) {
      toast.danger(t<string>("aigc.configs.error.nameRequired"));
      return;
    }
    if (!editing.providerId) {
      toast.danger(t<string>("aigc.configs.error.providerRequired"));
      return;
    }

    const isComfyUI = providerKind(editing.providerId) === AiProviderKind.ComfyUI;

    let parametersJson: string | null;
    let presetsForSave: any[] = [];

    if (isComfyUI) {
      // Simplified ComfyUI flow: only the workflow JSON is exposed; reconstruct ParametersJson
      // as { workflow, workflowHash } so existing-generator hash dedup keeps working.
      const text = comfyuiWorkflowText?.trim() ?? "";
      if (!text) {
        toast.danger(t<string>("aigc.configs.error.workflowRequired"));
        return;
      }
      let parsed: unknown;
      try {
        parsed = JSON.parse(stripJsonComments(text));
      } catch (e: any) {
        toast.danger(t<string>("aigc.configs.error.workflowInvalidJson", { msg: e?.message ?? "" }));
        return;
      }
      const canonical = JSON.stringify(parsed);
      const workflowHash = await sha256Hex(canonical);
      parametersJson = JSON.stringify({ workflow: parsed, workflowHash });
    } else {
      const presetResult = buildPresetInputs();
      if (!presetResult.ok) return;
      presetsForSave = presetResult.presets;
      parametersJson = editing.parametersJson ? stripJsonComments(editing.parametersJson) : null;
    }

    if (isEditMode && editing.id) {
      const r = await BApi.aigc.updateAigcGenerator(editing.id, {
        name: editing.name,
        providerId: editing.providerId,
        mediaType: editing.mediaType,
        promptTemplate: isComfyUI ? null : editing.promptTemplate,
        negativePromptTemplate: isComfyUI ? null : editing.negativePromptTemplate,
        parametersJson,
        filenameTemplate: editing.filenameTemplate,
        resourceMode: editing.resourceMode,
        allowDeletion: editing.allowDeletion,
        isEnabled: editing.isEnabled,
        propertyPresets: isComfyUI ? null : presetsForSave,
      } as BakabaseModulesAIModelsInputAigcGeneratorUpdateInputModel);
      if (!r.code) {
        toast.success(t<string>("common.success.saved"));
        onClose();
        await load();
      }
    } else {
      const r = await BApi.aigc.addAigcGenerator({
        name: editing.name!,
        providerId: editing.providerId!,
        mediaType: editing.mediaType ?? 1,
        promptTemplate: isComfyUI ? undefined : editing.promptTemplate,
        negativePromptTemplate: isComfyUI ? undefined : editing.negativePromptTemplate,
        parametersJson,
        filenameTemplate: editing.filenameTemplate ?? "{run}_{ordinal}_{timestamp}",
        resourceMode: editing.resourceMode ?? 1,
        allowDeletion: editing.allowDeletion ?? true,
        isEnabled: editing.isEnabled ?? true,
        propertyPresets: isComfyUI ? undefined : presetsForSave,
      } as BakabaseModulesAIModelsInputAigcGeneratorAddInputModel);
      if (!r.code) {
        toast.success(t<string>("common.success.saved"));
        onClose();
        await load();
      }
    }
  };

  // === Import flow ===
  const handleOpenImport = () => {
    // Pick the first importable kind that has at least one enabled provider so the
    // modal opens on a usable state. If none, fall back to a generic toast — the
    // per-kind noProvider toast would be misleading if multiple kinds had no providers.
    const firstReady = IMPORTABLE_KINDS.find(
      (k) => (importableProvidersByKind.get(k)?.length ?? 0) > 0,
    );
    if (firstReady == null) {
      toast.danger(t<string>("aigc.configs.import.noImportableProvider"));
      return;
    }
    const kindProviders = importableProvidersByKind.get(firstReady) ?? [];
    setImportKind(firstReady);
    setImportProviderId(kindProviders[0]?.id);
    setImportPaths([]);
    onImportOpen();
  };

  const handleImportKindChange = (k: AiProviderKind) => {
    setImportKind(k);
    const kindProviders = importableProvidersByKind.get(k) ?? [];
    setImportProviderId(kindProviders[0]?.id);
  };

  const handlePickImportPaths = () => {
    createPortal(FileSystemSelectorModal, {
      multiple: true,
      onMultipleSelected: (entries) => {
        const newPaths = entries
          .map((e) => e.path)
          .filter((p): p is string => !!p)
          .filter((p) => !importPaths.includes(p));
        if (newPaths.length > 0) setImportPaths([...importPaths, ...newPaths]);
      },
    });
  };

  const handleRemoveImportPath = (path: string) => {
    setImportPaths(importPaths.filter((p) => p !== path));
  };

  const handleSubmitImport = async () => {
    if (!importProviderId) return;
    if (importPaths.length === 0) return;
    setImportInFlight(true);
    try {
      // Dispatch by kind. Each importable kind has its own backend endpoint;
      // add a branch here when wiring a new one.
      let result: BakabaseModulesAIModelsInputAigcGeneratorComfyUIImportResult | null = null;
      if (importKind === AiProviderKind.ComfyUI) {
        const r = await BApi.aigc.importComfyUiWorkflows({
          providerId: importProviderId,
          paths: importPaths,
        });
        if (r.code || !r.data) return;
        result = r.data;
      } else {
        return;
      }
      setImportResult(result);
      setImportResultKind(importKind);
      onImportClose();
      onImportResultOpen();
      await load();
    } finally {
      setImportInFlight(false);
    }
  };

  const handleDelete = (id: number) => {
    createPortal(ConfirmModal, {
      title: t<string>("common.action.delete"),
      message: t<string>("aigc.configs.confirmDelete"),
      destructive: true,
      onConfirm: async () => {
        const r = await BApi.aigc.deleteAigcGenerator(id);
        if (!r.code) {
          toast.success(t<string>("common.success.saved"));
          await load();
        } else if (r.message) {
          toast.danger(r.message);
        }
      },
    });
  };

  const handleTrigger = async (g: Generator) => {
    // ComfyUI generators bake prompts into the workflow; skip the override prompt.
    let promptOverride: string | undefined;
    if (providerKind(g.providerId) !== AiProviderKind.ComfyUI) {
      const raw = window.prompt(t<string>("aigc.configs.promptOverridePrompt"));
      promptOverride = raw ? raw : undefined;
    }
    const r = await BApi.aigc.triggerAigcGeneration(g.id, {
      promptOverride,
    } as any);
    if (!r.code && r.data) {
      toast.success(t<string>("aigc.configs.runQueued", { id: r.data }));
    } else if (r.message) {
      toast.danger(r.message);
    }
  };

const editingProviderKind = editing?.providerId ? providerKind(editing.providerId) : undefined;
  const paramsSample = editingProviderKind
    ? AigcGeneratorParametersSamples[editingProviderKind]
    : undefined;

  return (
    <div className="flex flex-col gap-4 p-4">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold">{t<string>("aigc.configs.title")}</h1>
        <div className="flex gap-2">
          <Button
            variant="flat"
            startContent={<AiOutlineCloudUpload />}
            onPress={handleOpenImport}
          >
            {t<string>("aigc.configs.import.button")}
          </Button>
          <Button color="primary" startContent={<AiOutlinePlus />} onPress={handleAdd}>
            {t<string>("common.action.add")}
          </Button>
        </div>
      </div>
      <p className="text-sm text-default-500 leading-relaxed">
        {t<string>("aigc.configs.intro")}
      </p>

      <Table removeWrapper>
        <TableHeader>
          <TableColumn>{t<string>("aigc.configs.name")}</TableColumn>
          <TableColumn>{t<string>("aigc.configs.provider")}</TableColumn>
          <TableColumn>{t<string>("aigc.configs.mediaType")}</TableColumn>
          <TableColumn>{t<string>("aigc.configs.resourceMode")}</TableColumn>
          <TableColumn>{t<string>("aigc.configs.status")}</TableColumn>
          <TableColumn>&nbsp;</TableColumn>
        </TableHeader>
        <TableBody emptyContent={t<string>("common.empty")}>
          {views.map((v) => (
            <TableRow key={v.generator.id}>
              <TableCell>{v.generator.name}</TableCell>
              <TableCell>{providerName(v.generator.providerId)}</TableCell>
              <TableCell>
                <Chip size="sm" variant="flat">{tMediaType(v.generator.mediaType)}</Chip>
              </TableCell>
              <TableCell>
                <Chip size="sm" variant="flat">{tResourceMode(v.generator.resourceMode)}</Chip>
              </TableCell>
              <TableCell>
                <div className="flex items-center gap-2">
                  <Chip
                    size="sm"
                    color={v.generator.isEnabled ? "success" : "default"}
                    variant="dot"
                  >
                    {v.generator.isEnabled
                      ? t<string>("common.enabled")
                      : t<string>("common.disabled")}
                  </Chip>
                  {v.propertyPresets.length > 0 && (
                    <Chip size="sm" variant="flat">
                      {t<string>("aigc.configs.presetCount", { count: v.propertyPresets.length })}
                    </Chip>
                  )}
                </div>
              </TableCell>
              <TableCell>
                <div className="flex gap-2 flex-wrap">
                  <Button
                    size="sm"
                    color="primary"
                    variant="flat"
                    startContent={<AiOutlinePlayCircle />}
                    onPress={() => handleTrigger(v.generator)}
                  >
                    {t<string>("aigc.configs.run")}
                  </Button>
                  <Button size="sm" variant="flat" onPress={() => handleEdit(v)}>
                    {t<string>("common.action.edit")}
                  </Button>
                  <Button
                    size="sm"
                    variant="flat"
                    color="danger"
                    startContent={<AiOutlineDelete />}
                    onPress={() => handleDelete(v.generator.id)}
                  >
                    {t<string>("common.action.delete")}
                  </Button>
                </div>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>

      <Modal isOpen={isOpen} onClose={onClose} size="3xl" scrollBehavior="inside">
        <ModalContent>
          <ModalHeader>
            {isEditMode
              ? t<string>("aigc.configs.edit")
              : t<string>("aigc.configs.add")}
          </ModalHeader>
          <ModalBody>
            {editing && (
              <div className="flex flex-col gap-3">
                {/* Row 1: Name + Provider */}
                <div className="grid grid-cols-2 gap-3">
                  <Input
                    label={t<string>("aigc.configs.name")}
                    size="sm"
                    isRequired
                    value={editing.name ?? ""}
                    onValueChange={(v) => setEditing({ ...editing, name: v })}
                  />
                  <Select
                    label={t<string>("aigc.configs.provider")}
                    size="sm"
                    isRequired
                    selectedKeys={editing.providerId ? [String(editing.providerId)] : []}
                    onSelectionChange={(keys) => {
                      const v = Array.from(keys)[0];
                      if (v) setEditing({ ...editing, providerId: Number(v) });
                    }}
                    dataSource={providers.map((p) => ({ label: p.name, value: String(p.id) }))}
                  />
                </div>

                {/* Row 2: Media Type + Resource Mode */}
                <div className="grid grid-cols-2 gap-3">
                  <Select
                    label={t<string>("aigc.configs.mediaType")}
                    size="sm"
                    selectedKeys={editing.mediaType ? [String(editing.mediaType)] : []}
                    onSelectionChange={(keys) => {
                      const v = Array.from(keys)[0];
                      if (v) setEditing({ ...editing, mediaType: Number(v) as any });
                    }}
                    dataSource={MEDIA_TYPES.map((k) => ({ label: tMediaType(k), value: String(k) }))}
                  />
                  <Select
                    label={t<string>("aigc.configs.resourceMode")}
                    size="sm"
                    selectedKeys={editing.resourceMode ? [String(editing.resourceMode)] : []}
                    onSelectionChange={(keys) => {
                      const v = Array.from(keys)[0];
                      if (v) setEditing({ ...editing, resourceMode: Number(v) as any });
                    }}
                    dataSource={RESOURCE_MODES.map((k) => ({ label: tResourceMode(k), value: String(k) }))}
                    description={t<string>("aigc.configs.resourceModeHelp")}
                  />
                </div>

                {/* Row 3: Filename + flags */}
                <div className="grid grid-cols-2 gap-3">
                  <Input
                    label={t<string>("aigc.configs.filenameTemplate")}
                    size="sm"
                    description={t<string>("aigc.configs.filenameTemplateHelp")}
                    value={editing.filenameTemplate ?? ""}
                    onValueChange={(v) => setEditing({ ...editing, filenameTemplate: v })}
                  />
                  <div className="flex items-center gap-6 pt-2">
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
                        isSelected={editing.allowDeletion ?? true}
                        onValueChange={(v) => setEditing({ ...editing, allowDeletion: v })}
                      />
                      <span className="text-sm">{t<string>("aigc.configs.allowDeletion")}</span>
                    </div>
                  </div>
                </div>

                {editingProviderKind === AiProviderKind.ComfyUI ? (
                  <div>
                    <div className="text-xs text-default-500 mb-1">
                      {t<string>("aigc.configs.workflowJson")}
                      <span className="ml-2 text-default-400">
                        {t<string>("aigc.configs.workflowJsonHelp")}
                      </span>
                    </div>
                    <JsonEditor
                      key={`gen-workflow-${editing.id ?? "new"}`}
                      value={comfyuiWorkflowText}
                      onChange={setComfyuiWorkflowText}
                      minLines={12}
                      maxLines={40}
                    />
                  </div>
                ) : (
                  <>
                    <Textarea
                      label={t<string>("aigc.configs.promptTemplate")}
                      size="sm"
                      minRows={2}
                      value={editing.promptTemplate ?? ""}
                      onValueChange={(v) => setEditing({ ...editing, promptTemplate: v })}
                    />
                    <Textarea
                      label={t<string>("aigc.configs.negativePromptTemplate")}
                      size="sm"
                      minRows={2}
                      value={editing.negativePromptTemplate ?? ""}
                      onValueChange={(v) => setEditing({ ...editing, negativePromptTemplate: v })}
                    />

                    <div>
                      <div className="text-xs text-default-500 mb-1">
                        {t<string>("aigc.configs.parametersJson")}
                        <span className="ml-2 text-default-400">
                          {t<string>("aigc.configs.parametersJsonHelp")}
                        </span>
                      </div>
                      <JsonEditor
                        key={`gen-params-${editingProviderKind ?? 0}-${editing.id ?? "new"}`}
                        value={editing.parametersJson ?? ""}
                        onChange={(v) => setEditing({ ...editing, parametersJson: v })}
                        sampleHeader={paramsSample?.header}
                        sampleBody={paramsSample?.body}
                      />
                    </div>

                    <div>
                      <div className="text-xs text-default-500 mb-1">
                        {t<string>("aigc.configs.propertyPresets")}
                      </div>
                      <JsonEditor
                        key={`gen-presets-${editing.id ?? "new"}`}
                        value={presetsText}
                        onChange={setPresetsText}
                        sampleHeader={presetSampleHeader}
                        sampleBody={presetSampleBody}
                      />
                    </div>
                  </>
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

      {/* Import */}
      <Modal isOpen={isImportOpen} onClose={onImportClose} size="2xl" scrollBehavior="inside">
        <ModalContent>
          <ModalHeader>
            {t<string>(`aigc.configs.import.kind.${AiProviderKindLabel[importKind]}.title`)}
          </ModalHeader>
          <ModalBody>
            <div className="flex flex-col gap-3">
              <Select
                label={t<string>("aigc.configs.import.sourceLabel")}
                size="sm"
                isRequired
                selectedKeys={[String(importKind)]}
                onSelectionChange={(keys) => {
                  const v = Array.from(keys)[0];
                  if (v) handleImportKindChange(Number(v) as AiProviderKind);
                }}
                dataSource={IMPORTABLE_KINDS.map((k) => ({
                  label: AiProviderKindLabel[k],
                  value: String(k),
                }))}
              />
              <p className="text-sm text-default-500">
                {t<string>(`aigc.configs.import.kind.${AiProviderKindLabel[importKind]}.description`)}
              </p>
              <Select
                label={t<string>(`aigc.configs.import.kind.${AiProviderKindLabel[importKind]}.providerLabel`)}
                size="sm"
                isRequired
                selectedKeys={importProviderId ? [String(importProviderId)] : []}
                onSelectionChange={(keys) => {
                  const v = Array.from(keys)[0];
                  if (v) setImportProviderId(Number(v));
                }}
                dataSource={providersForImportKind.map((p) => ({ label: p.name, value: String(p.id) }))}
              />
              <div className="flex flex-col gap-2">
                <div className="flex items-center justify-between">
                  <span className="text-xs text-default-500">
                    {t<string>("aigc.configs.import.pathsLabel")}
                  </span>
                  <Button
                    size="sm"
                    variant="flat"
                    startContent={<AiOutlineFolderOpen />}
                    onPress={handlePickImportPaths}
                  >
                    {t<string>("aigc.configs.import.pickPaths")}
                  </Button>
                </div>
                {importPaths.length === 0 ? (
                  <div className="text-xs text-default-400 italic px-2 py-1">
                    {t<string>("aigc.configs.import.noPathsYet")}
                  </div>
                ) : (
                  <div className="flex flex-col gap-1 max-h-64 overflow-y-auto">
                    {importPaths.map((p) => (
                      <div
                        key={p}
                        className="flex items-center justify-between gap-2 px-2 py-1 rounded bg-default-50"
                      >
                        <span className="text-xs font-mono truncate flex-1" title={p}>{p}</span>
                        <Button
                          size="sm"
                          isIconOnly
                          variant="light"
                          color="danger"
                          onPress={() => handleRemoveImportPath(p)}
                        >
                          <AiOutlineDelete />
                        </Button>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>
          </ModalBody>
          <ModalFooter>
            <Button variant="flat" onPress={onImportClose}>
              {t<string>("common.action.cancel")}
            </Button>
            <Button
              color="primary"
              isLoading={importInFlight}
              isDisabled={!importProviderId || importPaths.length === 0}
              onPress={handleSubmitImport}
            >
              {t<string>("aigc.configs.import.start")}
            </Button>
          </ModalFooter>
        </ModalContent>
      </Modal>

      {/* Import result */}
      <Modal
        isOpen={isImportResultOpen}
        onClose={onImportResultClose}
        size="2xl"
        scrollBehavior="inside"
      >
        <ModalContent>
          <ModalHeader>{t<string>("aigc.configs.import.resultTitle")}</ModalHeader>
          <ModalBody>
            {importResult && (
              <div className="flex flex-col gap-3">
                <div className="flex gap-3 flex-wrap">
                  <Chip color="success" variant="flat">
                    {t<string>("aigc.configs.import.imported", { count: importResult.importedCount })}
                  </Chip>
                  <Chip color="warning" variant="flat">
                    {t<string>("aigc.configs.import.skipped", { count: importResult.skippedCount })}
                  </Chip>
                  {importResult.failedCount > 0 && (
                    <Chip color="danger" variant="flat">
                      {t<string>("aigc.configs.import.failed", { count: importResult.failedCount })}
                    </Chip>
                  )}
                </div>
                <Table removeWrapper aria-label="import-result">
                  <TableHeader>
                    <TableColumn>{t<string>("aigc.configs.import.status")}</TableColumn>
                    <TableColumn>{t<string>("aigc.configs.import.path")}</TableColumn>
                    <TableColumn>{t<string>("aigc.configs.import.reason")}</TableColumn>
                  </TableHeader>
                  <TableBody emptyContent={t<string>("common.empty")}>
                    {(importResult.items as BakabaseModulesAIModelsInputAigcGeneratorComfyUIImportItemResult[]).map(
                      (it, idx) => (
                        <TableRow key={idx}>
                          <TableCell>
                            <Chip size="sm" color={importStatusColor(it.status)} variant="flat">
                              {t<string>(
                                it.status === 4 && importResultKind != null
                                  ? `aigc.configs.import.kind.${AiProviderKindLabel[importResultKind]}.status.4`
                                  : `aigc.configs.import.status.${it.status}`,
                              )}
                            </Chip>
                          </TableCell>
                          <TableCell>
                            <span className="text-xs font-mono break-all" title={it.path}>{it.path}</span>
                          </TableCell>
                          <TableCell>
                            <span className="text-xs text-default-500">{it.reason ?? ""}</span>
                          </TableCell>
                        </TableRow>
                      ),
                    )}
                  </TableBody>
                </Table>
              </div>
            )}
          </ModalBody>
          <ModalFooter>
            <Button color="primary" onPress={onImportResultClose}>
              {t<string>("common.action.close")}
            </Button>
          </ModalFooter>
        </ModalContent>
      </Modal>
    </div>
  );
};

const importStatusColor = (
  status: BakabaseModulesAIModelsInputAigcGeneratorComfyUIImportItemResult["status"],
): "success" | "warning" | "danger" | "default" => {
  switch (status) {
    case 1: return "success"; // Imported
    case 2: return "warning"; // SkippedDuplicate
    case 3:
    case 4: return "warning"; // SkippedInvalidJson / SkippedNotComfyUIWorkflow
    case 5: return "danger"; // Failed
    default: return "default";
  }
};

export default AigcConfigsPage;
