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
import JsonEditor, { stripJsonComments } from "@/components/JsonEditor";

import { AigcProviderConfigSamples } from "./samples";

type AigcProvider = BakabaseModulesAIModelsDbAigcProviderConfigDbModel;
type AigcKindInfo = BakabaseModulesAIModelsDomainAigcProviderKindInfo;

const AigcProviderPanel = () => {
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
  const kindLabel = (kind: number) => kinds.find((k) => k.kind === kind)?.displayName ?? `#${kind}`;

  const handleAdd = () => {
    setEditing({
      kind: kinds[0]?.kind,
      name: "",
      endpoint: kinds[0]?.defaultEndpoint ?? "",
      apiKey: "",
      isEnabled: true,
      configJson: "",
    });
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
      toast.danger(t<string>("aigc.providers.error.kindRequired"));
      return;
    }
    if (!editing.name?.trim()) {
      toast.danger(t<string>("aigc.providers.error.nameRequired"));
      return;
    }
    const ki = kindInfo(editing.kind);
    if (ki?.requiresApiKey && !editing.apiKey?.trim()) {
      toast.danger(t<string>("aigc.providers.error.apiKeyRequired"));
      return;
    }
    if (ki?.requiresEndpoint && !editing.endpoint?.trim()) {
      toast.danger(t<string>("aigc.providers.error.endpointRequired"));
      return;
    }

    const cleanConfig = editing.configJson ? stripJsonComments(editing.configJson) : null;

    if (isEditMode && editing.id) {
      const r = await BApi.aigc.updateAigcProvider(editing.id, {
        kind: editing.kind,
        name: editing.name,
        endpoint: editing.endpoint,
        apiKey: editing.apiKey,
        configJson: cleanConfig,
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
        configJson: cleanConfig,
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
    if (!confirm(t<string>("aigc.providers.confirmDelete"))) return;
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
      if (!r.code && r.data) toast.success(t<string>("aigc.providers.testSuccess"));
      else toast.danger(t<string>("aigc.providers.testFailed"));
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
      configJson: editing.id ? editing.configJson : "",
    });
  };

  const sample = editing?.kind ? AigcProviderConfigSamples[editing.kind] : undefined;

  return (
    <>
      <Table removeWrapper>
        <TableHeader>
          <TableColumn width={200}>{t<string>("aigc.providers.title")}</TableColumn>
          <TableColumn>&nbsp;</TableColumn>
        </TableHeader>
        <TableBody>
          {providers.map((p) => (
            <TableRow key={p.id} className="hover:bg-[var(--bakaui-overlap-background)]">
              <TableCell>
                <div className="flex items-center font-medium">{p.name}</div>
              </TableCell>
              <TableCell>
                <div className="flex items-center gap-2 flex-wrap">
                  <Chip size="sm" variant="flat">{kindLabel(p.kind)}</Chip>
                  <Chip size="sm" color={p.isEnabled ? "success" : "default"} variant="dot">
                    {p.isEnabled ? t<string>("common.enabled") : t<string>("common.disabled")}
                  </Chip>
                  {p.endpoint && (
                    <span className="font-mono text-xs text-default-400">{p.endpoint}</span>
                  )}
                  <div className="ml-auto flex gap-2">
                    <Button size="sm" variant="flat" onPress={() => handleEdit(p)}>
                      {t<string>("common.action.edit")}
                    </Button>
                    <Button
                      size="sm"
                      variant="flat"
                      isLoading={testingIds.has(p.id)}
                      onPress={() => handleTest(p.id)}
                    >
                      {testingIds.has(p.id)
                        ? t<string>("aigc.providers.testing")
                        : t<string>("aigc.providers.testConnection")}
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
                </div>
              </TableCell>
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
                {t<string>("aigc.providers.add")}
              </Button>
            </TableCell>
            <TableCell>&nbsp;</TableCell>
          </TableRow>
        </TableBody>
      </Table>

      <Modal isOpen={isOpen} onClose={onClose} size="3xl" scrollBehavior="inside">
        <ModalContent>
          <ModalHeader>
            {isEditMode
              ? t<string>("aigc.providers.edit")
              : t<string>("aigc.providers.add")}
          </ModalHeader>
          <ModalBody>
            {editing && (
              <div className="flex flex-col gap-3">
                {/* Row 1: Kind + Name */}
                <div className="grid grid-cols-2 gap-3">
                  <Select
                    label={t<string>("aigc.providers.kind")}
                    size="sm"
                    selectedKeys={editing.kind ? [String(editing.kind)] : []}
                    onSelectionChange={(keys) => {
                      const v = Array.from(keys)[0];
                      if (v) handleKindChange(Number(v));
                    }}
                    dataSource={kinds.map((k) => ({ label: k.displayName, value: String(k.kind) }))}
                  />
                  <Input
                    label={t<string>("aigc.providers.name")}
                    size="sm"
                    value={editing.name ?? ""}
                    isRequired
                    onValueChange={(v) => setEditing({ ...editing, name: v })}
                  />
                </div>

                {/* Row 2: Endpoint + API Key */}
                <div className="grid grid-cols-2 gap-3">
                  <Input
                    label={t<string>("aigc.providers.endpoint")}
                    size="sm"
                    isRequired={kindInfo(editing.kind)?.requiresEndpoint}
                    placeholder={kindInfo(editing.kind)?.defaultEndpoint ?? ""}
                    value={editing.endpoint ?? ""}
                    onValueChange={(v) => setEditing({ ...editing, endpoint: v })}
                  />
                  <Input
                    label={t<string>("aigc.providers.apiKey")}
                    size="sm"
                    type="password"
                    isRequired={kindInfo(editing.kind)?.requiresApiKey}
                    value={editing.apiKey ?? ""}
                    onValueChange={(v) => setEditing({ ...editing, apiKey: v })}
                  />
                </div>

                {/* Row 3: Enabled */}
                <div className="flex items-center gap-2">
                  <Switch
                    isSelected={editing.isEnabled ?? true}
                    onValueChange={(v) => setEditing({ ...editing, isEnabled: v })}
                  />
                  <span className="text-sm">{t<string>("common.enabled")}</span>
                </div>

                {/* Config JSON editor */}
                <div>
                  <div className="text-xs text-default-500 mb-1">
                    {t<string>("aigc.providers.configJson")}
                    <span className="ml-2 text-default-400">
                      {t<string>("aigc.providers.configJsonHelp")}
                    </span>
                  </div>
                  <JsonEditor
                    key={`provider-config-${editing.kind ?? 0}-${editing.id ?? "new"}`}
                    value={editing.configJson ?? ""}
                    onChange={(v) => setEditing({ ...editing, configJson: v })}
                    sampleHeader={sample?.header}
                    sampleBody={sample?.body}
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
    </>
  );
};

export default AigcProviderPanel;
