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
  AiOutlinePlus,
  AiOutlinePlayCircle,
  AiOutlineImport,
} from "react-icons/ai";

import { Select, toast } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import type {
  BakabaseModulesAIModelsDbAigcGeneratorDbModel,
  BakabaseModulesAIModelsDbAigcProviderConfigDbModel,
  BakabaseModulesAIModelsDomainAigcGeneratorView,
  BakabaseModulesAIModelsInputAigcGeneratorAddInputModel,
  BakabaseModulesAIModelsInputAigcGeneratorUpdateInputModel,
} from "@/sdk/Api";

type Generator = BakabaseModulesAIModelsDbAigcGeneratorDbModel;
type GeneratorView = BakabaseModulesAIModelsDomainAigcGeneratorView;
type Provider = BakabaseModulesAIModelsDbAigcProviderConfigDbModel;

const MediaTypeLabels: Record<number, string> = {
  1: "Image",
  2: "Text",
  3: "Audio",
  4: "Video",
  99: "Other",
};
const ResourceModeLabels: Record<number, string> = {
  1: "Per-Artifact",
  2: "Per-Run",
};

const AigcGeneratorsPage = () => {
  const { t } = useTranslation();
  const { isOpen, onOpen, onClose } = useDisclosure();

  const [views, setViews] = useState<GeneratorView[]>([]);
  const [providers, setProviders] = useState<Provider[]>([]);
  const [editing, setEditing] = useState<Partial<Generator> | null>(null);
  const [isEditMode, setIsEditMode] = useState(false);
  const [presetsText, setPresetsText] = useState("[]");

  const load = useCallback(async () => {
    const [gr, pr] = await Promise.all([
      BApi.aigc.getAllAigcGenerators(),
      BApi.aigc.getAllAigcProviders(),
    ]);
    if (!gr.code && gr.data) setViews(gr.data);
    if (!pr.code && pr.data) setProviders(pr.data);
  }, []);

  useEffect(() => {
    load();
  }, []);

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
    setPresetsText("[]");
    setIsEditMode(false);
    onOpen();
  };

  const handleEdit = (v: GeneratorView) => {
    setEditing({ ...v.generator });
    setPresetsText(JSON.stringify(v.propertyPresets, null, 2));
    setIsEditMode(true);
    onOpen();
  };

  const handleSave = async () => {
    if (!editing) return;
    if (!editing.name?.trim()) {
      toast.danger("Name is required");
      return;
    }
    if (!editing.providerId) {
      toast.danger("Provider is required");
      return;
    }

    let presets: any[] = [];
    try {
      presets = JSON.parse(presetsText);
      if (!Array.isArray(presets)) throw new Error("not an array");
    } catch (e) {
      toast.danger("Property presets is not valid JSON array");
      return;
    }
    // Translate to input shape
    const presetInputs = presets.map((p) => ({
      pool: p.pool,
      propertyId: p.propertyId,
      serializedBizValue: p.serializedBizValue ?? null,
    }));

    if (isEditMode && editing.id) {
      const r = await BApi.aigc.updateAigcGenerator(editing.id, {
        name: editing.name,
        providerId: editing.providerId,
        mediaType: editing.mediaType,
        promptTemplate: editing.promptTemplate,
        negativePromptTemplate: editing.negativePromptTemplate,
        parametersJson: editing.parametersJson,
        filenameTemplate: editing.filenameTemplate,
        resourceMode: editing.resourceMode,
        allowDeletion: editing.allowDeletion,
        isEnabled: editing.isEnabled,
        propertyPresets: presetInputs,
      } as BakabaseModulesAIModelsInputAigcGeneratorUpdateInputModel);
      if (!r.code) {
        toast.success("Saved");
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
        parametersJson: editing.parametersJson,
        filenameTemplate: editing.filenameTemplate ?? "{run}_{ordinal}_{timestamp}",
        resourceMode: editing.resourceMode ?? 1,
        allowDeletion: editing.allowDeletion ?? true,
        isEnabled: editing.isEnabled ?? true,
        propertyPresets: presetInputs,
      } as BakabaseModulesAIModelsInputAigcGeneratorAddInputModel);
      if (!r.code) {
        toast.success("Saved");
        onClose();
        await load();
      }
    }
  };

  const handleDelete = async (id: number) => {
    if (!confirm("Delete this generator? Existing artifacts and runs are kept.")) return;
    const r = await BApi.aigc.deleteAigcGenerator(id);
    if (!r.code) {
      toast.success("Deleted");
      await load();
    }
  };

  const handleTrigger = async (g: Generator) => {
    const promptOverride = window.prompt("Optional prompt override (leave empty to use template):") ?? undefined;
    const r = await BApi.aigc.triggerAigcGeneration(g.id, {
      promptOverride: promptOverride || undefined,
    } as any);
    if (!r.code && r.data) {
      toast.success(`Run #${r.data} queued`);
    }
  };

  const handleImport = async (g: Generator) => {
    const raw = window.prompt("Paste absolute file paths (one per line) to import as artifacts:");
    if (!raw) return;
    const paths = raw.split(/\r?\n/).map((s) => s.trim()).filter(Boolean);
    if (paths.length === 0) return;
    const r = await BApi.aigc.importAigcArtifacts(g.id, { sourceFilePaths: paths } as any);
    if (!r.code && r.data) {
      toast.success(`Imported as run #${r.data}`);
    }
  };

  const providerName = (id: number) => providers.find((p) => p.id === id)?.name ?? `#${id}`;

  return (
    <div className="flex flex-col gap-4 p-4">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold">AIGC Generators</h1>
        <Button color="primary" startContent={<AiOutlinePlus />} onPress={handleAdd}>
          {t<string>("common.action.add")}
        </Button>
      </div>

      <Table removeWrapper>
        <TableHeader>
          <TableColumn>Name</TableColumn>
          <TableColumn>Provider</TableColumn>
          <TableColumn>Media</TableColumn>
          <TableColumn>Mode</TableColumn>
          <TableColumn>Status</TableColumn>
          <TableColumn>&nbsp;</TableColumn>
        </TableHeader>
        <TableBody emptyContent={t<string>("common.empty")}>
          {views.map((v) => (
            <TableRow key={v.generator.id}>
              <TableCell>{v.generator.name}</TableCell>
              <TableCell>{providerName(v.generator.providerId)}</TableCell>
              <TableCell>
                <Chip size="sm" variant="flat">{MediaTypeLabels[v.generator.mediaType]}</Chip>
              </TableCell>
              <TableCell>
                <Chip size="sm" variant="flat">{ResourceModeLabels[v.generator.resourceMode]}</Chip>
              </TableCell>
              <TableCell>
                <Chip size="sm" color={v.generator.isEnabled ? "success" : "default"} variant="dot">
                  {v.generator.isEnabled ? "Enabled" : "Disabled"}
                </Chip>
                {v.propertyPresets.length > 0 && (
                  <Chip size="sm" variant="flat" className="ml-2">
                    {v.propertyPresets.length} preset(s)
                  </Chip>
                )}
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
                    Run
                  </Button>
                  <Button
                    size="sm"
                    variant="flat"
                    startContent={<AiOutlineImport />}
                    onPress={() => handleImport(v.generator)}
                  >
                    Import
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
            {isEditMode ? t<string>("common.action.edit") : t<string>("common.action.add")}
          </ModalHeader>
          <ModalBody>
            <div className="flex flex-col gap-3">
              <Input
                label="Name"
                value={editing?.name ?? ""}
                onValueChange={(v) => setEditing(editing ? { ...editing, name: v } : null)}
              />
              <Select
                label="Provider"
                selectedKeys={editing?.providerId ? [String(editing.providerId)] : []}
                onSelectionChange={(keys) => {
                  const v = Array.from(keys)[0];
                  if (v) setEditing(editing ? { ...editing, providerId: Number(v) } : null);
                }}
                dataSource={providers.map((p) => ({ label: p.name, value: String(p.id) }))}
              />
              <Select
                label="Media Type"
                selectedKeys={editing?.mediaType ? [String(editing.mediaType)] : []}
                onSelectionChange={(keys) => {
                  const v = Array.from(keys)[0];
                  if (v) setEditing(editing ? { ...editing, mediaType: Number(v) as any } : null);
                }}
                dataSource={Object.entries(MediaTypeLabels).map(([k, label]) => ({ label, value: k }))}
              />
              <Select
                label="Resource Mode"
                selectedKeys={editing?.resourceMode ? [String(editing.resourceMode)] : []}
                onSelectionChange={(keys) => {
                  const v = Array.from(keys)[0];
                  if (v) setEditing(editing ? { ...editing, resourceMode: Number(v) as any } : null);
                }}
                dataSource={Object.entries(ResourceModeLabels).map(([k, label]) => ({ label, value: k }))}
                description="Per-Artifact: each file = 1 resource. Per-Run: the run folder = 1 resource."
              />
              <Textarea
                label="Prompt template"
                value={editing?.promptTemplate ?? ""}
                onValueChange={(v) => setEditing(editing ? { ...editing, promptTemplate: v } : null)}
                minRows={2}
              />
              <Textarea
                label="Negative prompt template"
                value={editing?.negativePromptTemplate ?? ""}
                onValueChange={(v) => setEditing(editing ? { ...editing, negativePromptTemplate: v } : null)}
                minRows={2}
              />
              <Textarea
                label="Parameters (JSON)"
                description="Forwarded to the provider invoker. Examples: width/height/steps/seed/sampler_name/model"
                value={editing?.parametersJson ?? ""}
                onValueChange={(v) => setEditing(editing ? { ...editing, parametersJson: v } : null)}
                minRows={3}
                classNames={{ input: "font-mono text-xs" }}
              />
              <Input
                label="Filename template"
                description="Tokens: {run} {ordinal} {timestamp}"
                value={editing?.filenameTemplate ?? ""}
                onValueChange={(v) => setEditing(editing ? { ...editing, filenameTemplate: v } : null)}
              />
              <Textarea
                label="Property presets (JSON array)"
                description='[{"pool":2,"propertyId":1,"serializedBizValue":"..."}] — pool: 1=Internal, 2=Reserved, 3=Custom'
                value={presetsText}
                onValueChange={setPresetsText}
                minRows={4}
                classNames={{ input: "font-mono text-xs" }}
              />
              <div className="flex items-center gap-4">
                <div className="flex items-center gap-2">
                  <Switch
                    isSelected={editing?.isEnabled ?? true}
                    onValueChange={(v) => setEditing(editing ? { ...editing, isEnabled: v } : null)}
                  />
                  <span>Enabled</span>
                </div>
                <div className="flex items-center gap-2">
                  <Switch
                    isSelected={editing?.allowDeletion ?? true}
                    onValueChange={(v) => setEditing(editing ? { ...editing, allowDeletion: v } : null)}
                  />
                  <span>Allow deletion</span>
                </div>
              </div>
            </div>
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

export default AigcGeneratorsPage;
