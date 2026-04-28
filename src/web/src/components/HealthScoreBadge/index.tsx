import { AiOutlineRadarChart } from "react-icons/ai";

import { Chip } from "@/components/bakaui";

export type HealthScoreColor = "success" | "warning" | "danger" | "default";

/**
 * 70+ green, 40-70 yellow, &lt;40 red. <c>null</c>/<c>undefined</c> falls back
 * to the neutral color so callers can render the badge without branching.
 */
export const getHealthScoreColor = (
  score: number | null | undefined
): HealthScoreColor => {
  if (score == null) return "default";
  if (score >= 70) return "success";
  if (score >= 40) return "warning";
  return "danger";
};

interface Props {
  score: number | null | undefined;
  /** Render a radar icon to the left of the number. Default true. */
  showIcon?: boolean;
  onClick?: (e: React.MouseEvent) => void;
  className?: string;
  size?: "sm" | "md" | "lg";
}

/**
 * Unified health-score chip used wherever a resource's score is displayed.
 * Renders "—" (neutral chip) when the score is missing.
 */
export const HealthScoreBadge = ({
  score,
  showIcon = true,
  onClick,
  className,
  size = "sm",
}: Props) => {
  const color = getHealthScoreColor(score);
  const label = score == null ? "—" : Math.round(score).toString();

  return (
    <Chip
      className={`${onClick ? "cursor-pointer" : ""} ${className ?? ""}`.trim()}
      color={color}
      radius="sm"
      size={size}
      variant="flat"
      onClick={onClick}
    >
      <div className="flex items-center gap-1">
        {showIcon && <AiOutlineRadarChart />}
        {label}
      </div>
    </Chip>
  );
};

export default HealthScoreBadge;
