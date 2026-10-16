import type { ResourceSourceLink } from "@/core/models/Resource";

import { ResourceSource } from "@/sdk/constants";

type SourceActionTarget = { kind: "web" | "protocol"; href: string };

/** Source keys are identifiers, never arbitrary URLs or commands from the server. */
export function sourceActionTarget(
  source: ResourceSource,
  key: string,
): SourceActionTarget | undefined {
  if (source === ResourceSource.DLsite && /^(?:RJ|BJ|VJ)\d+$/i.test(key)) {
    return {
      kind: "web",
      href: `https://www.dlsite.com/maniax/work/=/product_id/${key.toUpperCase()}.html`,
    };
  }
  if (source === ResourceSource.ExHentai && /^\d+\/[a-z0-9]+$/i.test(key)) {
    return { kind: "web", href: `https://exhentai.org/g/${key}/` };
  }
  if (source === ResourceSource.Steam && /^[1-9]\d*$/.test(key)) {
    return { kind: "protocol", href: `steam://rungameid/${key}` };
  }

  return undefined;
}

/** Original-site navigation is metadata, never a playable candidate. */
export function originalSourceActions(links: ResourceSourceLink[] | undefined) {
  const seen = new Set<string>();

  return (links ?? []).flatMap((link) => {
    const action =
      link.source === ResourceSource.Steam && /^[1-9]\d*$/.test(link.sourceKey)
        ? { kind: "web", href: `https://store.steampowered.com/app/${link.sourceKey}/` }
        : sourceActionTarget(link.source, link.sourceKey);

    if (!action || action.kind !== "web" || seen.has(action.href)) return [];
    seen.add(action.href);

    return [{ source: link.source, href: action.href }];
  });
}
