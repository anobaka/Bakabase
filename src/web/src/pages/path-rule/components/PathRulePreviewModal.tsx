"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { FolderOutlined, FileOutlined, LoadingOutlined } from "@ant-design/icons";

import { Modal, Input, Chip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

interface PathMark {
  type: number;
  priority: number;
  configJson: string;
}

interface PathRule {
  id?: number;
  path: string;
  name?: string;
  description?: string;
  priority: number;
  isEnabled: boolean;
  marks?: PathMark[];
}

type Props = {
  rule: PathRule;
} & DestroyableProps;

const PathRulePreviewModal = ({ rule, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [matchedPaths, setMatchedPaths] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [keyword, setKeyword] = useState("");

  useEffect(() => {
    const loadPreview = async () => {
      setLoading(true);
      try {
        // @ts-ignore - API will be available after SDK regeneration
        const rsp = await BApi.pathRule.previewPathRuleMatchedPaths(rule);
        // @ts-ignore
        setMatchedPaths(rsp.data || []);
      } catch (e) {
        console.error("Failed to preview matched paths", e);
      } finally {
        setLoading(false);
      }
    };

    loadPreview();
  }, [rule]);

  const filteredPaths = matchedPaths.filter(
    (p) => keyword === "" || p.toLowerCase().includes(keyword.toLowerCase())
  );

  return (
    <Modal
      defaultVisible
      size="3xl"
      title={
        <div className="flex items-center gap-2">
          {t("Preview Matched Paths")}
          {rule.name && (
            <Chip size="sm" variant="flat" color="primary">
              {rule.name}
            </Chip>
          )}
        </div>
      }
      footer={false}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-4">
        <div className="flex items-center justify-between">
          <div className="text-sm text-default-500">
            {t("Rule Path")}: <span className="font-mono">{rule.path}</span>
          </div>
          <div className="text-sm text-default-500">
            {t("Total")}: {filteredPaths.length}
          </div>
        </div>

        <Input
          size="sm"
          placeholder={t("Filter paths...")}
          value={keyword}
          onValueChange={setKeyword}
          className="max-w-sm"
        />

        {loading ? (
          <div className="flex items-center justify-center py-8">
            <LoadingOutlined className="text-2xl mr-2" />
            <span>{t("Loading matched paths...")}</span>
          </div>
        ) : filteredPaths.length === 0 ? (
          <div className="text-center text-default-400 py-8">
            {keyword ? t("No paths match your filter") : t("No paths matched by this rule")}
          </div>
        ) : (
          <div className="max-h-[400px] overflow-y-auto border rounded-lg">
            {filteredPaths.map((path, index) => (
              <div
                key={index}
                className="flex items-center gap-2 px-3 py-2 border-b last:border-b-0 hover:bg-default-100"
              >
                <FolderOutlined className="text-default-400" />
                <span className="font-mono text-sm truncate" title={path}>
                  {path}
                </span>
              </div>
            ))}
          </div>
        )}
      </div>
    </Modal>
  );
};

PathRulePreviewModal.displayName = "PathRulePreviewModal";

export default PathRulePreviewModal;
