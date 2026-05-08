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
import { AiOutlineDelete, AiOutlinePlus } from "react-icons/ai";

import { Select, toast } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import type {
  BakabaseModulesAIModelsDbAigcProviderConfigDbModel,
  BakabaseModulesAIModelsDomainAigcProviderKindInfo,
  BakabaseModulesAIModelsInputAigcProviderConfigAddInputModel,
  BakabaseModulesAIModelsInputAigcProviderConfigUpdateInputModel,
} from "@/sdk/Api";

type AigcProvider = BakabaseModulesAIModelsDbAigcProviderConfigDbModel;
type AigcKindInfo = BakabaseModulesAIModelsDomainAigcProviderKindInfo;

const KindLabels: Record<number, string> = {
  1: "Stable Diffusion WebUI",
  2: "ComfyUI",
  3: "OpenAI Image",
  4: "Gemini Image (Nano Banana)",
  99: "Custom HTTP",
};

const AigcProvidersPage = () => {
  const { t } = useTranslation();
  const { isOpen, onOpen, onClose } = useDisclosure();

  const [providers, setProviders] = useState<AigcProvider[]>([]);
  const [kinds, setKinds] = useState<AigcKindInfo[]>([]);
  const [editing, setEditing] = useState<Partial<AigcProvider> | null>(null);
  const [isEditMode, setIsEditMode] = useState(false);
  const [testingIds, setTestingIds] = useState<Set<number>>(new Set());

  const load = useCallback(async () => {
    const [pr, kr] = await Promise.all([
      BApi.aigc.getAllAigcProviders(),
      BApi.aigc.getAigcProviderKinds(),
    ]);
    if (!pr.code && pr.data) setProviders(pr.data);
    if (!kr.code && kr.data) setKinds(kr.data);
  }, []);

  useEffect(() => {
    load();
  }, []);

  const kindInfo = (kind: number | undefined) => kinds.find((k) => k.kind === kind);

  const handleAdd = () => {
    setEditing({ name: "", endpoint: "", apiKey: "", isEnabled: true, configJson: "" });
    setIsEditMode(false);
    onOpen();
  };

  const handleEdit = (p: AigcProvider) => {
    setEditing({ ...p });
    setIsEditMode(true);
    onOpen();
  };

  const handleSave = async () => {
    if (!editing) return;
    if (!editing.kind) {
      toast.danger(t<string>("aigc.providers.kind") + " is required");
      return;
    }
    if (!editing.name?.trim()) {
      toast.danger(t<string>("aigc.providers.name") + " is required");
      return;
    }
    const ki = kindInfo(editing.kind);
    if (ki?.requiresApiKey && !editing.apiKey?.trim()) {
      toast.danger("API key is required for this provider");
      return;
    }
    if (ki?.requiresEndpoint && !editing.endpoint?.trim()) {
      toast.danger("Endpoint is required for this provider");
      return;
    }

    if (isEditMode && editing.id) {
      const r = await BApi.aigc.updateAigcProvider(editing.id, {
        kind: editing.kind,
        name: editing.name,
        endpoint: editing.endpoint,
        apiKey: editing.apiKey,
        configJson: editing.configJson,
        isEnabled: editing.isEnabled,
      } as BakabaseModulesAIModelsInputAigcProviderConfigUpdateInputModel);
      if (!r.code) {
        toast.success(t<string>("common.success.saved"));
        onClose();
        setEditing(null);
        await load();
      }
    } else {
      const r = await BApi.aigc.addAigcProvider({
        kind: editing.kind,
        name: editing.name!,
        endpoint: editing.endpoint,
        apiKey: editing.apiKey,
        configJson: editing.configJson,
        isEnabled: editing.isEnabled ?? true,
      } as BakabaseModulesAIModelsInputAigcProviderConfigAddInputModel);
      if (!r.code) {
        toast.success(t<string>("common.success.saved"));
        onClose();
        setEditing(null);
        await load();
      }
    }
  };

  const handleDelete = async (id: number) => {
    if (!confirm(t<string>("common.confirm.delete"))) return;
    const r = await BApi.aigc.deleteAigcProvider(id);
    if (!r.code) {
      toast.success(t<string>("common.success.saved"));
      await load();
    }
  };

  const handleTest = async (id: number) => {
    setTestingIds((prev) => new Set([...prev, id]));
    try {
      const r = await BApi.aigc.testAigcProvider(id);
      if (!r.code && r.data) toast.success("Connection OK");
      else toast.danger("Connection failed");
    } finally {
      setTestingIds((prev) => {
        const n = new Set(prev);
        n.delete(id);
        return n;
      });
    }
  };

  const handleKindChange = (kind: number) => {
    if (!editing) return;
    const ki = kindInfo(kind);
    setEditing({
      ...editing,
      kind: kind as AigcProvider["kind"],
      endpoint: ki?.defaultEndpoint ?? editing.endpoint ?? "",
    });
  };

  return (
    <div className="flex flex-col gap-4 p-4">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold">{t<string>("aigc.providers.title")}</h1>
        <Button color="primary" startContent={<AiOutlinePlus />} onPress={handleAdd}>
          {t<string>("common.action.add")}
        </Button>
      </div>

      <Table removeWrapper>
        <TableHeader>
          <TableColumn>{t<string>("aigc.providers.name")}</TableColumn>
          <TableColumn>{t<string>("aigc.providers.kind")}</TableColumn>
          <TableColumn>{t<string>("aigc.providers.endpoint")}</TableColumn>
          <TableColumn>{t<string>("aigc.providers.status")}</TableColumn>
          <TableColumn>&nbsp;</TableColumn>
        </TableHeader>
        <TableBody emptyContent={t<string>("common.empty")}>
          {providers.map((p) => (
            <TableRow key={p.id}>
              <TableCell>{p.name}</TableCell>
              <TableCell>
                <Chip size="sm" variant="flat">{KindLabels[p.kind] ?? p.kind}</Chip>
              </TableCell>
              <TableCell>
                <span className="font-mono text-xs">{p.endpoint || "-"}</span>
              </TableCell>
              <TableCell>
                <Chip size="sm" color={p.isEnabled ? "success" : "default"} variant="dot">
                  {p.isEnabled ? "Enabled" : "Disabled"}
                </Chip>
              </TableCell>
              <TableCell>
                <div className="flex gap-2">
                  <Button size="sm" variant="flat" onPress={() => handleEdit(p)}>
                    {t<string>("common.action.edit")}
                  </Button>
                  <Button
                    size="sm"
                    variant="flat"
                    isLoading={testingIds.has(p.id)}
                    onPress={() => handleTest(p.id)}
                  >
                    {testingIds.has(p.id) ? "Testing" : "Test"}
                  </Button>
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
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>

      <Modal isOpen={isOpen} onClose={onClose} size="2xl">
        <ModalContent>
          <ModalHeader>
            {isEditMode ? t<string>("common.action.edit") : t<string>("common.action.add")}
          </ModalHeader>
          <ModalBody>
            <div className="flex flex-col gap-3">
              <Select
                label={t<string>("aigc.providers.kind")}
                selectedKeys={editing?.kind ? [String(editing.kind)] : []}
                onSelectionChange={(keys) => {
                  const v = Array.from(keys)[0];
                  if (v) handleKindChange(Number(v));
                }}
                dataSource={kinds.map((k) => ({ label: k.displayName, value: String(k.kind) }))}
              />
              <Input
                label={t<string>("aigc.providers.name")}
                value={editing?.name ?? ""}
                onValueChange={(v) => setEditing(editing ? { ...editing, name: v } : null)}
              />
              <Input
                label={t<string>("aigc.providers.endpoint")}
                value={editing?.endpoint ?? ""}
                onValueChange={(v) => setEditing(editing ? { ...editing, endpoint: v } : null)}
                placeholder={kindInfo(editing?.kind)?.defaultEndpoint ?? ""}
              />
              <Input
                label="API Key"
                type="password"
                value={editing?.apiKey ?? ""}
                onValueChange={(v) => setEditing(editing ? { ...editing, apiKey: v } : null)}
              />
              <Textarea
                label={t<string>("aigc.providers.configJson")}
                description={t<string>("aigc.providers.configJsonHelp")}
                value={editing?.configJson ?? ""}
                onValueChange={(v) => setEditing(editing ? { ...editing, configJson: v } : null)}
                minRows={4}
                classNames={{ input: "font-mono text-xs" }}
              />
              <div className="flex items-center gap-2">
                <Switch
                  isSelected={editing?.isEnabled ?? true}
                  onValueChange={(v) => setEditing(editing ? { ...editing, isEnabled: v } : null)}
                />
                <span>Enabled</span>
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

export default AigcProvidersPage;
