"use client";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Button,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Input,
  Switch,
  Chip,
  Modal,
  ModalContent,
  ModalHeader,
  ModalBody,
  ModalFooter,
  useDisclosure,
} from "@heroui/react";
import { AiOutlineDelete, AiOutlinePlus } from "react-icons/ai";

import { toast, Select } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import type {
  BakabaseModulesAIModelsDbLlmProviderConfigDbModel,
  BakabaseModulesAIModelsDomainLlmProviderTypeInfo,
  BakabaseModulesAIModelsDomainLlmModelInfo,
  BakabaseModulesAIModelsInputLlmProviderConfigAddInputModel,
  BakabaseModulesAIModelsInputLlmProviderConfigUpdateInputModel,
} from "@/sdk/Api";
import { LlmProviderTypeLabel, AiFeature, AiFeatureLabel } from "@/sdk/constants";
import AiFeatureConfigShortcut from "@/components/AiFeatureConfigShortcut";
import QuotaSettings from "./QuotaSettings";
import CacheSettings from "./CacheSettings";

type LlmProviderConfig = BakabaseModulesAIModelsDbLlmProviderConfigDbModel;
type LlmProviderTypeInfo = BakabaseModulesAIModelsDomainLlmProviderTypeInfo;
type LlmModelInfo = BakabaseModulesAIModelsDomainLlmModelInfo;

const AISettings = () => {
  const { t } = useTranslation();
  const { isOpen, onOpen, onClose } = useDisclosure();

  const [providers, setProviders] = useState<LlmProviderConfig[]>([]);
  const [providerTypes, setProviderTypes] = useState<LlmProviderTypeInfo[]>([]);
  const [editingProvider, setEditingProvider] = useState<Partial<LlmProviderConfig> | null>(null);
  const [isEditing, setIsEditing] = useState(false);
  const [testingIds, setTestingIds] = useState<Set<number>>(new Set());
  const [loadingModels, setLoadingModels] = useState<Record<number, boolean>>({});
  const [providerModels, setProviderModels] = useState<Record<number, LlmModelInfo[]>>({});

  const loadProviders = useCallback(async () => {
    const r = await BApi.ai.getAllLlmProviders();
    if (!r.code && r.data) {
      setProviders(r.data);
    }
  }, []);

  const loadProviderTypes = useCallback(async () => {
    const r = await BApi.ai.getLlmProviderTypes();
    if (!r.code && r.data) {
      setProviderTypes(r.data);
    }
  }, []);

  useEffect(() => {
    loadProviders();
    loadProviderTypes();
  }, []);

  const getProviderTypeInfo = (type: number): LlmProviderTypeInfo | undefined =>
    providerTypes.find((pt) => pt.type === type);

  const handleAdd = () => {
    setEditingProvider({
      name: "",
      endpoint: "",
      apiKey: "",
      isEnabled: true,
    });
    setIsEditing(false);
    onOpen();
  };

  const handleEdit = (provider: LlmProviderConfig) => {
    setEditingProvider({ ...provider });
    setIsEditing(true);
    onOpen();
  };

  const handleSave = async () => {
    if (!editingProvider) return;

    if (!editingProvider.providerType) {
      toast.danger(t<string>("configuration.ai.providerType") + " is required");
      return;
    }

    if (!editingProvider.name?.trim()) {
      toast.danger(t<string>("configuration.ai.providerName") + " is required");
      return;
    }

    const typeInfo = getProviderTypeInfo(editingProvider.providerType);
    if (typeInfo?.requiresEndpoint && !editingProvider.endpoint?.trim()) {
      toast.danger(t<string>("configuration.ai.endpoint") + " is required");
      return;
    }

    if (isEditing && editingProvider.id) {
      const r = await BApi.ai.updateLlmProvider(editingProvider.id, {
        providerType: editingProvider.providerType,
        name: editingProvider.name,
        endpoint: editingProvider.endpoint,
        apiKey: editingProvider.apiKey,
        isEnabled: editingProvider.isEnabled,
      } as BakabaseModulesAIModelsInputLlmProviderConfigUpdateInputModel);
      if (!r.code) {
        toast.success(t<string>("common.success.saved"));
        onClose();
        setEditingProvider(null);
        await loadProviders();
      }
    } else {
      const r = await BApi.ai.addLlmProvider({
        providerType: editingProvider.providerType,
        name: editingProvider.name!,
        endpoint: editingProvider.endpoint,
        apiKey: editingProvider.apiKey,
        isEnabled: editingProvider.isEnabled ?? true,
      } as BakabaseModulesAIModelsInputLlmProviderConfigAddInputModel);
      if (!r.code) {
        toast.success(t<string>("common.success.saved"));
        onClose();
        setEditingProvider(null);
        await loadProviders();
      }
    }
  };

  const handleDelete = async (id: number) => {
    const r = await BApi.ai.deleteLlmProvider(id);
    if (!r.code) {
      toast.success(t<string>("common.success.saved"));
      await loadProviders();
    }
  };

  const handleTest = async (id: number) => {
    setTestingIds((prev) => new Set([...prev, id]));
    try {
      const r = await BApi.ai.testLlmProvider(id);
      if (!r.code && r.data) {
        toast.success(t<string>("configuration.ai.testSuccess"));
      } else {
        toast.danger(t<string>("configuration.ai.testFailed"));
      }
    } catch {
      toast.danger(t<string>("configuration.ai.testFailed"));
    } finally {
      setTestingIds((prev) => {
        const next = new Set(prev);
        next.delete(id);
        return next;
      });
    }
  };

  const handleLoadModels = async (id: number) => {
    setLoadingModels((prev) => ({ ...prev, [id]: true }));
    try {
      const r = await BApi.ai.getLlmProviderModels(id);
      if (!r.code && r.data) {
        setProviderModels((prev) => ({ ...prev, [id]: r.data! }));
      }
    } finally {
      setLoadingModels((prev) => ({ ...prev, [id]: false }));
    }
  };

  const handleProviderTypeChange = (type: number) => {
    if (!editingProvider) return;
    const typeInfo = getProviderTypeInfo(type);
    setEditingProvider({
      ...editingProvider,
      providerType: type as LlmProviderConfig["providerType"],
      endpoint: typeInfo?.defaultEndpoint ?? editingProvider.endpoint ?? "",
    });
  };

  const renderProviderCell = (provider: LlmProviderConfig) => {
    const typeName = LlmProviderTypeLabel[provider.providerType] ?? "Unknown";
    const models = providerModels[provider.id];

    return (
      <div className="flex flex-col gap-2">
        <div className="flex items-center gap-2 flex-wrap">
          <Chip size="sm" variant="flat">{typeName}</Chip>
          <Chip size="sm" variant="dot" color={provider.isEnabled ? "success" : "default"}>
            {provider.isEnabled ? t<string>("configuration.ai.enabled") : t<string>("configuration.ai.disabled")}
          </Chip>
          {provider.endpoint && (
            <span className="text-xs text-default-400 font-mono">{provider.endpoint}</span>
          )}
        </div>

        {models && models.length > 0 && (
          <div className="flex flex-wrap gap-1">
            {models.map((m) => (
              <Chip key={m.modelId} size="sm" variant="flat">{m.displayName}</Chip>
            ))}
          </div>
        )}
        {models && models.length === 0 && (
          <div className="text-xs text-default-400">{t<string>("configuration.ai.noModels")}</div>
        )}

        <div className="flex gap-2">
          <Button size="sm" variant="flat" onPress={() => handleEdit(provider)}>
            {t<string>("common.action.edit")}
          </Button>
          <Button
            size="sm"
            variant="flat"
            isLoading={testingIds.has(provider.id)}
            onPress={() => handleTest(provider.id)}
          >
            {testingIds.has(provider.id)
              ? t<string>("configuration.ai.testing")
              : t<string>("configuration.ai.testConnection")}
          </Button>
          <Button
            size="sm"
            variant="flat"
            isLoading={loadingModels[provider.id]}
            onPress={() => handleLoadModels(provider.id)}
          >
            {t<string>("configuration.ai.loadModels")}
          </Button>
          <Button
            size="sm"
            variant="flat"
            color="danger"
            startContent={<AiOutlineDelete />}
            onPress={() => {
              if (confirm(t<string>("configuration.ai.deleteConfirm"))) {
                handleDelete(provider.id);
              }
            }}
          >
            {t<string>("common.action.delete")}
          </Button>
        </div>
      </div>
    );
  };

  return (
    <div className="group">
      <div className="settings">
        <Table removeWrapper>
          <TableHeader>
            <TableColumn width={200}>
              {t<string>("configuration.ai.providers")}
            </TableColumn>
            <TableColumn>&nbsp;</TableColumn>
          </TableHeader>
          <TableBody>
            {providers.map((provider) => (
              <TableRow key={provider.id} className="hover:bg-[var(--bakaui-overlap-background)]">
                <TableCell>
                  <div className="flex items-center">
                    {provider.name}
                  </div>
                </TableCell>
                <TableCell>{renderProviderCell(provider)}</TableCell>
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
                  {t<string>("configuration.ai.addProvider")}
                </Button>
              </TableCell>
              <TableCell>&nbsp;</TableCell>
            </TableRow>
          </TableBody>
        </Table>

        <Table removeWrapper className="mt-4">
          <TableHeader>
            <TableColumn width={200}>
              {t<string>("configuration.ai.scenarios")}
            </TableColumn>
            <TableColumn>&nbsp;</TableColumn>
          </TableHeader>
          <TableBody>
            {[AiFeature.Default, AiFeature.Enhancer, AiFeature.Translation, AiFeature.FileProcessor].map((feature) => (
              <TableRow key={`feature-${feature}`} className="hover:bg-[var(--bakaui-overlap-background)]">
                <TableCell>
                  <div className="flex items-center">
                    {t<string>(`configuration.ai.feature.${AiFeatureLabel[feature]}`)}
                  </div>
                </TableCell>
                <TableCell>
                  <AiFeatureConfigShortcut feature={feature} label={false} />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>

        <Table removeWrapper className="mt-4">
          <TableHeader>
            <TableColumn width={200}>
              {t<string>("configuration.ai.cache.title")}
            </TableColumn>
            <TableColumn>&nbsp;</TableColumn>
          </TableHeader>
          <TableBody>
            <TableRow>
              <TableCell>
                <span className="text-sm">{t("configuration.ai.cache.responseCache")}</span>
              </TableCell>
              <TableCell><CacheSettings /></TableCell>
            </TableRow>
          </TableBody>
        </Table>

        <Table removeWrapper className="mt-4">
          <TableHeader>
            <TableColumn width={200}>
              {t<string>("configuration.ai.quota.title")}
            </TableColumn>
            <TableColumn>&nbsp;</TableColumn>
          </TableHeader>
          <TableBody>
            <TableRow>
              <TableCell>
                <span className="text-sm">{t("configuration.ai.quota.auditContent")}</span>
              </TableCell>
              <TableCell><QuotaSettings /></TableCell>
            </TableRow>
          </TableBody>
        </Table>

      </div>

      {/* Add/Edit Provider Modal */}
      <Modal isOpen={isOpen} onClose={onClose} size="lg">
        <ModalContent>
          <ModalHeader>
            {isEditing
              ? t<string>("configuration.ai.editProvider")
              : t<string>("configuration.ai.addProvider")}
          </ModalHeader>
          <ModalBody>
            {editingProvider && (
              <div className="flex flex-col gap-4">
                <Input
                  label={t<string>("configuration.ai.providerName")}
                  size="sm"
                  value={editingProvider.name ?? ""}
                  isRequired
                  onValueChange={(v) =>
                    setEditingProvider({ ...editingProvider, name: v })
                  }
                />

                <Select
                  label={t<string>("configuration.ai.providerType")}
                  size="sm"
                  isRequired
                  dataSource={providerTypes.map((pt) => ({
                    label: pt.displayName,
                    value: String(pt.type),
                  }))}
                  selectedKeys={
                    editingProvider.providerType
                      ? [String(editingProvider.providerType)]
                      : undefined
                  }
                  onSelectionChange={(keys) => {
                    const arr = Array.from(keys);
                    if (arr.length > 0) handleProviderTypeChange(Number(arr[0]));
                  }}
                />

                {(() => {
                  const typeInfo = getProviderTypeInfo(
                    editingProvider.providerType ?? 1,
                  );
                  return (
                    <>
                      {typeInfo?.requiresEndpoint !== false && (
                        <Input
                          label={t<string>("configuration.ai.endpoint")}
                          size="sm"
                          isRequired={typeInfo?.requiresEndpoint ?? false}
                          placeholder={typeInfo?.defaultEndpoint ?? ""}
                          value={editingProvider.endpoint ?? ""}
                          onValueChange={(v) =>
                            setEditingProvider({
                              ...editingProvider,
                              endpoint: v,
                            })
                          }
                        />
                      )}

                      {typeInfo?.requiresApiKey && (
                        <Input
                          label={t<string>("configuration.ai.apiKey")}
                          size="sm"
                          type="password"
                          value={editingProvider.apiKey ?? ""}
                          onValueChange={(v) =>
                            setEditingProvider({
                              ...editingProvider,
                              apiKey: v,
                            })
                          }
                        />
                      )}
                    </>
                  );
                })()}

                <div className="flex items-center gap-2">
                  <Switch
                    size="sm"
                    isSelected={editingProvider.isEnabled ?? true}
                    onValueChange={(v) =>
                      setEditingProvider({ ...editingProvider, isEnabled: v })
                    }
                  />
                  <span className="text-sm">
                    {t<string>("configuration.ai.enabled")}
                  </span>
                </div>
              </div>
            )}
          </ModalBody>
          <ModalFooter>
            <Button size="sm" variant="flat" onPress={onClose}>
              {t<string>("common.action.cancel")}
            </Button>
            <Button color="primary" size="sm" onPress={handleSave}>
              {t<string>("common.action.save")}
            </Button>
          </ModalFooter>
        </ModalContent>
      </Modal>
    </div>
  );
};

AISettings.displayName = "AISettings";

export default AISettings;
