"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { DestroyableProps } from "@/components/bakaui/types";
import type {
  BakabaseServiceModelsViewFileSystemEntryGroupResultViewModel,
  BakabaseServiceModelsInputFileSystemEntryGroupInputModel,
  BakabaseServiceModelsInputFileSystemEntryGroupStrategyType,
  BakabaseServiceModelsInputFileSystemEntryGroupAffixDirection,
} from "@/sdk/Api";

import React, { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Slider, Tab, Tabs } from "@heroui/react";

import BApi from "@/sdk/BApi";
import { Button, Modal, Input, Radio, RadioGroup } from "@/components/bakaui";
import FileChangePreview, {
  FileChangeBatch,
} from "@/components/FileChangePreview";

const KEY_EXTRACTION_PRESETS: { labelKey: string; regex: string }[] = [
  {
    labelKey: "fileExplorer.groupModal.keyExtraction.presetAv",
    regex: "([A-Z]{2,6}-\\d{2,5})",
  },
  {
    labelKey: "fileExplorer.groupModal.keyExtraction.presetTvShow",
    regex: "^(.+?)[\\s\\.]+[Ss]\\d+[Ee]\\d+",
  },
  {
    labelKey: "fileExplorer.groupModal.keyExtraction.presetNumericSeq",
    regex: "^(.+?)[\\s_\\-\\.]+\\d+$",
  },
  {
    labelKey: "fileExplorer.groupModal.keyExtraction.presetVolume",
    regex: "^(.+?)\\s*(?:[Vv]ol|[Cc]h|第)\\.?\\s*\\d+",
  },
];

type Props = {
  entries: Entry[];
  groupInternal: boolean;
} & DestroyableProps;

type ApiResult = BakabaseServiceModelsViewFileSystemEntryGroupResultViewModel;

const StrategyType = {
  Similarity: 0,
  KeyExtraction: 1,
  Affix: 2,
} as const;

const AffixDirection = {
  Prefix: 0,
  Suffix: 1,
  Both: 2,
} as const;

const buildBaseModel = (
  entries: Entry[],
  groupInternal: boolean,
): Partial<BakabaseServiceModelsInputFileSystemEntryGroupInputModel> => ({
  paths: entries.map((e) => e.path).filter(Boolean) as string[],
  groupInternal,
});

const mapToBatches = (results: ApiResult[]): FileChangeBatch[] =>
  results.map((r) => ({
    rootPath: r.rootPath,
    groups: (r.groups ?? []).map((g) => ({
      targetName: g.directoryName,
      targetIsDirectory: true,
      members: (g.entries ?? []).map((e) => ({
        name: e.name,
        isDirectory: e.isDirectory,
        highlightSpans: (e.matchSpans ?? []).map((s) => ({
          start: s.start,
          length: s.length,
        })),
      })),
    })),
    untouched: (r.untouchedEntries ?? []).map((e) => ({
      name: e.name,
      isDirectory: e.isDirectory,
    })),
  }));

const GroupModal = ({ entries = [], groupInternal, onDestroyed }: Props) => {
  const { t } = useTranslation();

  const [strategyType, setStrategyType] = useState<number>(StrategyType.Similarity);

  const [breakpoints, setBreakpoints] = useState<number[]>([0, 1]);
  const [similarityIndex, setSimilarityIndex] = useState<number>(0);

  const [keyRegex, setKeyRegex] = useState<string>("");
  const [regexError, setRegexError] = useState<string>("");

  const [affixDirection, setAffixDirection] = useState<number>(AffixDirection.Prefix);
  const [affixMinLength, setAffixMinLength] = useState<number>(3);

  const [preview, setPreview] = useState<ApiResult[]>([]);
  const [calculating, setCalculating] = useState(true);

  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const buildModel = (): BakabaseServiceModelsInputFileSystemEntryGroupInputModel => ({
    ...(buildBaseModel(entries, groupInternal) as BakabaseServiceModelsInputFileSystemEntryGroupInputModel),
    strategyType: strategyType as BakabaseServiceModelsInputFileSystemEntryGroupStrategyType,
    similarityThreshold:
      breakpoints.length > 0
        ? breakpoints[Math.min(similarityIndex, breakpoints.length - 1)]
        : 1,
    keyExtractionRegex: keyRegex || undefined,
    affixDirection: affixDirection as BakabaseServiceModelsInputFileSystemEntryGroupAffixDirection,
    affixMinLength,
  });

  useEffect(() => {
    BApi.file
      .getFileSystemEntriesGroupSimilarityBreakpoints(
        buildBaseModel(entries, groupInternal) as BakabaseServiceModelsInputFileSystemEntryGroupInputModel,
      )
      .then((r) => {
        const bps = (r.data ?? [0, 1]).map((d) => Number(d)).sort((a, b) => a - b);
        const dedupedClustered: number[] = [];
        for (const v of bps) {
          if (
            dedupedClustered.length === 0 ||
            v - dedupedClustered[dedupedClustered.length - 1] >= 0.01
          ) {
            dedupedClustered.push(v);
          }
        }
        if (dedupedClustered[dedupedClustered.length - 1] !== 1) {
          dedupedClustered.push(1);
        }
        setBreakpoints(dedupedClustered);
        setSimilarityIndex(dedupedClustered.length - 1);
      });
  }, []);

  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);

    if (strategyType === StrategyType.KeyExtraction && keyRegex) {
      try {
        new RegExp(keyRegex);
        setRegexError("");
      } catch {
        setRegexError(t<string>("fileExplorer.groupModal.keyExtraction.invalidRegex"));
        setPreview([]);
        setCalculating(false);
        return;
      }
    } else {
      setRegexError("");
    }

    setCalculating(true);
    debounceRef.current = setTimeout(() => {
      BApi.file
        .previewFileSystemEntriesGroupResult(buildModel())
        .then((x) => setPreview(x.data ?? []))
        .finally(() => setCalculating(false));
    }, 250);

    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
    };
  }, [
    strategyType,
    similarityIndex,
    breakpoints,
    keyRegex,
    affixDirection,
    affixMinLength,
  ]);

  const batches = useMemo(() => mapToBatches(preview), [preview]);

  const currentSimilarity = breakpoints.length
    ? breakpoints[Math.min(similarityIndex, breakpoints.length - 1)]
    : 1;
  const currentSimilarityPercent = Math.round(currentSimilarity * 100);
  const similarityHint =
    currentSimilarity === 0
      ? t<string>("fileExplorer.groupModal.allItemsOneFolder")
      : currentSimilarity === 1
        ? t<string>("fileExplorer.groupModal.exactSameNameFolder")
        : t<string>("fileExplorer.groupModal.similarityHintMid", {
            percent: currentSimilarityPercent,
          });

  const modeDescriptionKey =
    strategyType === StrategyType.Similarity
      ? "fileExplorer.groupModal.mode.similarityDesc"
      : strategyType === StrategyType.KeyExtraction
        ? "fileExplorer.groupModal.mode.keyExtractionDesc"
        : "fileExplorer.groupModal.mode.affixDesc";

  return (
    <Modal
      defaultVisible
      disableAnimation
      classNames={{ base: "h-[85vh]" }}
      footer={{
        actions: ["ok", "cancel"],
        okProps: {
          children: `${t<string>("fileExplorer.groupModal.okButton")}(Enter)`,
          autoFocus: true,
          disabled: !preview.some((p) => (p.groups ?? []).length > 0),
        },
      }}
      size={"2xl"}
      title={t<string>(
        groupInternal
          ? "fileExplorer.groupModal.titleInternal"
          : "fileExplorer.groupModal.title",
        { count: entries.length },
      )}
      onDestroyed={onDestroyed}
      onOk={async () => {
        await BApi.file.groupFileSystemEntries(buildModel());
      }}
    >
      <div className="flex gap-4 items-start h-full">
        <div className="w-[360px] flex-shrink-0 flex flex-col gap-3">
          <Tabs
            selectedKey={String(strategyType)}
            onSelectionChange={(k) => setStrategyType(Number(k))}
            size="sm"
            fullWidth
          >
            <Tab
              key={String(StrategyType.Similarity)}
              title={t<string>("fileExplorer.groupModal.mode.similarityShort")}
            />
            <Tab
              key={String(StrategyType.KeyExtraction)}
              title={t<string>("fileExplorer.groupModal.mode.keyExtractionShort")}
            />
            <Tab
              key={String(StrategyType.Affix)}
              title={t<string>("fileExplorer.groupModal.mode.affixShort")}
            />
          </Tabs>
          <p className="text-xs opacity-70 leading-relaxed">
            {t<string>(modeDescriptionKey)}
          </p>

          {strategyType === StrategyType.Similarity && (
            <div className="flex flex-col gap-1">
              <Slider
                isDisabled={calculating || breakpoints.length <= 1}
                minValue={0}
                maxValue={Math.max(0, breakpoints.length - 1)}
                step={1}
                size="sm"
                value={similarityIndex}
                onChange={(v) => setSimilarityIndex(Array.isArray(v) ? v[0] : v)}
                renderValue={() => `${currentSimilarityPercent}%`}
                label={t<string>("fileExplorer.groupModal.similarityThreshold")}
              />
              <span className="text-xs opacity-60 leading-relaxed">
                {similarityHint}
              </span>
              {breakpoints.length > 2 && (
                <span className="text-xs opacity-50">
                  {t<string>("fileExplorer.groupModal.snapToBreakpoint", {
                    count: breakpoints.length,
                  })}
                </span>
              )}
            </div>
          )}

          {strategyType === StrategyType.KeyExtraction && (
            <div className="flex flex-col gap-2">
              <div className="flex flex-wrap items-center gap-1">
                <span className="text-xs opacity-60 mr-1">
                  {t<string>("fileExplorer.groupModal.keyExtraction.presetsLabel")}
                </span>
                {KEY_EXTRACTION_PRESETS.map((p) => (
                  <Button
                    key={p.labelKey}
                    size="sm"
                    variant="flat"
                    onClick={() => setKeyRegex(p.regex)}
                  >
                    {t<string>(p.labelKey)}
                  </Button>
                ))}
              </div>
              <Input
                size="sm"
                value={keyRegex}
                placeholder={t<string>("fileExplorer.groupModal.keyExtraction.regexPlaceholder")}
                onValueChange={setKeyRegex}
                label={t<string>("fileExplorer.groupModal.keyExtraction.regexLabel")}
                description={t<string>("fileExplorer.groupModal.keyExtraction.regexDesc")}
                isInvalid={!!regexError}
                errorMessage={regexError}
              />
            </div>
          )}

          {strategyType === StrategyType.Affix && (
            <div className="flex flex-col gap-3">
              <RadioGroup
                size="sm"
                orientation="horizontal"
                label={t<string>("fileExplorer.groupModal.affix.direction")}
                value={String(affixDirection)}
                onValueChange={(v) => setAffixDirection(Number(v))}
              >
                <Radio value={String(AffixDirection.Prefix)}>
                  {t<string>("fileExplorer.groupModal.affix.directionPrefix")}
                </Radio>
                <Radio value={String(AffixDirection.Suffix)}>
                  {t<string>("fileExplorer.groupModal.affix.directionSuffix")}
                </Radio>
                <Radio value={String(AffixDirection.Both)}>
                  {t<string>("fileExplorer.groupModal.affix.directionBoth")}
                </Radio>
              </RadioGroup>
              <Input
                size="sm"
                type="number"
                min={1}
                value={String(affixMinLength)}
                onValueChange={(v) => {
                  const n = Number(v);
                  if (!Number.isNaN(n) && n >= 1) setAffixMinLength(n);
                }}
                label={t<string>("fileExplorer.groupModal.affix.minLength")}
                className="max-w-[160px]"
              />
            </div>
          )}
        </div>

        <div className="flex-1 min-w-0 h-full">
          <FileChangePreview
            batches={batches}
            isLoading={calculating}
            className="h-full max-h-full"
          />
        </div>
      </div>
    </Modal>
  );
};

GroupModal.displayName = "GroupModal";

export default GroupModal;
