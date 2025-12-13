"use client";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { SearchOutlined, PlusOutlined, EditOutlined, DeleteOutlined, CopyOutlined, EyeOutlined } from "@ant-design/icons";

import BApi from "@/sdk/BApi";
import { Button, Input, Table, TableHeader, TableBody, TableColumn, TableRow, TableCell, Chip, Tooltip } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import PathRuleModal from "./components/PathRuleModal";
import PathRulePreviewModal from "./components/PathRulePreviewModal";

interface PathRule {
  id: number;
  path: string;
  name?: string;
  description?: string;
  priority: number;
  isEnabled: boolean;
  marks?: PathMark[];
  createDt?: string;
  updateDt?: string;
}

interface PathMark {
  id: number;
  ruleId: number;
  type: number; // PathMarkType: 1=Resource, 2=Property
  priority: number;
  configJson: string;
}

const PathRulePage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [rules, setRules] = useState<PathRule[]>([]);
  const [keyword, setKeyword] = useState("");
  const [loading, setLoading] = useState(false);

  const loadRules = async () => {
    setLoading(true);
    try {
      // @ts-ignore - API will be available after SDK regeneration
      const rsp = await BApi.pathRule.getAllPathRules();
      // @ts-ignore
      setRules((rsp.data || []).sort((a, b) => a.priority - b.priority));
    } catch (e) {
      console.error("Failed to load path rules", e);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadRules();
  }, []);

  const filteredRules = rules.filter(
    (r) =>
      keyword === "" ||
      r.path?.toLowerCase().includes(keyword.toLowerCase()) ||
      r.name?.toLowerCase().includes(keyword.toLowerCase())
  );

  const handleDelete = async (id: number) => {
    if (!window.confirm(t("Are you sure you want to delete this path rule?"))) {
      return;
    }
    try {
      // @ts-ignore
      await BApi.pathRule.deletePathRule(id);
      loadRules();
    } catch (e) {
      console.error("Failed to delete path rule", e);
    }
  };

  const handleDuplicate = async (rule: PathRule) => {
    try {
      // @ts-ignore
      await BApi.pathRule.duplicatePathRule(rule.id);
      loadRules();
    } catch (e) {
      console.error("Failed to duplicate path rule", e);
    }
  };

  const handlePreview = (rule: PathRule) => {
    createPortal(PathRulePreviewModal, {
      rule,
    });
  };

  const columns = [
    {
      key: "priority",
      label: t("Priority"),
      width: 80,
    },
    {
      key: "name",
      label: t("Name"),
      width: 150,
    },
    {
      key: "path",
      label: t("Path"),
    },
    {
      key: "marks",
      label: t("Marks"),
      width: 120,
      render: (rule: PathRule) => (
        <div className="flex gap-1">
          <Chip size="sm" variant="flat" color="primary">
            {rule.marks?.filter(m => m.type === 1).length || 0} {t("Resource")}
          </Chip>
          <Chip size="sm" variant="flat" color="secondary">
            {rule.marks?.filter(m => m.type === 2).length || 0} {t("Property")}
          </Chip>
        </div>
      ),
    },
    {
      key: "isEnabled",
      label: t("Status"),
      width: 100,
      render: (rule: PathRule) => (
        <Chip
          size="sm"
          color={rule.isEnabled ? "success" : "default"}
          variant="flat"
        >
          {rule.isEnabled ? t("Enabled") : t("Disabled")}
        </Chip>
      ),
    },
    {
      key: "actions",
      label: t("Actions"),
      width: 180,
      render: (rule: PathRule) => (
        <div className="flex gap-1">
          <Tooltip content={t("Preview matched paths")}>
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={() => handlePreview(rule)}
            >
              <EyeOutlined />
            </Button>
          </Tooltip>
          <Tooltip content={t("Edit")}>
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={() => {
                createPortal(PathRuleModal, {
                  rule,
                  onSaved: loadRules,
                });
              }}
            >
              <EditOutlined />
            </Button>
          </Tooltip>
          <Tooltip content={t("Duplicate")}>
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={() => handleDuplicate(rule)}
            >
              <CopyOutlined />
            </Button>
          </Tooltip>
          <Tooltip content={t("Delete")}>
            <Button
              isIconOnly
              size="sm"
              variant="light"
              color="danger"
              onPress={() => handleDelete(rule.id)}
            >
              <DeleteOutlined />
            </Button>
          </Tooltip>
        </div>
      ),
    },
  ];

  return (
    <div>
      <div className="flex items-center justify-between gap-2 mb-4">
        <div className="flex items-center gap-2">
          <Button
            color="primary"
            size="sm"
            startContent={<PlusOutlined />}
            onPress={() => {
              createPortal(PathRuleModal, {
                onSaved: loadRules,
              });
            }}
          >
            {t("Add Path Rule")}
          </Button>
          <Input
            size="sm"
            placeholder={t("Search by path or name")}
            startContent={<SearchOutlined className="text-small" />}
            value={keyword}
            onValueChange={setKeyword}
            className="w-64"
          />
        </div>
        <div className="text-sm text-default-500">
          {t("Total")}: {filteredRules.length}
        </div>
      </div>

      <Table
        aria-label="Path Rules Table"
        isHeaderSticky
        classNames={{
          wrapper: "max-h-[calc(100vh-200px)]",
        }}
      >
        <TableHeader>
          {columns.map((column) => (
            <TableColumn
              key={column.key}
              width={column.width}
            >
              {column.label}
            </TableColumn>
          ))}
        </TableHeader>
        <TableBody isLoading={loading} emptyContent={t("No rules found")}>
          {filteredRules.map((rule) => (
            <TableRow key={rule.id}>
              {columns.map((column) => (
                <TableCell key={column.key}>
                  {column.render ? column.render(rule) : String(rule[column.key as keyof PathRule] ?? '')}
                </TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
};

PathRulePage.displayName = "PathRulePage";

export default PathRulePage;
