"use client";

import { useCallback, useEffect, useState } from "react";
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
  AiOutlineDelete,
  AiOutlineImport,
  AiOutlinePlayCircle,
  AiOutlinePlus,
} from "react-icons/ai";

import { Select, toast } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import type {
  BakabaseModulesAIModelsDbAigcGeneratorDbModel,
  BakabaseModulesAIModelsDbAiProviderDbModel,
  BakabaseModulesAIModelsDomainAigcGeneratorView,
  BakabaseModulesAIModelsInputAigcGeneratorAddInputModel,
  BakabaseModulesAIModelsInputAigcGeneratorUpdateInputModel,
} from "@/sdk/Api";
import JsonEditor, { stripJsonComments } from "@/components/JsonEditor";
import { AigcGeneratorParametersSamples } from "@/components/AiProviderPanel/samples";

type Generator = BakabaseModulesAIModelsDbAigcGeneratorDbModel;
type GeneratorView = BakabaseModulesAIModelsDomainAigcGeneratorView;
type Provider = BakabaseModulesAIModelsDbAiProviderDbModel;

const MEDIA_TYPES = [1, 2, 3, 4, 99];
const RESOURCE_MODES = [1, 2];

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

const AigcConfigsPage = () => {
  const { t } = useTranslation();
  const { isOpen, onOpen, onClose } = useDisclosure();

  const [views, setViews] = useState<GeneratorView[]>([]);
  const [providers, setProviders] = useState<Provider[]>([]);
  const [editing, setEditing] = useState<Partial<Generator> | null>(null);
  const [isEditMode, setIsEditMode] = useState(false);
  const [presetsText, setPresetsText] = useState("");

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
    setIsEditMode(false);
    onOpen();
  };

  const handleEdit = (v: GeneratorView) => {
    setEditing({ ...v.generator });
    setPresetsText(v.propertyPresets.length > 0 ? JSON.stringify(v.propertyPresets, null, 2) : "");
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
    const presetResult = buildPresetInputs();
    if (!presetResult.ok) return;

    const cleanParams = editing.parametersJson ? stripJsonComments(editing.parametersJson) : null;

    if (isEditMode && editing.id) {
      const r = await BApi.aigc.updateAigcGenerator(editing.id, {
        name: editing.name,
        providerId: editing.providerId,
        mediaType: editing.mediaType,
        promptTemplate: editing.promptTemplate,
        negativePromptTemplate: editing.negativePromptTemplate,
        parametersJson: cleanParams,
        filenameTemplate: editing.filenameTemplate,
        resourceMode: editing.resourceMode,
        allowDeletion: editing.allowDeletion,
        isEnabled: editing.isEnabled,
        propertyPresets: presetResult.presets,
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
        promptTemplate: editing.promptTemplate,
        negativePromptTemplate: editing.negativePromptTemplate,
        parametersJson: cleanParams,
        filenameTemplate: editing.filenameTemplate ?? "{run}_{ordinal}_{timestamp}",
        resourceMode: editing.resourceMode ?? 1,
        allowDeletion: editing.allowDeletion ?? true,
        isEnabled: editing.isEnabled ?? true,
        propertyPresets: presetResult.presets,
      } as BakabaseModulesAIModelsInputAigcGeneratorAddInputModel);
      if (!r.code) {
        toast.success(t<string>("common.success.saved"));
        onClose();
        await load();
      }
    }
  };

  const handleDelete = async (id: number) => {
    if (!confirm(t<string>("aigc.configs.confirmDelete"))) return;
    const r = await BApi.aigc.deleteAigcGenerator(id);
    if (!r.code) {
      toast.success(t<string>("common.success.saved"));
      await load();
    }
  };

  const handleTrigger = async (g: Generator) => {
    const promptOverride =
      window.prompt(t<string>("aigc.configs.promptOverridePrompt")) ?? undefined;
    const r = await BApi.aigc.triggerAigcGeneration(g.id, {
      promptOverride: promptOverride || undefined,
    } as any);
    if (!r.code && r.data) {
      toast.success(t<string>("aigc.configs.runQueued", { id: r.data }));
    }
  };

  const handleImport = async (g: Generator) => {
    const raw = window.prompt(t<string>("aigc.configs.importPrompt"));
    if (!raw) return;
    const paths = raw
      .split(/\r?\n/)
      .map((s) => s.trim())
      .filter(Boolean);
    if (paths.length === 0) return;
    const r = await BApi.aigc.importAigcArtifacts(g.id, { sourceFilePaths: paths } as any);
    if (!r.code && r.data) {
      toast.success(t<string>("aigc.configs.importedAsRun", { id: r.data }));
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
        <Button color="primary" startContent={<AiOutlinePlus />} onPress={handleAdd}>
          {t<string>("common.action.add")}
        </Button>
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
                  <Button
                    size="sm"
                    variant="flat"
                    startContent={<AiOutlineImport />}
                    onPress={() => handleImport(v.generator)}
                  >
                    {t<string>("aigc.configs.import")}
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
    </div>
  );
};

export default AigcConfigsPage;
