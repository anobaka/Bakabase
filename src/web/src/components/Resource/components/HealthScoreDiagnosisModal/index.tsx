import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { Chip, Modal, Spinner } from "@/components/bakaui";
import HealthScoreBadge from "@/components/HealthScoreBadge";
import BApi from "@/sdk/BApi";

interface MatchedRule {
  ruleId: number;
  ruleName?: string | null;
  delta: number;
}

interface DiagnosisRow {
  profileId: number;
  score: number;
  profileHash: string;
  matchedRulesJson?: string | null;
  evaluatedAt: string;
}

interface ProfileSummary {
  id: number;
  name: string;
  enabled?: boolean;
  priority?: number;
  baseScore?: number;
}

interface Props {
  resourceId: number;
  onClose?: () => void;
  onDestroyed?: () => void;
}

export const HealthScoreDiagnosisModal = ({ resourceId, onClose, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [rows, setRows] = useState<DiagnosisRow[]>();
  const [profiles, setProfiles] = useState<Record<number, ProfileSummary>>({});

  useEffect(() => {
    Promise.all([
      BApi.healthScore.getHealthScoreDiagnosisForResource(resourceId),
      BApi.healthScore.getAllHealthScoreProfiles(),
    ]).then(([diagRes, profileRes]) => {
      setRows((diagRes.data ?? []) as DiagnosisRow[]);
      const map: Record<number, ProfileSummary> = {};
      for (const p of profileRes.data ?? []) {
        if (p?.id != null) map[p.id] = p as ProfileSummary;
      }
      setProfiles(map);
    });
  }, [resourceId]);

  const renderRow = (row: DiagnosisRow) => {
    const profile = profiles[row.profileId];
    const matched: MatchedRule[] = (() => {
      if (!row.matchedRulesJson) return [];
      try { return JSON.parse(row.matchedRulesJson) ?? []; }
      catch { return []; }
    })();

    return (
      <div key={row.profileId} className="border border-default-200 rounded-md p-3 flex flex-col gap-2">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <span className="font-medium">{profile?.name ?? `#${row.profileId}`}</span>
            {profile?.enabled === false && (
              <Chip color="default" size="sm" variant="flat">
                {t<string>("healthScore.label.disabled")}
              </Chip>
            )}
            {profile?.priority != null && (
              <Chip size="sm" variant="flat">
                {t<string>("healthScore.label.priority")}: {profile.priority}
              </Chip>
            )}
          </div>
          <HealthScoreBadge score={row.score} showIcon={false} />
        </div>

        <div className="text-xs text-default-500">
          {t<string>("healthScore.diagnosis.matchedRules")}:
        </div>
        {matched.length === 0 ? (
          <div className="text-xs text-default-400 italic">
            {t<string>("healthScore.diagnosis.noMatched")}
          </div>
        ) : (
          <div className="flex flex-col gap-1">
            {matched.map((m, i) => (
              <div key={i} className="flex items-center gap-2 text-sm">
                <Chip
                  color={m.delta < 0 ? "danger" : "success"}
                  size="sm"
                  variant="flat"
                >
                  {m.delta > 0 ? `+${m.delta}` : m.delta}
                </Chip>
                <span>{m.ruleName ?? `Rule #${m.ruleId}`}</span>
              </div>
            ))}
          </div>
        )}

        <div className="text-xs text-default-400">
          {t<string>("healthScore.diagnosis.evaluatedAt")}: {new Date(row.evaluatedAt).toLocaleString()}
        </div>
      </div>
    );
  };

  return (
    <Modal
      defaultVisible
      footer={false}
      size="2xl"
      title={t<string>("healthScore.diagnosis.title")}
      onClose={onClose}
      onDestroyed={onDestroyed}
    >
      {!rows ? (
        <Spinner />
      ) : rows.length === 0 ? (
        <div className="text-default-500 text-sm py-6 text-center">
          {t<string>("healthScore.diagnosis.empty")}
        </div>
      ) : (
        <div className="flex flex-col gap-3">
          {rows.map(renderRow)}
        </div>
      )}
    </Modal>
  );
};

export default HealthScoreDiagnosisModal;
