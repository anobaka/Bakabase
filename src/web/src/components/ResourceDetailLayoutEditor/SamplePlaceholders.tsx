import type { SectionId } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

// Shared sample placeholders. Shown in the layout editor whenever real
// resource data isn't available — the live config editor in display
// options, and the dev test page. Keeping the visual the same in both
// places means what users tweak matches what they'll see (modulo the
// runtime caveat that section heights are content-driven).

function PlaceholderCard({
  children,
  color,
  tone,
}: {
  children: React.ReactNode;
  color: string;
  tone?: string;
}) {
  return (
    <div className={`rounded-medium p-3 h-full overflow-hidden ${color} ${tone ?? ""}`}>
      {children}
    </div>
  );
}

function InfoRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between text-xs gap-2">
      <span className="opacity-70">{label}</span>
      <span className="font-medium text-right">{value}</span>
    </div>
  );
}

function PropertyRow({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-0.5 min-w-0">
      <span className="text-[10px] uppercase tracking-wide opacity-60">{label}</span>
      <span className="text-xs font-medium truncate">{value}</span>
    </div>
  );
}

function PlaceholderContent({ id }: { id: SectionId }) {
  const { t } = useTranslation();

  switch (id) {
    case "cover":
      return (
        <PlaceholderCard color="bg-rose-100" tone="text-rose-900">
          <div className="aspect-[2/3] w-full rounded-medium bg-rose-300/50 flex items-center justify-center text-rose-900/80 font-semibold">
            {t<string>("resource.detailLayout.sample.cover.label")}
          </div>
        </PlaceholderCard>
      );
    case "rating":
      return (
        <PlaceholderCard color="bg-amber-100" tone="text-amber-900">
          <div className="flex items-center gap-1 text-2xl">★★★★☆</div>
          <div className="text-xs opacity-70 mt-1">
            {t<string>("resource.detailLayout.sample.rating.score")}
          </div>
        </PlaceholderCard>
      );
    case "actions":
      return (
        <PlaceholderCard color="bg-sky-100" tone="text-sky-900">
          <div className="flex flex-wrap gap-2">
            {[
              t<string>("resource.detailLayout.sample.actions.play"),
              t<string>("resource.detailLayout.sample.actions.open"),
              t<string>("resource.detailLayout.sample.actions.refresh"),
              t<string>("resource.detailLayout.sample.actions.enhance"),
            ].map((a) => (
              <span key={a} className="px-2 py-1 rounded-small bg-sky-300/40 text-xs font-medium">
                {a}
              </span>
            ))}
          </div>
        </PlaceholderCard>
      );
    case "basicInfo":
      return (
        <PlaceholderCard color="bg-violet-100" tone="text-violet-900">
          <div className="flex flex-col gap-1">
            <InfoRow
              label={t<string>("resource.detailLayout.sample.basicInfo.createdLabel")}
              value={t<string>("resource.detailLayout.sample.basicInfo.createdValue")}
            />
            <InfoRow
              label={t<string>("resource.detailLayout.sample.basicInfo.modifiedLabel")}
              value={t<string>("resource.detailLayout.sample.basicInfo.modifiedValue")}
            />
            <InfoRow
              label={t<string>("resource.detailLayout.sample.basicInfo.sizeLabel")}
              value={t<string>("resource.detailLayout.sample.basicInfo.sizeValue")}
            />
          </div>
        </PlaceholderCard>
      );
    case "hierarchy":
      return (
        <PlaceholderCard color="bg-teal-100" tone="text-teal-900">
          <div className="text-sm leading-6">
            {t<string>("resource.detailLayout.sample.hierarchy.path")}
          </div>
          <div className="text-xs opacity-70 mt-2">
            {t<string>("resource.detailLayout.sample.hierarchy.summary")}
          </div>
        </PlaceholderCard>
      );
    case "introduction":
      return (
        <PlaceholderCard color="bg-emerald-100" tone="text-emerald-900">
          <p className="text-sm leading-6">
            {t<string>("resource.detailLayout.sample.introduction.paragraph1")}
          </p>
          <p className="text-sm leading-6 mt-2 opacity-80">
            {t<string>("resource.detailLayout.sample.introduction.paragraph2")}
          </p>
        </PlaceholderCard>
      );
    case "playedAt":
      return (
        <PlaceholderCard color="bg-lime-100" tone="text-lime-900">
          <InfoRow
            label={t<string>("resource.detailLayout.sample.playedAt.label")}
            value={t<string>("resource.detailLayout.sample.playedAt.value")}
          />
        </PlaceholderCard>
      );
    case "properties":
      return (
        <PlaceholderCard color="bg-indigo-100" tone="text-indigo-900">
          <div className="grid grid-cols-2 gap-x-4 gap-y-2">
            <PropertyRow
              label={t<string>("resource.detailLayout.sample.properties.director.label")}
              value={t<string>("resource.detailLayout.sample.properties.director.value")}
            />
            <PropertyRow
              label={t<string>("resource.detailLayout.sample.properties.rating.label")}
              value={t<string>("resource.detailLayout.sample.properties.rating.value")}
            />
            <PropertyRow
              label={t<string>("resource.detailLayout.sample.properties.genre.label")}
              value={t<string>("resource.detailLayout.sample.properties.genre.value")}
            />
            <PropertyRow
              label={t<string>("resource.detailLayout.sample.properties.tags.label")}
              value={t<string>("resource.detailLayout.sample.properties.tags.value")}
            />
            <PropertyRow
              label={t<string>("resource.detailLayout.sample.properties.released.label")}
              value={t<string>("resource.detailLayout.sample.properties.released.value")}
            />
            <PropertyRow
              label={t<string>("resource.detailLayout.sample.properties.completion.label")}
              value={t<string>("resource.detailLayout.sample.properties.completion.value")}
            />
          </div>
        </PlaceholderCard>
      );
    case "relatedDataCards":
      return (
        <PlaceholderCard color="bg-orange-100" tone="text-orange-900">
          <div className="flex gap-2 overflow-hidden">
            {[1, 2, 3, 4].map((i) => (
              <div
                key={i}
                className="w-28 h-36 shrink-0 rounded-small bg-orange-300/40 flex items-center justify-center text-xs"
              >
                {t<string>("resource.detailLayout.sample.relatedDataCards.cardLabel", { n: i })}
              </div>
            ))}
          </div>
        </PlaceholderCard>
      );
    case "mediaLibs":
      return (
        <PlaceholderCard color="bg-cyan-100" tone="text-cyan-900">
          <div className="flex flex-wrap gap-1">
            {[
              t<string>("resource.detailLayout.sample.mediaLibs.lib1"),
              t<string>("resource.detailLayout.sample.mediaLibs.lib2"),
            ].map((l) => (
              <span key={l} className="px-2 py-0.5 rounded-small bg-cyan-300/40 text-xs">
                {l}
              </span>
            ))}
          </div>
        </PlaceholderCard>
      );
    case "profiles":
      return (
        <PlaceholderCard color="bg-fuchsia-100" tone="text-fuchsia-900">
          <div className="flex flex-col gap-1">
            <InfoRow
              label={t<string>("resource.detailLayout.sample.profiles.qualityLabel")}
              value={t<string>("resource.detailLayout.sample.profiles.qualityValue")}
            />
            <InfoRow
              label={t<string>("resource.detailLayout.sample.profiles.audioLabel")}
              value={t<string>("resource.detailLayout.sample.profiles.audioValue")}
            />
            <InfoRow
              label={t<string>("resource.detailLayout.sample.profiles.subsLabel")}
              value={t<string>("resource.detailLayout.sample.profiles.subsValue")}
            />
          </div>
        </PlaceholderCard>
      );
    default:
      return null;
  }
}

function SamplePlaceholder({ id }: { id: SectionId }) {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col h-full">
      <div className="text-[10px] uppercase tracking-wider text-default-500 mb-1 px-1">
        {t<string>(`resource.detailLayout.section.${id}`)}
      </div>
      <PlaceholderContent id={id} />
    </div>
  );
}

export function renderSamplePlaceholder(id: SectionId): React.ReactNode {
  return <SamplePlaceholder id={id} />;
}
