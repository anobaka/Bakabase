"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { CloseOutlined, ReloadOutlined } from "@ant-design/icons";

import {
  Modal,
  Spinner,
  Chip,
  Button,
  Tooltip,
  Divider,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BriefProperty from "@/components/Chips/Property/BriefProperty";
import type { PropertyPool } from "@/sdk/constants";

// Types for the pairs response
interface RuleScoreDetail {
  ruleId: number;
  order: number;
  score: number;
  weight: number;
  value1?: string;
  value2?: string;
  isSkipped: boolean;
  isVetoed: boolean;
}

interface ComparisonResultPair {
  id: number;
  groupId: number;
  resource1Id: number;
  resource2Id: number;
  totalScore: number;
  ruleScores?: RuleScoreDetail[];
}

interface ComparisonResultPairsResponse {
  pairs: ComparisonResultPair[];
  totalCount: number;
  isTruncated: boolean;
  limit: number;
}

interface ResourceNode {
  id: number;
  displayName: string;
  x: number;
  y: number;
  vx: number;
  vy: number;
}

interface Edge {
  source: number;
  target: number;
  score: number;
  pair: ComparisonResultPair;
}

interface Transform {
  scale: number;
  offsetX: number;
  offsetY: number;
}

interface ComparisonNetworkGraphProps {
  planId: number;
  groupId: number;
  threshold: number;
  isOpen: boolean;
  onClose: () => void;
}

const PAIR_LIMIT = 1000;
const CANVAS_WIDTH = 1200;
const CANVAS_HEIGHT = 600;
const MIN_SCALE = 0.1;
const MAX_SCALE = 20;
const ZOOM_SENSITIVITY = 0.001;

// Rule info for displaying property in tooltip
interface RuleInfo {
  id: number;
  order: number;
  propertyPool: PropertyPool;
  propertyId: number;
  propertyName?: string;
}

// Run force-directed layout simulation synchronously to calculate final positions
function computeForceLayout(
  nodes: ResourceNode[],
  edges: Edge[],
  iterations: number = 300
): ResourceNode[] {
  if (nodes.length === 0) return nodes;

  const width = CANVAS_WIDTH;
  const height = CANVAS_HEIGHT;
  const centerX = width / 2;
  const centerY = height / 2;

  const nodeMap = new Map(nodes.map((n) => [n.id, n]));

  for (let i = 0; i < iterations; i++) {
    const alpha = 0.1 * (1 - i / iterations);
    const repulsionStrength = 800;
    const attractionStrength = 0.02;
    const centerStrength = 0.01;

    nodes.forEach((node) => {
      node.vx = 0;
      node.vy = 0;
    });

    for (let j = 0; j < nodes.length; j++) {
      for (let k = j + 1; k < nodes.length; k++) {
        const nodeA = nodes[j];
        const nodeB = nodes[k];
        const dx = nodeB.x - nodeA.x;
        const dy = nodeB.y - nodeA.y;
        const distance = Math.sqrt(dx * dx + dy * dy) || 1;
        const force = repulsionStrength / (distance * distance);
        const fx = (dx / distance) * force;
        const fy = (dy / distance) * force;
        nodeA.vx -= fx;
        nodeA.vy -= fy;
        nodeB.vx += fx;
        nodeB.vy += fy;
      }
    }

    edges.forEach((edge) => {
      const sourceNode = nodeMap.get(edge.source);
      const targetNode = nodeMap.get(edge.target);
      if (sourceNode && targetNode) {
        const dx = targetNode.x - sourceNode.x;
        const dy = targetNode.y - sourceNode.y;
        const distance = Math.sqrt(dx * dx + dy * dy) || 1;
        const force = distance * attractionStrength * (edge.score / 100);
        const fx = (dx / distance) * force;
        const fy = (dy / distance) * force;
        sourceNode.vx += fx;
        sourceNode.vy += fy;
        targetNode.vx -= fx;
        targetNode.vy -= fy;
      }
    });

    nodes.forEach((node) => {
      node.vx += (centerX - node.x) * centerStrength;
      node.vy += (centerY - node.y) * centerStrength;
    });

    nodes.forEach((node) => {
      node.x += node.vx * alpha;
      node.y += node.vy * alpha;
      node.x = Math.max(60, Math.min(width - 60, node.x));
      node.y = Math.max(60, Math.min(height - 60, node.y));
    });
  }

  return nodes;
}

const ComparisonNetworkGraph = ({
  planId,
  groupId,
  threshold,
  isOpen,
  onClose,
}: ComparisonNetworkGraphProps) => {
  const { t } = useTranslation();
  const { isDarkMode } = useBakabaseContext();
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  const [loading, setLoading] = useState(true);
  const [nodes, setNodes] = useState<ResourceNode[]>([]);
  const [edges, setEdges] = useState<Edge[]>([]);
  const [rules, setRules] = useState<RuleInfo[]>([]);
  const [isTruncated, setIsTruncated] = useState(false);
  const [totalCount, setTotalCount] = useState(0);
  const [hoveredEdge, setHoveredEdge] = useState<Edge | null>(null);
  const [pinnedEdge, setPinnedEdge] = useState<Edge | null>(null);
  const [tooltipPosition, setTooltipPosition] = useState({ x: 0, y: 0 });

  const [transform, setTransform] = useState<Transform>({
    scale: 1,
    offsetX: 0,
    offsetY: 0,
  });

  const [isDragging, setIsDragging] = useState(false);
  const [dragStart, setDragStart] = useState({ x: 0, y: 0 });

  const activeEdge = pinnedEdge || hoveredEdge;

  // Theme-aware colors
  const colors = {
    background: isDarkMode ? "#1a1a2e" : "#ffffff",
    nodeColor: isDarkMode ? "#60a5fa" : "#3b82f6",
    nodeStroke: isDarkMode ? "#93c5fd" : "#1d4ed8",
    edgeColor: isDarkMode ? "rgba(156, 163, 175, " : "rgba(100, 100, 100, ",
    edgeActiveColor: isDarkMode ? "rgba(96, 165, 250, " : "rgba(59, 130, 246, ",
    textColor: isDarkMode ? "#e5e7eb" : "#333333",
  };

  // Get resource displayName by ID
  const getResourceDisplayName = useCallback(
    (resourceId: number): string => {
      const node = nodes.find((n) => n.id === resourceId);
      return node?.displayName || `Resource ${resourceId}`;
    },
    [nodes]
  );

  const resetTransform = useCallback(() => {
    setTransform({ scale: 1, offsetX: 0, offsetY: 0 });
  }, []);

  const loadData = useCallback(async () => {
    setLoading(true);
    setPinnedEdge(null);
    setHoveredEdge(null);
    resetTransform();
    try {
      // Fetch plan to get rules with property info
      const planResponse = await BApi.comparison.getComparisonPlan(planId);
      const planRules = planResponse.data?.rules || [];
      setRules(planRules as RuleInfo[]);

      const pairsResponse = await BApi.comparison.getComparisonResultGroupPairs(
        planId,
        groupId,
        { limit: PAIR_LIMIT }
      );

      const data = pairsResponse as unknown as ComparisonResultPairsResponse;
      const pairs = data.pairs || [];

      setIsTruncated(data.isTruncated);
      setTotalCount(data.totalCount);

      const resourceIds = await BApi.comparison.getComparisonResultGroupResourceIds(
        planId,
        groupId
      );
      const ids = resourceIds.data || [];

      const resourcesResponse = await BApi.resource.getResourcesByKeys({
        ids,
        additionalItems: 0,
      });
      const resources = resourcesResponse.data || [];

      const edgeList: Edge[] = pairs
        .filter((pair) => pair.totalScore >= threshold)
        .map((pair) => ({
          source: pair.resource1Id,
          target: pair.resource2Id,
          score: pair.totalScore,
          pair,
        }));

      const resourceMap = new Map(resources.map((r) => [r.id, r]));
      const centerX = CANVAS_WIDTH / 2;
      const centerY = CANVAS_HEIGHT / 2;
      const radius = Math.min(CANVAS_WIDTH, CANVAS_HEIGHT) / 2 - 100;

      const nodeList: ResourceNode[] = ids.map((id, index) => {
        const resource = resourceMap.get(id);
        const angle = (2 * Math.PI * index) / ids.length - Math.PI / 2;
        return {
          id,
          displayName: resource?.displayName || `Resource ${id}`,
          x: centerX + radius * Math.cos(angle),
          y: centerY + radius * Math.sin(angle),
          vx: 0,
          vy: 0,
        };
      });

      const layoutedNodes = computeForceLayout(nodeList, edgeList, 300);

      setNodes(layoutedNodes);
      setEdges(edgeList);
    } finally {
      setLoading(false);
    }
  }, [planId, groupId, threshold, resetTransform]);

  useEffect(() => {
    if (isOpen) {
      loadData();
    }
  }, [isOpen, loadData]);

  // Canvas rendering with theme colors
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas || loading) return;

    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    const nodeMap = new Map(nodes.map((n) => [n.id, n]));

    // Clear canvas with theme background
    ctx.fillStyle = colors.background;
    ctx.fillRect(0, 0, canvas.width, canvas.height);

    ctx.save();
    ctx.translate(transform.offsetX, transform.offsetY);
    ctx.scale(transform.scale, transform.scale);

    // Draw edges
    edges.forEach((edge) => {
      const sourceNode = nodeMap.get(edge.source);
      const targetNode = nodeMap.get(edge.target);
      if (!sourceNode || !targetNode) return;

      const isActive = activeEdge === edge;
      const opacity = Math.max(0.3, edge.score / 100);

      ctx.beginPath();
      ctx.moveTo(sourceNode.x, sourceNode.y);
      ctx.lineTo(targetNode.x, targetNode.y);
      ctx.strokeStyle = isActive
        ? `${colors.edgeActiveColor}${opacity})`
        : `${colors.edgeColor}${opacity})`;
      ctx.lineWidth = (isActive ? 3 : Math.max(1, (edge.score / 100) * 3)) / transform.scale;
      ctx.stroke();
    });

    // Draw nodes
    nodes.forEach((node) => {
      const nodeRadius = 8 / transform.scale;
      ctx.beginPath();
      ctx.arc(node.x, node.y, nodeRadius, 0, 2 * Math.PI);
      ctx.fillStyle = colors.nodeColor;
      ctx.fill();
      ctx.strokeStyle = colors.nodeStroke;
      ctx.lineWidth = 2 / transform.scale;
      ctx.stroke();

      // Draw label
      ctx.font = `${11 / transform.scale}px sans-serif`;
      ctx.fillStyle = colors.textColor;
      ctx.textAlign = "center";
      const displayName =
        node.displayName.length > 20
          ? node.displayName.substring(0, 20) + "..."
          : node.displayName;
      ctx.fillText(displayName, node.x, node.y + 22 / transform.scale);
    });

    ctx.restore();
  }, [nodes, edges, activeEdge, loading, transform, colors, isDarkMode]);

  const screenToWorld = useCallback(
    (screenX: number, screenY: number): { x: number; y: number } => {
      const canvas = canvasRef.current;
      if (!canvas) return { x: 0, y: 0 };

      const rect = canvas.getBoundingClientRect();
      const canvasX = (screenX - rect.left) * (canvas.width / rect.width);
      const canvasY = (screenY - rect.top) * (canvas.height / rect.height);
      const worldX = (canvasX - transform.offsetX) / transform.scale;
      const worldY = (canvasY - transform.offsetY) / transform.scale;

      return { x: worldX, y: worldY };
    },
    [transform]
  );

  const findEdgeAtPosition = useCallback(
    (x: number, y: number): Edge | null => {
      const nodeMap = new Map(nodes.map((n) => [n.id, n]));
      let closestEdge: Edge | null = null;
      let minDistance = 15 / transform.scale;

      edges.forEach((edge) => {
        const sourceNode = nodeMap.get(edge.source);
        const targetNode = nodeMap.get(edge.target);
        if (!sourceNode || !targetNode) return;

        const dx = targetNode.x - sourceNode.x;
        const dy = targetNode.y - sourceNode.y;
        const lengthSq = dx * dx + dy * dy;

        if (lengthSq === 0) return;

        const t = Math.max(
          0,
          Math.min(
            1,
            ((x - sourceNode.x) * dx + (y - sourceNode.y) * dy) / lengthSq
          )
        );
        const nearestX = sourceNode.x + t * dx;
        const nearestY = sourceNode.y + t * dy;
        const distance = Math.sqrt((x - nearestX) ** 2 + (y - nearestY) ** 2);

        if (distance < minDistance) {
          minDistance = distance;
          closestEdge = edge;
        }
      });

      return closestEdge;
    },
    [nodes, edges, transform.scale]
  );

  const handleWheel = useCallback(
    (e: React.WheelEvent<HTMLCanvasElement>) => {
      e.preventDefault();

      const canvas = canvasRef.current;
      if (!canvas) return;

      const rect = canvas.getBoundingClientRect();
      const mouseX = (e.clientX - rect.left) * (canvas.width / rect.width);
      const mouseY = (e.clientY - rect.top) * (canvas.height / rect.height);

      const delta = -e.deltaY * ZOOM_SENSITIVITY;
      const newScale = Math.max(MIN_SCALE, Math.min(MAX_SCALE, transform.scale * (1 + delta)));

      const scaleRatio = newScale / transform.scale;
      const newOffsetX = mouseX - (mouseX - transform.offsetX) * scaleRatio;
      const newOffsetY = mouseY - (mouseY - transform.offsetY) * scaleRatio;

      setTransform({
        scale: newScale,
        offsetX: newOffsetX,
        offsetY: newOffsetY,
      });
    },
    [transform]
  );

  const handleMouseDown = useCallback(
    (e: React.MouseEvent<HTMLCanvasElement>) => {
      if (e.button === 1 || (e.button === 0 && (e.ctrlKey || e.metaKey))) {
        e.preventDefault();
        setIsDragging(true);
        setDragStart({ x: e.clientX, y: e.clientY });
      }
    },
    []
  );

  const handleMouseMove = useCallback(
    (e: React.MouseEvent<HTMLCanvasElement>) => {
      if (isDragging) {
        const canvas = canvasRef.current;
        if (!canvas) return;

        const rect = canvas.getBoundingClientRect();
        const scaleX = canvas.width / rect.width;
        const scaleY = canvas.height / rect.height;

        const dx = (e.clientX - dragStart.x) * scaleX;
        const dy = (e.clientY - dragStart.y) * scaleY;

        setTransform((prev) => ({
          ...prev,
          offsetX: prev.offsetX + dx,
          offsetY: prev.offsetY + dy,
        }));

        setDragStart({ x: e.clientX, y: e.clientY });
        return;
      }

      if (pinnedEdge) return;

      const { x, y } = screenToWorld(e.clientX, e.clientY);
      const edge = findEdgeAtPosition(x, y);
      setHoveredEdge(edge);
      if (edge) {
        setTooltipPosition({ x: e.clientX, y: e.clientY });
      }
    },
    [isDragging, dragStart, pinnedEdge, screenToWorld, findEdgeAtPosition]
  );

  const handleMouseUp = useCallback(() => {
    setIsDragging(false);
  }, []);

  const handleClick = useCallback(
    (e: React.MouseEvent<HTMLCanvasElement>) => {
      if (isDragging) return;
      if (e.ctrlKey || e.metaKey) return;

      const { x, y } = screenToWorld(e.clientX, e.clientY);
      const edge = findEdgeAtPosition(x, y);

      if (edge) {
        setPinnedEdge(edge);
        setTooltipPosition({ x: e.clientX, y: e.clientY });
      } else {
        setPinnedEdge(null);
      }
    },
    [isDragging, screenToWorld, findEdgeAtPosition]
  );

  const handleMouseLeave = useCallback(() => {
    setIsDragging(false);
    if (!pinnedEdge) {
      setHoveredEdge(null);
    }
  }, [pinnedEdge]);

  const handleCloseTooltip = useCallback(() => {
    setPinnedEdge(null);
  }, []);

  const renderTitle = () => (
    <div className="flex flex-col gap-2">
      <div className="flex items-center justify-between">
        <span>{t("comparison.networkGraph.title")}</span>
        <div className="flex items-center gap-2">
          {isTruncated && (
            <Chip color="warning" size="sm" variant="flat">
              {t("comparison.networkGraph.truncatedWarning", {
                shown: PAIR_LIMIT,
                total: totalCount,
              })}
            </Chip>
          )}
          <Chip size="sm" variant="flat">
            {t("comparison.networkGraph.nodeCount", { count: nodes.length })}
          </Chip>
          <Chip size="sm" variant="flat">
            {t("comparison.networkGraph.edgeCount", { count: edges.length })}
          </Chip>
        </div>
      </div>
      <div className="text-sm text-default-500 font-normal">
        {t("comparison.networkGraph.hint")}
      </div>
    </div>
  );

  // Get rule info by ruleId
  const getRuleInfo = useCallback(
    (ruleId: number): RuleInfo | undefined => {
      return rules.find((r) => r.id === ruleId);
    },
    [rules]
  );

  // Render improved tooltip content with formula
  const renderTooltipContent = (edge: Edge) => {
    const ruleScores = edge.pair.ruleScores || [];
    const validRules = ruleScores.filter((rs) => !rs.isSkipped && !rs.isVetoed);
    const totalWeight = validRules.reduce((sum, rs) => sum + rs.weight, 0);
    const weightedSum = validRules.reduce((sum, rs) => sum + rs.score * rs.weight, 0);

    // Build formula parts for display
    const formulaParts = validRules.map(
      (rs) => `${(rs.score * 100).toFixed(0)}%×${rs.weight}`
    );

    return (
      <>
        {/* Resource names */}
        <div className="font-medium mb-2 text-foreground break-words">
          <span className="break-all">{getResourceDisplayName(edge.source)}</span>
          <span className="text-default-400 mx-2 whitespace-nowrap">vs</span>
          <span className="break-all">{getResourceDisplayName(edge.target)}</span>
        </div>

        {/* Final score */}
        <div className="text-lg font-bold text-primary mb-3">
          {t("comparison.networkGraph.pairScore", {
            score: edge.score.toFixed(1),
          })}
        </div>

        <Divider className="my-2" />

        {/* Rule details */}
        {ruleScores.length > 0 && (
          <div className="text-xs space-y-2">
            <div className="font-medium text-foreground">
              {t("comparison.networkGraph.ruleDetails")}:
            </div>
            {ruleScores.map((rs, idx) => {
              const ruleInfo = getRuleInfo(rs.ruleId);
              return (
                <div
                  key={idx}
                  className={`p-2 rounded ${
                    rs.isSkipped
                      ? "bg-default-100 text-default-400"
                      : rs.isVetoed
                        ? "bg-danger-50 text-danger"
                        : "bg-default-50"
                  }`}
                >
                  <div className="flex items-center justify-between gap-2 mb-1 flex-wrap">
                    <div className="flex items-center gap-2">
                      <span className="font-medium">
                        {t("comparison.label.rule")} {rs.order + 1}
                      </span>
                      {ruleInfo?.propertyName && (
                        <BriefProperty
                          property={{
                            name: ruleInfo.propertyName,
                            pool: ruleInfo.propertyPool,
                          }}
                          fields={["pool", "name"]}
                          showPoolChip={false}
                          chipProps={{ size: "sm" }}
                        />
                      )}
                    </div>
                    {rs.isSkipped ? (
                      <Chip size="sm" variant="flat" color="default">
                        {t("comparison.networkGraph.skipped")}
                      </Chip>
                    ) : rs.isVetoed ? (
                      <Chip size="sm" variant="flat" color="danger">
                        {t("comparison.networkGraph.vetoed")}
                      </Chip>
                    ) : (
                      <span className="text-primary font-medium">
                        {(rs.score * 100).toFixed(0)}% × {rs.weight}
                      </span>
                    )}
                  </div>
                  {!rs.isSkipped && !rs.isVetoed && rs.value1 !== undefined && rs.value2 !== undefined && (
                    <div className="text-default-500 mt-1">
                      <span className="break-all">{rs.value1 || "(empty)"}</span>
                      <span className="mx-2 text-default-300">vs</span>
                      <span className="break-all">{rs.value2 || "(empty)"}</span>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        )}

        {/* Calculation formula */}
        {validRules.length > 0 && (
          <>
            <Divider className="my-2" />
            <div className="text-xs text-default-500 space-y-1">
              <div className="font-medium text-foreground">{t("comparison.scoring.title")}:</div>
              <div className="font-mono bg-default-100 p-2 rounded overflow-x-auto">
                ({formulaParts.join(" + ")}) / {totalWeight} = {((weightedSum / totalWeight) * 100).toFixed(1)}%
              </div>
            </div>
          </>
        )}
      </>
    );
  };

  return (
    <Modal
      visible={isOpen}
      onClose={onClose}
      size="7xl"
      title={renderTitle()}
      footer={false}
      className="max-h-[100vh]"
    >
      {loading ? (
        <div className="flex items-center justify-center min-h-[400px]">
          <Spinner size="lg" />
        </div>
      ) : nodes.length === 0 ? (
        <div className="flex items-center justify-center min-h-[400px] text-default-400">
          {t("comparison.networkGraph.noData")}
        </div>
      ) : (
        <div ref={containerRef} className="relative">
          {/* Zoom controls */}
          <div className="absolute top-2 right-2 z-10 flex items-center gap-2 bg-background/80 backdrop-blur-sm rounded-lg p-1 border border-default-200">
            <span className="text-xs text-default-500 px-2">
              {Math.round(transform.scale * 100)}%
            </span>
            <Tooltip content={t("Reset view")}>
              <Button
                isIconOnly
                size="sm"
                variant="light"
                onPress={resetTransform}
              >
                <ReloadOutlined />
              </Button>
            </Tooltip>
          </div>

          {/* Help text */}
          <div className="absolute bottom-2 left-2 z-10 text-xs text-default-400 bg-background/80 backdrop-blur-sm rounded px-2 py-1 border border-default-200">
            Scroll to zoom | Ctrl+Drag to pan
          </div>

          <canvas
            ref={canvasRef}
            width={CANVAS_WIDTH}
            height={CANVAS_HEIGHT}
            className={`border border-default-200 rounded-lg w-full ${
              isDragging ? "cursor-grabbing" : "cursor-crosshair"
            }`}
            onWheel={handleWheel}
            onMouseDown={handleMouseDown}
            onMouseMove={handleMouseMove}
            onMouseUp={handleMouseUp}
            onMouseLeave={handleMouseLeave}
            onClick={handleClick}
          />

          {/* Tooltip */}
          {activeEdge && (
            <div
              className={`fixed z-50 bg-background border border-default-200 rounded-lg shadow-lg p-3 max-w-md max-h-96 overflow-y-auto ${
                pinnedEdge ? "ring-2 ring-primary" : ""
              }`}
              style={{
                left: Math.min(tooltipPosition.x + 10, window.innerWidth - 420),
                top: Math.min(tooltipPosition.y + 10, window.innerHeight - 400),
              }}
            >
              {pinnedEdge && (
                <button
                  className="absolute top-1 right-1 p-1 text-default-400 hover:text-default-600 rounded"
                  onClick={handleCloseTooltip}
                >
                  <CloseOutlined className="text-xs" />
                </button>
              )}
              {renderTooltipContent(activeEdge)}
            </div>
          )}
        </div>
      )}
    </Modal>
  );
};

export default ComparisonNetworkGraph;
